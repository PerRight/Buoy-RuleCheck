// 오브 그리기(GDI+): 떠 있는 로고·팔레트·카드·미리보기 화면용. 도면 위 부표는 BuoyLayer(Transient)가 같은 OrbGeometry로 그린다.
// 상태별 속도·색(명세 5절)도 여기 한곳에 둔다 — 로고와 부표가 같은 규격을 쓰게.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    /// 그리는 모양: 점 색, 부표 등 색(없으면 등 없음), 등이 꺼졌으면 속 빈 고리, 수선 고리 색, 전체 투명도.
    public sealed class OrbLook
    {
        public Color Dots = Theme.Text;
        public Color? Lamp;
        public bool LampHollow;
        public Color SeaBack = Color.FromArgb(0x2E, 0x33, 0x3B);
        public Color SeaFront = Color.FromArgb(0x5C, 0x63, 0x6E);
        public double Opacity = 1;

        /// 로고·아이콘 잉크(점 #F4F6F9)
        public static readonly Color Ink = Color.FromArgb(0xF4, 0xF6, 0xF9);
    }

    public static class OrbRenderer
    {
        [ThreadStatic] private static List<OrbDot>? _dots;

        /// 상자 왼쪽 위 at에 geo.Size × scale 크기로 그린다.
        public static void Draw(Graphics g, OrbGeometry geo, double turn, PointF at, OrbLook look, float scale = 1f)
        {
            var dots = _dots ??= new List<OrbDot>(200);
            geo.Frame(turn, dots);
            var oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float S(double v) => (float)(v * scale);
            double op = Math.Clamp(look.Opacity, 0, 1);
            float c = S(geo.Center);

            if (geo.HasSea)
                using (var p = new Pen(Fade(look.SeaBack, op), 1f))
                    g.DrawEllipse(p, at.X + c - S(geo.SeaA), at.Y + S(geo.SeaY - geo.SeaB), S(2 * geo.SeaA), S(2 * geo.SeaB));

            if (geo.HasBody)
            {
                float r = S(geo.BodyR);
                var rect = new RectangleF(at.X + c - r, at.Y + c - r, 2 * r, 2 * r);
                using var path = new GraphicsPath();
                path.AddEllipse(rect);
                using var br = new PathGradientBrush(path)
                {
                    CenterPoint = new PointF(rect.X + rect.Width * 0.36f, rect.Y + rect.Height * 0.30f),
                    InterpolationColors = new ColorBlend
                    {
                        // 경계(0) → 중심(1): 바깥 3% → 42% 지점 5.4% → 빛 중심 12%
                        Positions = new[] { 0f, 0.58f, 1f },
                        Colors = new[]
                        {
                            Fade(look.Dots, geo.BodyOut * op),
                            Fade(look.Dots, geo.BodyIn * 0.45 * op),
                            Fade(look.Dots, geo.BodyIn * op),
                        },
                    },
                };
                g.FillEllipse(br, rect);
            }

            foreach (var d in dots)
            {
                if (d.Alpha <= 0.004) continue;
                float dd = Math.Max(S(d.D), 0.6f);
                using var b = new SolidBrush(Fade(look.Dots, d.Alpha * op));
                g.FillEllipse(b, at.X + S(d.X) - dd / 2, at.Y + S(d.Y) - dd / 2, dd, dd);
            }

            if (geo.HasSea)
            {
                var clip = g.Clip;
                g.SetClip(new RectangleF(at.X - 2, at.Y + S(geo.SeaY), S(geo.Size) + 4, S(geo.Size)), CombineMode.Intersect);
                using (var p = new Pen(Fade(look.SeaFront, op), 1f))
                    g.DrawEllipse(p, at.X + c - S(geo.SeaA), at.Y + S(geo.SeaY - geo.SeaB), S(2 * geo.SeaA), S(2 * geo.SeaB));
                g.Clip = clip;
            }

            if (look.Lamp is Color lamp)
            {
                float ld = S(geo.LampD);
                var lr = new RectangleF(at.X + S(geo.LampX) - ld / 2, at.Y + S(geo.LampY) - ld / 2, ld, ld);
                if (look.LampHollow) using (var p = new Pen(Fade(lamp, op), 1f)) g.DrawEllipse(p, lr);
                else using (var b = new SolidBrush(Fade(lamp, op))) g.FillEllipse(b, lr);
            }
            g.SmoothingMode = oldSmooth;
        }

        public static Color Fade(Color c, double alpha) =>
            Color.FromArgb((int)Math.Round(Math.Clamp(alpha, 0, 1) * 255 * (c.A / 255.0)), c.R, c.G, c.B);
    }

    /// 상태별 움직임·색 규격(명세 5절). Period 0 = 정지.
    public static class OrbStates
    {
        public const double GapFrom = 280, GapTo = 359;

        // ── 떠 있는 로고(판 52 px · 오브 36 px · 부표 등 색 = 전체 상태) ──
        public sealed record LogoState(string Key, double Period, Color? Lamp, bool Hollow, bool Gap, double Opacity, string Name);
        public static readonly LogoState Watching = new("watch", 8, Color.White, false, false, 1, "감시 중");
        public static readonly LogoState Checking = new("check", 1.4, Color.White, false, false, 1, "검사 중");
        public static readonly LogoState Fail = new("fail", 6, Theme.Fail, false, false, 1, "수정 소요");
        public static readonly LogoState Warn = new("warn", 8, Theme.Warn, false, true, 1, "확인 필요");
        public static readonly LogoState Pass = new("pass", 0, Theme.Pass, false, false, 1, "모두 충족");
        public static readonly LogoState Down = new("down", 0, Theme.Faint, true, false, 0.45, "연결 끊김");
        /// 엔진은 있는데 플러그인이 검사를 끝내지 못함 — 모양은 연결 끊김과 같고(판정하지 않음) 이름만 다르다.
        public static readonly LogoState Error = new("error", 0, Theme.Faint, true, false, 0.45, "검사하지 못함");
        /// 검사 중 표시는 최소 0.6 s 보여 준다
        public const double MinCheckSeconds = 0.6;

        /// 상태 키(fail / warn / pass / down / check / 그 밖 = 감시 중) → 로고·머리 오브 모습
        public static LogoState ForState(string key) => key switch
        {
            "fail" => Fail,
            "warn" => Warn,
            "pass" => Pass,
            "down" => Down,
            "error" => Error,
            "check" => Checking,
            _ => Watching,
        };

        // ── 도면 위 부표(30 px · 점 36 · 판정 색 점) ──
        public const int BuoySize = 30;
        public const double BuoyIdle = 5, BuoyRecheck = 1.2, BuoyWarn = 8;
        public const double HoverScale = 1.25, HoverMs = 160;
        public const double EditingOpacity = 0.35;
        public const double AppearMs = 700, VanishMs = 700, VanishScale = 1.8, AppearScale = 1.7;
        public const double RippleSeconds = 3.2, RippleScale = 2.4;
        public const int RippleCount = 2;
        public const double CardInMs = 160, CardGraceMs = 150;
        public const int CardWidth = 280, HitRadius = 16, ClusterPx = 28;
        /// 고정점 3 px → 지시선 16 px(45°) → 오브
        public const double AnchorDot = 3, LeaderLen = 16;
    }

    /// 동작 줄이기: 플러그인 설정(ui.json) 또는 Windows '애니메이션 표시' 꺼짐. 멈춘 자세, 상태 색은 그대로.
    public static class Motion
    {
        public static bool Reduced
        {
            get
            {
                if (UiState.Current.ReduceMotion) return true;
                try { return !System.Windows.SystemParameters.ClientAreaAnimation; } catch { return false; }
            }
        }
    }

    /// 애니메이션 박자: 화면에 보이는 오브들이 타이머 하나를 나눠 쓴다(약 30 fps, 동작 줄이기면 멈춤).
    public static class OrbClock
    {
        private static readonly Timer Timer = new() { Interval = 33 };
        private static readonly HashSet<Action<double>> Subs = new();
        private static long _last;

        static OrbClock()
        {
            Timer.Tick += (_, _) =>
            {
                long now = Environment.TickCount64;
                double dt = _last == 0 ? 0.033 : Math.Min((now - _last) / 1000.0, 0.1);
                _last = now;
                foreach (var s in new List<Action<double>>(Subs))
                {
                    try { s(dt); }
                    catch (Exception ex)
                    {
                        Subs.Remove(s);   // 닫힌 컨트롤 등 — 한 구독자 때문에 매 틱 예외 창이 뜨지 않게 빼낸다
                        Guard.Report("오브 박자", ex);   // 조용히 멈추지 않게 기록(도면 부표는 자기 박자 안에서 잡으므로 여기 오지 않음)
                    }
                }
                if (Subs.Count == 0) Timer.Stop();
            };
        }

        public static void Add(Action<double> tick) { Subs.Add(tick); if (!Timer.Enabled) { _last = 0; Timer.Start(); } }
        public static void Remove(Action<double> tick) { Subs.Remove(tick); if (Subs.Count == 0) Timer.Stop(); }
    }

    /// 자전하는 오브 하나(WinForms 컨트롤).
    public sealed class OrbView : Control
    {
        private OrbGeometry _geo;
        private readonly OrbMotion _motion;
        private readonly Action<double> _tick;
        public OrbLook Look { get; } = new();
        public float Bob { get; set; }

        public OrbView(OrbSpec spec, double period)
        {
            _geo = new OrbGeometry(spec);
            _motion = new OrbMotion(period);
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size((int)Math.Ceiling(spec.Size), (int)Math.Ceiling(spec.Size));
            // 돌거나 떠 있을 때만 다시 그린다(멈춘 오브가 30 fps로 그려지지 않게). 동작 줄이기면 멈춘 자세.
            _tick = dt =>
            {
                bool reduced = Motion.Reduced;
                _motion.Step(dt, reduced);
                if (!reduced && (!_motion.Stopped || Bob > 0)) Invalidate();
            };
            UiState.MotionChanged += OnMotionChanged;
        }

        private void OnMotionChanged() { if (!IsDisposed) Invalidate(); }

        public void SetSpec(OrbSpec spec) { _geo = new OrbGeometry(spec); Invalidate(); }
        public void SetPeriod(double period, bool immediate = false) { _motion.SetPeriod(period, immediate); Invalidate(); }

        /// 로고·팔레트 머리 오브: 잉크 점 + 부표 등(상태 색), 확인 필요는 빈 조각, 연결 끊김은 흐리게 멈춤.
        public void SetState(OrbStates.LogoState st)
        {
            var spec = new OrbSpec { Size = _geo.Size, Lamp = st.Lamp != null };
            if (st.Gap) { spec.GapFrom = OrbStates.GapFrom; spec.GapTo = OrbStates.GapTo; }
            _geo = new OrbGeometry(spec);
            Look.Dots = OrbLook.Ink;
            Look.Lamp = st.Lamp;
            Look.LampHollow = st.Hollow;
            Look.Opacity = st.Opacity;
            SetPeriod(st.Period);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) UiState.MotionChanged -= OnMotionChanged;
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); OrbClock.Add(_tick); }
        protected override void OnHandleDestroyed(EventArgs e) { OrbClock.Remove(_tick); base.OnHandleDestroyed(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.EffectiveBack(Parent));
            float sc = Width / (float)_geo.Size;
            OrbRenderer.Draw(e.Graphics, _geo, _motion.Turn, new PointF(0, (float)_motion.Bob(Motion.Reduced) * (Bob > 0 ? 1 : 0)), Look, sc);
        }
    }
}

