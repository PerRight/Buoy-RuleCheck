// 팔레트 검토 탭의 부품(명세 6-2, 디자인 보드 05·06): 머리, 탭 줄, 변경 알림, 준비 상태 카드, 안내 카드,
// '이 판정이 다루지 않는 것' 접힘, 하단 고지. 전부 코드로 그리며 판정은 하지 않는다(엔진 결과를 옮겨 보여 주기만).
// 잉크는 진하기로(Text·Muted·Faint), 색은 판정에만(빨강·주황·파랑) — 버튼·탭·테두리는 흑백.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    /// 카드 목록의 폭이 바뀌면 높이를 다시 정하는 부품
    public interface IFits
    {
        void FitTo(int width);
    }

    internal static class Ink
    {
        public static readonly Font Head = new(Theme.FontName, 10f, FontStyle.Bold);       // 카드 항목명 13px
        public static readonly Font StatusBold = new(Theme.FontName, 8f, FontStyle.Bold);  // 상태 글자 11px
        public static readonly Font Value = new(Theme.MonoName, 10f);                      // 게이지 수치 13px
        public static readonly Font Tiny = new(Theme.MonoName, 7.5f);                      // 규정 번호 10px
        public static readonly Font Readiness = new(Theme.FontName, 10.5f, FontStyle.Bold);
        public static readonly Font Micro = new(Theme.FontName, 7.5f);                       // 칸 아래 작은 표시 10px(직접 입력·비어 있음)

        public const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
        public const TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        /// 재기용 플래그 — 줄임표(EndEllipsis)를 넣으면 안 된다. GDI DrawText(DT_CALCRECT)는 줄임표 플래그가 있으면
        /// 제안 폭(Size.Empty = 0)에 맞춰 잘린 폭을 돌려줘서, 실제 글자보다 좁게 재고 '수…'처럼 잘리거나 글자가 겹친다
        /// (2026-10-26 사용자 PC 미리보기에서 발견). 줄임표는 그릴 때(Line)만 쓴다.
        public const TextFormatFlags Measure = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        public static int Height(string text, Font f, int width) =>
            string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, f, new Size(Math.Max(width, 20), 0), Wrap).Height;

        public static int Width(string text, Font f) =>
            string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, f, new Size(int.MaxValue, int.MaxValue), Measure).Width + 2;   // +2: 잰 폭 그대로인 칸에 그릴 때 1 px 차이로 '…'가 붙지 않게
        public static int LineHeight(string text, Font f) =>
            TextRenderer.MeasureText(string.IsNullOrEmpty(text) ? "가" : text, f, new Size(int.MaxValue, int.MaxValue), Measure).Height;

        /// 카드 바탕: 둥근 사각형(12) + 1px 링
        public static void Card(Graphics g, Rectangle r, bool hover, int radius = 12)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), radius);
            using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
            using var p = new Pen(hover ? Theme.Over(Theme.ButtonRing, Theme.Card) : Theme.RingOnCard, 1f);
            g.DrawPath(p, path);
        }

        public static void Dot(Graphics g, Color c, float cx, float cy, float d, bool hollow = false)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(cx - d / 2, cy - d / 2, d, d);
            if (hollow) using (var p = new Pen(c, 1f)) g.DrawEllipse(p, r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            else using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
        }

        /// 상태 키(fail/warn/pass/skip, 엔진 상태 포함) → 판정 색
        public static Color Of(string status) => status switch
        {
            "warn" => Theme.Warn,
            "skip" => Theme.Muted,
            _ => Theme.Status(status),
        };
    }

    /// 머리: 오브(부표 등 = 전체 상태) + "수정 소요 2  문제없음 0" + "rev 2 · 파일 · 14:32" + 오른쪽 "● 0.3 s".
    public sealed class HeaderView : Control
    {
        private readonly OrbView _orb;
        private string _main = "", _side = "", _sub = "", _right = "";
        private Color _rightDot = Theme.Muted, _rightInk = Theme.Faint;

        public HeaderView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = Math.Max(66, 24 + Ink.LineHeight("가", Theme.Big) + Ink.LineHeight("0", Theme.MonoSmall));   // 화면 배율이 크면 같이 늘림
            _orb = new OrbView(new OrbSpec { Size = 34, Lamp = true }, 8) { Location = new Point(16, 16) };
            _orb.SetState(OrbStates.Watching);
            Controls.Add(_orb);
            AccessibleRole = AccessibleRole.StatusBar;
        }

        public void Set(string stateKey, string main, string side, string sub)
        {
            _orb.SetState(OrbStates.ForState(stateKey));
            _main = main;
            _side = side;
            _sub = sub;
            AccessibleName = $"{main} {side}";
            AccessibleDescription = sub;
            Invalidate();
        }

        /// 오른쪽 위: 걸린 시간(예: 0.3 s) 또는 연결 상태
        public void SetRight(string text, Color dot, Color ink)
        {
            _right = text;
            _rightDot = dot;
            _rightInk = ink;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int x = 62;
            int rw = string.IsNullOrEmpty(_right) ? 0 : Ink.Width(_right, Ink.Tiny) + 12;
            int right = Width - 16;
            if (rw > 0)
            {
                TextRenderer.DrawText(g, _right, Ink.Tiny, new Point(right - rw + 10, 16), _rightInk, Ink.Line);
                Ink.Dot(g, _rightDot, right - rw + 3.5f, 16 + Ink.LineHeight(_right, Ink.Tiny) / 2f, 5);
            }
            int avail = right - rw - x - 8;
            int mw = Math.Min(Ink.Width(_main, Theme.Big), avail);
            int mh = Ink.LineHeight(_main, Theme.Big);   // 고정 26 px이면 화면 배율 125·150%에서 아래가 잘린다
            TextRenderer.DrawText(g, _main, Theme.Big, new Rectangle(x, 13, mw, mh), Theme.Text, Ink.Line);
            if (!string.IsNullOrEmpty(_side) && avail - mw > 30)
            {
                int bh = Ink.LineHeight(_main, Theme.Big);
                int sh = Ink.LineHeight(_side, Theme.Body);
                TextRenderer.DrawText(g, _side, Theme.Body, new Rectangle(x + mw + 8, 13 + bh - sh - 2, avail - mw - 8, sh + 2), Theme.Muted, Ink.Line);
            }
            TextRenderer.DrawText(g, _sub, Theme.MonoSmall, new Rectangle(x, Math.Max(40, 13 + mh + 1), right - x, Ink.LineHeight(_sub, Theme.MonoSmall)), Theme.Faint, Ink.Line);
            using var p = new Pen(Theme.Line, 1f);
            g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    /// 탭 하나: 고르면 흰 잉크 + 2px 밑줄, 아니면 Faint.
    public sealed class TabButton : Control
    {
        private bool _selected, _hover;
        public string Id { get; }
        public event Action<string>? Picked;

        public TabButton(string id, string text)
        {
            Id = id;
            Text = text;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTab;
            AccessibleName = text;
            Size = new Size(Ink.Width(text, Theme.Bold) + 16, 33);
            Margin = new Padding(0, 0, 4, 0);
        }

        public bool Selected
        {
            get => _selected;
            set { _selected = value; AccessibleDescription = value ? "선택됨" : ""; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Focus(); Picked?.Invoke(Id); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space) { Picked?.Invoke(Id); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var font = _selected ? Theme.Bold : Theme.Body;
            var ink = _selected ? Theme.Text : _hover ? Theme.Muted : Theme.Faint;
            TextRenderer.DrawText(g, Text, font, new Rectangle(0, 0, Width, Height - 2), ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (_selected) using (var b = new SolidBrush(Theme.Text)) g.FillRectangle(b, 0, Height - 2, Width, 2);
            if (Focused && ShowFocusCues)
                using (var fp = new Pen(Theme.Muted, 1f) { DashStyle = DashStyle.Dot }) g.DrawRectangle(fp, 1, 3, Width - 3, Height - 8);
        }
    }

    /// 흑백 체크 상자(예: 수정 소요만). 켜면 흰 잉크로 채우고 어두운 체크.
    public sealed class InkCheck : Control
    {
        private bool _checked;
        public event Action? Toggled;

        public InkCheck(string text)
        {
            Text = text;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
            Size = new Size(Ink.Width(text, Theme.Small) + 22, 33);
            Sync();
        }

        public bool Checked { get => _checked; set { _checked = value; Sync(); Invalidate(); } }

        private void Sync()
        {
            AccessibleName = Text;
            AccessibleDescription = _checked ? "켜짐" : "꺼짐";
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new CheckAccessible(this);

        private sealed class CheckAccessible : ControlAccessibleObject
        {
            private readonly InkCheck _o;
            public CheckAccessible(InkCheck o) : base(o) { _o = o; }
            public override AccessibleRole Role => AccessibleRole.CheckButton;
            public override AccessibleStates State => base.State | AccessibleStates.Focusable | (_o._checked ? AccessibleStates.Checked : AccessibleStates.None);
            public override string DefaultAction => _o._checked ? "끄기" : "켜기";
            public override void DoDefaultAction() => _o.Toggled?.Invoke();
        }

        protected override void OnClick(EventArgs e) { Focus(); Toggled?.Invoke(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Toggled?.Invoke(); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cy = (Height - 2) / 2f;
            var box = new RectangleF(1.5f, cy - 6, 12, 12);
            using (var path = Theme.RoundRect(box, 2.5f))
            {
                if (_checked)
                {
                    using var b = new SolidBrush(Theme.Text);
                    g.FillPath(b, path);
                    using var ck = new Pen(Theme.Bg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawLines(ck, new[] { new PointF(box.X + 3, box.Y + 6.2f), new PointF(box.X + 5.2f, box.Y + 8.4f), new PointF(box.X + 9.2f, box.Y + 3.8f) });
                }
                else using (var p = new Pen(Theme.Faint, 1f)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, Theme.Small, new Point(20, (int)(cy - Ink.LineHeight(Text, Theme.Small) / 2f)), Theme.Muted, Ink.Line);
            if (Focused && ShowFocusCues)
                using (var fp = new Pen(Theme.Muted, 1f) { DashStyle = DashStyle.Dot }) g.DrawRectangle(fp, 0, 6, Width - 1, Height - 14);
        }
    }

    /// 탭 줄 바탕: 아래 1px 선
    public sealed class LinedStrip : Panel
    {
        public LinedStrip()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        public bool LineOnTop { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using var p = new Pen(Theme.Line, 1f);
            int y = LineOnTop ? 0 : Height - 1;
            e.Graphics.DrawLine(p, 0, y, Width, y);
        }
    }

    /// 하단 고지 문구(위 1px 선). 판정이 보이는 화면에는 항상.
    public sealed class FooterView : Control
    {
        public FooterView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Text = UiText.Disclaimer;
            AccessibleRole = AccessibleRole.StaticText;
            Height = 52;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }

        private void Fit()
        {
            int h = Ink.Height(Text, Theme.Small, Width - 32) + 24;
            if (Width > 40 && h != Height) Height = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            using (var p = new Pen(Theme.Line, 1f)) g.DrawLine(p, 0, 0, Width, 0);
            TextRenderer.DrawText(g, Text, Theme.Small, new Rectangle(16, 12, Width - 32, Height - 18), Theme.Faint, Ink.Wrap);
        }
    }

    /// 변경 알림: 상태 색 35% 링 + 점 + "이번 변경으로 **충돌격벽 위치**가 수정 소요가 됐어요"(여러 줄이면 줄마다).
    public sealed class NoticeBox : Control, IFits
    {
        private readonly List<(string Pre, string Bold, string Post, string Status)> _lines;
        private readonly string _ring;

        public NoticeBox(IEnumerable<TransitionDto> transitions)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            _lines = transitions.Select(UiText.Notice).ToList();
            // 링 색 = 가장 나쁜 알림(수정 소요 > 확인 필요 > 문제없음)
            _ring = _lines.Select(l => l.Status == "warn" ? "na" : l.Status).OrderBy(Theme.StatusRank).FirstOrDefault() ?? "skip";
            Margin = new Padding(0, 0, 0, 10);
            AccessibleRole = AccessibleRole.Alert;
            AccessibleName = string.Join(" / ", _lines.Select(l => l.Pre + l.Bold + l.Post));
        }

        public void FitTo(int width)
        {
            Width = width;
            int h = 20;
            foreach (var l in _lines) h += Math.Max(LineHeight(l, width - 46), 18) + 4;
            Height = h - 4;
        }

        private static int LineHeight((string Pre, string Bold, string Post, string Status) l, int w) =>
            Ink.Height(l.Pre + l.Bold + l.Post, Theme.Bold, w);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10))
            using (var p = new Pen(Theme.Over(Color.FromArgb(90, Ink.Of(_ring)), Theme.Bg), 1f))
                g.DrawPath(p, path);
            int y = 10, x = 34, w = Width - x - 12;
            foreach (var l in _lines)
            {
                int lh = Math.Max(LineHeight(l, w), 18);
                Ink.Dot(g, Ink.Of(l.Status), 18, y + 9, 6);
                DrawRich(g, l, new Rectangle(x, y, w, lh));
                y += lh + 4;
            }
        }

        /// 앞·굵게·뒤 세 조각을 한 줄로(넘치면 단순 줄바꿈 — 굵게는 포기하지 않고 앞 조각만 줄인다)
        private static void DrawRich(Graphics g, (string Pre, string Bold, string Post, string Status) l, Rectangle r)
        {
            int w1 = Ink.Width(l.Pre, Theme.Body), w2 = Ink.Width(l.Bold, Theme.Bold), w3 = Ink.Width(l.Post, Theme.Body);
            if (w1 + w2 + w3 <= r.Width)
            {
                int top = r.Y + 1;
                TextRenderer.DrawText(g, l.Pre, Theme.Body, new Point(r.X, top), Theme.Text, Ink.Line);
                TextRenderer.DrawText(g, l.Bold, Theme.Bold, new Point(r.X + w1, top), Theme.Text, Ink.Line);
                TextRenderer.DrawText(g, l.Post, Theme.Body, new Point(r.X + w1 + w2, top), Theme.Text, Ink.Line);
                return;
            }
            TextRenderer.DrawText(g, l.Pre + l.Bold + l.Post, Theme.Bold, r, Theme.Text, Ink.Wrap);
        }
    }

    /// 연결 전·빈 입력: 준비 n / m, 10칸 진행, 빠진 입력(주황 빈 고리)·채운 입력(흐린 점), [실도면 연결 시작].
    /// '모르면 멈춘다'의 이유를 보여 준다 — 빈 입력이 채워지기 전에는 판정하지 않는다.
    public sealed class ReadinessCard : Control, IFits
    {
        private readonly ReadinessDto _rd;
        private readonly OrbView _orb;
        private readonly Button _go;
        private readonly List<(string Text, string Kind)> _rows = new();   // kind: missing / done / note
        public event Action? LinkRequested;

        public ReadinessCard(ReadinessDto rd)
        {
            _rd = rd;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Card;
            Margin = new Padding(0, 0, 0, 10);
            _orb = new OrbView(new OrbSpec { Size = 30, Vivid = true, GapFrom = OrbStates.GapFrom, GapTo = OrbStates.GapTo }, OrbStates.BuoyWarn);
            _orb.Look.Dots = Theme.Warn;
            Controls.Add(_orb);
            _go = Theme.PrimaryButton("실도면 연결 시작");
            _go.Click += (_, _) => LinkRequested?.Invoke();
            Controls.Add(_go);

            foreach (var it in rd.Items.Where(i => i.Status is "missing" or "invalid"))
                _rows.Add((it.Status == "invalid" && !string.IsNullOrEmpty(it.ProblemKo) ? $"{it.NameKo} — {it.ProblemKo}" : it.NameKo, "missing"));
            foreach (var p in rd.ProblemsKo.Where(p => !rd.Items.Any(i => i.ProblemKo == p)))
                _rows.Add((p, "missing"));
            foreach (var it in rd.Items.Where(i => i.Status == "default" && i.Required))
                _rows.Add(($"{it.NameKo} — 기본값 {it.ValueKo}{(string.IsNullOrEmpty(it.HintKo) ? "" : " · " + it.HintKo)}", "note"));
            var done = rd.Items.Where(i => i.Status == "ok").Select(i => i.NameKo).ToList();
            if (done.Count > 0) _rows.Add((string.Join(" · ", done), "done"));
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = $"준비 {rd.Ready} / {rd.Total} — 빈 입력이 채워지기 전에는 판정하지 않아요";
            AccessibleDescription = string.Join(", ", _rows.Where(r => r.Kind == "missing").Select(r => r.Text));
        }

        private const int Pad = 16;
        private int _yBar, _yList;
        private readonly List<int> _rowY = new();

        public void FitTo(int width)
        {
            Width = width;
            int w = width - Pad * 2;
            int y = Pad;
            _orb.Location = new Point(Pad, y);
            y += 30 + 14;
            _yBar = y;
            y += 3 + 14;
            _yList = y;
            _rowY.Clear();
            foreach (var r in _rows)
            {
                _rowY.Add(y);
                y += Math.Max(Ink.Height(r.Text, Theme.Body, w - 16), 16) + 7;
            }
            y += 7;
            _go.Location = new Point(Pad, y);
            _go.Width = w;
            _go.Height = 34;
            y += 34 + Pad;
            Height = y;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            Ink.Card(g, ClientRectangle, false);
            int w = Width - Pad * 2;
            int tx = Pad + 30 + 12;
            string title = $"준비 {_rd.Ready} / {_rd.Total}";
            TextRenderer.DrawText(g, title, Ink.Readiness, new Point(tx, Pad - 1), Theme.Text, Ink.Line);
            TextRenderer.DrawText(g, "빈 입력이 채워지기 전에는 판정하지 않아요", Theme.Small,
                new Rectangle(tx, Pad + 20, Width - tx - Pad, 30), Theme.Muted, Ink.Wrap);
            // 10칸(전체가 10이 아니어도 칸 수 = 전체, 최대 12)
            int n = Math.Clamp(_rd.Total, 1, 12);
            float gap = 3, seg = (w - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                bool on = i < Math.Round((double)_rd.Ready / Math.Max(_rd.Total, 1) * n);
                using var b = new SolidBrush(on ? Theme.Text : Theme.Line);
                using var path = Theme.RoundRect(new RectangleF(Pad + i * (seg + gap), _yBar, seg, 3), 1.5f);
                g.FillPath(b, path);
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                var (text, kind) = _rows[i];
                int y = _rowY[i];
                var ink = kind == "done" ? Theme.Faint : kind == "note" ? Theme.Muted : Theme.Text;
                if (kind == "missing") Ink.Dot(g, Theme.Warn, Pad + 3.5f, y + 8, 7, hollow: true);
                else Ink.Dot(g, Theme.Faint, Pad + 3.5f, y + 8, 7);
                TextRenderer.DrawText(g, text, Theme.Body, new Rectangle(Pad + 16, y, w - 16, 400), ink, Ink.Wrap);
            }
        }
    }

    /// 안내 카드(검사 전·연결 끊김·감시 중지): 제목, 설명, 버튼들. 판정 색은 상태 점에만.
    public sealed class InfoCard : Control, IFits
    {
        private readonly string _title, _body;
        private readonly string? _status;
        private readonly List<Button> _buttons = new();
        private const int Pad = 16;
        private int _yBody;

        public InfoCard(string title, string body, string? status = null)
        {
            _title = title;
            _body = body;
            _status = status;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Margin = new Padding(0, 0, 0, 10);
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = title;
            AccessibleDescription = body;
        }

        public Button Add(Button b)
        {
            _buttons.Add(b);
            Controls.Add(b);
            return b;
        }

        public void FitTo(int width)
        {
            Width = width;
            int w = width - Pad * 2;
            int y = Pad + Math.Max(Ink.Height(_title, Ink.Head, w - (_status != null ? 16 : 0)), 18) + 6;
            _yBody = y;
            y += Ink.Height(_body, Theme.Small, w) + 14;
            int x = Pad;
            foreach (var b in _buttons)
            {
                if (x + b.Width > width - Pad && x > Pad) { x = Pad; y += b.Height + 6; }
                b.Location = new Point(x, y);
                x += b.Width + 6;
            }
            if (_buttons.Count > 0) y += _buttons[^1].Height;
            Height = y + Pad;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            Ink.Card(g, ClientRectangle, false);
            int x = Pad, w = Width - Pad * 2;
            if (_status != null)
            {
                Ink.Dot(g, Ink.Of(_status), x + 3, Pad + 9, 6);
                x += 16;
                w -= 16;
            }
            TextRenderer.DrawText(g, _title, Ink.Head, new Rectangle(x, Pad, w, 200), Theme.Text, Ink.Wrap);
            TextRenderer.DrawText(g, _body, Theme.Small, new Rectangle(Pad, _yBody, Width - Pad * 2, 400), Theme.Muted, Ink.Wrap);
        }
    }

    /// 접힘: "이 판정이 다루지 않는 것 — 좌우 10°, 평면 사각 섹터 ▾" → 펼치면 규칙별 미검토 범위.
    public sealed class NotCoveredView : Control, IFits
    {
        private readonly string _head;
        private readonly string _body;
        private bool _open, _hover;
        private int _headH;

        public NotCoveredView(IEnumerable<RuleResultDto> results)
        {
            var list = results.Where(r => r.Limitations.Count > 0).ToList();
            var shorts = list.Select(r => UiText.NotCoveredShort(r.RuleId)).Where(s => s.Length > 0).ToList();
            _head = "이 판정이 다루지 않는 것" + (shorts.Count > 0 ? " — " + string.Join(", ", shorts) : "");
            _body = string.Join("\n\n", list.Select(r =>
                (UiText.Short(r.RuleId) != r.RuleId ? UiText.Short(r.RuleId) : r.TitleKo) + "\n" +
                string.Join("\n", r.Limitations.Select(x => "· " + x))))
                + "\n\n수식·중간값 전체는 '리포트'에서 볼 수 있어요.";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = true;
            Margin = new Padding(0, 0, 0, 10);
            AccessibleRole = AccessibleRole.OutlineButton;
            AccessibleName = _head;
            Visible = list.Count > 0;
        }

        public bool Open
        {
            get => _open;
            set
            {
                _open = value;
                AccessibleDescription = value ? "펼쳐짐" : "접힘";
                if (Width > 0) FitTo(Width);
            }
        }

        public void FitTo(int width)
        {
            Width = width;
            _headH = Ink.Height(_head + " ▾", Theme.Small, width - 8) + 8;
            Height = _headH + (_open ? Ink.Height(_body, Theme.Small, width - 20) + 8 : 0);
            Invalidate();
        }

        protected override void OnClick(EventArgs e) { Focus(); Open = !_open; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space) { Open = !_open; e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            TextRenderer.DrawText(g, _head + (_open ? " ▴" : " ▾"), Theme.Small, new Rectangle(4, 4, Width - 8, _headH),
                _hover || _open ? Theme.Muted : Theme.Faint, Ink.Wrap);
            if (_open)
                TextRenderer.DrawText(g, _body, Theme.Small, new Rectangle(12, _headH, Width - 20, Height - _headH), Theme.Faint, Ink.Wrap);
            if (Focused && ShowFocusCues)
                using (var fp = new Pen(Theme.Muted, 1f) { DashStyle = DashStyle.Dot }) g.DrawRectangle(fp, 0, 0, Width - 1, _headH - 1);
        }
    }

    /// 역할 표시 범례(BRCROLESHOW 동안): 선체·장애물·충돌격벽·구상선수·조종위치 색과 객체 수. 판정 색과 겹치지 않는 색.
    public sealed class RoleLegendCard : Control, IFits
    {
        private readonly Dictionary<string, int> _counts;
        private const int Pad = 16;
        private static readonly (string Role, string Ko, Color Ink, bool Ring)[] Items =
        {
            ("hull", "선체", Theme.RoleHull, false), ("obstruction", "장애물", Theme.RoleObstruction, false),
            ("collision_bhd", "충돌격벽", Theme.RoleBulkhead, false), ("eye", "조종위치", Theme.RoleEye, true),
            ("bulb", "구상선수", Theme.RoleBulb, false),
        };

        public RoleLegendCard(Dictionary<string, int> counts)
        {
            _counts = counts;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Margin = new Padding(0, 0, 0, 10);
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = "역할 표시 — " + string.Join(", ", Items.Select(i => $"{i.Ko} {(_counts.TryGetValue(i.Role, out var n) ? n : 0)}개"));
        }

        private const string LegendNote = "판정 색(빨강·주황·파랑)과 겹치지 않는 색으로 — 다시 실행하면 꺼져요";

        public void FitTo(int width)
        {
            Width = width;
            Height = Pad + 20 + 8 + 3 * 24 + 4 + Math.Max(18, Ink.Height(LegendNote, Theme.Small, width - Pad * 2)) + Pad - 6;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            Ink.Card(g, ClientRectangle, false);
            int w = Width - Pad * 2;
            TextRenderer.DrawText(g, "역할 표시", Theme.Bold, new Point(Pad, Pad), Theme.Text, Ink.Line);
            const string cmd = "BRCROLESHOW";
            int cw = Ink.Width(cmd, Ink.Tiny);
            TextRenderer.DrawText(g, cmd, Ink.Tiny, new Point(Width - Pad - cw, Pad + 2), Theme.Faint, Ink.Line);
            int colW = w / 2, y0 = Pad + 28;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Items.Length; i++)
            {
                var (role, ko, ink, ring) = Items[i];
                int x = Pad + (i % 2) * colW, y = y0 + (i / 2) * 24;
                if (ring) using (var p = new Pen(ink, 1.5f)) g.DrawEllipse(p, x + 4, y + 4, 8, 8);
                else using (var p = new Pen(ink, 1.5f)) g.DrawLine(p, x, y + 8, x + 16, y + 8);
                int n = _counts.TryGetValue(role, out var c) ? c : 0;
                TextRenderer.DrawText(g, $"{ko} {n}", Theme.Small, new Point(x + 23, y + 1), n > 0 ? Theme.Muted : Theme.Faint, Ink.Line);
            }
            TextRenderer.DrawText(g, LegendNote, Theme.Small,
                new Rectangle(Pad, y0 + 3 * 24 + 2, w, Math.Max(18, Ink.Height(LegendNote, Theme.Small, w))), Theme.Faint, Ink.Wrap);
        }
    }

    /// 감싸 줄바꿈하는 작은 글(숨긴 항목 안내 등)
    public sealed class NoteLabel : Label, IFits
    {
        public NoteLabel(string text, Color ink, Font? font = null)
        {
            Text = text;
            AutoSize = false;
            Font = font ?? Theme.Small;
            ForeColor = ink;
            BackColor = Color.Transparent;
            Margin = new Padding(4, 0, 0, 10);
        }

        public void FitTo(int width)
        {
            Width = width - 4;
            Height = Ink.Height(Text, Font, Width) + 2;
        }
    }
}
