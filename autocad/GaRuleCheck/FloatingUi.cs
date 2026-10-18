// 떠 있는 로고(Guider 스타일): 화면 오른쪽 아래 로고를 누르면 검토 패널이 로고 쪽에서 펼쳐지고, 다시 누르면 접힌다.
//   - 접힌 상태: 로고만 남고 도면이 전부 보인다. 로고에 미충족 개수 배지(빨강), 미충족이 없고 확인 필요만 있으면 주황.
//   - 로고는 드래그로 옮길 수 있고, 마지막 위치와 열림/닫힘은 %APPDATA%\GaRuleCheck\ui.json에 저장.
//   - Ctrl + . 로도 열고 닫는다(AutoCAD 메시지 훅). 접근성: 로고 버튼이 '펼쳐짐/접힘' 상태를 보고(aria-expanded에 해당).
// 판정·표시 로직은 없다 — 패널은 팔레트와 같은 ResultPanel을 하나 더 띄운 것.
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace GaRuleCheck
{
    public static class FloatingUi
    {
        private static LogoButton? _logo;
        private static ReviewPopup? _popup;
        private static Func<ResultPanel>? _panelFactory;
        private static bool _hooked;

        public static ResultPanel? Panel => _popup?.Panel;
        public static bool PanelOpen => _popup is { Visible: true, IsCollapsing: false };
        public static event Action? CollapseAllRequested;

        /// 플러그인 로드 후(메인 창이 준비된 다음) 한 번 부른다.
        public static void Init(Func<ResultPanel> panelFactory)
        {
            _panelFactory = panelFactory;
            if (!_hooked)
            {
                try { AcApp.PreTranslateMessage += OnPreTranslate; _hooked = true; } catch { /* 훅 실패 시 단축키만 안 됨 */ }
            }
            if (UiState.Current.LogoVisible) ShowLogo();
            if (UiState.Current.PanelOpen && _logo != null) Open(animate: false);
        }

        public static void Shutdown()
        {
            if (_hooked) { try { AcApp.PreTranslateMessage -= OnPreTranslate; } catch { } _hooked = false; }
            _popup?.Dispose(); _popup = null;
            _logo?.Dispose(); _logo = null;
        }

        private static IWin32Window Owner() => new WindowWrapper(AcApp.MainWindow.Handle);

        public static void ShowLogo()
        {
            if (_logo == null || _logo.IsDisposed)
            {
                _logo = new LogoButton();
                _logo.ToggleRequested += Toggle;
                _logo.Moved += () => { if (_popup is { Visible: true }) _popup.PlaceNear(_logo.Bounds); };
                _logo.Location = InitialLogoLocation();
                _logo.Show(Owner());
            }
            _logo.Visible = true;
            UiState.Current.LogoVisible = true;
            UiState.Current.Save();
        }

        public static void ToggleLogo()
        {
            if (_logo is { Visible: true })
            {
                Close(animate: false);
                _logo.Visible = false;
                UiState.Current.LogoVisible = false;
                UiState.Current.Save();
            }
            else ShowLogo();
        }

        public static void Toggle()
        {
            if (PanelOpen) Close(animate: true);
            else Open(animate: true);
        }

        public static void Open(bool animate)
        {
            if (_panelFactory == null) return;
            if (_logo == null || !_logo.Visible) ShowLogo();
            if (_popup == null || _popup.IsDisposed)
            {
                _popup = new ReviewPopup(_panelFactory());
                _popup.CollapseRequested += () => Close(animate: true);
                _popup.Opacity = 0;          // 소유 관계를 만들려고 한 번 띄울 때 깜빡이지 않게
                _popup.Show(Owner());
                _popup.Hide();
            }
            _popup.PlaceNear(_logo!.Bounds);
            _popup.Expand(_logo.Bounds, animate);
            _logo.SetExpanded(true);
            UiState.Current.PanelOpen = true;
            UiState.Current.Save();
        }

        /// 접기: 패널을 로고 쪽으로 접고, 도킹된 팔레트도 숨겨 도면이 전부 보이게.
        public static void Close(bool animate)
        {
            if (_popup is { Visible: true } && _logo != null) _popup.Collapse(_logo.Bounds, animate);
            _logo?.SetExpanded(false);
            CollapseAllRequested?.Invoke();
            UiState.Current.PanelOpen = false;
            UiState.Current.Save();
        }

        /// 검사 결과가 바뀔 때마다: 상태 고리 색 + 배지(미충족 n, 없으면 확인 필요 n).
        public static void SetStatus(string overall, int fail, int unknown) => _logo?.SetStatus(overall, fail, unknown);

        private static Point InitialLogoLocation()
        {
            var st = UiState.Current;
            if (st.LogoX is int x && st.LogoY is int y)
            {
                var wa = Screen.FromPoint(new Point(x + 30, y + 30)).WorkingArea;
                if (wa.Contains(x + 30, y + 30)) return new Point(x, y);
            }
            try
            {
                if (GetWindowRect(AcApp.MainWindow.Handle, out var r))
                    return new Point(r.Right - LogoButton.Side - 72, r.Bottom - LogoButton.Side - 64);   // 오른쪽 아래(상태 표시줄 위)
            }
            catch { }
            var a = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            return new Point(a.Right - LogoButton.Side - 72, a.Bottom - LogoButton.Side - 64);
        }

        private const int WM_KEYDOWN = 0x0100, VK_OEM_PERIOD = 0xBE;

        private static void OnPreTranslate(object? sender, Autodesk.AutoCAD.ApplicationServices.PreTranslateMessageEventArgs e)
        {
            var m = e.Message;
            if (m.message == WM_KEYDOWN && (int)m.wParam == VK_OEM_PERIOD && (Control.ModifierKeys & Keys.Control) != 0)
            {
                Toggle();
                e.Handled = true;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        private sealed class WindowWrapper : IWin32Window
        {
            public WindowWrapper(IntPtr h) { Handle = h; }
            public IntPtr Handle { get; }
        }
    }

    /// 원형 로고 버튼 + 배지. 클릭 = 열고 닫기, 드래그 = 위치 이동, Enter/Space = 열고 닫기.
    public sealed class LogoButton : Form
    {
        public const int Side = 60;          // 창 크기(배지 포함)
        private const int LogoD = 50;        // 로고 원 지름
        public event Action? ToggleRequested;
        public event Action? Moved;

        private bool _expanded;
        private string _overall = "idle";
        private int _fail, _unknown;
        private Point _downScreen, _downLoc;
        private bool _down, _dragged;
        private readonly ToolTip _tip = new();
        private readonly Image? _logoImage;
        private static readonly Font LogoFont = new(Theme.FontName, 12f, FontStyle.Bold);
        private static readonly Font BadgeFont = new(Theme.FontName, 8.5f, FontStyle.Bold);
        private static readonly Font BadgeSmall = new(Theme.FontName, 7f, FontStyle.Bold);

        public LogoButton()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Side, Side);
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = "GA RuleCheck 검토 패널";
            // 로고 그림: DLL 옆 garc_logo.png가 있으면 그것을, 없으면 코드로 그린 기본 표식
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                var f = Path.Combine(dir, "garc_logo.png");
                if (File.Exists(f)) _logoImage = Image.FromFile(f);
            }
            catch { _logoImage = null; }
            UpdateRegion();
            UpdateAccessibility();
        }

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

        private void UpdateRegion()
        {
            var path = new GraphicsPath();
            path.AddEllipse(Side - LogoD - 2, Side - LogoD - 2, LogoD, LogoD);
            if (_fail + _unknown > 0) path.AddEllipse(Side - 24, 0, 24, 24);
            Region?.Dispose();
            Region = new Region(path);
        }

        public void SetExpanded(bool on)
        {
            _expanded = on;
            UpdateAccessibility();
            Invalidate();
        }

        public void SetStatus(string overall, int fail, int unknown)
        {
            _overall = overall;
            _fail = fail;
            _unknown = unknown;
            UpdateRegion();
            UpdateAccessibility();
            Invalidate();
        }

        private string CountText() => _fail > 0 ? $"미충족 {_fail}개" : _unknown > 0 ? $"확인 필요 {_unknown}개" : "미충족 없음";

        private void UpdateAccessibility()
        {
            AccessibleDescription = $"{(_expanded ? "펼쳐짐" : "접힘")} · {CountText()} · 클릭 또는 Ctrl+. 으로 {(_expanded ? "접기" : "펼치기")}";
            _tip.SetToolTip(this, $"GA RuleCheck — {CountText()}\n클릭/Ctrl+.: 검토 패널 {(_expanded ? "접기" : "펼치기")} · 드래그: 위치 옮기기");
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new LogoAccessible(this);

        /// 스크린리더용: 버튼 + 펼쳐짐/접힘 상태(웹의 aria-expanded와 같은 정보).
        private sealed class LogoAccessible : ControlAccessibleObject
        {
            private readonly LogoButton _o;
            public LogoAccessible(LogoButton o) : base(o) { _o = o; }
            public override AccessibleRole Role => AccessibleRole.PushButton;
            public override string Name => $"GA RuleCheck 검토 패널 ({_o.CountText()})";
            public override AccessibleStates State =>
                base.State | AccessibleStates.Focusable | (_o._expanded ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
            public override string DefaultAction => _o._expanded ? "접기" : "펼치기";
            public override void DoDefaultAction() => _o.ToggleRequested?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(Side - LogoD - 2, Side - LogoD - 2, LogoD, LogoD);
            var ring = _overall switch
            {
                "pass" => Theme.Pass,
                "fail" => Theme.Fail,
                "incomplete" or "na" or "down" => Theme.Warn,
                _ => Theme.Skip,
            };
            using (var bg = new SolidBrush(Color.FromArgb(36, 40, 48))) g.FillEllipse(bg, r);
            if (_logoImage != null)
            {
                using var clip = new GraphicsPath();
                clip.AddEllipse(Rectangle.Inflate(r, -5, -5));
                var old = g.Clip;
                g.SetClip(clip);
                g.DrawImage(_logoImage, Rectangle.Inflate(r, -5, -5));
                g.Clip = old;
            }
            else
            {
                TextRenderer.DrawText(g, "GA", LogoFont, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            using (var pen = new Pen(ring, 4f)) g.DrawEllipse(pen, Rectangle.Inflate(r, -2, -2));
            if (_expanded)
                using (var p2 = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f)) g.DrawEllipse(p2, Rectangle.Inflate(r, -7, -7));
            int n = _fail > 0 ? _fail : _unknown;
            if (n > 0)
            {
                var br = new Rectangle(Side - 24, 0, 23, 23);
                using (var b = new SolidBrush(_fail > 0 ? Theme.Fail : Theme.Warn)) g.FillEllipse(b, br);
                using (var p = new Pen(Color.White, 1.5f)) g.DrawEllipse(p, br);
                TextRenderer.DrawText(g, n > 99 ? "99+" : n.ToString(), n > 9 ? BadgeSmall : BadgeFont, br,
                                      Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (Focused)
                using (var fp = new Pen(Color.White, 1f) { DashStyle = DashStyle.Dot }) g.DrawEllipse(fp, Rectangle.Inflate(r, -1, -1));
        }

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
            else ToggleRequested?.Invoke();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space || (e.Control && e.KeyCode == Keys.OemPeriod))
            {
                ToggleRequested?.Invoke();
                e.Handled = true;
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tip.Dispose(); _logoImage?.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// 로고에서 펼쳐지는 검토 패널. 펼침·접힘은 250 ms(이즈 아웃) — 창 크기는 그대로 두고
    /// 보이는 영역(Region)을 로고 쪽 모서리에서 키워 자식 컨트롤 재배치 없이 부드럽게 보이게 한다.
    public sealed class ReviewPopup : Form
    {
        private const int DurationMs = 250;
        public ResultPanel Panel { get; }
        public event Action? CollapseRequested;
        public bool IsCollapsing => _closing;

        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
        private DateTime _t0;
        private bool _closing;
        private Point _corner;   // 로고 쪽 모서리(창 좌표)

        public ReviewPopup(ResultPanel panel)
        {
            Panel = panel;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(400, 620);
            BackColor = Theme.Line;
            Padding = new Padding(1);
            KeyPreview = true;
            AccessibleName = "GA RuleCheck 검토 패널";

            var bar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Theme.Card };
            var title = new Label { Text = "GA RuleCheck", Font = Theme.Bold, ForeColor = Theme.Text, AutoSize = true, Location = new Point(10, 7) };
            var collapse = Theme.FlatButton("—");
            collapse.AccessibleName = "검토 패널 접기";
            collapse.Dock = DockStyle.Right;
            collapse.Click += (_, _) => CollapseRequested?.Invoke();
            new ToolTip().SetToolTip(collapse, "접기 (Ctrl+. / Esc)");
            bar.Controls.Add(title);
            bar.Controls.Add(collapse);
            panel.Dock = DockStyle.Fill;
            Controls.Add(panel);
            Controls.Add(bar);
            _timer.Tick += (_, _) => Step();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;   // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || (e.Control && e.KeyCode == Keys.OemPeriod))
            {
                CollapseRequested?.Invoke();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        /// 로고 위·왼쪽에 붙여 놓되 화면 밖으로 나가지 않게.
        public void PlaceNear(Rectangle logo)
        {
            var wa = Screen.FromRectangle(logo).WorkingArea;
            int x = logo.Right - Width, y = logo.Top - Height - 8;
            if (y < wa.Top) y = logo.Bottom + 8;                  // 위가 모자라면 아래로
            if (y + Height > wa.Bottom) y = wa.Bottom - Height;
            if (x < wa.Left) x = logo.Left;                       // 왼쪽이 모자라면 오른쪽으로
            if (x + Width > wa.Right) x = wa.Right - Width;
            Location = new Point(Math.Max(x, wa.Left), Math.Max(y, wa.Top));
            int cx = Math.Clamp(logo.Left + logo.Width / 2 - Left, 0, Width);
            int cy = Math.Clamp(logo.Top + logo.Height / 2 - Top, 0, Height);
            _corner = new Point(cx < Width / 2 ? 0 : Width, cy < Height / 2 ? 0 : Height);
        }

        public void Expand(Rectangle logo, bool animate)
        {
            _closing = false;
            if (!animate) { Region = null; Opacity = 1; Show(); return; }
            Opacity = 0;
            SetReveal(0);
            Show();
            _t0 = DateTime.UtcNow;
            _timer.Start();
        }

        public void Collapse(Rectangle logo, bool animate)
        {
            if (!animate) { Hide(); return; }
            _closing = true;
            _t0 = DateTime.UtcNow;
            _timer.Start();
        }

        private void Step()
        {
            double t = Math.Clamp((DateTime.UtcNow - _t0).TotalMilliseconds / DurationMs, 0, 1);
            double e = 1 - Math.Pow(1 - t, 3);                     // ease-out cubic
            double k = _closing ? 1 - e : e;
            SetReveal(k);
            Opacity = Math.Max(0.05, k);
            if (t < 1) return;
            _timer.Stop();
            if (_closing) { Hide(); _closing = false; }
            else { Region = null; Opacity = 1; }
        }

        /// k=0: 로고 쪽 모서리의 작은 사각형, k=1: 전체.
        private void SetReveal(double k)
        {
            int w = Math.Max(24, (int)(Width * k)), h = Math.Max(24, (int)(Height * k));
            int x = _corner.X == 0 ? 0 : Width - w, y = _corner.Y == 0 ? 0 : Height - h;
            var old = Region;
            Region = new Region(new Rectangle(x, y, w, h));
            old?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