namespace BuoyRuleCheck
{
    /// 떠 있는 로고 그리기: 판(지름 52) + 오브(36, 잉크 점) + 부표 등(전체 상태 색) + 숫자/'!' 칩.
    public static class LogoPainter
    {
        public const int Plate = 52, OrbSize = 36;
        private static readonly Font ChipFont = new(Theme.MonoName, 7.5f, FontStyle.Bold);
        private static readonly Dictionary<string, OrbGeometry> Geo = new();

        public static OrbGeometry GeometryFor(OrbStates.LogoState st)
        {
            if (Geo.TryGetValue(st.Key, out var g)) return g;
            var spec = new OrbSpec { Size = OrbSize, Lamp = st.Lamp != null };
            if (st.Gap) { spec.GapFrom = OrbStates.GapFrom; spec.GapTo = OrbStates.GapTo; }
            return Geo[st.Key] = new OrbGeometry(spec);
        }

        /// 숫자 칩 자리(판 오른쪽 위, 높이 18, 최소 폭 18) — 창 모양(Region)도 이 값으로 만든다.
        public static RectangleF ChipRect(Point plate, string chip)
        {
            int w = Math.Max(18, TextRenderer.MeasureText(chip, ChipFont).Width + 6);
            return new RectangleF(plate.X + 38, plate.Y - 6, w, 18);
        }

