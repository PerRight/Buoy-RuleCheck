// 항목 카드(명세 6-2, 디자인 보드 05): 판정 오브(18) · 항목명 · 상태 글자 / 고칠 내용(제목) · 조건 / 게이지(기준·현재·최악 조건)
// / 기여 객체 목록 / [부표로 이동] · 규정 번호 · 재계산|캐시. 카드 띠·칠한 배지 없이 점과 글자로만 상태를 보인다.
// 카드에 커서를 올리면 도면에 근거(시선·허용선)를, 객체 줄에 올리면 그 객체를 강조하고, 줄을 누르면 그 객체로 이동한다.
// 판정·수치는 엔진이 준 그대로(표시용 fix·anchor) — 여기서는 고르고 배치만 한다.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    public sealed class RuleCard : Control, IFits
    {
        /// 카드 hover: 도면에 근거(규칙, 대표 객체 handle, 켜짐)
        public event Action<RuleResultDto, string?, bool>? HoverChanged;
        /// [부표로 이동]: 그 항목의 부표(없으면 대상 객체)로 화면 이동 + 카드 고정
        public event Action<RuleResultDto, ElementCheckDto?>? BuoyRequested;
        /// 기여 객체 줄 hover / 클릭
        public event Action<ElementCheckDto, bool>? ElementHover;
        public event Action<ElementCheckDto>? ElementClicked;

        private readonly RuleResultDto _r;
        private readonly ElementCheckDto? _decisive;
        private readonly string _status;        // fail / warn / pass / skip
        private readonly string _name, _title, _sub;
        private readonly List<double>? _lim, _act;
        private readonly List<ElementCheckDto> _rows;
        private readonly string? _more;
        private readonly bool _moreWarn;
        private readonly PillButton? _buoyBtn;
        private readonly OrbGeometry _orb;
        private bool _hover, _flash;
        private int _hoverRow = -1;
        private readonly Timer _flashTimer = new() { Interval = 1200 };

        private const int Pad = 14, Gap = 11, RowH = 28, MaxRows = 40;
        // 배치(FitTo에서 계산)
        private Rectangle _rTitle, _rSub, _rGaugeTop, _rBar, _rGaugeBottom, _rMore, _rFoot;
        private int _ySep = -1;
        private readonly List<Rectangle> _rowRects = new();

        public RuleCard(RuleResultDto r, bool failOnly = false)
        {
            _r = r;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Margin = new Padding(0, 0, 0, 10);
            _status = r.Status switch { "fail" => "fail", "pass" => "pass", "skip" => "skip", _ => "warn" };
            _name = UiText.Short(r.RuleId) != r.RuleId ? UiText.Short(r.RuleId) : r.TitleKo;
            (_title, _sub) = UiText.Headline(r);
            _decisive = UiText.Decisive(r);
            if (r.Status is "fail" or "pass")
            {
                _lim = UiText.Numbers(r.Limit);
                _act = UiText.Numbers(r.Actual);
                if (_lim.Count == 0) _lim = null;
            }
            _orb = new OrbGeometry(new OrbSpec
            {
                Size = 18, Vivid = _status != "skip",
                GapFrom = _status == "warn" ? OrbStates.GapFrom : double.NaN,
                GapTo = _status == "warn" ? OrbStates.GapTo : double.NaN,
            });

            // 기여 객체: 수정 소요 → 확인 필요 → 문제없음. 실도면은 블록을 펼친 하위 객체가 수천 개 → 블록 하나를 한 줄로(대표 = 가장 나쁜 것).
            var all = r.Elements.Where(e => !failOnly || e.Status == "fail").ToList();
            var rows = all.GroupBy(e => Contract.BaseHandle(e.ElementId))
                .Select(g =>
                {
                    var worst = g.OrderBy(e => Theme.StatusRank(e.Status)).ThenByDescending(e => e.Fix?.Delta ?? double.NegativeInfinity)
                                 .ThenBy(e => e.Margin ?? double.MaxValue).First();
                    if (g.Count() == 1) return worst;
                    int bad = g.Count(e => e.Status == "fail");
                    return new ElementCheckDto
                    {
                        ElementId = g.Key, RuleId = worst.RuleId, RuleName = worst.RuleName, Role = worst.Role,
                        RoleKo = $"{worst.RoleKo} 블록 · 하위 {g.Count()}{(bad > 0 ? $" · 수정 소요 {bad}" : "")}",
                        Status = worst.Status, Required = worst.Required, Actual = worst.Actual, Diff = worst.Diff,
                        Guide = worst.Guide, Margin = worst.Margin, Anchor = worst.Anchor, Fix = worst.Fix,
                    };
                })
                .OrderBy(e => Theme.StatusRank(e.Status)).ThenByDescending(e => e.Fix?.Delta ?? double.NegativeInfinity)
                .ThenBy(e => e.RoleKo).ThenBy(e => e.ElementId, StringComparer.Ordinal).ToList();
            if (rows.Count > MaxRows)
            {
                var rest = rows.Skip(MaxRows).ToList();
                rows = rows.Take(MaxRows).ToList();
                int f = rest.Count(e => e.Status == "fail"), u = rest.Count(e => e.Status == "unknown"), p = rest.Count - f - u;
                _more = $"… 외 {rest.Count}줄 (수정 소요 {f} · 확인 필요 {u} · 문제없음 {p}) — 도면에서 객체에 커서를 올리면 판정이 보여요";
                _moreWarn = f + u > 0;
            }
            _rows = rows;

            if (r.Status != "skip" && (r.Elements.Any(e => e.Anchor != null) || r.Targets.Count > 0))
            {
                _buoyBtn = new PillButton("부표로 이동", PillKind.Ring) { Font = Theme.Small };
                _buoyBtn.Size = _buoyBtn.GetPreferredSize(Size.Empty);
                _buoyBtn.Height = 24;
                _buoyBtn.AccessibleName = $"{_name} 부표로 이동";
                _buoyBtn.Click += (_, _) => BuoyRequested?.Invoke(_r, _decisive ?? _rows.FirstOrDefault());
                Controls.Add(_buoyBtn);
                new ToolTip().SetToolTip(_buoyBtn, "고칠 자리로 화면을 옮기고 부표 카드를 고정해 엽니다(도면은 그대로).");
            }

            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = $"{_name} {Theme.StatusName(r.Status)} — {_title}";
            AccessibleDescription = string.Join(" / ", new[] { _sub }.Concat(_rows.Take(8).Select(e => $"{UiText.RowLabel(e)} {UiText.RowValue(e)}")));
            _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); _flash = false; Invalidate(); };
        }

        public string RuleId => _r.RuleId;
        public RuleResultDto Result => _r;

        /// '근거 ›'(부표 카드)에서 왔을 때 잠깐 링을 밝혀 어느 카드인지 보인다.
        public void Flash()
        {
            _flash = true;
            Invalidate();
            _flashTimer.Stop();
            _flashTimer.Start();
        }

        // ── 배치 ────────────────────────────────────────────────────────────────

        public void FitTo(int width)
        {
            Width = Math.Max(width, 200);
            int x = Pad, w = Width - Pad * 2;
            int y = Pad + 20 + Gap;
            int th = Math.Max(Ink.Height(_title, Theme.Title, w), 20);
            _rTitle = new Rectangle(x, y, w, th);
            y += th + 3;
            int sh = Ink.Height(_sub, Theme.Small, w);
            _rSub = new Rectangle(x, y, w, sh);
            y += sh;
            if (_lim != null)
            {
                y += Gap;
                _rGaugeTop = new Rectangle(x, y, w, 15);
                y += 15 + 5;
                _rBar = new Rectangle(x, y, w, 10);
                y += 10 + 5;
                _rGaugeBottom = new Rectangle(x, y, w, 18);
                y += 18;
            }
            _rowRects.Clear();
            _ySep = -1;
            if (_rows.Count > 0)
            {
                y += Gap;
                _ySep = y;
                y += 8;
                foreach (var _ in _rows)
                {
                    _rowRects.Add(new Rectangle(x, y, w, RowH));
                    y += RowH;
                }
                if (_more != null)
                {
                    int mh = Ink.Height(_more, Theme.Small, w - 8);
                    _rMore = new Rectangle(x + 8, y + 4, w - 8, mh);
                    y += mh + 6;
                }
            }
            y += Gap;
            _rFoot = new Rectangle(x, y, w, 24);
            if (_buoyBtn != null) _buoyBtn.Location = new Point(x, y);
            y += 24 + Pad;
            Height = y;
            Invalidate();
        }

        // ── 마우스 ──────────────────────────────────────────────────────────────

        private int RowAt(Point p)
        {
            for (int i = 0; i < _rowRects.Count; i++) if (_rowRects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetHover(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetRow(-1);
            // 자식(버튼)으로 옮겨 간 것은 카드 안 — 근거 표시를 유지한다
            if (!IsDisposed && !ClientRectangle.Contains(PointToClient(Cursor.Position))) SetHover(false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHover(true);
            int i = RowAt(e.Location);
            SetRow(i);
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = RowAt(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left) ElementClicked?.Invoke(_rows[i]);
        }

        private void SetHover(bool on)
        {
            if (_hover == on) return;
            _hover = on;
            Invalidate();
            HoverChanged?.Invoke(_r, _decisive?.ElementId, on);
        }

        private void SetRow(int i)
        {
            if (_hoverRow == i) return;
            if (_hoverRow >= 0 && _hoverRow < _rows.Count) ElementHover?.Invoke(_rows[_hoverRow], false);
            _hoverRow = i;
            if (i >= 0) ElementHover?.Invoke(_rows[i], true);
            Invalidate();
        }

        // ── 그리기 ──────────────────────────────────────────────────────────────

        private Color StatusInk => Ink.Of(_status);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            Ink.Card(g, ClientRectangle, _hover || _flash);
            if (_flash)
                using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12))
                using (var p = new Pen(Theme.Muted, 1f)) g.DrawPath(p, path);
            int x = Pad, w = Width - Pad * 2;

            // 머리: 오브 18(멈춘 자세, 판정 색) · 항목명 · 상태 글자
            OrbRenderer.Draw(g, _orb, 0.0, new PointF(x, Pad + 1), new OrbLook { Dots = StatusInk });
            string st = Theme.StatusName(_r.Status);
            int sw = Ink.Width(st, Ink.StatusBold);
            TextRenderer.DrawText(g, st, Ink.StatusBold, new Rectangle(x + w - sw, Pad, sw, 20), StatusInk,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _name, Ink.Head, new Rectangle(x + 27, Pad, w - 27 - sw - 8, 20), Theme.Text,
                TextFormatFlags.VerticalCenter | Ink.Line);

            // 제목 · 조건
            TextRenderer.DrawText(g, _title, Theme.Title, _rTitle, Theme.Text, Ink.Wrap);
            TextRenderer.DrawText(g, _sub, Theme.Small, _rSub, Theme.Muted, Ink.Wrap);

            if (_lim != null) DrawGauge(g);

            // 기여 객체
            if (_ySep >= 0)
            {
                using (var p = new Pen(Theme.Line, 1f)) g.DrawLine(p, x, _ySep, x + w, _ySep);
                for (int i = 0; i < _rows.Count; i++) DrawRow(g, _rows[i], _rowRects[i], i == _hoverRow, i == 0 && _rows[i].Status == "fail");
                if (_more != null) TextRenderer.DrawText(g, _more, Theme.Small, _rMore, _moreWarn ? Theme.Warn : Theme.Faint, Ink.Wrap);
            }

            // 꼬리: 규정 번호 · 재계산|캐시
            string foot = $"{UiText.RefOf(_r)} · {(_r.Cached ? "캐시" : "재계산")}";
            int fw = Ink.Width(foot, Ink.Tiny);
            TextRenderer.DrawText(g, foot, Ink.Tiny, new Rectangle(_rFoot.Right - fw, _rFoot.Y, fw, _rFoot.Height), Theme.Faint,
                TextFormatFlags.VerticalCenter | Ink.Line);
        }

        private void DrawGauge(Graphics g)
        {
            var lim = _lim!;
            var act = _act ?? new List<double>();
            double hi = lim[^1], lo = lim.Count >= 2 ? lim[0] : 0;
            bool infinite = act.Count == 0;
            double top = infinite ? hi : act.Max();
            double max = Math.Max(hi, top) * 1.15;
            if (infinite) max = hi * 1.6;
            if (max <= 0) return;
            var bar = _rBar;
            float X(double v) => (float)(bar.X + Math.Clamp(v / max, 0, 1) * bar.Width);

            // 위: 이름 · 기준
            TextRenderer.DrawText(g, UiText.GaugeName(_r.RuleId), Theme.Small, _rGaugeTop, Theme.Faint, Ink.Line | TextFormatFlags.VerticalCenter);
            string limText = UiText.GaugeLimit(_r.RuleId, lim);
            int lw = Ink.Width(limText, Theme.MonoSmall);
            TextRenderer.DrawText(g, limText, Theme.MonoSmall, new Rectangle(_rGaugeTop.Right - lw, _rGaugeTop.Y, lw, _rGaugeTop.Height), Theme.Faint,
                Ink.Line | TextFormatFlags.VerticalCenter);

            // 막대: 2px 바탕 · 허용 구간 채움 · 기준 눈금(흰 잉크) · 현재 점(판정 색)
            g.SmoothingMode = SmoothingMode.None;
            using (var b = new SolidBrush(Theme.Line)) g.FillRectangle(b, bar.X, bar.Y + 4, bar.Width, 2);
            using (var b = new SolidBrush(Theme.GaugeFill)) g.FillRectangle(b, X(lo), bar.Y + 4, X(hi) - X(lo), 2);
            using (var b = new SolidBrush(Theme.Text))
            {
                if (lim.Count >= 2) g.FillRectangle(b, X(lo), bar.Y, 1, 10);
                g.FillRectangle(b, X(hi), bar.Y, 1, 10);
            }
            var col = StatusInk;
            if (infinite) Ink.Dot(g, col, bar.Right - 3, bar.Y + 5, 6);
            else
            {
                double a0 = act.Min(), a1 = act.Max();
                if (a1 - a0 > 1e-9)
                    using (var b = new SolidBrush(col)) g.FillRectangle(b, X(a0), bar.Y + 4, X(a1) - X(a0), 2);
                Ink.Dot(g, col, X(a0), bar.Y + 5, 6);
                if (a1 - a0 > 1e-9) Ink.Dot(g, col, X(a1), bar.Y + 5, 6);
            }

            // 아래: 최악 조건 · 현재값(수정 소요는 밝은 빨강 잉크)
            string note = UiText.GaugeNote(_r);
            string val = UiText.GaugeActual(act);
            int vw = Ink.Width(val, Ink.Value);
            TextRenderer.DrawText(g, note, Theme.Small, new Rectangle(_rGaugeBottom.X, _rGaugeBottom.Y, _rGaugeBottom.Width - vw - 8, _rGaugeBottom.Height),
                Theme.Faint, Ink.Line | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, val, Ink.Value, new Rectangle(_rGaugeBottom.Right - vw, _rGaugeBottom.Y, vw, _rGaugeBottom.Height),
                _status == "fail" ? Theme.FailInk : Theme.Text, Ink.Line | TextFormatFlags.VerticalCenter);
        }

        private static void DrawRow(Graphics g, ElementCheckDto e, Rectangle r, bool hover, bool lead)
        {
            if (hover || lead)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = Theme.RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), 6);
                using var b = new SolidBrush(hover ? Theme.Line : Theme.CardHover);
                g.FillPath(b, path);
            }
            var col = e.Status switch { "fail" => Theme.Fail, "pass" => Theme.Pass, _ => Theme.Warn };
            Ink.Dot(g, col, r.X + 11, r.Y + r.Height / 2f, 6);
            string val = UiText.RowValue(e);
            int vw = Math.Min(Ink.Width(val, Theme.Mono), r.Width / 2);
            var valInk = e.Status switch { "fail" => Theme.FailInk, "unknown" => Theme.Warn, _ => Theme.Faint };
            TextRenderer.DrawText(g, val, Theme.Mono, new Rectangle(r.Right - 8 - vw, r.Y, vw, r.Height), valInk, Ink.Line | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, UiText.RowLabel(e), Theme.Body, new Rectangle(r.X + 26, r.Y, r.Width - 26 - vw - 16, r.Height),
                e.Status == "pass" ? Theme.Muted : Theme.Text, Ink.Line | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _flashTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
