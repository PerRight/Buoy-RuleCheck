// 도면 위 표시: Transient(임시 그래픽)만 쓴다. 도면 DB를 고치지 않으므로 Undo 기록을 오염시키지 않고
// 저장 파일에도 남지 않는다. 매 검사마다 전부 지우고 새로 그린다(증분 갱신 없음 → 동기화 오류 없음).
// Buoy UI(10-26): 평소에는 부표(BuoyLayer)만 떠 있고, 근거 선(시선·허용선·라벨)과 대상 강조는
// 부표나 팔레트 카드에 커서를 올린 동안만 그린다(ShowEvidence). 색은 판정 색(수정 소요 빨강·문제없음 파랑·확인 필요 주황)과
// 흑백 잉크(가이드)만 — 노랑·하늘색 같은 다른 색은 쓰지 않는다.
//
// 엔진 overlays(m 좌표) 종류
//   sightline/tick : 시선·허용 높이 (문제없음 파랑 / 수정 소요 빨강 / 확인 필요 주황)
//   (버전 비교 표시는 별도 목록: 추가 초록·삭제 빨강 파선·변경 노랑, ShowDiff)
//   waterline      : 최악 조건 수선 (흐린 잉크 Faint)
//   limit_marker, band_line, band : 허용 한계·허용 구간 (보조 잉크 Muted)
//   eye, ref_point : 점 표식 (원)
//   label          : 글자 (height[m])
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
// GraphicsInterface에도 같은 이름이 있어 DB 엔티티로 고정
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using Circle = Autodesk.AutoCAD.DatabaseServices.Circle;
using MText = Autodesk.AutoCAD.DatabaseServices.MText;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BuoyRuleCheck
{
    public static class Overlay
    {
        private static readonly List<Entity> Items = new();

        private static readonly List<Entity> HoverItems = new();
        private static readonly List<Entity> ElemItems = new();   // 요소 hover 강조
        private static readonly List<Entity> DiffItems = new();   // 버전 비교 표시

        // 판정 색은 Theme v2 토큰 하나에서(도면 위 = 다크 값)
        internal static AcColor Ac(System.Drawing.Color c) => AcColor.FromRgb(c.R, c.G, c.B);
        private static readonly AcColor PassColor = Ac(Theme.Pass);
        private static readonly AcColor FailColor = Ac(Theme.Fail);
        private static readonly AcColor UnknownColor = Ac(Theme.Warn);
        public static AcColor StatusColor(string? s) => s switch { "pass" => PassColor, "fail" => FailColor, _ => UnknownColor };

        public static bool Enabled { get; set; } = true;
        public static bool Labels { get; set; } = true;
        /// 라벨 확대: 라벨·선 두께·표식 크기 배율 (프로젝터·화면 공유에서 잘 보이게). 표시만 바뀌고 판정·도면은 그대로
        public static double Scale { get; set; } = 1.0;

        public static void Clear() => ClearList(Items);

        private static CheckResponse? _resp;
        private static Document? _respDoc;
        private static (string rule, string? element)? _evidence;

        /// 새 판정: 저장만 하고(근거 선은 커서를 올렸을 때만), 보고 있던 근거가 있으면 새 값으로 다시 그린다.
        public static void Show(Document doc, CheckResponse resp)
        {
            Clear();
            _resp = resp;
            _respDoc = doc;
            if (_evidence is { } ev) ShowEvidence(doc, ev.rule, ev.element);
        }

        /// 근거 보기: 그 규칙의 시선·허용선·라벨 + 대상 객체(판정 색). 부표·팔레트 카드에 커서를 올린 동안만.
        public static void ShowEvidence(Document doc, string ruleId, string? elementId)
        {
            Clear();
            _evidence = (ruleId, elementId);
            if (!Enabled || _resp == null || _respDoc != doc) return;
            var r = _resp.Results.FirstOrDefault(x => x.RuleId == ruleId);
            if (r == null) return;
            var db = doc.Database;
            double scale = _resp.UnitsScale ?? 0;   // 1 도면 단위 = scale m
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var style = DrawingProps.LabelStyleId(tr, db);
                var el = elementId == null ? null : r.Elements.FirstOrDefault(e => e.ElementId == elementId);
                var targets = el != null ? new List<string> { el.ElementId } : r.Targets;
                var col = StatusColor(el?.Status ?? r.Status);
                // 펼친 블록의 하위 객체 handle('1F3.12')은 원래 블록 하나로 — 같은 블록을 수백 번 복제하지 않게
                foreach (var h in targets.Select(Extractor.BaseHandle).Distinct())
                {
                    var id = Extractor.HandleToId(db, h);
                    if (id.IsNull || id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                    var clone = (Entity)ent.Clone();
                    clone.Color = col;
                    if (clone is Polyline pl0) pl0.ConstantWidth = 1.5 * UnitsPerPixel(doc);
                    Add(clone);
                }
                if (scale > 0)
                    foreach (var ov in r.Overlays) Draw(ov, scale, style, db);
                tr.Commit();
            }
        }

        /// 검사 실패·문서 닫힘: 이전 판정으로 근거를 그리지 않게 잊는다.
        public static void Forget()
        {
            _resp = null;
            _respDoc = null;
            ClearEvidence();
        }

        public static void ClearEvidence()
        {
            _evidence = null;
            Clear();
        }

        private static Point3d P(double[] p, double scale) => new(p[0] / scale, p[1] / scale, 0);

        /// MText 서식 문자(\ { })를 글자로 취급되게 이스케이프.
        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");

        private static void Draw(OverlayDto ov, double scale, ObjectId style, Database db)
        {
            if (ov.Points.Count == 0) return;
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
                    t.Color = ov.Status switch { "pass" => PassColor, "fail" => FailColor, _ => Ac(Theme.Muted) };
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
                        Color = Ac(ov.Kind == "eye" ? Theme.Text : Theme.Muted),
                    };
                    Add(c);
                    return;
            }
            if (ov.Points.Count < 2) return;
            // 근거 선은 판정 색, 수선·허용 한계·허용 구간은 흑백 잉크(가이드)
            var color = ov.Kind switch
            {
                "waterline" => Ac(Theme.Faint),
                "limit_marker" or "band_line" or "band" => Ac(Theme.Muted),
                _ => ov.Status switch { "fail" => FailColor, "pass" => PassColor, _ => UnknownColor },
            };
            var pl = new Polyline();
            for (int i = 0; i < ov.Points.Count; i++)
                pl.AddVertexAt(i, new Point2d(ov.Points[i][0] / scale, ov.Points[i][1] / scale), 0, 0, 0);
            pl.Color = color;
            if (ov.Kind is "sightline" or "band")
            {
                double w = (ov.Kind == "band" ? 0.5 : 0.15) * Scale / scale;   // 도면에서 잘 보이게 폭을 준다
                pl.ConstantWidth = w;
            }
            Add(pl);
        }

        /// 팔레트 카드에 마우스를 올린 동안 대상 객체를 흰 잉크로 강조한다.
        public static void Hover(Document doc, IEnumerable<string> handles, bool on)
        {
            ClearList(HoverItems);
            if (!on) return;
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var h in handles.Select(Extractor.BaseHandle).Distinct())
                {
                    var id = Extractor.HandleToId(db, h);
                    if (id.IsNull || id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                    var clone = (Entity)ent.Clone();
                    clone.Color = Ac(Theme.Text);   // 흰 잉크(판정 색과 겹치지 않게)
                    Put(HoverItems, clone, TransientDrawingMode.Highlight, 129);
                }
                tr.Commit();
            }
        }

        /// 화면 1픽셀이 도면 단위로 얼마인지(표식·선 두께를 화면 크기에 맞추기 위해).
        public static double UnitsPerPixel(Document doc)
        {
            try
            {
                using var view = doc.Editor.GetCurrentView();
                var ss = (Point2d)AcApp.GetSystemVariable("SCREENSIZE");
                return view.Height / Math.Max(ss.Y, 1);
            }
            catch { return 1; }
        }

        /// 임시 그래픽을 지운다. 지워진 것만 해제하고, 지우지 못한 것(다른 도면 탭이 현재일 때 등)은 해제하지 않고 남겨 다음에 다시 지운다
        /// — 해제한 객체를 AutoCAD가 그 도면으로 돌아와 그리지 않게(2026-10-26 검증 N6).
        private static readonly List<Entity> Stale = new();
        private const int MaxStale = 500;
        private static bool _staleReported;
        private static void ClearList(List<Entity> items)
        {
            var tm = TransientManager.CurrentTransientManager;
            var all = new List<Entity>(Stale.Count + items.Count);
            all.AddRange(Stale);
            if (!ReferenceEquals(items, Stale)) all.AddRange(items);
            Stale.Clear();
            items.Clear();
            foreach (var e in all)
            {
                bool erased = false;
                try { erased = tm.EraseTransient(e, new IntegerCollection()); } catch { }
                if (erased) { try { e.Dispose(); } catch { } }
                else if (Stale.Count < MaxStale) Stale.Add(e);
                else if (!_staleReported)   // 무한히 쌓이지 않게(넘치면 해제하지 않은 채 놓아 둠) — 한 번 알림
                {
                    _staleReported = true;
                    Guard.Report("근거 표시 지우기", new InvalidOperationException($"지우지 못한 임시 그래픽이 {MaxStale}개를 넘었습니다"));
                }
            }
        }

        private static void AddTo(List<Entity> items, Entity e, int order) => Put(items, e, TransientDrawingMode.DirectTopmost, order);

        /// 올라간 것만 목록에 넣는다(못 올린 것은 바로 해제) — 지우기 목록에 영영 못 지우는 객체가 쌓이지 않게
        private static void Put(List<Entity> items, Entity e, TransientDrawingMode mode, int order)
        {
            bool ok = false;
            try { ok = TransientManager.CurrentTransientManager.AddTransient(e, mode, order, new IntegerCollection()); }
            finally { if (ok) items.Add(e); else e.Dispose(); }
        }

        /// 요소 hover 강조: 상태 색(충족 파랑·미충족 빨강·확인 필요 주황)의 굵은 외곽선 + 닫힌 도형은 반투명 채우기.
        /// pts는 도면 단위(추출 스냅샷 좌표). null이면 지우기만.
        public static void ElementHover(Document doc, IReadOnlyList<double[]>? pts, bool closed, string status, IReadOnlyList<double>? bulges = null)
        {
            ClearList(ElemItems);
            if (pts == null || pts.Count == 0) return;
            double upp = UnitsPerPixel(doc);
            var col = StatusColor(status);
            if (pts.Count == 1)
            {
                var c = new Circle(new Point3d(pts[0][0], pts[0][1], 0), Vector3d.ZAxis, 9 * upp) { Color = col };
                AddTo(ElemItems, c, 130);
                return;
            }
            var pl = new Polyline();
            // 호(ARC·CIRCLE·폴리선 호)는 bulge로 곡선 그대로 강조
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, new Point2d(pts[i][0], pts[i][1]), bulges != null && i < bulges.Count ? bulges[i] : 0, 0, 0);
            pl.Closed = closed;
            pl.ConstantWidth = 4 * upp;
            pl.Color = col;
            pl.Transparency = new Transparency((byte)150);
            AddTo(ElemItems, pl, 130);
            if (closed && pts.Count >= 3)
            {
                try
                {
                    var h = new Hatch();
                    h.SetDatabaseDefaults(doc.Database);
                    h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                    var verts = new Point2dCollection();
                    var hb = new DoubleCollection();
                    foreach (var p in pts) { verts.Add(new Point2d(p[0], p[1])); hb.Add(0); }
                    if (pts[0][0] != pts[^1][0] || pts[0][1] != pts[^1][1]) { verts.Add(new Point2d(pts[0][0], pts[0][1])); hb.Add(0); }
                    h.AppendLoop(HatchLoopTypes.Default, verts, hb);
                    h.EvaluateHatch(true);
                    h.Color = col;
                    h.Transparency = new Transparency((byte)70);   // 반투명 채우기
                    AddTo(ElemItems, h, 129);
                }
                catch { /* 채우기 실패 시 외곽선만 */ }
            }
        }

        public static void ClearDiff() => ClearList(DiffItems);

        // ── 실도면 연결: 역할 보기 ─────────────────────────────────────────────
        private static readonly List<Entity> RoleItems = new();
        public static bool RolesShown => RoleItems.Count > 0;
        public static void ClearRoles() => ClearList(RoleItems);

        /// 엔진이 판정에 쓴 객체(요소 판정의 element_id·role)를 역할별 색으로 표시한다. 좌표는 추출 스냅샷(도면 단위).
        /// 역할 색은 판정 색(빨강·주황·파랑)과 겹치지 않게: 선체 흰 잉크 · 장애물 연보라 · 충돌격벽 청록 · 구상선수 회색 · 조종위치 연두. 반환: 역할별 개수.
        public static Dictionary<string, int> ShowRoles(Document doc, RawDrawing raw, CheckResponse resp)
        {
            ClearRoles();
            var counts = new Dictionary<string, int>();
            var role = new Dictionary<string, string>();
            foreach (var r in resp.Results)
                foreach (var e in r.Elements)
                    role[e.ElementId] = e.Role;
            double upp = UnitsPerPixel(doc);
            foreach (var en in raw.Entities)
            {
                // 엔진 요소 판정에 나온 역할이 우선(측면도 영역 거르기 반영), 없으면 객체 지정·GA_ 레이어 역할
                var ro = role.TryGetValue(en.Handle, out var er) ? er
                       : en.Role ?? (Contract.LayerRoles.TryGetValue(en.Layer, out var lr) ? lr : null);
                if (ro == null || en.Points.Count == 0) continue;
                counts[ro] = counts.TryGetValue(ro, out var n) ? n + 1 : 1;
                var col = Ac(ro switch
                {
                    "hull" => Theme.RoleHull, "obstruction" => Theme.RoleObstruction, "collision_bhd" => Theme.RoleBulkhead,
                    "bulb" => Theme.RoleBulb, "eye" => Theme.RoleEye, _ => Theme.Faint,
                });
                if (en.Points.Count == 1)
                {
                    AddTo(RoleItems, new Circle(new Point3d(en.Points[0][0], en.Points[0][1], 0), Vector3d.ZAxis, 8 * upp) { Color = col }, 125);
                    continue;
                }
                var pl = new Polyline();
                for (int i = 0; i < en.Points.Count; i++)
                    pl.AddVertexAt(i, new Point2d(en.Points[i][0], en.Points[i][1]), en.Bulges != null && i < en.Bulges.Count ? en.Bulges[i] : 0, 0, 0);
                pl.Closed = en.Closed == true;
                pl.ConstantWidth = 2.5 * upp;
                pl.Color = col;
                AddTo(RoleItems, pl, 125);
            }
            return counts;
        }

        /// 버전 비교 결과를 도면 위에 임시 표시(도면은 그대로): 추가 초록, 삭제 빨강 파선, 변경 이전 노랑 점선·이후 노랑 실선, 번호 표식.
        public static void ShowDiff(Document doc, CompareResponse? d)
        {
            ClearList(DiffItems);
            if (d == null || d.UnitsScale is not double scale || scale <= 0) return;
            double upp = UnitsPerPixel(doc);
            var green = Ac(Theme.DiffAdded);
            var red = Ac(Theme.DiffRemoved);
            var yellow = Ac(Theme.DiffModified);
            var a = new Dictionary<string, GeomDto>();
            foreach (var g in d.Geometry.A) a[g.Handle] = g;
            var b = new Dictionary<string, GeomDto>();
            foreach (var g in d.Geometry.B) b[g.Handle] = g;
            var style = ObjectId.Null;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                style = DrawingProps.LabelStyleId(tr, doc.Database);
                tr.Commit();
            }

            void Path(GeomDto g, AcColor col, double widthPx, double dashPx)
            {
                var pts = new List<Point2d>();
                foreach (var p in g.Points) pts.Add(new Point2d(p[0] / scale, p[1] / scale));
                if (pts.Count == 1)
                {
                    AddTo(DiffItems, new Circle(new Point3d(pts[0].X, pts[0].Y, 0), Vector3d.ZAxis, 7 * upp) { Color = col }, 131);
                    return;
                }
                if (dashPx <= 0)
                {
                    var pl = new Polyline();
                    for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, pts[i], 0, 0, 0);
                    pl.ConstantWidth = widthPx * upp;
                    pl.Color = col;
                    AddTo(DiffItems, pl, 131);
                    return;
                }
                // 파선·점선: 선분을 직접 잘라 그린다(임시 객체는 선 종류 로드에 기대지 않음)
                double dash = dashPx * upp, gap = dashPx * upp * 0.8;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var v = pts[i + 1] - pts[i];
                    double len = v.Length;
                    if (len <= 0) continue;
                    var u = v / len;
                    // 아주 확대한 화면에서 긴 선이면 조각이 수십만 개가 될 수 있다 → 선분당 400조각까지(넘으면 파선 간격을 늘림)
                    double dl = dash, gl = gap, step = dl + gl;   // 이 선분에서만 쓰는 간격
                    if (!(step > 0) || double.IsInfinity(step)) { dl = len; gl = 0; step = len; }
                    if (len / step > 400) { double k = len / step / 400; dl *= k; gl *= k; step = dl + gl; }
                    for (double t = 0; t < len; t += step)
                    {
                        var s0 = pts[i] + u * t;
                        var s1 = pts[i] + u * Math.Min(t + dl, len);
                        var seg = new Polyline();
                        seg.AddVertexAt(0, s0, 0, 0, 0);
                        seg.AddVertexAt(1, s1, 0, 0, 0);
                        seg.ConstantWidth = widthPx * upp;
                        seg.Color = col;
                        AddTo(DiffItems, seg, 131);
                    }
                }
            }

            foreach (var c in d.Changes)
            {
                if (c.ElementId == null) continue;
                AcColor col = c.Kind switch { "added" => green, "removed" => red, _ => yellow };
                foreach (var h in c.Handles)   // 묶인 블록이면 하위 객체 전부
                {
                    if (c.Kind == "added" && b.TryGetValue(h, out var gb)) Path(gb, col, 3, 0);
                    else if (c.Kind == "removed" && a.TryGetValue(h, out var ga)) Path(ga, col, 3, 10);
                    else if (c.Kind == "modified")
                    {
                        if (a.TryGetValue(h, out var g0)) Path(g0, col, 1.5, 3);
                        if (b.TryGetValue(h, out var g1)) Path(g1, col, 3, 0);
                    }
                }
                if (c.Center == null || c.Bbox == null) continue;
                // 번호 표식: 추가 ○, 삭제 □, 변경 ◇ (색 + 모양)
                double r = 11 * upp;
                var at = new Point2d(c.Center[0] / scale, c.Bbox[3] / scale + 2.4 * r);
                var mk = new Polyline();
                if (c.Kind == "added")
                {
                    AddTo(DiffItems, new Circle(new Point3d(at.X, at.Y, 0), Vector3d.ZAxis, r) { Color = col }, 132);
                }
                else
                {
                    var corners = c.Kind == "removed"
                        ? new[] { new Point2d(at.X - r, at.Y - r), new Point2d(at.X + r, at.Y - r), new Point2d(at.X + r, at.Y + r), new Point2d(at.X - r, at.Y + r) }
                        : new[] { new Point2d(at.X, at.Y - r * 1.25), new Point2d(at.X + r * 1.25, at.Y), new Point2d(at.X, at.Y + r * 1.25), new Point2d(at.X - r * 1.25, at.Y) };
                    for (int i = 0; i < corners.Length; i++) mk.AddVertexAt(i, corners[i], 0, 0, 0);
                    mk.Closed = true;
                    mk.Color = col;
                    AddTo(DiffItems, mk, 132);
                }
                var t = new MText();
                t.SetDatabaseDefaults(doc.Database);
                if (!style.IsNull) t.TextStyleId = style;
                t.Location = new Point3d(at.X, at.Y, 0);
                t.Attachment = AttachmentPoint.MiddleCenter;
                t.TextHeight = 11 * upp;
                t.Contents = "{\\fMalgun Gothic|b1|i0|c129|p34;" + c.Number + "}";
                t.Color = col;
                AddTo(DiffItems, t, 133);
            }
        }

        /// 비교 목록에서 고른 변경 위치로 도면 화면 이동(m → 도면 단위).
        public static void FocusBox(Document doc, double[] bbox, double scale)
        {
            if (bbox.Length < 4 || scale <= 0) return;
            double x0 = bbox[0] / scale, y0 = bbox[1] / scale, x1 = bbox[2] / scale, y1 = bbox[3] / scale;
            double w = Math.Max(x1 - x0, 1), h = Math.Max(y1 - y0, 1);
            using var view = doc.Editor.GetCurrentView();
            double aspect = view.Width / Math.Max(view.Height, 1e-9);
            view.CenterPoint = new Point2d((x0 + x1) / 2, (y0 + y1) / 2);
            double vw = w * 4, vh = h * 4;
            if (vw / vh > aspect) { view.Width = vw; view.Height = vw / aspect; } else { view.Height = vh; view.Width = vh * aspect; }
            doc.Editor.SetCurrentView(view);
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
                foreach (var h in r.Targets.Select(Extractor.BaseHandle).Distinct())
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

        private static void Add(Entity e) => Put(Items, e, TransientDrawingMode.DirectTopmost, 128);
    }
}
