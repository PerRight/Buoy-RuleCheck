// 실도면 연결 2~5단계 안내 띠: 도면에서 점·객체를 고르는 동안 AutoCAD 창 위쪽 가운데에 떠서
// 지금 단계(5칸 진행 줄)와 할 일, 'Enter 건너뛰기 · Esc 멈춤'을 보여 준다. 포커스를 가져가지 않는다(명령줄 입력 그대로).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GaRuleCheck
{
    public sealed class LinkProgress : Form
    {
        private int _step = 2;
        private string _what = "";
        private const int Wd = 560, Ht = 112, Pad = 20;

        public LinkProgress()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Size = new Size(Wd, Ht);
            AccessibleRole = AccessibleRole.StatusBar;
            using var path = Theme.RoundRect(new RectangleF(0, 0, Wd, Ht), 14);
            Region = new Region(path);
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

        /// owner 창(AutoCAD) 위쪽 가운데, 리본 아래쯤
        public void ShowOver(IWin32Window owner, Rectangle ownerBounds)
        {
            Location = new Point(ownerBounds.Left + (ownerBounds.Width - Wd) / 2, ownerBounds.Top + 190);
            if (!Visible) Show(owner);
        }

        public void Set(int step, string what)
        {
            _step = step;
            _what = what;
            AccessibleName = $"실도면 연결 {step}/5 {LinkSteps.Names[Math.Clamp(step - 1, 0, 4)]} — {what}";
            Invalidate();
            Update();
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 14))
            using (var p = new Pen(Theme.Over(Color.FromArgb(20, 255, 255, 255), Theme.Bg), 1f)) g.DrawPath(p, path);
            LinkSteps.PaintHeader(g, new Rectangle(Pad, 14, Wd - Pad * 2, 60), _step);
            const string keys = "Enter 건너뛰기 · Esc 멈춤";
            int kw = Ink.Width(keys, Theme.Small);
            TextRenderer.DrawText(g, _what, Theme.Body, new Rectangle(Pad, 80, Wd - Pad * 2 - kw - 12, 20), Theme.Muted, Ink.Line | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, keys, Theme.Small, new Rectangle(Wd - Pad - kw, 80, kw, 20), Theme.Faint, Ink.Line | TextFormatFlags.VerticalCenter);
        }
    }
}

namespace GaRuleCheck
{
    /// 창 핸들을 WinForms 소유자로 쓰기(AutoCAD 메인 창 등)
    internal sealed class Win32Owner : IWin32Window
    {
        public Win32Owner(IntPtr h) { Handle = h; }
        public IntPtr Handle { get; }
    }
}
