// 떠 있는 로고의 화면 부품(AutoCAD 없이 그리는 WinForms): 로고 버튼, 수정 소요 목록, 목록 한 줄, 토스트.
// AutoCAD와 잇는 쪽(창 위치·단축키·결과 반영)은 FloatingUi.cs. 단독 미리보기(BuoyPreview)도 이 파일을 그대로 쓴다.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace GaRuleCheck
{
    /// 떠 있는 로고: 판 52 + 오브 36 + 부표 등 + 칩. 클릭 = 목록 열고 닫기, 드래그 = 옮기기, Enter/Space = 열고 닫기.
    public sealed class LogoButton : Form
    {
        public const int W = 80, H = 66;
        private static readonly Point PlateAt = new(4, 10);
        /// 키보드로 열었는지(true면 목록 첫 줄에 초점)
        public event Action<bool>? ToggleRequested;
        public event Action? Moved;

        private OrbStates.LogoState _st = OrbStates.Watching;
        private string? _chip;
        private string _label = UiText.LogoLabel("idle", 0);
        private (OrbStates.LogoState st, string? chip, string label)? _pending;
        private readonly OrbMotion _motion = new(OrbStates.Watching.Period);
        private double _checkStart = double.NegativeInfinity;
        private bool _expanded, _hover;
        private Point _downScreen, _downLoc;
        private bool _down, _dragged;
        private readonly ToolTip _tip = new();
        private readonly Action<double> _tick;

        public LogoButton()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(W, H);
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            UpdateRegion();
            UpdateAccessibility();
            _tick = OnTick;
            OrbClock.Add(_tick);
            UiState.MotionChanged += OnMotionChanged;
        }

        /// 판(지름 52)의 화면 좌표 — 목록·토스트를 여기에 붙인다
        public Rectangle PlateBounds => new(Left + PlateAt.X, Top + PlateAt.Y, LogoPainter.Plate, LogoPainter.Plate);

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;   // WS_EX_TOOLWINDOW: Alt+Tab 목록에 나오지 않게
                return cp;
            }
        }

        private void OnMotionChanged() { if (!IsDisposed) Invalidate(); }

        private void OnTick(double dt)
        {
            if (IsDisposed || !Visible) return;
            bool reduced = Motion.Reduced;
            _motion.Step(dt, reduced);
            if (_pending is { } p && _motion.Clock - _checkStart >= OrbStates.MinCheckSeconds)
            {
                _pending = null;
                Apply(p.st, p.chip, p.label);
            }
            if (!reduced) Invalidate();
        }

        /// 검사 시작: 1.4 s로 빠르게(최소 0.6 s 보여 준 뒤 결과 상태로)
        public void BeginCheck()
        {
            _checkStart = _motion.Clock;
            if (_st != OrbStates.Checking) { _pending = (_st, _chip, _label); }
            Apply(OrbStates.Checking, null, UiText.LogoLabel("check", 0), keepPending: true);
            Update();   // 검사(동기)가 끝나기 전에 한 번 그려 둔다
        }

        public void SetState(OrbStates.LogoState st, string? chip, string label)
        {
            if (_st == OrbStates.Checking && _motion.Clock - _checkStart < OrbStates.MinCheckSeconds)
            {
                _pending = (st, chip, label);   // 검사 중을 0.6 s는 보여 준 뒤에 바꾼다
                return;
            }
            Apply(st, chip, label);
        }

        private void Apply(OrbStates.LogoState st, string? chip, string label, bool keepPending = false)
        {
            if (!keepPending) _pending = null;
            _st = st;
            _chip = chip;
            _label = label;
            _motion.SetPeriod(st.Period);
            UpdateRegion();
            UpdateAccessibility();
            Invalidate();
        }

        public void SetExpanded(bool on)
        {
            _expanded = on;
            UpdateAccessibility();
            Invalidate();
        }

        private void UpdateRegion()
        {
            using var path = new GraphicsPath(FillMode.Winding);
            path.AddEllipse(PlateAt.X - 1, PlateAt.Y - 1, LogoPainter.Plate + 2, LogoPainter.Plate + 2);
            if (!string.IsNullOrEmpty(_chip))
            {
                var cr = RectangleF.Inflate(LogoPainter.ChipRect(PlateAt, _chip), 3, 3);
                using var chip = Theme.RoundRect(cr, cr.Height / 2);
                path.AddPath(chip, false);
            }
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }

        private void UpdateAccessibility()
        {
            AccessibleName = _label;
            AccessibleDescription = (_expanded ? "목록 펼쳐짐" : "목록 접힘") + " · 클릭 또는 Ctrl+. · 드래그로 옮기기";
            _tip.SetToolTip(this, _label + "\n클릭/Ctrl+.: 목록 " + (_expanded ? "닫기" : "열기") + " · 드래그: 위치 옮기기");
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new LogoAccessible(this);

        /// 스크린리더용: 버튼 + 펼쳐짐/접힘(웹의 aria-expanded와 같은 정보)
        private sealed class LogoAccessible : ControlAccessibleObject
        {
            private readonly LogoButton _o;
            public LogoAccessible(LogoButton o) : base(o) { _o = o; }
            public override AccessibleRole Role => AccessibleRole.PushButton;
            public override string Name => _o._label;
            public override AccessibleStates State =>
                base.State | AccessibleStates.Focusable | (_o._expanded ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
            public override string DefaultAction => _o._expanded ? "목록 닫기" : "목록 열기";
            public override void DoDefaultAction() => _o.ToggleRequested?.Invoke(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            bool reduced = Motion.Reduced;
            LogoPainter.Paint(g, PlateAt, _st, _motion.Turn, _chip, _motion.Bob(reduced), Focused && ShowFocusCues, _hover || _expanded);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _down = true;
            _dragged = false;
            _downScreen = Cursor.Position;
            _downLoc = Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_down) return;
            var c = Cursor.Position;
            int dx = c.X - _downScreen.X, dy = c.Y - _downScreen.Y;
            if (!_dragged && Math.Abs(dx) + Math.Abs(dy) < 5) return;
            _dragged = true;
            Location = new Point(_downLoc.X + dx, _downLoc.Y + dy);
            Moved?.Invoke();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_down) return;
            _down = false;
            Capture = false;
            if (_dragged)
            {
                UiState.Current.LogoX = Left;
                UiState.Current.LogoY = Top;
                UiState.Current.Save();
            }
            else ToggleRequested?.Invoke(false);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space || (e.Control && e.KeyCode == Keys.OemPeriod))
            {
                ToggleRequested?.Invoke(true);
                e.Handled = true;
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                OrbClock.Remove(_tick);
                UiState.MotionChanged -= OnMotionChanged;
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// 로고를 누르면 열리는 수정 소요 목록(폭 308): 제목(수정 소요 n · rev) · 항목 줄 · 팔레트에서 자세히 · 고지.
    public sealed class LogoListPopup : Form
    {
        public event Action<RuleResultDto>? RowPicked;
        public event Action? PaletteRequested;
        public event Action? CloseRequested;
        public event Action? Deactivated;

        private const int Wd = 308, Pad = 12;
        private string _title = "검사 전", _rev = "";
        private readonly FlowLayoutPanel _rows = new()
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Margin = Padding.Empty, Padding = Padding.Empty,
        };
        private readonly ListRowButton _palette;
        private string _foot = UiText.Disclaimer;
        private int _yRows, _yFoot;
        private readonly Timer _fade = new() { Interval = 15 };
        private DateTime _t0;

        public LogoListPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            KeyPreview = true;
            DoubleBuffered = true;
            Width = Wd;
            AccessibleRole = AccessibleRole.Dialog;
            AccessibleName = "수정 소요 목록";
            Controls.Add(_rows);
            _palette = new ListRowButton("팔레트에서 자세히 ›", "", null) { Small = true };
            _palette.Picked += () => PaletteRequested?.Invoke();
            Controls.Add(_palette);
            _fade.Tick += (_, _) =>
            {
                double t = Math.Clamp((DateTime.UtcNow - _t0).TotalMilliseconds / OrbStates.CardInMs, 0, 1);
                Opacity = t;
                if (t >= 1) _fade.Stop();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;   // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        public void SetResult(CheckResponse? r, string state)
        {
            _rows.Controls.Clear();
            var c = r != null ? UiText.Count(r) : default;
            _title = state switch
            {
                "fail" => $"수정 소요 {c.Fail}",
                "warn" => $"확인 필요 {c.Warn}",
                "pass" => "모두 기준 충족",
                "down" => "연결 끊김 — 판정하지 않음",
                _ => r == null ? "검사 전" : "판정한 항목 없음",
            };
            _rev = r != null ? $"rev {r.Revision}" : "";
            AccessibleName = _title;
            if (r != null)
            {
                bool failOnly = UiState.Current.FailOnly;
                foreach (var x in r.Results.OrderBy(x => Theme.StatusRank(x.Status)))
                {
                    if (failOnly && x.Status != "fail") continue;
                    var (title, sub, status) = UiText.ListRow(x);
                    var row = new ListRowButton(title, sub, status);
                    var rr = x;
                    row.Picked += () => RowPicked?.Invoke(rr);
                    _rows.Controls.Add(row);
                }
                if (failOnly && c.Fail == 0)
                    _rows.Controls.Add(new ListRowButton(c.Warn > 0 ? $"확인 필요 {c.Warn}건을 숨겼어요" : "수정 소요가 없어요",
                        "'수정 소요만'을 끄면 모두 보여요", c.Warn > 0 ? "warn" : null) { Enabled = false });
            }
            else if (state == "down")
                _rows.Controls.Add(new ListRowButton("검사 엔진에 연결할 수 없어요", "'python -m brc serve' 실행 후 다시 검사", "down") { Enabled = false });
            Relayout();
        }

        private void Relayout()
        {
            int w = Wd - Pad * 2;
            _yRows = Pad + 34;
            int h = 0;
            foreach (Control c in _rows.Controls)
            {
                c.Width = w;
                if (c is ListRowButton lb) lb.FitHeight();
                h += c.Height + c.Margin.Vertical;
            }
            _rows.SetBounds(Pad, _yRows, w, h);
            _palette.Width = w;
            _palette.FitHeight();
            _palette.Location = new Point(Pad, _yRows + h + 4);
            _yFoot = _palette.Bottom + 6;
            int fh = Ink.Height(_foot, Theme.Small, w - 20);
            Height = _yFoot + 10 + fh + Pad;
            var old = Region;
            using (var path = Theme.RoundRect(new RectangleF(0, 0, Width, Height), 14)) Region = new Region(path);
            old?.Dispose();
            Invalidate();
        }

        /// 로고(판) 위·왼쪽에 붙이되 화면 밖으로 나가지 않게
        public void PlaceNear(Rectangle plate)
        {
            var wa = Screen.FromRectangle(plate).WorkingArea;
            int x = plate.Right - Width, y = plate.Top - Height - 12;
            if (y < wa.Top) y = plate.Bottom + 12;
            if (y + Height > wa.Bottom) y = wa.Bottom - Height;
            if (x < wa.Left) x = plate.Left;
            if (x + Width > wa.Right) x = wa.Right - Width;
            Location = new Point(Math.Max(x, wa.Left), Math.Max(y, wa.Top));
        }

        public void Open(IWin32Window owner, bool keyboard)
        {
            Opacity = Motion.Reduced ? 1 : 0;
            if (!Visible) Show(owner);
            Activate();
            if (keyboard)
            {
                var first = _rows.Controls.OfType<ListRowButton>().FirstOrDefault(b => b.Enabled) ?? _palette;
                first.Focus();
            }
            if (!Motion.Reduced) { _t0 = DateTime.UtcNow; _fade.Start(); }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Deactivated?.Invoke();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || (e.Control && e.KeyCode == Keys.OemPeriod))
            {
                CloseRequested?.Invoke();
                e.Handled = true;
                return;
            }
            if (e.KeyCode is Keys.Down or Keys.Up)
            {
                SelectNextControl(ActiveControl, e.KeyCode == Keys.Down, true, true, true);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 14))
            using (var p = new Pen(Theme.Over(Color.FromArgb(26, 255, 255, 255), Theme.Bg), 1f))
                g.DrawPath(p, path);
            int w = Wd - Pad * 2;
            int rw = string.IsNullOrEmpty(_rev) ? 0 : Ink.Width(_rev, Ink.Tiny);
            TextRenderer.DrawText(g, _title, Ink.Readiness, new Rectangle(Pad + 10, Pad + 2, w - 20 - rw - 8, 24), Theme.Text, Ink.Line | TextFormatFlags.VerticalCenter);
            if (rw > 0) TextRenderer.DrawText(g, _rev, Ink.Tiny, new Rectangle(Pad + w - 10 - rw, Pad + 2, rw, 24), Theme.Faint, Ink.Line | TextFormatFlags.VerticalCenter);
            using (var p = new Pen(Theme.Line, 1f)) g.DrawLine(p, Pad + 10, _yFoot, Pad + w - 10, _yFoot);
            TextRenderer.DrawText(g, _foot, Theme.Small, new Rectangle(Pad + 10, _yFoot + 8, w - 20, Height - _yFoot - 8), Theme.Faint, Ink.Wrap);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _fade.Dispose();
            base.Dispose(disposing);
        }
    }

    /// 목록 한 줄: 점(판정 색) · 항목명(굵게) · 설명. 누르면 그 항목으로. 키보드 Enter/Space.
    public sealed class ListRowButton : Control
    {
        private readonly string _title, _sub;
        private readonly string? _status;
        private bool _hover;
        public bool Small { get; set; }
        public event Action? Picked;

        public ListRowButton(string title, string sub, string? status)
        {
            _title = title;
            _sub = sub;
            _status = status;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = true;
            Margin = Padding.Empty;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = string.IsNullOrEmpty(sub) ? title : $"{title} — {sub}";
            AccessibleDescription = status == null ? "" : Theme.StatusName(status switch { "warn" => "na", var s => s });
        }

        public void FitHeight()
        {
            if (Small) { Height = 28; return; }
            int w = Width - 38;
            Height = 8 + Ink.Height(_title, Theme.Bold, w) + 2 + Ink.Height(_sub, Theme.Small, w) + 9;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { if (Enabled) Picked?.Invoke(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space && Enabled) { Picked?.Invoke(); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if ((_hover || Focused) && Enabled)
                using (var path = Theme.RoundRect(new RectangleF(0, 0, Width, Height), 8))
                using (var b = new SolidBrush(Theme.CardHover)) g.FillPath(b, path);
            if (Small)
            {
                TextRenderer.DrawText(g, _title, Theme.Small, new Rectangle(10, 0, Width - 20, Height), _hover || Focused ? Theme.Text : Theme.Muted,
                    Ink.Line | TextFormatFlags.VerticalCenter);
                return;
            }
            int w = Width - 38;
            if (_status != null)
            {
                var col = _status switch { "skip" => Theme.Muted, "warn" => Theme.Warn, var s => Theme.Status(s) };
                Ink.Dot(g, col, 14, 8 + 8, 8, hollow: _status == "skip");
            }
            int th = Ink.Height(_title, Theme.Bold, w);
            TextRenderer.DrawText(g, _title, Theme.Bold, new Rectangle(28, 8, w, th), Enabled ? Theme.Text : Theme.Muted, Ink.Wrap);
            TextRenderer.DrawText(g, _sub, Theme.Small, new Rectangle(28, 8 + th + 2, w, Height - th - 10), Theme.Muted, Ink.Wrap);
            if (Focused && ShowFocusCues)
                using (var fp = new Pen(Theme.Muted, 1f) { DashStyle = DashStyle.Dot })
                using (var path = Theme.RoundRect(new RectangleF(1.5f, 1.5f, Width - 3, Height - 3), 7)) g.DrawPath(fp, path);
        }
    }

    /// 토스트: 높이 30 알약, 점 + 글(예: 해결 2건 · 저장하면 버전 기록에 남아요). 160 ms 나타나 3.4 s 뒤 사라짐.
    public sealed class ToastForm : Form
    {
        private readonly string _text;
        private readonly string _status;
        private readonly Timer _timer = new() { Interval = 15 };
        private DateTime _t0;
        private const double ShowMs = 3400;

        public ToastForm(string text, string status)
        {
            _text = text;
            _status = status;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Card;
            DoubleBuffered = true;
            AccessibleRole = AccessibleRole.Alert;
            AccessibleName = text;
            Size = new Size(Ink.Width(text, Theme.Body) + 44, 30);
            using (var path = Theme.RoundRect(new RectangleF(0, 0, Width, Height), 15)) Region = new Region(path);
            _timer.Tick += (_, _) => Step();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000;   // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                return cp;
            }
        }

        /// 로고(판) 왼쪽에, 판 가운데 높이로
        public void ShowNear(Rectangle plate, IWin32Window owner)
        {
            var wa = Screen.FromRectangle(plate).WorkingArea;
            int x = Math.Max(wa.Left + 4, plate.Left - Width - 14);
            int y = Math.Clamp(plate.Top + plate.Height / 2 - Height / 2, wa.Top + 4, wa.Bottom - Height - 4);
            Location = new Point(x, y);
            Opacity = Motion.Reduced ? 1 : 0;
            Show(owner);
            AccessibilityNotifyClients(AccessibleEvents.SystemAlert, -1);
            _t0 = DateTime.UtcNow;
            _timer.Start();
        }

        private void Step()
        {
            double ms = (DateTime.UtcNow - _t0).TotalMilliseconds;
            if (!Motion.Reduced)
                Opacity = ms < OrbStates.CardInMs ? ms / OrbStates.CardInMs
                        : ms > ShowMs ? Math.Max(0, 1 - (ms - ShowMs) / OrbStates.CardInMs) : 1;
            if (ms > ShowMs + OrbStates.CardInMs) { _timer.Stop(); Close(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 14.5f))
            using (var p = new Pen(Theme.Over(Color.FromArgb(23, 255, 255, 255), Theme.Card), 1f)) g.DrawPath(p, path);
            Ink.Dot(g, Ink.Of(_status), 17.5f, Height / 2f, 7);
            TextRenderer.DrawText(g, _text, Theme.Body, new Rectangle(28, 0, Width - 34, Height), Theme.Text, Ink.Line | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
