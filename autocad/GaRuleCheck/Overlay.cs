// 도면 위 표시: Transient(임시 그래픽)만 쓴다. 도면 DB를 고치지 않으므로 Undo 기록을 오염시키지 않고
// 저장 파일에도 남지 않는다. 매 검사마다 전부 지우고 새로 그린다(증분 갱신 없음 → 동기화 오류 없음).
//
// 엔진 overlays(m 좌표) 종류
//   sightline/tick : 시선·허용 높이 (pass 초록 / fail 빨강)
//   waterline      : 최악 조건 수선 (하늘색)
//   limit_marker, band_line, band : 허용 한계·허용 구간 (노랑)
//   eye, ref_point : 점 표식 (원)
//   label          : 글자 (height[m])
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
// GraphicsInterface에도 같은 이름이 있어 DB 엔티티로 고정
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using Circle = Autodesk.AutoCAD.DatabaseServices.Circle;
using MText = Autodesk.AutoCAD.DatabaseServices.MText;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace GaRuleCheck
{
    public static class Overlay
    {
        private const short Red = 1, Yellow = 2, Green = 3, Cyan = 4, Orange = 30, Gray = 8;
        private static readonly List<Entity> Items = new();

        private static readonly List<Entity> HoverItems = new();

        public static bool Enabled { get; set; } = true;
        public static bool Labels { get; set; } = true;
        /// 라벨 확대: 라벨·선 두께·표식 크기 배율 (프로젝터·화면 공유에서 잘 보이게). 표시만 바뀌고 판정·도면은 그대로
        public static double Scale { get; set; } = 1.0;

        public static void Clear()
        {
            var tm = TransientManager.CurrentTransientManager;
            foreach (var e in Items)
            {
                try { tm.EraseTransient(e, new IntegerCollection()); } catch { /* 다른 문서 뷰 */ }
                e.Dispose();
            }
            Items.Clear();
        }

        public static void Show(Document doc, CheckResponse resp)
        {
            Clear();
            if (!Enabled) return;
            var db = doc.Database;
            double scale = resp.UnitsScale ?? 0;   // 1 도면 단위 = scale m
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var style = DrawingProps.LabelStyleId(tr, db);
            foreach (var r in resp.Results)
            {
                if (r.Status is "fail" or "review")
                {
                    short color = r.Status == "fail" ? Red : Orange;
                    foreach (var h in r.Targets)
                    {
                        var id = Extractor.HandleToId(db, h);
                        if (id.IsNull || id.IsErased) continue;
                        if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                        var clone = (Entity)ent.Clone();
                        clone.ColorIndex = color;
                        Add(clone);
                    }
                }
                if (scale <= 0) continue;
                foreach (var ov in r.Overlays) Draw(ov, scale, style, db);
            }
            tr.Commit();
        }

        private static Point3d P(double[] p, double scale) => new(p[0] / scale, p[1] / scale, 0);

        /// MText 서식 문자(\ { })를 글자로 취급되게 이스케이프.
        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");

        private static void Draw(OverlayDto ov, double scale, ObjectId style, Database db)
        {
            if (ov.Points.Count == 0) return;
            short statusColor = ov.Status switch { "fail" => Red, "pass" => Green, _ => Gray };
            switch (ov.Kind)
            {
                case "label":
                    if (!Labels || string.IsNullOrEmpty(ov.Text)) return;
                    // 한글이 ??로 보이지 않게: 도면에 없는 임시 객체는 문자 스타일 글꼴을 못 찾는 경우가 있어
                    // MText 내용에 TrueType 글꼴(맑은 고딕, 한글 문자셋 c129)을 직접 지정한다.
                    var t = new MText();
                    t.SetDatabaseDefaults(db);
                    t.TextStyleId = style;
                    t.Location = P(ov.Points[0], scale);
                    t.TextHeight = (ov.Height ?? 1.0) * Scale / scale;
                    t.Attachment = AttachmentPoint.BottomLeft;
                    t.Contents = "{\\fMalgun Gothic|b0|i0|c129|p34;" + Esc(ov.Text) + "}";
                    t.ColorIndex = ov.Status switch { "fail" => Red, "pass" => Green, _ => Yellow };
                    // 선 위에 겹쳐도 읽히도록 배경 가림
                    t.BackgroundFill = true;
                    t.UseBackgroundColor = true;
                    t.BackgroundScaleFactor = 1.2;
                    Add(t);
                    return;
                case "eye":
                case "ref_point":
                    var c = new Circle(P(ov.Points[0], scale), Vector3d.ZAxis, 0.6 * Scale / scale)
                    {
                        ColorIndex = ov.Kind == "eye" ? Green : Yellow,
                    };
                    Add(c);
                    return;
            }
            if (ov.Points.Count < 2) return;
            short color = ov.Kind switch
            {
                "waterline" => Cyan,
                "limit_marker" or "band_line" or "band" => Yellow,
                _ => statusColor,
            };
            var pl = new Polyline();
            for (int i = 0; i < ov.Points.Count; i++)
                pl.AddVertexAt(i, new Point2d(ov.Points[i][0] / scale, ov.Points[i][1] / scale), 0, 0, 0);
            pl.ColorIndex = color;
            if (ov.Kind is "sightline" or "band")
            {
                double w = (ov.Kind == "band" ? 0.5 : 0.15) * Scale / scale;   // 도면에서 잘 보이게 폭을 준다
                pl.ConstantWidth = w;
            }
            Add(pl);
        }

        /// 팔레트 카드에 마우스를 올린 동안 대상 객체를 자홍색으로 강조한다.
        public static void Hover(Document doc, IEnumerable<string> handles, bool on)
        {
            var tm = TransientManager.CurrentTransientManager;
            foreach (var e in HoverItems)
            {
                try { tm.EraseTransient(e, new IntegerCollection()); } catch { }
                e.Dispose();
            }
            HoverItems.Clear();
            if (!on) return;
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var h in handles)
                {
                    var id = Extractor.HandleToId(db, h);
                    if (id.IsNull || id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                    var clone = (Entity)ent.Clone();
                    clone.ColorIndex = 6;   // 자홍
                    tm.AddTransient(clone, TransientDrawingMode.Highlight, 129, new IntegerCollection());
                    HoverItems.Add(clone);
                }
                tr.Commit();
            }
        }

        /// 결과의 표시 요소(시선·허용선·라벨)와 대상 객체가 모두 화면에 들어오게 줌(2D 평면 뷰).
        public static void Fit(Document doc, RuleResultDto r, double scale)
        {
            if (scale <= 0) return;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            void Acc(double x, double y) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            foreach (var ov in r.Overlays)
                foreach (var p in ov.Points) Acc(p[0] / scale, p[1] / scale);
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var h in r.Targets)
                {
                    var id = Extractor.HandleToId(db, h);
                    if (id.IsNull || id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                    try { var e = ent.GeometricExtents; Acc(e.MinPoint.X, e.MinPoint.Y); Acc(e.MaxPoint.X, e.MaxPoint.Y); } catch { }
                }
                tr.Commit();
                if (x0 > x1) return;
                double w = Math.Max(x1 - x0, 1), hgt = Math.Max(y1 - y0, 1);
                using var view = doc.Editor.GetCurrentView();
                view.CenterPoint = new Point2d((x0 + x1) / 2, (y0 + y1) / 2);
                double aspect = view.Width / Math.Max(view.Height, 1e-9);
                // 화면 비율을 유지하면서 둘 다 들어오게 + 여백 12%
                if (w / hgt > aspect) { view.Width = w * 1.12; view.Height = w * 1.12 / aspect; }
                else { view.Height = hgt * 1.12; view.Width = hgt * 1.12 * aspect; }
                doc.Editor.SetCurrentView(view);
            }
        }

        private static void Add(Entity e)
        {
            TransientManager.CurrentTransientManager.AddTransient(
                e, TransientDrawingMode.DirectTopmost, 128, new IntegerCollection());
            Items.Add(e);
        }
    }
}
