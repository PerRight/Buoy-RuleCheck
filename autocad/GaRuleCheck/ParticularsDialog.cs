// 실도면 연결 1단계(명세 6-5, 디자인 보드 06): 치수·축척 확인 창.
//   머리 '실도면 연결' · '레이어와 객체는 바꾸지 않습니다' / 5단계 진행 줄 / 표(항목 · 도면에서 찾은 값 · 적용 값) / 흘수 조건 표 / [취소] [적용하고 다음]
// 도면 문자에서 엔진이 찾은 후보(/v1/particulars)를 보여 주고, 사람이 확인·수정한 값만 도면 특성(BRC_*)에 쓴다.
// 이미 입력된 값이 있으면 그 값을 먼저 보여 준다(조용히 덮어쓰지 않음). L(ICLL)·밸러스트 흘수는 도면에 없으므로 비워 두고 주황 테로 알린다.
// 확인하지 않은 값으로는 판정하지 않는다 — 빈 칸은 엔진이 '확인 필요'로 멈춘다.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace GaRuleCheck
{
    public sealed class ParticularsDialog : Form
    {
        /// 확인 후 도면 특성에 쓸 값. 빈 칸은 그 특성을 지운다(DrawingProps.Write) — 칸에는 현재 값이 미리 채워져 있으므로 비우지 않으면 그대로
        public Dictionary<string, string> Result { get; } = new();

        private sealed record RowSpec(string Key, string Label, string Cand, string Unit, bool Required, string Missing, bool Confirm = false, string Placeholder = "");

        private static readonly RowSpec[] Rows =
        {
            new("BRC_LOA", "LOA", "loa", "m", true, "도면에 없음 — 직접 입력"),
            new("BRC_LBP", "LBP", "lbp", "m", false, "도면에 없음 · 참고값(AP–FP 간격 대조)"),
            new("BRC_L_LL", "L (ICLL)", "", "m", true, "도면에 없음 · LBP 참고, 선급 계산서 값"),
            new("BRC_SCALE", "축척", "scale", "", true, "도면에 없음 — 1/N로 입력(예: 1/1000)", Confirm: true, Placeholder: "1/1000"),
            new("BRC_SHIP_TYPE", "선종", "ship_type", "", false, "도면에 없음", Confirm: true),
            new("BRC_KEEL_LAID", "용골 거치일", "", "", false, "도면에 없음 · 비우면 1998-07-01 이후로 봄", Placeholder: "YYYY-MM-DD"),
        };

        private readonly Dictionary<string, ValueCell> _cells = new();
        private readonly List<(RowSpec Spec, string Found, bool Conflict, Control Cell)> _rows = new();
        private readonly List<CondRow> _conds = new();
        private readonly Panel _body = new() { AutoScroll = true, BackColor = Theme.Bg };
        private readonly Panel _grid = new() { BackColor = Theme.Bg };
        private readonly Panel _condGrid = new() { BackColor = Theme.Bg };
        private readonly Label _intro, _notes;
        private readonly Button _addCond;
        private readonly Button _ok, _skip, _cancel;
        private string? _units;
        private readonly List<PillButton> _unitButtons = new();
        private Point _dragFrom;
        private bool _dragging;

        private const int Wd = 600, Pad = 20, HeadH = 96, FootH = 58;
        private const int C1 = 120, C3 = 170;

        /// unitless: 도면 단위($INSUNITS)가 없음 → 단위 고르기 줄을 보인다
        public ParticularsDialog(ParticularsResponse? found, IReadOnlyDictionary<string, string> current, bool unitless, string drawingName)
        {
            Text = "실도면 연결 — 1 치수 · 축척";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            KeyPreview = true;
            DoubleBuffered = true;
            AccessibleName = "실도면 연결 1단계 — 치수 · 축척";
            Width = Wd;

            _intro = new Label
            {
                AutoSize = false, ForeColor = Theme.Muted, BackColor = Theme.Bg, Font = Theme.Body,
                Text = (found == null
                    ? "엔진에 연결하지 못해 도면 문자를 읽지 못했어요. 값을 직접 입력하세요."
                    : "도면 문자(치수표)에서 읽은 후보입니다. 맞는지 보고 빈 칸만 채우세요.")
                    + " 확인하지 않은 값으로는 판정하지 않습니다.",
            };
            _body.Controls.Add(_intro);
            _body.Controls.Add(_grid);
            _grid.Paint += PaintGrid;

            // ── 표: 항목 · 도면에서 찾은 값 · 적용 값 ──
            if (unitless)
            {
                _units = current.TryGetValue("BRC_UNITS", out var u) && u.Trim().Length > 0 ? u.Trim().ToLowerInvariant() : null;
                var seg = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, WrapContents = false, Margin = Padding.Empty };
                foreach (var name in new[] { "mm", "cm", "m" })
                {
                    var b = new PillButton(name, PillKind.Ring) { Font = Theme.Small };
                    b.Size = new Size(Math.Max(b.GetPreferredSize(Size.Empty).Width, 40), 24);
                    b.Margin = new Padding(0, 0, 4, 0);
                    b.Click += (_, _) => { _units = name; SyncUnits(); };
                    _unitButtons.Add(b);
                    seg.Controls.Add(b);
                }
                SyncUnits();
                _grid.Controls.Add(seg);
                _rows.Add((new RowSpec("BRC_UNITS", "도면 단위", "", "", true, ""), "단위 없음 ($INSUNITS = 0)", true, seg));
            }
            foreach (var spec in Rows)
            {
                string foundText = spec.Missing;
                string? candValue = null;
                bool conflict = false;
                if (found != null && spec.Cand.Length > 0 && found.Items.TryGetValue(spec.Cand, out var it))
                {
                    if (it.Status == "found")
                    {
                        candValue = spec.Key == "BRC_SCALE" ? "1/" + it.ValueText : it.ValueText;
                        foundText = candValue + (spec.Unit.Length > 0 ? " " + spec.Unit : "");
                    }
                    else if (it.Status == "conflict") { foundText = "값이 여러 개 — 직접 확인"; conflict = true; }
                }
                if (spec.Key == "BRC_L_LL" && found != null && found.Items.TryGetValue("lbp", out var lbp) && lbp.Status == "found")
                    foundText = $"도면에 없음 · LBP {lbp.ValueText} m 참고, 선급 계산서 값";
                if (found == null && spec.Cand.Length > 0) foundText = "도면 문자를 읽지 못함";

                string value = current.TryGetValue(spec.Key, out var cur) && !string.IsNullOrWhiteSpace(cur) ? cur.Trim()
                    : found != null && found.SuggestedProps.TryGetValue(spec.Key, out var sug) ? sug
                    : candValue ?? "";
                var cell = new ValueCell(spec.Label, value, spec.Unit, spec.Required, spec.Placeholder,
                                         confirmed: spec.Confirm && candValue != null && value == candValue);
                cell.Changed += () => _grid.Invalidate();
                _cells[spec.Key] = cell;
                _grid.Controls.Add(cell);
                _rows.Add((spec, foundText, conflict, cell));
            }

            // ── 흘수 조건 ──
            _body.Controls.Add(_condGrid);
            _condGrid.Paint += PaintCondHead;
            IEnumerable<ConditionRow> start;
            if (current.TryGetValue("BRC_CONDITIONS", out var conds) && !string.IsNullOrWhiteSpace(conds))
                start = DrawingProps.ParseConditions(conds);
            else
            {
                var list = (found?.SuggestedConditions ?? new()).Select(cd => new ConditionRow(cd.Name,
                    cd.TAft.ToString("0.###", CultureInfo.InvariantCulture), cd.TFwd.ToString("0.###", CultureInfo.InvariantCulture))).ToList();
                list.Add(new ConditionRow("밸러스트 도착", "", ""));   // 비워 두면 엔진이 '확인 필요'로 멈춘다 — 흘수를 넣거나 줄을 지우세요
                start = list;
            }
            foreach (var c in start) AddCond(c);
            _addCond = Theme.TextButton("+ 조건 추가");
            _addCond.Font = Theme.Small;
            _addCond.Size = _addCond.GetPreferredSize(Size.Empty);
            _addCond.Click += (_, _) => { AddCond(new ConditionRow("", "", "")); Relayout(); _conds[^1].FocusName(); };
            _condGrid.Controls.Add(_addCond);

            var notes = new List<string>
            {
                "가시거리는 보통 밸러스트(선수 흘수가 작은) 상태가 가장 불리해요. 비워 둔 조건은 판정하지 않으니 흘수를 넣거나 줄을 지우세요.",
            };
            if (found != null) notes.AddRange(found.HumanInputsKo.Select(h => "사람 입력: " + h));
            _notes = new Label { AutoSize = false, ForeColor = Theme.Faint, BackColor = Theme.Bg, Font = Theme.Small, Text = string.Join("\n", notes.Select(n => "· " + n)) };
            _body.Controls.Add(_notes);
            Controls.Add(_body);

            // ── 아래: 다음 안내 · 취소 · 건너뛰기 · 적용하고 다음 ──
            _ok = Theme.PrimaryButton("적용하고 다음");
            _skip = Theme.TextButton("이 단계 건너뛰기");
            _cancel = Theme.TextButton("취소");
            _ok.Click += (_, _) => Apply();
            _skip.Click += (_, _) => { DialogResult = DialogResult.Ignore; };
            _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; };
            new ToolTip().SetToolTip(_cancel, "실도면 연결을 멈춥니다(이미 기록한 값은 남음). Esc");
            new ToolTip().SetToolTip(_skip, "이 단계의 값은 그대로 두고 다음 단계로");
            Controls.AddRange(new Control[] { _ok, _skip, _cancel });
            AcceptButton = _ok;
            CancelButton = _cancel;
            Relayout();
        }

        private void SyncUnits()
        {
            foreach (var b in _unitButtons) b.Selected = b.Text == _units;
            _grid.Invalidate();
        }

        private void AddCond(ConditionRow c)
        {
            var row = new CondRow(c);
            row.Changed += () => _condGrid.Invalidate();
            row.Removed += () => { _conds.Remove(row); _condGrid.Controls.Remove(row); row.Dispose(); Relayout(); };
            _conds.Add(row);
            _condGrid.Controls.Add(row);
        }

        // ── 배치 ────────────────────────────────────────────────────────────────

        private const int RowH = 38, CondH = 34;

        private void Relayout()
        {
            int w = Wd - Pad * 2;
            int y = 16;
            _intro.SetBounds(Pad, y, w, Ink.Height(_intro.Text, Theme.Body, w) + 4);
            y = _intro.Bottom + 14;
            // 표
            int gy = 22;
            foreach (var (spec, _, _, cell) in _rows)
            {
                cell.Location = new Point(C1 + (w - C1 - C3) + 0, gy + (RowH - cell.Height) / 2);
                if (cell is ValueCell vc) vc.Width = C3;
                gy += RowH;
            }
            _grid.SetBounds(Pad, y, w, gy + 1);
            y = _grid.Bottom + 20;
            // 흘수 조건
            int cy = 44;
            foreach (var r in _conds)
            {
                r.SetBounds(0, cy, w, CondH);
                cy += CondH;
            }
            _addCond.Location = new Point(-6, cy + 2);
            _condGrid.SetBounds(Pad, y, w, cy + _addCond.Height + 4);
            y = _condGrid.Bottom + 10;
            _notes.SetBounds(Pad, y, w, Ink.Height(_notes.Text, Theme.Small, w) + 4);
            int bodyH = _notes.Bottom + 16;

            int maxBody = Math.Max(300, (Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80) - HeadH - FootH);
            int bh = Math.Min(bodyH, maxBody);
            _body.SetBounds(1, HeadH, Wd - 2, bh);
            _body.AutoScrollMinSize = new Size(0, bodyH);
            Height = HeadH + bh + FootH;
            int fy = Height - FootH + (FootH - _ok.Height) / 2;
            _ok.Location = new Point(Wd - Pad - _ok.Width, fy);
            _skip.Location = new Point(_ok.Left - 6 - _skip.Width, fy);
            _cancel.Location = new Point(_skip.Left - 2 - _cancel.Width, fy);
            using (var path = Theme.RoundRect(new RectangleF(0, 0, Width, Height), 14))
            {
                var old = Region;
                Region = new Region(path);
                old?.Dispose();
            }
            Invalidate(true);
        }

        // ── 그리기 ──────────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 14))
            using (var p = new Pen(Theme.Over(Color.FromArgb(20, 255, 255, 255), Theme.Bg), 1f)) g.DrawPath(p, path);
            LinkSteps.PaintHeader(g, new Rectangle(Pad, 16, Wd - Pad * 2, HeadH - 16), 1);
            using (var p = new Pen(Theme.Line, 1f))
            {
                g.DrawLine(p, 1, HeadH - 1, Width - 2, HeadH - 1);
                g.DrawLine(p, 1, Height - FootH, Width - 2, Height - FootH);
            }
            TextRenderer.DrawText(g, "다음 — 도면에서 측면도 두 구석을 클릭", Theme.Small,
                new Rectangle(Pad, Height - FootH, Math.Max(_cancel.Left - Pad - 8, 40), FootH), Theme.Faint, Ink.Line | TextFormatFlags.VerticalCenter);
        }

        private void PaintGrid(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            int w = _grid.Width;
            int c2w = w - C1 - C3 - 12;
            TextRenderer.DrawText(g, "항목", Theme.Small, new Point(0, 0), Theme.Faint);
            TextRenderer.DrawText(g, "도면에서 찾은 값", Theme.Small, new Point(C1, 0), Theme.Faint);
            TextRenderer.DrawText(g, "적용 값", Theme.Small, new Point(w - C3, 0), Theme.Faint);
            int y = 22;
            using var line = new Pen(Theme.Line, 1f);
            foreach (var (spec, foundText, conflict, cell) in _rows)
            {
                g.DrawLine(line, 0, y, w, y);
                bool empty = cell is ValueCell vc ? vc.IsEmpty : _units == null;
                bool flag = spec.Required && empty;
                int labelH = flag ? 16 : RowH;
                TextRenderer.DrawText(g, spec.Label, Theme.Body, new Rectangle(0, flag ? y + 4 : y, C1 - 8, labelH),
                    Theme.Text, Ink.Line | TextFormatFlags.VerticalCenter);
                if (flag)
                    TextRenderer.DrawText(g, "직접 입력", Ink.Micro, new Point(0, y + 21), Theme.Warn, Ink.Line);
                bool mono = !foundText.StartsWith("도면에", StringComparison.Ordinal) && !conflict && !foundText.StartsWith("단위", StringComparison.Ordinal);
                TextRenderer.DrawText(g, foundText, mono ? Theme.Mono : Theme.Small, new Rectangle(C1, y, c2w, RowH),
                    conflict ? Theme.Warn : Theme.Muted, Ink.Wrap | TextFormatFlags.VerticalCenter);
                y += RowH;
            }
            g.DrawLine(line, 0, y, w, y);
        }

        private void PaintCondHead(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            const string t = "흘수 조건";
            int tw = Ink.Width(t, Theme.Bold);
            TextRenderer.DrawText(g, t, Theme.Bold, new Point(0, 0), Theme.Text, Ink.Line);
            TextRenderer.DrawText(g, " — 가시거리는 이 중 가장 나쁜 조건으로 판정", Theme.Small, new Point(tw, 2), Theme.Faint, Ink.Line);
            TextRenderer.DrawText(g, "조건", Theme.Small, new Point(0, 24), Theme.Faint);
            TextRenderer.DrawText(g, "AP 흘수", Theme.Small, new Point(_condGrid.Width - CondRow.DraftsW, 24), Theme.Faint);
            TextRenderer.DrawText(g, "FP 흘수", Theme.Small, new Point(_condGrid.Width - CondRow.DraftsW + CondRow.Box + 6, 24), Theme.Faint);
        }

        // ── 적용 ────────────────────────────────────────────────────────────────

        private void Apply()
        {
            foreach (var (k, c) in _cells) Result[k] = c.Value;
            Result["BRC_CONDITIONS"] = DrawingProps.FormatConditions(_conds.Select(r => r.Row).Where(r => !string.IsNullOrWhiteSpace(r.Name)));
            if (_unitButtons.Count > 0 && _units != null) Result["BRC_UNITS"] = _units;
            DialogResult = DialogResult.OK;
        }

        // ── 창 옮기기(머리를 끌어서) ────────────────────────────────────────────

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < HeadH) { _dragging = true; _dragFrom = e.Location; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) Location = new Point(Left + e.X - _dragFrom.X, Top + e.Y - _dragFrom.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; }

    }

    /// 실도면 연결 5단계 진행 줄(대화창 머리·도면 단계 안내 띠가 같이 쓴다).
    internal static class LinkSteps
    {
        public static readonly string[] Names = { "치수 · 축척", "측면도 영역", "AP · FP · 기선", "역할 지정", "조종위치" };
        private static readonly Font TitleFont = new(Theme.FontName, 11f, FontStyle.Bold);
        private static readonly Font StepOn = new(Theme.FontName, 8f, FontStyle.Bold);

        /// r 안에: 첫 줄 '실도면 연결' + 오른쪽 '레이어와 객체는 바꾸지 않습니다', 둘째 줄 5칸(2px 막대 + 이름). step = 1~5
        public static void PaintHeader(Graphics g, Rectangle r, int step)
        {
            TextRenderer.DrawText(g, "실도면 연결", TitleFont, new Point(r.X, r.Y), Theme.Text, Ink.Line);
            const string sub = "레이어와 객체는 바꾸지 않습니다";
            int sw = Ink.Width(sub, Theme.Small);
            TextRenderer.DrawText(g, sub, Theme.Small, new Point(r.Right - sw, r.Y + 4), Theme.Faint, Ink.Line);
            int y = r.Y + 34, gap = 6;
            float segW = (r.Width - gap * 4) / 5f;
            for (int i = 0; i < 5; i++)
            {
                float x = r.X + i * (segW + gap);
                bool on = i + 1 == step, done = i + 1 < step;
                using (var b = new SolidBrush(on ? Theme.Text : done ? Theme.Faint : Theme.Line)) g.FillRectangle(b, x, y, segW, 2);
                TextRenderer.DrawText(g, $"{i + 1} {Names[i]}", on ? StepOn : Theme.Small, new Rectangle((int)x, y + 9, (int)segW, 18),
                    on ? Theme.Text : Theme.Faint, Ink.Line);
            }
        }
    }

    /// 적용 값 한 칸: 입력 상자(둥근 테, 빈 필수 칸은 주황 테) + 단위. 도면 값을 그대로 쓰는 칸은 '● 확인' + [고치기].
    internal sealed class ValueCell : Control
    {
        private readonly TextBox _box;
        private readonly string _unit;
        private readonly bool _required;
        private bool _confirmed;
        private readonly LinkLabel _edit;
        public event Action? Changed;

        public ValueCell(string label, string value, string unit, bool required, string placeholder, bool confirmed)
        {
            _unit = unit;
            _required = required;
            _confirmed = confirmed;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = 28;
            _box = new TextBox
            {
                BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Body,
                Text = value, PlaceholderText = string.IsNullOrEmpty(placeholder) ? "입력" : placeholder, AccessibleName = label,
            };
            _box.TextChanged += (_, _) => { Invalidate(); Changed?.Invoke(); };
            _box.GotFocus += (_, _) => Invalidate();
            _box.LostFocus += (_, _) => Invalidate();
            Controls.Add(_box);
            _edit = new LinkLabel
            {
                Text = "고치기", AutoSize = true, Font = Theme.Small, BackColor = Theme.Bg, LinkColor = Theme.Faint, ActiveLinkColor = Theme.Text,
                LinkBehavior = LinkBehavior.HoverUnderline, AccessibleName = label + " 고치기",
            };
            _edit.LinkClicked += (_, _) => { _confirmed = false; Sync(); _box.Focus(); _box.SelectAll(); };
            Controls.Add(_edit);
            AccessibleName = label;
            Sync();
        }

        public string Value => _box.Text.Trim();
        public bool IsEmpty => Value.Length == 0;

        private const int BoxW = 110;

        private void Sync()
        {
            _box.Visible = !_confirmed;
            _edit.Visible = _confirmed;
            AccessibleDescription = _confirmed ? $"도면 값 {Value} 확인" : (_required && IsEmpty ? "비어 있음 — 직접 입력" : "");
            OnResize(EventArgs.Empty);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int th = TextRenderer.MeasureText("가", Theme.Body).Height;
            _box.SetBounds(9, (Height - th) / 2 + 1, BoxW - 18, th);
            _edit.Location = new Point(Ink.Width("● 확인 " + Value, Theme.Body) + 14, (Height - _edit.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_confirmed)
            {
                // 디자인 보드 06: 도면에서 찾은 값을 그대로 쓴다 = 파란 점 + '확인'
                Ink.Dot(g, Theme.Pass, 4, Height / 2f, 6);
                TextRenderer.DrawText(g, $"확인 {Value}", Theme.Body, new Rectangle(12, 0, Width - 12, Height), Theme.Muted, Ink.Line | TextFormatFlags.VerticalCenter);
                return;
            }
            var r = new RectangleF(0.5f, 0.5f, BoxW - 1, Height - 1);
            using (var path = Theme.RoundRect(r, 8))
            {
                using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
                var ring = _box.Focused ? Theme.Muted : _required && IsEmpty ? Theme.Warn : Theme.Over(Theme.ButtonRing, Theme.Bg);
                using var p = new Pen(ring, 1f);
                g.DrawPath(p, path);
            }
            if (_unit.Length > 0) TextRenderer.DrawText(g, _unit, Theme.Body, new Point(BoxW + 6, (Height - 16) / 2), Theme.Faint);
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); if (!_confirmed) _box.Focus(); }
    }

    /// 흘수 조건 한 줄: 이름 · AP 흘수 · FP 흘수 · ×. 흘수가 비면 '비어 있음'(주황) — 그 조건은 판정하지 않는다.
    internal sealed class CondRow : Control
    {
        public const int Box = 90, DraftsW = Box * 2 + 6 + 30;
        private readonly TextBox _name, _aft, _fwd;
        private readonly Button _del;
        public event Action? Changed;
        public event Action? Removed;

        public CondRow(ConditionRow c)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            _name = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.Body, Text = c.Name, PlaceholderText = "조건 이름", AccessibleName = "조건 이름" };
            _aft = Draft(c.TAft, "AP 흘수");
            _fwd = Draft(c.TFwd, "FP 흘수");
            _del = Theme.TextButton("×");
            _del.Size = new Size(26, 24);
            _del.AccessibleName = "조건 줄 삭제";
            _del.Click += (_, _) => Removed?.Invoke();
            foreach (var t in new[] { _name, _aft, _fwd })
            {
                t.TextChanged += (_, _) => { Place(); Invalidate(); Changed?.Invoke(); };
                t.GotFocus += (_, _) => Invalidate();
                t.LostFocus += (_, _) => Invalidate();
            }
            Controls.AddRange(new Control[] { _name, _aft, _fwd, _del });
        }

        private static TextBox Draft(string v, string name) => new()
        {
            BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Mono, Text = v,
            PlaceholderText = "—", AccessibleName = name, TextAlign = HorizontalAlignment.Left,
        };

        public ConditionRow Row => new(_name.Text.Trim(), _aft.Text.Trim(), _fwd.Text.Trim());
        private bool Empty => _aft.Text.Trim().Length == 0 || _fwd.Text.Trim().Length == 0;
        public void FocusName() => _name.Focus();

        private Rectangle AftR => new(Width - DraftsW, 3, Box, Height - 6);
        private Rectangle FwdR => new(Width - DraftsW + Box + 6, 3, Box, Height - 6);

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Place();
        }

        private void Place()
        {
            int th = TextRenderer.MeasureText("가", Theme.Body).Height;
            int nameW = Width - DraftsW - 8 - (Empty ? Ink.Width("비어 있음", Ink.Micro) + 10 : 0);
            _name.SetBounds(0, (Height - th) / 2, Math.Max(nameW, 60), th);
            _aft.SetBounds(AftR.X + 8, (Height - th) / 2 + 1, Box - 14, th);
            _fwd.SetBounds(FwdR.X + 8, (Height - th) / 2 + 1, Box - 14, th);
            _del.Location = new Point(Width - 28, (Height - _del.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool empty = Empty && _name.Text.Trim().Length > 0;
            if (empty)
            {
                int nw = Math.Min(Ink.Width(_name.Text, Theme.Body) + 8, Width - DraftsW - 70);
                TextRenderer.DrawText(g, "비어 있음", Ink.Micro, new Rectangle(nw, 0, 70, Height), Theme.Warn, Ink.Line | TextFormatFlags.VerticalCenter);
            }
            foreach (var (r, box) in new[] { (AftR, _aft), (FwdR, _fwd) })
            {
                using var path = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 8);
                using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
                var ring = box.Focused ? Theme.Muted : empty && box.Text.Trim().Length == 0 ? Theme.Over(Color.FromArgb(153, Theme.Warn), Theme.Bg) : Theme.Over(Theme.ButtonRing, Theme.Bg);
                using var p = new Pen(ring, 1f);
                g.DrawPath(p, path);
            }
        }
    }
}
