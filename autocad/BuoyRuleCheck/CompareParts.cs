// 버전 비교 창의 부품(명세 6-6, 디자인 보드 07): 버전 고르기 칸, 보기 방식 묶음(나란히 · 겹쳐 · 차이만), 투명도 막대,
// 요약 줄(수정 소요 3 → 0 · 변경 4건 · 전체 ● → ●), 변경 내역 한 칸, 범례 알약. 그리기만 한다 — 무엇이 바뀌었고 해결됐는지는 엔진(/v1/compare).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    /// 어두운 버전 고르기 칸: 둥근 입력 바탕 + ▾. 누르면 어두운 목록.
    public sealed class InkCombo : Control
    {
        public sealed record Entry(string Text, string Key, string? Long = null);
        private readonly List<Entry> _items = new();
        private int _sel = -1;
        private bool _hover;
        public event Action? SelectionChanged;

        public InkCombo(string accessibleName)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(190, 30);
            AccessibleRole = AccessibleRole.ComboBox;
            AccessibleName = accessibleName;
        }

        public IReadOnlyList<Entry> Items => _items;
        public Entry? Selected => _sel >= 0 && _sel < _items.Count ? _items[_sel] : null;

        public void SetItems(IEnumerable<Entry> items, int select)
        {
            _items.Clear();
            _items.AddRange(items);
            _sel = _items.Count == 0 ? -1 : Math.Clamp(select, 0, _items.Count - 1);
            Sync();
        }

        public int SelectedIndex
        {
            get => _sel;
            set
            {
                int v = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
                if (v == _sel) return;
                _sel = v;
                Sync();
                SelectionChanged?.Invoke();
            }
        }

        private void Sync()
        {
            AccessibleDescription = Selected?.Long ?? Selected?.Text ?? "";
            Invalidate();
        }

        private void Open()
        {
            if (_items.Count == 0) return;
            var menu = new ContextMenuStrip { Renderer = new InkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = false, Font = Theme.Body, BackColor = Theme.Card };
            for (int i = 0; i < _items.Count; i++)
            {
                int k = i;
                var it = new ToolStripMenuItem(_items[i].Long ?? _items[i].Text) { ForeColor = Theme.Text, Checked = i == _sel, Padding = new Padding(4, 3, 4, 3) };
                it.Click += (_, _) => SelectedIndex = k;
                menu.Items.Add(it);
            }
            menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(this, new Point(0, Height + 2));
        }

        protected override void OnClick(EventArgs e) { Focus(); Open(); base.OnClick(e); }
        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && e.Alt || e.KeyCode is Keys.F4 or Keys.Space or Keys.Enter) { Open(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { SelectedIndex = _sel + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = _sel - 1; e.Handled = true; }
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
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8))
            {
                using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
                using var p = new Pen(Focused || _hover ? Theme.Muted : Theme.Over(Color.FromArgb(31, 255, 255, 255), Theme.Bg), 1f);
                g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Selected?.Text ?? "—", Theme.Body, new Rectangle(10, 0, Width - 32, Height), Theme.Text, Ink.Line | TextFormatFlags.VerticalCenter);
            float cx = Width - 15, cy = Height / 2f;
            using var chev = new Pen(Theme.Muted, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(chev, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
        }
    }

    /// 어두운 펼침 목록 그리기(카드 바탕, 호버 줄, 고른 줄은 흰 점)
    public sealed class InkMenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var b = new SolidBrush(Theme.Card);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var p = new Pen(Theme.RingOnCard, 1f);
            e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            using var b = new SolidBrush(Theme.CardHover);
            e.Graphics.FillRectangle(b, 2, 0, e.Item.Width - 4, e.Item.Height);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
            if (e.Item is ToolStripMenuItem { Checked: true })
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Ink.Dot(e.Graphics, Theme.Text, 9, e.Item.Height / 2f, 5);
            }
            e.TextRectangle = new Rectangle(e.TextRectangle.X + 10, e.TextRectangle.Y, e.TextRectangle.Width, e.TextRectangle.Height);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { /* 점은 OnRenderItemText에서 */ }
    }

    /// 보기 방식 묶음: 바탕 알약 안에서 고른 것만 흰 잉크로 채운다(나란히 · 겹쳐 · 차이만).
    public sealed class Segmented : Control
    {
        private readonly string[] _labels;
        private int _sel, _hover = -1;
        private readonly List<Rectangle> _rects = new();
        public event Action<int>? Changed;

        public Segmented(string accessibleName, params string[] labels)
        {
            _labels = labels;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTabList;
            AccessibleName = accessibleName;
            int x = 3;
            foreach (var l in labels)
            {
                int w = Ink.Width(l, Theme.Bold) + 26;
                _rects.Add(new Rectangle(x, 3, w, 26));
                x += w + 2;
            }
            Size = new Size(x + 1, 32);
            Sync();
        }

        public int Selected
        {
            get => _sel;
            set { _sel = Math.Clamp(value, 0, _labels.Length - 1); Sync(); Invalidate(); }
        }

        private void Sync() => AccessibleDescription = _labels[_sel] + " 선택됨";

        private int At(Point p) => _rects.FindIndex(r => r.Contains(p));

        protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.Location); if (h != _hover) { _hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            Focus();
            int i = At(e.Location);
            if (i >= 0 && i != _sel) { Selected = i; Changed?.Invoke(i); }
            base.OnMouseClick(e);
        }
        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int d = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
            if (d != 0)
            {
                int n = Math.Clamp(_sel + d, 0, _labels.Length - 1);
                if (n != _sel) { Selected = n; Changed?.Invoke(n); }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f))
            {
                using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
                using var p = new Pen(Theme.Over(Color.FromArgb(20, 255, 255, 255), Theme.Bg), 1f);
                g.DrawPath(p, path);
            }
            for (int i = 0; i < _labels.Length; i++)
            {
                var r = _rects[i];
                bool on = i == _sel;
                if (on)
                    using (var path = Theme.RoundRect(r, r.Height / 2f))
                    using (var b = new SolidBrush(Theme.Text)) g.FillPath(b, path);
                var ink = on ? Theme.Bg : i == _hover ? Theme.Text : Theme.Muted;
                TextRenderer.DrawText(g, _labels[i], on ? Theme.Bold : Theme.Body, r, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            if (Focused && ShowFocusCues)
                using (var fp = new Pen(Theme.Muted, 1f) { DashStyle = DashStyle.Dot })
                using (var path = Theme.RoundRect(RectangleF.Inflate(_rects[_sel], 2, 2), 15)) g.DrawPath(fp, path);
        }
    }

    /// 흑백 막대(겹쳐 보기의 이후 투명도). 끌기·←→.
    public sealed class InkSlider : Control
    {
        private int _value = 60;
        private bool _drag;
        public event Action? ValueChanged;

        public InkSlider(string accessibleName)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(110, 30);
            AccessibleRole = AccessibleRole.Slider;
            AccessibleName = accessibleName;
            Sync();
        }

        public int Value
        {
            get => _value;
            set
            {
                int v = Math.Clamp(value, 0, 100);
                if (v == _value) return;
                _value = v;
                Sync();
                Invalidate();
                ValueChanged?.Invoke();
            }
        }

        private void Sync() => AccessibleDescription = $"{_value}%";
        private int ValueAt(int x) => (int)Math.Round(Math.Clamp((x - 6.0) / Math.Max(Width - 12, 1), 0, 1) * 100);

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); _drag = true; Value = ValueAt(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (_drag) Value = ValueAt(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; base.OnMouseUp(e); }
        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) { Value -= 5; e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Value += 5; e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float y = Height / 2f, x0 = 6, x1 = Width - 6, kx = x0 + (x1 - x0) * _value / 100f;
            using (var b = new SolidBrush(Theme.Line)) g.FillRectangle(b, x0, y - 1, x1 - x0, 2);
            using (var b = new SolidBrush(Enabled ? Theme.Muted : Theme.Line)) g.FillRectangle(b, x0, y - 1, kx - x0, 2);
            Ink.Dot(g, Enabled ? Theme.Text : Theme.Faint, kx, y, 10);
            if (Focused && ShowFocusCues)
                using (var fp = new Pen(Theme.Muted, 1f) { DashStyle = DashStyle.Dot }) g.DrawEllipse(fp, kx - 8, y - 8, 16, 16);
        }
    }

    /// 요약 줄: 수정 소요 3 → 0 · 변경 4건 · 전체 ● 수정 소요 → ● 문제없음 · (오른쪽) 확인 필요 0
    public sealed class CompareSummaryBar : Control
    {
        private CompareResponse? _d;
        private string? _message;
        private bool _messageWarn;

        public CompareSummaryBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = 46;
            AccessibleRole = AccessibleRole.StatusBar;
        }

        public void Set(CompareResponse d)
        {
            _d = d;
            _message = null;
            var (fb, fa, n, w) = UiText.CompareCounts(d);
            AccessibleName = d.Same ? "바뀐 것이 없어요"
                : $"수정 소요 {fb} → {fa}, 변경 {n}건, 전체 {UiText.OverallName(d.A.Overall)} → {UiText.OverallName(d.B.Overall)}, 확인 필요 {w}";
            Invalidate();
        }

        public void SetMessage(string text, bool warn)
        {
            _d = null;
            _message = text;
            _messageWarn = warn;
            AccessibleName = text;
            Invalidate();
        }

        private static readonly Color PassInk = Color.FromArgb(0x8F, 0xBB, 0xFF);   // 디자인 보드 07: 밝은 파랑 숫자(문제없음)
        private static readonly Font MonoBold = new(Theme.MonoName, 9.5f, FontStyle.Bold);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            using (var p = new Pen(Theme.Line, 1f)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            int x = 18, cy = Height / 2;
            if (_d == null)
            {
                TextRenderer.DrawText(g, _message ?? "", Theme.Body, new Rectangle(x, 0, Width - 36, Height), _messageWarn ? Theme.Warn : Theme.Muted,
                    Ink.Line | TextFormatFlags.VerticalCenter);
                return;
            }
            var d = _d;
            if (d.Same)
            {
                TextRenderer.DrawText(g, "바뀐 것이 없어요 — GA_ 레이어·선박 정보와 판정이 같습니다", Theme.Body, new Rectangle(x, 0, Width - 36, Height),
                    Theme.Muted, Ink.Line | TextFormatFlags.VerticalCenter);
                return;
            }
            var (fb, fa, n, w) = UiText.CompareCounts(d);
            void T(string s, Font f, Color c, int gap = 8)
            {
                TextRenderer.DrawText(g, s, f, new Rectangle(x, 0, Ink.Width(s, f) + 4, Height), c, Ink.Line | TextFormatFlags.VerticalCenter);
                x += Ink.Width(s, f) + gap;
            }
            void Status(string overall)
            {
                var key = UiText.OverallKey(overall);
                var col = key == "none" ? Theme.Faint : Ink.Of(key == "warn" ? "na" : key);
                Ink.Dot(g, col, x + 3, cy, 6);
                x += 11;
                T(UiText.OverallName(overall), Theme.Body, col);
            }
            T("수정 소요", Theme.Body, Theme.Faint);
            T(fb.ToString(), MonoBold, fb > 0 ? Theme.FailInk : Theme.Text);
            T("→", Theme.Body, Theme.Faint);
            var afterKey = UiText.OverallKey(d.B.Overall);
            var afterInk = fa > 0 ? Theme.FailInk : afterKey == "pass" ? PassInk : afterKey == "warn" ? Theme.Warn : Theme.Text;
            T(fa.ToString(), MonoBold, afterInk, 26);
            T("변경", Theme.Body, Theme.Faint);
            T($"{n}건", MonoBold, Theme.Text, 26);
            T("전체", Theme.Body, Theme.Faint);
            Status(d.A.Overall);
            T("→", Theme.Body, Theme.Faint);
            Status(d.B.Overall);
            string right = $"확인 필요 {w}";
            int rw = Ink.Width(right, Theme.Body);
            if (Width - 18 - rw > x)
                TextRenderer.DrawText(g, right, Theme.Body, new Rectangle(Width - 18 - rw, 0, rw + 2, Height), w > 0 ? Theme.Warn : Theme.Faint,
                    Ink.Line | TextFormatFlags.VerticalCenter);
        }
    }

    /// 변경 내역 한 칸: 번호 표식(모양·색) · 제목 · 상태(해결 등) / 수치 변화(앞 → 뒤 굵게) / 덧붙임. 고른 칸은 링.
    public sealed class ChangeCard : Control
    {
        public ChangeDto Change { get; }
        public event Action<bool>? Hovered;
        public event Action? Clicked;
        private bool _active, _hover;
        private static readonly Font MonoBold = new(Theme.MonoName, 9f, FontStyle.Bold);
        private static readonly Font MarkerFont = new(Theme.MonoName, 8.5f, FontStyle.Bold);
        private const int PadX = 12, PadY = 11, Col = 34;

        public ChangeCard(ChangeDto c)
        {
            Change = c;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            TabStop = true;
            Margin = new Padding(0, 0, 0, 6);
            var (from, to) = UiText.ChangeValue(c);
            var (st, _) = UiText.ChangeStatus(c);
            AccessibleRole = AccessibleRole.ListItem;
            AccessibleName = $"{c.Number}. {UiText.ChangeTitle(c)}{(st.Length > 0 ? " — " + st : "")}";
            AccessibleDescription = $"{(from == null ? "" : from + " → ")}{to}. {UiText.ChangeNote(c)}";
        }

        public void FitTo(int width)
        {
            Width = width;
            int w = width - PadX * 2 - Col;
            var (from, to) = UiText.ChangeValue(c: Change);
            string value = (from == null ? "" : from + " → ") + to;
            string note = UiText.ChangeNote(Change);
            int h = PadY + Ink.Height(UiText.ChangeTitle(Change), Theme.Bold, w - 80) + 5 + Ink.Height(value, Theme.Mono, w);
            if (note.Length > 0) h += 4 + Ink.Height(note, Theme.Small, w);
            Height = h + PadY;
            Invalidate();
        }

        public void SetActive(bool on)
        {
            if (_active == on) return;
            _active = on;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Hovered?.Invoke(true); Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Hovered?.Invoke(false); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Focus(); Clicked?.Invoke(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space) { Clicked?.Invoke(); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Hovered?.Invoke(true); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Hovered?.Invoke(false); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            if (_active || _hover || Focused)
                using (var path = Theme.RoundRect(box, 10))
                {
                    using (var b = new SolidBrush(_active ? Theme.Card : Theme.CardHover)) g.FillPath(b, path);
                    if (_active || Focused)
                        using (var p = new Pen(Theme.Over(Color.FromArgb(102, Theme.Text), Theme.Card), 1f)) g.DrawPath(p, path);
                }
            // 번호 표식: 위치가 있는 변경은 모양(○ 추가 · □ 삭제 · ◇ 변경), 선박 정보·흘수 조건은 번호만
            var mr = new RectangleF(PadX, PadY, 22, 22);
            if (Change.Bbox != null) CanvasPainter.DrawMarker(g, PaintStyle.Screen, Change, mr, null, MarkerFont, false);
            else TextRenderer.DrawText(g, $"{Change.Number}.", MarkerFont, Rectangle.Round(mr), Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            int x = PadX + Col, w = Width - x - PadX;
            var (st, key) = UiText.ChangeStatus(Change);
            int sw = st.Length > 0 ? Ink.Width(st, Theme.Small) + (key == "muted" ? 0 : 11) : 0;
            string title = UiText.ChangeTitle(Change);
            int th = Ink.Height(title, Theme.Bold, w - sw - 8);
            TextRenderer.DrawText(g, title, Theme.Bold, new Rectangle(x, PadY, w - sw - 8, th), Theme.Text, Ink.Wrap);
            if (sw > 0)
            {
                var col = key == "muted" ? Theme.Faint : Ink.Of(key == "warn" ? "na" : key);
                int sx = x + w - sw;
                if (key != "muted") { Ink.Dot(g, col, sx + 3, PadY + 9, 6); sx += 11; }
                TextRenderer.DrawText(g, st, Theme.Small, new Point(sx, PadY + 2), col, Ink.Line);
            }
            int y = PadY + th + 5;
            var (from, to) = UiText.ChangeValue(Change);
            string value = (from == null ? "" : from + " → ") + to;
            int vh = Ink.Height(value, Theme.Mono, w);
            if (from != null && Ink.Width(value, Theme.Mono) <= w && to != "없음")
            {
                // 앞은 흐리게, 뒤(지금 값)는 굵게
                string head = from + " → ";
                TextRenderer.DrawText(g, head, Theme.Mono, new Point(x, y), Theme.Muted, Ink.Line);
                TextRenderer.DrawText(g, to, MonoBold, new Point(x + Ink.Width(head, Theme.Mono), y), Theme.Text, Ink.Line);
            }
            else TextRenderer.DrawText(g, value, Theme.Mono, new Rectangle(x, y, w, vh), Theme.Muted, Ink.Wrap);
            y += vh + 4;
            string note = UiText.ChangeNote(Change);
            if (note.Length > 0) TextRenderer.DrawText(g, note, Theme.Small, new Rectangle(x, y, w, Height - y), Theme.Muted, Ink.Wrap);
        }
    }

    /// 그림 왼쪽 아래 범례 알약
    public sealed class LegendPill : Control
    {
        public LegendPill()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;
            Size = new Size(470, 32);
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = "범례: ○ 추가 + 초록 빗금, □ 삭제 − 빨강 파선, ◇ 변경 ~ 노랑 이전 점선 이후 실선";
            using var path = Theme.RoundRect(new RectangleF(0, 0, Width, Height), 16);
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 15.5f))
            using (var p = new Pen(Theme.Over(Color.FromArgb(20, 255, 255, 255), Theme.Bg), 1f)) g.DrawPath(p, path);
            CanvasPainter.PaintLegend(g, new RectangleF(14, 0, Width - 20, Height), PaintStyle.Screen);
        }
    }
}
