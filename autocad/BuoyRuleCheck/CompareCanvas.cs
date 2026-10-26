// 버전 비교 그림: 엔진 /v1/compare가 준 두 버전 형상(m 좌표)을 그린다. 판단·diff 계산은 하지 않는다.
//   Before/After : 나란히 보기의 왼쪽(이전)·오른쪽(이후) — 두 칸이 같은 ViewState를 공유해 같이 확대·이동
//   Overlay      : 이전 위에 이후를 슬라이더 투명도로 겹쳐 보기
//   Diff         : 차이만 — 추가 초록(빗금), 삭제 빨강(파선), 변경 노랑(이전 점선 + 이후 실선), 바뀌지 않은 형상은 회색 잉크
// 흑백 인쇄에서도 구분되게 색과 함께 선 모양·빗금·표식 모양(추가 ○, 삭제 □, 변경 ◇)과 기호(+ − ~)를 쓴다.
// 모양은 디자인 보드 07(화면)·08(A4 종이) 토큰을 따른다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    public enum CanvasMode { Before, After, Overlay, Diff }

    /// 화면 보기 상태(중심 m, 배율 px/m). 나란히 보기의 두 칸이 하나를 공유한다.
    public sealed class ViewState
    {
        public double Cx, Cy, Scale = 1;
        public event Action? Changed;
        public void Notify() => Changed?.Invoke();

        public void Fit(double[] box, Size size, double margin = 0.08)
        {
            if (box.Length < 4 || size.Width <= 0 || size.Height <= 0) return;
            double w = Math.Max(box[2] - box[0], 1e-6), h = Math.Max(box[3] - box[1], 1e-6);
            Cx = (box[0] + box[2]) / 2;
            Cy = (box[1] + box[3]) / 2;
            Scale = Math.Min(size.Width * (1 - 2 * margin) / w, size.Height * (1 - 2 * margin) / h);
            Notify();
        }
    }

    /// 그림 잉크(화면 = 다크, 종이 = 라이트 토큰). 판정 색이 아니라 비교 색(추가·삭제·변경)과 흑백 잉크만.
    public sealed class PaintStyle
    {
        public bool Print;
        public Color Back, Base, Text, Faint, MarkerFill, Added, Removed, Modified, RemovedNumber, Focus;
        public float Px = 1f;   // 선 두께 배율(인쇄 해상도)

        /// 디자인 보드 07: 그림 바탕 #212830, 바뀌지 않은 형상 #7D8796, 표식 속 #17191D
        public static PaintStyle Screen => new()
        {
            Back = Color.FromArgb(0x21, 0x28, 0x30), Base = Color.FromArgb(0x7D, 0x87, 0x96), Text = Theme.Text,
            Faint = Color.FromArgb(0x4A, 0x53, 0x61), MarkerFill = Theme.Bg,
            Added = Theme.DiffAdded, Removed = Theme.DiffRemoved, Modified = Theme.DiffModified, RemovedNumber = Theme.FailInk,
            Focus = Color.FromArgb(140, Theme.Text),
        };

        /// 디자인 보드 08(A4 흰 종이): 바뀌지 않은 형상 mid, 가이드 lo, 표식·글자 hi. 비교 색은 종이용으로 짙게.
        public static PaintStyle Paper(float px) => new()
        {
            Print = true, Back = Theme.Paper.Plate, Base = Theme.Paper.Mid, Text = Theme.Paper.Hi, Faint = Theme.Paper.Lo,
            MarkerFill = Theme.Paper.Plate,
            Added = Color.FromArgb(0x1F, 0x8A, 0x4C), Removed = Theme.Paper.Fail, Modified = Color.FromArgb(0xA0, 0x7A, 0x00),
            RemovedNumber = Theme.Paper.Hi, Focus = Theme.Paper.Edge, Px = px,
        };

        public Color Kind(string kind) => kind switch { "added" => Added, "removed" => Removed, _ => Modified };
    }

    public static class CanvasPainter
    {
        /// 화면 변경 색(목록·표식 공용)
        public static Color KindColor(string kind) => kind switch
        {
            "added" => Theme.DiffAdded,
            "removed" => Theme.DiffRemoved,
            _ => Theme.DiffModified,
        };

        public static string KindGlyph(string kind) => kind switch { "added" => "+", "removed" => "−", _ => "~" };
        public static string KindKo(string kind) => kind switch { "added" => "추가", "removed" => "삭제", "modified" => "변경", "ship" => "선박 정보", _ => "흘수 조건" };

        public static PointF ToScreen(ViewState v, RectangleF area, double x, double y) =>
            new((float)(area.X + area.Width / 2 + (x - v.Cx) * v.Scale), (float)(area.Y + area.Height / 2 - (y - v.Cy) * v.Scale));

        public static (double x, double y) ToModel(ViewState v, RectangleF area, PointF p) =>
            (v.Cx + (p.X - area.X - area.Width / 2) / v.Scale, v.Cy - (p.Y - area.Y - area.Height / 2) / v.Scale);

        public static void Paint(Graphics g, RectangleF area, CompareResponse d, CanvasMode mode, ViewState v,
                                 PaintStyle st, float overlayOpacity, int? highlight, IList<(ChangeDto c, RectangleF r)>? markersOut = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(st.Back)) g.FillRectangle(bg, area);
            var clip = g.Clip;
            g.SetClip(area);
            var changed = d.Changes.SelectMany(c => c.Handles.Select(h => (h, c))).GroupBy(x => x.h).ToDictionary(x => x.Key, x => x.First().c);
            // handle이 겹친 도형이 와도(엔진 버그·손상 도면) 죽지 않게 첫 것만
            var a = d.Geometry.A.GroupBy(x => x.Handle).ToDictionary(x => x.Key, x => x.First());
            var b = d.Geometry.B.GroupBy(x => x.Handle).ToDictionary(x => x.Key, x => x.First());

            switch (mode)
            {
                case CanvasMode.Before:
                case CanvasMode.After:
                {
                    bool before = mode == CanvasMode.Before;
                    var geo = before ? d.Geometry.A : d.Geometry.B;
                    foreach (var gm in geo)
                    {
                        if (changed.TryGetValue(gm.Handle, out var c) && (before ? c.Kind != "added" : c.Kind != "removed"))
                            DrawChanged(g, area, v, st, gm, c.Kind, before ? "before" : "after");
                        else
                            DrawBase(g, area, v, st, gm, st.Base);
                    }
                    break;
                }
                case CanvasMode.Overlay:
                {
                    // 이전 = 흐린 잉크, 이후 = 밝은 잉크(투명도 막대) — 판정 색을 쓰지 않는다
                    var colA = st.Print ? st.Base : Color.FromArgb(0x5C, 0x66, 0x75);
                    var colB = Color.FromArgb((int)Math.Round(255 * Math.Clamp(overlayOpacity, 0f, 1f)), st.Print ? st.Text : Theme.Text);
                    foreach (var gm in d.Geometry.A) DrawBase(g, area, v, st, gm, colA);
                    foreach (var gm in d.Geometry.B) DrawBase(g, area, v, st, gm, colB, 1.4f);
                    break;
                }
                default:   // Diff
                {
                    foreach (var gm in d.Geometry.B)
                        if (!changed.ContainsKey(gm.Handle)) DrawBase(g, area, v, st, gm, st.Base);
                    foreach (var c in d.Changes)
                    {
                        foreach (var h in c.Handles)   // 묶인 블록이면 하위 객체 전부
                        {
                            if (c.Kind == "added" && b.TryGetValue(h, out var gb)) DrawChanged(g, area, v, st, gb, "added", "after");
                            else if (c.Kind == "removed" && a.TryGetValue(h, out var ga)) DrawChanged(g, area, v, st, ga, "removed", "before");
                            else if (c.Kind == "modified")
                            {
                                if (a.TryGetValue(h, out var g0)) DrawChanged(g, area, v, st, g0, "modified", "before");
                                if (b.TryGetValue(h, out var g1)) DrawChanged(g, area, v, st, g1, "modified", "after");
                            }
                        }
                    }
                    break;
                }
            }

            // 고른 변경: 범위 둘레에 점선 둥근 틀(디자인 보드 07)
            var hlc = highlight is int hn ? d.Changes.FirstOrDefault(c => c.Number == hn) : null;
            if (hlc?.Bbox is double[] hb)
            {
                var p0 = ToScreen(v, area, hb[0], hb[3]);
                var p1 = ToScreen(v, area, hb[2], hb[1]);
                float pad = 12 * st.Px;
                var fr = RectangleF.FromLTRB(Math.Min(p0.X, p1.X) - pad, Math.Min(p0.Y, p1.Y) - pad, Math.Max(p0.X, p1.X) + pad, Math.Max(p0.Y, p1.Y) + pad);
                using var path = Theme.RoundRect(fr, 6 * st.Px);
                using var fp = new Pen(st.Focus, 1f * st.Px) { DashStyle = DashStyle.Custom, DashPattern = new[] { 4f, 3f } };
                g.DrawPath(fp, path);
            }

            // 번호 표식: 형상 위에. 겹치면 위로 비켜 놓고, 비켰을 때만 가는 지시선으로 잇는다
            var placed = new List<RectangleF>();
            float r = 11 * st.Px;
            using var font = new Font(Theme.MonoName, 11f * st.Px, FontStyle.Bold, GraphicsUnit.Pixel);   // 번호 11 px(디자인 보드 07)
            foreach (var c in d.Changes.Where(x => x.Bbox != null && x.Center != null))
            {
                if (mode == CanvasMode.Before && c.Kind == "added") continue;
                if (mode == CanvasMode.After && c.Kind == "removed") continue;
                var top = ToScreen(v, area, c.Center![0], c.Bbox![3]);
                var anchor = ToScreen(v, area, c.Center[0], c.Bbox[3]);
                var rect = new RectangleF(top.X - r, top.Y - 2 * r - 10 * st.Px, 2 * r, 2 * r);
                bool moved = false;
                for (int guard = 0; guard < 20 && placed.Any(p => p.IntersectsWith(RectangleF.Inflate(rect, 2, 2))); guard++)
                {
                    rect.Y -= 2 * r + 4;
                    moved = true;
                }
                placed.Add(rect);
                markersOut?.Add((c, rect));
                DrawMarker(g, st, c, rect, moved ? anchor : null, font, c.Number == highlight);
            }
            g.Clip = clip;
        }

        private static PointF[] Pts(GeomDto gm, RectangleF area, ViewState v)
        {
            var pts = gm.Points.Select(p => ToScreen(v, area, p[0], p[1])).ToList();
            if (gm.Closed && pts.Count > 2) pts.Add(pts[0]);
            return pts.ToArray();
        }

        private static void DrawBase(Graphics g, RectangleF area, ViewState v, PaintStyle st, GeomDto gm, Color col, float width = 1.2f)
        {
            var pts = Pts(gm, area, v);
            using var pen = new Pen(col, width * st.Px * (st.Print ? 0.75f : 1f));
            if (pts.Length == 1) { float s = 3 * st.Px; g.DrawEllipse(pen, pts[0].X - s, pts[0].Y - s, 2 * s, 2 * s); return; }
            if (pts.Length >= 2) g.DrawLines(pen, pts);
        }

        /// stage: before(이전 형상) / after(이후 형상). 변경은 이전 점선(흐리게) · 이후 실선(옅은 채움), 삭제는 파선, 추가는 빗금 + 실선.
        private static void DrawChanged(Graphics g, RectangleF area, ViewState v, PaintStyle st, GeomDto gm, string kind, string stage)
        {
            var pts = Pts(gm, area, v);
            var col = st.Kind(kind);
            bool closed = pts.Length > 3 && pts[0] == pts[^1];
            if (kind == "modified" && stage == "before")
            {
                using var dot = new Pen(Color.FromArgb(180, col), 1.4f * st.Px) { DashStyle = DashStyle.Custom, DashPattern = new[] { 1.4f, 2.2f } };
                Stroke(g, dot, pts, st);
                return;
            }
            if (kind == "removed")
            {
                using var dash = new Pen(col, 1.8f * st.Px) { DashStyle = DashStyle.Custom, DashPattern = new[] { 3.3f, 2.2f } };
                Stroke(g, dash, pts, st);
                return;
            }
            if (closed)
            {
                if (kind == "added") Hatch(g, pts, col, st);
                else using (var fill = new SolidBrush(Color.FromArgb(26, col))) g.FillPolygon(fill, pts);
            }
            using var pen = new Pen(col, (kind == "added" ? 1.8f : 2f) * st.Px);
            Stroke(g, pen, pts, st);
        }

        private static void Stroke(Graphics g, Pen pen, PointF[] pts, PaintStyle st)
        {
            if (pts.Length == 1) { float s = 5 * st.Px; g.DrawEllipse(pen, pts[0].X - s, pts[0].Y - s, 2 * s, 2 * s); return; }
            if (pts.Length >= 2) g.DrawLines(pen, pts);
        }

        /// 45° 빗금(간격 7 px) — 흑백 인쇄에서도 '추가'로 읽히게
        private static void Hatch(Graphics g, PointF[] pts, Color col, PaintStyle st)
        {
            using var path = new GraphicsPath();
            path.AddPolygon(pts);
            var bb = path.GetBounds();
            var old = g.Clip;
            g.SetClip(path, CombineMode.Intersect);
            using var hp = new Pen(col, 1.6f * st.Px);
            float step = 7 * st.Px;
            for (float x = bb.Left - bb.Height; x < bb.Right; x += step)
                g.DrawLine(hp, x, bb.Bottom, x + bb.Height, bb.Top);
            g.Clip = old;
        }

        /// 번호 표식: ○ 추가 · □ 삭제 · ◇ 변경. 속은 바탕색, 테·번호는 비교 색(종이는 검정).
        public static void DrawMarker(Graphics g, PaintStyle st, ChangeDto c, RectangleF rect, PointF? anchor, Font font, bool hl)
        {
            var col = st.Print ? st.Text : st.Kind(c.Kind);
            if (anchor is PointF an)
                using (var lead = new Pen(Color.FromArgb(120, col), 1f * st.Px)) g.DrawLine(lead, rect.X + rect.Width / 2, rect.Bottom, an.X, an.Y);
            using var path = MarkerPath(c.Kind, rect);
            using (var fill = new SolidBrush(st.MarkerFill)) g.FillPath(fill, path);
            using (var pen = new Pen(col, (hl ? 2.4f : 1.6f) * st.Px)) g.DrawPath(pen, path);
            var txt = c.Number.ToString();
            var tc = st.Print ? st.Text : c.Kind == "removed" ? st.RemovedNumber : col;
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var tb = new SolidBrush(tc);
            g.DrawString(txt, font, tb, new RectangleF(rect.X, rect.Y + 0.5f * st.Px, rect.Width, rect.Height), sf);
        }

        public static GraphicsPath MarkerPath(string kind, RectangleF r)
        {
            var p = new GraphicsPath();
            switch (kind)
            {
                case "removed": p.AddRectangle(RectangleF.Inflate(r, -1, -1)); break;
                case "modified":
                    p.AddPolygon(new[] { new PointF(r.X + r.Width / 2, r.Y - 1), new PointF(r.Right + 1, r.Y + r.Height / 2),
                                         new PointF(r.X + r.Width / 2, r.Bottom + 1), new PointF(r.X - 1, r.Y + r.Height / 2) });
                    break;
                default: p.AddEllipse(r); break;
            }
            return p;
        }

        /// 범례(그림 알약·A4 공용): 견본 + 표식 모양 + 기호. 화면 '○ 추가 +' · 종이 '○ 추가 (+) 실선·빗금'
        public static void PaintLegend(Graphics g, RectangleF area, PaintStyle st)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = st.Px;
            using var font = new Font(Theme.FontName, 11f * s, GraphicsUnit.Pixel);   // 범례 글자 11 px
            float x = area.X, y = area.Y + area.Height / 2;
            foreach (var kind in new[] { "added", "removed", "modified" })
            {
                var col = st.Kind(kind);
                var sw = new RectangleF(x, y - 6 * s, 22 * s, 12 * s);
                switch (kind)
                {
                    case "added":
                        Hatch(g, new[] { new PointF(sw.Left, sw.Top), new PointF(sw.Right, sw.Top), new PointF(sw.Right, sw.Bottom), new PointF(sw.Left, sw.Bottom), new PointF(sw.Left, sw.Top) }, col, st.Print ? st : PaintStyle.Screen);
                        using (var p = new Pen(col, 1.4f * s)) g.DrawRectangle(p, sw.X, sw.Y, sw.Width, sw.Height);
                        break;
                    case "removed":
                        using (var p = new Pen(col, 1.4f * s) { DashStyle = DashStyle.Custom, DashPattern = new[] { 3f, 2f } }) g.DrawRectangle(p, sw.X, sw.Y, sw.Width, sw.Height);
                        break;
                    default:
                        using (var p = new Pen(Color.FromArgb(180, col), 1.2f * s) { DashStyle = DashStyle.Custom, DashPattern = new[] { 1.4f, 2f } }) g.DrawLine(p, x, y, x + 16 * s, y);
                        using (var p = new Pen(col, 2.4f * s)) g.DrawLine(p, x + 20 * s, y, x + 34 * s, y);
                        sw.Width = 34 * s;
                        break;
                }
                x = sw.Right + 7 * s;
                string label = (kind, st.Print) switch
                {
                    ("added", false) => "○ 추가 +",
                    ("removed", false) => "□ 삭제 −",
                    (_, false) => "◇ 변경 ~ (이전 점선 · 이후 실선)",
                    ("added", true) => "○ 추가 (+) 실선·빗금",
                    ("removed", true) => "□ 삭제 (−) 파선",
                    _ => "◇ 변경 (~) 이전 점선 · 이후 굵은 실선",
                };
                using var tb = new SolidBrush(st.Print ? st.Text : Theme.Muted);
                var sz = g.MeasureString(label, font);
                g.DrawString(label, font, tb, x, y - sz.Height / 2f);
                x += sz.Width + 16 * s;
            }
        }
    }

    /// 화면용 비교 캔버스. 휠 확대, 드래그 이동, 더블클릭 전체 보기, 표식 클릭·hover.
    public sealed class CompareCanvas : Control
    {
        public CompareResponse? Data { get; private set; }
        public CanvasMode Mode { get; set; } = CanvasMode.Diff;
        public float OverlayOpacity { get; set; } = 0.6f;
        public int? Highlight { get; set; }
        public ViewState View { get; private set; } = new();
        public string Caption { get; set; } = "";

        public event Action<ChangeDto>? MarkerClicked;
        public event Action<ChangeDto?>? ChangeHovered;

        private readonly List<(ChangeDto c, RectangleF r)> _markers = new();
        private HitIndex? _index;
        private Dictionary<string, ChangeDto> _byHandle = new();
        private Point _down;
        private bool _dragging;
        private (double cx, double cy) _downCenter;
        private readonly Stopwatch _throttle = Stopwatch.StartNew();
        private readonly ToolTip _tip = new() { InitialDelay = 150, ReshowDelay = 50, AutoPopDelay = 15000 };
        private ChangeDto? _hover;

        public CompareCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = PaintStyle.Screen.Back;
        }

        /// data가 null이면(비교 실패) 이전 그림을 지우고 안내 문구만 보인다 — 이전 결과가 남아 보이지 않게.
        public void Bind(CompareResponse? data, ViewState shared)
        {
            View.Changed -= Invalidate;
            View = shared;
            View.Changed += Invalidate;
            Data = data;
            _markers.Clear();
            _hover = null;
            _tip.SetToolTip(this, "");
            if (data == null) { _byHandle = new(); _index = null; Invalidate(); return; }
            _byHandle = data.Changes.SelectMany(c => c.Handles.Select(h => (h, c))).GroupBy(x => x.h).ToDictionary(g => g.Key, g => g.First().c);
            var shapes = data.Geometry.A.Concat(data.Geometry.B)
                .Where(gm => _byHandle.ContainsKey(gm.Handle))
                .Select(gm => (gm.Handle, (IReadOnlyList<double[]>)gm.Points, gm.Closed));
            _index = new HitIndex(shapes);
            Invalidate();
        }

        public void FitAll()
        {
            if (Data?.Bounds is double[] b) View.Fit(b, ClientSize);
        }

        public void FocusChange(ChangeDto c)
        {
            if (c.Bbox is not double[] b) return;
            double w = Math.Max(b[2] - b[0], 1), h = Math.Max(b[3] - b[1], 1);
            View.Fit(new[] { b[0] - w * 1.5, b[1] - h * 1.5, b[2] + w * 1.5, b[3] + h * 1.5 }, ClientSize);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var st = PaintStyle.Screen;
            if (Data == null)
            {
                e.Graphics.Clear(st.Back);
                TextRenderer.DrawText(e.Graphics, "비교할 버전을 고르세요", Theme.Body, ClientRectangle, Theme.Muted,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            _markers.Clear();
            try
            {
                CanvasPainter.Paint(e.Graphics, ClientRectangle, Data, Mode, View, st, OverlayOpacity, Highlight, _markers);
            }
            catch (Exception ex)   // 극단 확대 등으로 GDI+ 오버플로가 나도 창이 죽지 않게
            {
                e.Graphics.Clear(st.Back);
                TextRenderer.DrawText(e.Graphics, "그림을 그리지 못했습니다 — 두 번 클릭하면 전체 보기 (" + ex.GetType().Name + ")", Theme.Body,
                                      ClientRectangle, Theme.Warn, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (!string.IsNullOrEmpty(Caption))
            {
                var sz = TextRenderer.MeasureText(Caption, Theme.Small);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = Theme.RoundRect(new RectangleF(12, 12, sz.Width + 18, sz.Height + 8), (sz.Height + 8) / 2f);
                using (var bb = new SolidBrush(Theme.Bg)) e.Graphics.FillPath(bb, path);
                TextRenderer.DrawText(e.Graphics, Caption, Theme.Small, new Point(21, 16), Theme.Muted);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Data == null) return;
            var (mx, my) = CanvasPainter.ToModel(View, ClientRectangle, e.Location);
            double f = e.Delta > 0 ? 1.2 : 1 / 1.2;
            // 확대 범위 제한: 전체 보기의 1/20 ~ 2000배 (GDI+ 좌표 넘침 방지)
            if (Data.Bounds is double[] b && b.Length >= 4 && ClientSize.Width > 0 && ClientSize.Height > 0)
            {
                double fit = Math.Min(ClientSize.Width / Math.Max(b[2] - b[0], 1e-6), ClientSize.Height / Math.Max(b[3] - b[1], 1e-6));
                double next = View.Scale * f;
                if (next < fit * 0.05 || next > fit * 2000 || double.IsNaN(next)) return;
            }
            View.Scale *= f;
            View.Cx = mx - (mx - View.Cx) / f;
            View.Cy = my - (my - View.Cy) / f;
            View.Notify();
        }

        /// 마우스가 올라오면 포커스를 받아 휠 확대가 바로 먹게 한다(창이 활성일 때만).
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (FindForm() is Form f && f == Form.ActiveForm && !Focused) Focus();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _down = e.Location;
            _downCenter = (View.Cx, View.Cy);
            _dragging = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Data == null) return;
            if (e.Button is MouseButtons.Left or MouseButtons.Middle)
            {
                int dx = e.X - _down.X, dy = e.Y - _down.Y;
                if (_dragging || Math.Abs(dx) + Math.Abs(dy) > 4)
                {
                    _dragging = true;
                    View.Cx = _downCenter.cx - dx / View.Scale;
                    View.Cy = _downCenter.cy + dy / View.Scale;
                    View.Notify();
                }
                return;
            }
            if (_throttle.ElapsedMilliseconds < 30) return;   // hover는 30 ms에 한 번만
            _throttle.Restart();
            var hit = HitAt(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                ChangeHovered?.Invoke(hit);
                _tip.SetToolTip(this, hit == null ? "" : $"{hit.Number}. {UiText.ChangeTitle(hit)}\n이전: {hit.BeforeKo}\n이후: {hit.AfterKo}"
                    + (UiText.ChangeNote(hit) is { Length: > 0 } note ? "\n" + note : ""));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (Data == null || _dragging) { _dragging = false; return; }
            var hit = HitAt(e.Location);
            if (hit != null) MarkerClicked?.Invoke(hit);
        }

        protected override void OnDoubleClick(EventArgs e) => FitAll();

        private ChangeDto? HitAt(Point p)
        {
            foreach (var (c, r) in _markers)
                if (RectangleF.Inflate(r, 3, 3).Contains(p)) return c;
            if (_index == null) return null;
            var (x, y) = CanvasPainter.ToModel(View, ClientRectangle, p);
            var h = _index.Hit(x, y, 6 / View.Scale);
            return h != null && _byHandle.TryGetValue(h, out var c2) ? c2 : null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { View.Changed -= Invalidate; _tip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