        /// plate = 판 왼쪽 위. chip: 수정 소요 개수 또는 "!"(확인 필요), 없으면 null.
        public static void Paint(Graphics g, Point plate, OrbStates.LogoState st, double turn, string? chip, double bob = 0, bool focused = false, bool hover = false)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var pr = new Rectangle(plate.X, plate.Y, Plate, Plate);
            using (var b = new SolidBrush(Theme.Bg)) g.FillEllipse(b, pr);
            using (var p = new Pen(Theme.Over(Color.FromArgb(hover ? 52 : 26, 255, 255, 255), Theme.Bg), 1f)) g.DrawEllipse(p, pr.X + 0.5f, pr.Y + 0.5f, pr.Width - 1, pr.Height - 1);
            var look = new OrbLook { Dots = OrbLook.Ink, Lamp = st.Lamp, LampHollow = st.Hollow, Opacity = st.Opacity };
            float off = (Plate - OrbSize) / 2f;
            OrbRenderer.Draw(g, GeometryFor(st), turn, new PointF(plate.X + off, plate.Y + off + (float)bob), look);
            if (focused)
                using (var fp = new Pen(Theme.Muted, 1f) { DashStyle = DashStyle.Dot }) g.DrawEllipse(fp, Rectangle.Inflate(pr, -3, -3));
            if (!string.IsNullOrEmpty(chip))
            {
                var col = chip == "!" ? Theme.Warn : Theme.Fail;
                var cr = ChipRect(plate, chip);
                using (var ring = new SolidBrush(Theme.Bg)) using (var rp = Theme.RoundRect(RectangleF.Inflate(cr, 2, 2), 11)) g.FillPath(ring, rp);
                using (var b = new SolidBrush(col)) using (var path = Theme.RoundRect(cr, 9)) g.FillPath(b, path);
                TextRenderer.DrawText(g, chip, ChipFont, Rectangle.Round(cr), Theme.Bg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }
    }

