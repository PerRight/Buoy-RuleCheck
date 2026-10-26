// 부표 카드(명세 6-1): 부표에 커서를 올리면 고칠 내용을 보여 준다. 폭 280, 160 ms 나타남, 커서가 떠나면 150 ms 유예.
//   수정 소요: 머리(● 수정 소요 · 항목명 · 닫기) → 제목(상단을 1.55 m 낮추세요) → 조건 → 표(현재·기준·차이) → 근거 줄 + '근거 ›'
//   문제없음: 상단 z 21.40 m — 기준 안쪽 · 여유·기준 · "마우스를 떼면 표시가 사라져요"
//   확인 필요: 사유 + "빈 입력이 채워지기 전에는 판정하지 않아요"
//   묶음: 부표 목록(누르면 그 자리로) + [맞춰 확대]
// 핀을 누르면 고정(다른 부표를 올려도 남음), ×로 닫는다. 숫자는 엔진 값(fix)을 그대로 쓴다 — 계산하지 않는다.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    public sealed record BuoyCardItem(string Status, string RuleId, string? RuleCondition, ElementCheckDto Element);

    public sealed class BuoyCard : Form
    {
        public const int CardW = OrbStates.CardWidth;
        private readonly List<BuoyCardItem> _items;
        private bool _pinned;
        private readonly Timer _fade = new() { Interval = 16 };
        private readonly Timer _grace = new() { Interval = (int)OrbStates.CardGraceMs };
        private DateTime _t0;
        private readonly List<(Rectangle r, Action act, string tip)> _hits = new();
        private Rectangle _hover = Rectangle.Empty;
        private readonly ToolTip _tip = new();

        /// '근거 ›' → 팔레트에서 그 항목 열기
        public event Action<BuoyCardItem>? EvidenceRequested;
        /// 묶음 목록에서 한 줄 → 그 부표로 이동, [맞춰 확대] → 모두 들어오게
        public event Action<IReadOnlyList<BuoyCardItem>>? ZoomRequested;
        public event Action<BuoyCard>? Closed2;
        public bool Pinned => _pinned;
        public bool MouseInside => Visible && Bounds.Contains(Cursor.Position);

        private static readonly Font HeadFont = new(Theme.FontName, 8.5f, FontStyle.Bold);
        private static readonly Font SubFont = new(Theme.FontName, 8.5f);
        private static readonly Font Tiny = new(Theme.FontName, 7.5f);
        private static readonly Font PassTitle = new(Theme.FontName, 10.5f, FontStyle.Bold);

        public BuoyCard(IReadOnlyList<BuoyCardItem> items)
        {
            _items = items.ToList();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Card;
            DoubleBuffered = true;
            KeyPreview = true;
            AccessibleRole = AccessibleRole.Dialog;
            AccessibleName = Product.Name + " — " + Describe();
            Opacity = 0;
            Size = new Size(CardW, Measure());
            UpdateRegion();
            _fade.Tick += (_, _) =>
            {
                double p = Math.Clamp((DateTime.UtcNow - _t0).TotalMilliseconds / OrbStates.CardInMs, 0, 1);
                Opacity = Motion.Reduced ? 1 : CubicBezier.EaseOut.Ease(p);
                if (p >= 1) _fade.Stop();
            };
            _grace.Tick += (_, _) =>
            {
                _grace.Stop();
                if (!_pinned && !MouseInside) Close();
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000;   // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE: 도면 포커스를 빼앗지 않게
                return cp;
            }
        }

        private string Describe()
        {
            if (_items.Count > 1) return $"부표 {_items.Count}개 묶음";
            var it = _items[0];
            return $"{Theme.StatusName(it.Status == "warn" ? "unknown" : it.Status)} — {UiText.Short(it.RuleId)} — {it.Element.Fix?.ActionKo ?? it.Element.Guide ?? ""}";
        }

        /// 커서 근처(부표 왼쪽 위)에 띄운다. 화면 밖으로 나가지 않게.
        public void ShowNear(Point cursor, IWin32Window owner)
        {
            var wa = Screen.FromPoint(cursor).WorkingArea;
            int x = cursor.X - Width - 14, y = cursor.Y - Height / 2;
            if (x < wa.Left) x = cursor.X + 22;
            y = Math.Clamp(y, wa.Top + 4, wa.Bottom - Height - 4);
            x = Math.Clamp(x, wa.Left + 4, wa.Right - Width - 4);
            Location = new Point(x, y);
            _t0 = DateTime.UtcNow;
            if (!Visible) Show(owner);
            if (!Motion.Reduced) _fade.Start(); else Opacity = 1;
        }

        /// 커서가 부표를 떠났다 — 150 ms 유예 뒤 닫는다(카드 위로 옮겨 오면 남는다)
        public void LeaveSoon()
        {
            if (_pinned) return;
            _grace.Stop();
            _grace.Start();
        }

        public void StayOpen() => _grace.Stop();

        /// 미리보기용: 처음부터 고정
        public void Pinned2() { _pinned = true; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _grace.Stop(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = Rectangle.Empty;
            Invalidate();
            if (!_pinned && !Bounds.Contains(Cursor.Position)) LeaveSoon();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = _hits.FirstOrDefault(x => x.r.Contains(e.Location));
            if (h.r != _hover)
            {
                _hover = h.r;
                Cursor = h.act != null ? Cursors.Hand : Cursors.Default;
                _tip.SetToolTip(this, h.tip ?? "");
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            foreach (var (r, act, _) in _hits.ToList())
                if (r.Contains(e.Location)) { act(); return; }
            base.OnMouseClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _fade.Dispose();
            _grace.Dispose();
            _tip.Dispose();
            Closed2?.Invoke(this);
            base.OnFormClosed(e);
        }

        private void UpdateRegion()
        {
            using var path = Theme.RoundRect(new RectangleF(0, 0, Width, Height), 12);
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }

        // ── 배치 ────────────────────────────────────────────────────────────────
        private const int Pad = 12;

        private int Measure()
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            return LayoutCard(g, paint: false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int h = LayoutCard(g, paint: true);
            using (var p = new Pen(Theme.RingOnCard, 1f)) using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12)) g.DrawPath(p, path);
            if (h != Height) { Height = h; UpdateRegion(); }
        }

        private int LayoutCard(Graphics g, bool paint)
        {
            _hits.Clear();
            int y = 10, w = CardW - 2 * Pad;
            if (_items.Count > 1) return LayoutCluster(g, paint, y, w);
            var it = _items[0];
            var el = it.Element;
            string st = it.Status;
            var col = BuoyPainter.ColorOf(st);

            // 머리: ● 상태 · 항목명 · 핀 · 닫기
            if (paint)
            {
                using (var b = new SolidBrush(col)) g.FillEllipse(b, Pad, y + 4, 7, 7);
                TextRenderer.DrawText(g, Theme.StatusName(st == "warn" ? "unknown" : st), HeadFont, new Point(Pad + 11, y), col);
            }
            int right = CardW - Pad;
            right = IconButton(g, paint, right, y, "×", "닫기 (Esc)", Close);
            right = IconButton(g, paint, right, y, _pinned ? "●" : "○", _pinned ? "고정 풀기" : "고정 — 다른 부표를 봐도 남김", () => { _pinned = !_pinned; _grace.Stop(); Invalidate(); });
            if (paint)
            {
                string name = UiText.Short(it.RuleId);
                var sz = TextRenderer.MeasureText(name, Tiny);
                TextRenderer.DrawText(g, name, Tiny, new Point(right - sz.Width - 2, y + 1), Theme.Faint);
            }
            y += 20;

            var fx = el.Fix;
            if (st == "warn")
            {
                y = TextBlock(g, paint, "입력을 확인하세요", Theme.Title, Theme.Text, y, w);
                y = TextBlock(g, paint, el.Guide ?? "판정에 필요한 입력이 비어 있습니다.", SubFont, Theme.Muted, y + 2, w);
                y = TextBlock(g, paint, "빈 입력이 채워지기 전에는 판정하지 않아요", Tiny, Theme.Faint, y + 6, w);
                return y + 10;
            }
            if (fx == null)
            {
                y = TextBlock(g, paint, el.Guide ?? el.Actual, Theme.Title, Theme.Text, y, w);
                y = TextBlock(g, paint, "기준 " + el.Required, SubFont, Theme.Muted, y + 2, w);
                return y + 10;
            }
            // 제목·조건
            y = TextBlock(g, paint, fx.ActionKo, st == "pass" ? PassTitle : Theme.Title, Theme.Text, y, w);
            y = TextBlock(g, paint, fx.ConditionKo, SubFont, Theme.Muted, y + 2, w);
            if (st == "pass")
            {
                y = TextBlock(g, paint, "마우스를 떼면 표시가 사라져요", Tiny, Theme.Faint, y + 6, w);
                return y + 10;
            }
            // 표: 현재 · 기준 · 차이
            y += 7;
            if (paint) using (var p = new Pen(Theme.Line, 1f)) g.DrawLine(p, Pad, y, CardW - Pad, y);
            y += 6;
            foreach (var (k, v, c) in new[] { ("현재", UiText.Current(fx), Theme.Text), ("기준", UiText.Limit(fx), Theme.Text), ("차이", UiText.Delta(fx), (fx.Delta ?? 0) > 0 ? Theme.FailInk : Theme.Muted) })
            {
                if (paint)
                {
                    TextRenderer.DrawText(g, k, Theme.MonoSmall, new Point(Pad, y), Theme.Faint);
                    TextRenderer.DrawText(g, v, Theme.Mono, new Point(Pad + 40, y - 1), c);
                }
                y += 17;
            }
            // 근거 줄
            y += 4;
            string basis = UiText.Basis(it.RuleId, fx, it.RuleCondition);
            if (paint) TextRenderer.DrawText(g, basis, Tiny, new Point(Pad, y + 1), Theme.Faint);
            const string link = "근거 ›";
            var lsz = TextRenderer.MeasureText(link, SubFont);
            var lr = new Rectangle(CardW - Pad - lsz.Width, y - 1, lsz.Width, lsz.Height);
            _hits.Add((lr, () => EvidenceRequested?.Invoke(it), "팔레트에서 이 항목의 근거·게이지 보기"));
            if (paint) TextRenderer.DrawText(g, link, SubFont, lr.Location, _hover == lr ? Theme.Text : Theme.Muted);
            return y + lsz.Height + 10;
        }

        private int LayoutCluster(Graphics g, bool paint, int y, int w)
        {
            int f = _items.Count(i => i.Status == "fail");
            string head = f > 0 ? $"수정 소요 {f}" : $"부표 {_items.Count}";
            var col = BuoyPainter.ColorOf(_items.OrderBy(i => Theme.StatusRank(i.Status == "warn" ? "unknown" : i.Status)).First().Status);
            if (paint)
            {
                using (var b = new SolidBrush(col)) g.FillEllipse(b, Pad, y + 4, 7, 7);
                TextRenderer.DrawText(g, head, HeadFont, new Point(Pad + 11, y), col);
                TextRenderer.DrawText(g, $"이 근처 {_items.Count}곳", Tiny, new Point(Pad + 90, y + 1), Theme.Faint);
            }
            IconButton(g, paint, CardW - Pad, y, "×", "닫기 (Esc)", Close);
            y += 22;
            foreach (var it in _items.Take(8))
            {
                var r = new Rectangle(Pad - 4, y - 2, w + 8, 22);
                var item = it;
                _hits.Add((r, () => ZoomRequested?.Invoke(new[] { item }), "이 부표로 이동"));
                if (paint)
                {
                    if (_hover == r) using (var hb = new SolidBrush(Theme.CardHover)) using (var hp = Theme.RoundRect(r, 6)) g.FillPath(hb, hp);
                    using (var b = new SolidBrush(BuoyPainter.ColorOf(it.Status))) g.FillEllipse(b, Pad, y + 6, 6, 6);
                    string name = $"{it.Element.RoleKo} ({it.Element.ElementId})";
                    TextRenderer.DrawText(g, name, Theme.Body, new Rectangle(Pad + 12, y, w - 90, 18), Theme.Text, TextFormatFlags.EndEllipsis);
                    string d = it.Element.Fix != null ? UiText.Delta(it.Element.Fix) : "확인 필요";
                    var dsz = TextRenderer.MeasureText(d, Theme.Mono);
                    TextRenderer.DrawText(g, d, Theme.Mono, new Point(CardW - Pad - dsz.Width, y), it.Status == "fail" ? Theme.FailInk : Theme.Muted);
                }
                y += 22;
            }
            if (_items.Count > 8 && paint) TextRenderer.DrawText(g, $"… 외 {_items.Count - 8}곳", Tiny, new Point(Pad, y), Theme.Faint);
            if (_items.Count > 8) y += 16;
            y += 6;
            var btn = new Rectangle(Pad, y, 92, 26);
            _hits.Add((btn, () => ZoomRequested?.Invoke(_items), "묶인 부표가 모두 들어오게 확대"));
            if (paint)
            {
                using var path = Theme.RoundRect(btn, 13);
                using (var b = new SolidBrush(_hover == btn ? Color.White : Theme.Text)) g.FillPath(b, path);
                TextRenderer.DrawText(g, "맞춰 확대", Theme.Bold, btn, Theme.Bg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            return y + 26 + 12;
        }

        private int IconButton(Graphics g, bool paint, int right, int y, string glyph, string tip, Action act)
        {
            var r = new Rectangle(right - 18, y - 1, 18, 18);
            _hits.Add((r, act, tip));
            if (paint)
            {
                if (_hover == r) using (var hb = new SolidBrush(Theme.CardHover)) g.FillEllipse(hb, r);
                TextRenderer.DrawText(g, glyph, Theme.Body, r, _hover == r ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            return r.Left - 4;
        }

        private static int TextBlock(Graphics g, bool paint, string text, Font font, Color color, int y, int w)
        {
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            var sz = TextRenderer.MeasureText(g, text, font, new Size(w, 0), flags);
            if (paint) TextRenderer.DrawText(g, text, font, new Rectangle(Pad, y, w, sz.Height), color, flags);
            return y + sz.Height;
        }
    }
}