    /// 도면 위 부표를 GDI+로 그린 모습(미리보기·카드용). 도면에서는 BuoyLayer가 같은 규격으로 Transient를 그린다.
    public static class BuoyPainter
    {
        private static readonly Dictionary<string, OrbGeometry> Geo = new();
        /// 고정점 → 오브 중심 거리(px, 45° 위 오른쪽). 지시선 16 px 끝에서 오브 가장자리가 시작되게.
        public const float CenterOffset = 19.5f;

        public static OrbGeometry GeometryFor(bool gap)
        {
            string key = gap ? "gap" : "full";
            if (Geo.TryGetValue(key, out var g)) return g;
            var spec = new OrbSpec { Size = OrbStates.BuoySize, Vivid = true };
            if (gap) { spec.GapFrom = OrbStates.GapFrom; spec.GapTo = OrbStates.GapTo; }
            return Geo[key] = new OrbGeometry(spec);
        }

        public static Color ColorOf(string status) => status switch { "fail" => Theme.Fail, "pass" => Theme.Pass, _ => Theme.Warn };
        /// 두 판정 색 사이(k: 0 = a, 1 = b) — 상태가 바뀔 때 0.3 s 섞기
        public static Color Mix(Color a, Color b, double k)
        {
            k = Math.Clamp(k, 0, 1);
            int L(int x, int y) => (int)Math.Round(x + (y - x) * k);
            return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
        }

        public static void Paint(Graphics g, PointF anchor, BuoyAnim a, bool reduced, string? cluster = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var col = a.FromStatus is string from ? Mix(ColorOf(from), ColorOf(a.Status), a.ColorK) : ColorOf(a.Status);
            double lo = a.LeaderOpacity(reduced);
            using (var b = new SolidBrush(OrbRenderer.Fade(col, Math.Min(1, lo / 0.75))))
                g.FillEllipse(b, anchor.X - 1.5f, anchor.Y - 1.5f, 3, 3);
            float d = (float)(OrbStates.LeaderLen / Math.Sqrt(2));
            using (var p = new Pen(OrbRenderer.Fade(col, lo), 1f))
                g.DrawLine(p, anchor.X + 1, anchor.Y - 1, anchor.X + d, anchor.Y - d);
            var center = new PointF(anchor.X + CenterOffset, anchor.Y - CenterOffset);
            foreach (var (s, al) in a.Ripples(reduced))
            {
                float r = (float)(12 * s);
                using var rp = new Pen(OrbRenderer.Fade(col, al), 1f);
                g.DrawEllipse(rp, center.X - r, center.Y - r, 2 * r, 2 * r);
            }
            var geo = GeometryFor(a.Gap);
            float sc = (float)a.Scale(reduced);
            float half = (float)(geo.Size / 2 * sc);
            float bob = (float)a.Motion.Bob(reduced);
            OrbRenderer.Draw(g, geo, a.Motion.Turn, new PointF(center.X - half, center.Y - half + bob),
                new OrbLook { Dots = col, Opacity = a.Opacity(reduced) }, sc);
            if (cluster != null)
            {
                using var f = new Font(Theme.MonoName, 7.5f, FontStyle.Bold);
                var cr = new RectangleF(center.X + 8, center.Y - 24, Math.Max(18, TextRenderer.MeasureText(cluster, f).Width + 6), 18);
                using (var b = new SolidBrush(col)) using (var path = Theme.RoundRect(cr, 9)) g.FillPath(b, path);
                TextRenderer.DrawText(g, cluster, f, Rectangle.Round(cr), Theme.Bg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }
    }
}
