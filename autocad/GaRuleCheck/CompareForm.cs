// 버전 비교 창(GARCCOMPARE): 두 버전을 골라 나란히 / 겹쳐 / 차이만 보기, 번호 표식, 변경 목록, 생산팀 전달 내보내기.
// 무엇이 바뀌었고 무엇이 해결됐는지는 엔진(/v1/compare)이 계산한다. 이 창은 고르고·그리고·내보내기만 한다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;

namespace GaRuleCheck
{
    public sealed class CompareForm : Form
    {
        private const string Live = "__live__";
        private readonly Document _doc;
        private readonly string _folder;
        private VersionIndex _index = new();
        private readonly Action<CompareResponse?> _showOnDrawing;
        private readonly Action<ChangeDto, double> _focusInDrawing;

        private readonly ComboBox _a = Combo(), _b = Combo();
        private readonly Button _side, _over, _diff, _export, _onDwg;
        private readonly TrackBar _opacity = new() { Minimum = 0, Maximum = 100, Value = 60, TickStyle = TickStyle.None, Width = 110, Height = 26 };
        private readonly Label _opLabel = Theme.Label("투명도 60%", Theme.Small, Theme.Muted);
        private readonly Label _summary = new() { Dock = DockStyle.Top, Height = 44, Font = Theme.Body, ForeColor = Theme.Text, BackColor = Theme.Card, Padding = new Padding(12, 4, 12, 4) };
        private readonly CompareCanvas _left = new() { Dock = DockStyle.Fill, Mode = CanvasMode.Before };
        private readonly CompareCanvas _right = new() { Dock = DockStyle.Fill, Mode = CanvasMode.After };
        private readonly CompareCanvas _main = new() { Dock = DockStyle.Fill, Mode = CanvasMode.Diff };
        private readonly TableLayoutPanel _sideBySide = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
        private readonly FlowLayoutPanel _list = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(8) };
        private readonly ViewState _view = new();
        private CanvasMode _mode = CanvasMode.Diff;
        private CompareResponse? _data;
        private bool _drawOnDwg;
        private bool _loading;

        public CompareForm(Document doc, string folder, Action<CompareResponse?> showOnDrawing, Action<ChangeDto, double> focusInDrawing)
        {
            _doc = doc;
            _folder = folder;
            _showOnDrawing = showOnDrawing;
            _focusInDrawing = focusInDrawing;
            Text = $"버전 비교 — {Path.GetFileName(doc.Name)}";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Size = new Size(1280, 780);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            KeyPreview = true;

            // ── 도구 줄 ──
            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Bg, Padding = new Padding(10, 8, 8, 0), WrapContents = false };
            _side = Toggle("나란히 보기");
            _over = Toggle("겹쳐 보기");
            _diff = Toggle("차이만 강조");
            _onDwg = Toggle("도면에 표시");
            _export = Theme.FlatButton("생산팀 전달", Theme.Info);
            var tip = new ToolTip();
            tip.SetToolTip(_side, "이전·이후를 좌우로. 한쪽을 확대·이동하면 다른 쪽도 같이 움직입니다.");
            tip.SetToolTip(_over, "이전 위에 이후를 겹칩니다. 슬라이더로 이후의 투명도를 조절합니다.");
            tip.SetToolTip(_diff, "추가 초록(빗금) · 삭제 빨강(파선) · 변경 노랑(이전 점선/이후 실선)");
            tip.SetToolTip(_onDwg, "변경 위치를 AutoCAD 도면 위에 임시 표시합니다(도면은 바뀌지 않음).");
            tip.SetToolTip(_export, "도면 번호·버전·날짜·작성자·차이 그림·변경 목록·범례를 PDF 또는 PNG 한 장으로.");
            tools.Controls.AddRange(new Control[]
            {
                Theme.Label("이전", Theme.Bold), _a, Theme.Label("→  이후", Theme.Bold), _b, Spacer(14),
                _side, _over, _diff, Spacer(6), _opLabel, _opacity, Spacer(10), _onDwg, _export,
            });
            foreach (Control c in tools.Controls) if (c is Label l) l.Margin = new Padding(0, 5, 6, 0);
            _opacity.Margin = new Padding(0, 2, 6, 0);

            // ── 본문: 그림 | 변경 목록 ──
            var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Line, SplitterWidth = 4, FixedPanel = FixedPanel.Panel2 };
            split.Panel1.BackColor = Theme.Bg;
            split.Panel2.BackColor = Theme.Bg;
            _sideBySide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _sideBySide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _sideBySide.Controls.Add(_left, 0, 0);
            _sideBySide.Controls.Add(_right, 1, 0);
            _left.Margin = new Padding(0, 0, 2, 0);
            _right.Margin = new Padding(2, 0, 0, 0);
            split.Panel1.Controls.Add(_main);
            split.Panel1.Controls.Add(_sideBySide);
            var listTitle = new Label { Dock = DockStyle.Top, Height = 30, Text = "변경 내역", Font = Theme.Title, ForeColor = Theme.Text, Padding = new Padding(10, 6, 0, 0) };
            split.Panel2.Controls.Add(_list);
            split.Panel2.Controls.Add(listTitle);

            var legend = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Theme.Card };
            legend.Paint += (_, e) => CanvasPainter.PaintLegend(e.Graphics, new RectangleF(12, 0, legend.Width - 24, legend.Height), PaintStyle.Screen);

            Controls.Add(split);
            Controls.Add(legend);
            Controls.Add(_summary);
            Controls.Add(tools);
            Shown += (_, _) =>
            {
                split.SplitterDistance = Math.Max(split.Width - 380, 300);
                // 창이 보이기 전엔 그림 칸 크기가 0이라 맞춤이 안 된다 → 보인 뒤 한 번 더
                if (_data?.Bounds is double[] b) _view.Fit(b, _mode == CanvasMode.Before ? _left.ClientSize : _main.ClientSize);
            };

            // ── 동작 ──
            _side.Click += (_, _) => SetMode(CanvasMode.Before);
            _over.Click += (_, _) => SetMode(CanvasMode.Overlay);
            _diff.Click += (_, _) => SetMode(CanvasMode.Diff);
            _opacity.Scroll += (_, _) =>
            {
                _main.OverlayOpacity = _opacity.Value / 100f;
                _opLabel.Text = $"투명도 {_opacity.Value}%";
                _main.Invalidate();
            };
            _onDwg.Click += (_, _) =>
            {
                _drawOnDwg = !_drawOnDwg;
                Mark(_onDwg, _drawOnDwg);
                _showOnDrawing(_drawOnDwg ? _data : null);
            };
            _export.Click += (_, _) => Export();
            _a.SelectedIndexChanged += (_, _) => Reload();
            _b.SelectedIndexChanged += (_, _) => Reload();
            foreach (var cv in new[] { _left, _right, _main })
            {
                cv.MarkerClicked += Select;
                cv.ChangeHovered += c => SetHighlight(c?.Number);
            }
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            FormClosed += (_, _) => _showOnDrawing(null);

            LoadVersions();
            SetMode(CanvasMode.Diff);
        }

        private void LoadVersions()
        {
            _loading = true;
            try
            {
                _index = VersionStore.Load(_folder);
                var items = _index.Versions.OrderBy(v => v.Version)
                    .Select(v => new Item(VersionStore.Describe(v), v.Version.ToString())).ToList();
                items.Add(new Item("현재 도면 (저장 전 상태 포함)", Live));
                foreach (var cb in new[] { _a, _b })
                {
                    cb.Items.Clear();
                    cb.Items.AddRange(items.Cast<object>().ToArray());
                }
                int n = _index.Versions.Count;
                // 기본: 직전 버전 → 현재 버전. 버전이 하나면 그 버전 → 현재 도면.
                _a.SelectedIndex = n >= 2 ? n - 2 : 0;
                _b.SelectedIndex = n >= 2 ? n - 1 : items.Count - 1;
            }
            catch (Exception ex)
            {
                _summary.Text = "버전 기록을 읽지 못했습니다: " + ex.Message;
                _summary.ForeColor = Theme.Warn;
            }
            finally { _loading = false; }
            Reload();
        }

        private (RawDrawing raw, VersionEntry? entry, string label) Pick(ComboBox cb)
        {
            var it = (Item)cb.SelectedItem!;
            if (it.Key == Live)
            {
                using (_doc.LockDocument()) return (Extractor.Extract(_doc), null, "현재 도면");
            }
            var v = _index.Versions.First(x => x.Version.ToString() == it.Key);
            return (VersionStore.LoadRaw(_folder, v), v, VersionStore.Label(v));
        }

        private VersionEntry? _ea, _eb;
        private string _la = "", _lb = "";

        private void Reload()
        {
            if (_loading || _a.SelectedItem == null || _b.SelectedItem == null) return;
            if (_index.Versions.Count == 0)
            {
                _summary.Text = "아직 버전 기록이 없습니다. 도면을 저장하면(또는 GARCVERSION) 버전이 생깁니다. 지금은 '현재 도면'만 있습니다.";
                _summary.ForeColor = Theme.Muted;
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                var (ra, ea, la) = Pick(_a);
                var (rb, eb, lb) = Pick(_b);
                _ea = ea; _eb = eb; _la = la; _lb = lb;
                _data = EngineClient.Compare(ra, rb, la, lb);
                foreach (var cv in new[] { _left, _right, _main }) cv.Bind(_data, _view);
                _left.Caption = $"이전 · {la}";
                _right.Caption = $"이후 · {lb}";
                _view.Fit(_data.Bounds ?? new double[] { 0, 0, 1, 1 }, _mode == CanvasMode.Before ? _left.ClientSize : _main.ClientSize);
                FillList();
                var resolved = _data.ResolvedRules.Count > 0 ? "  ·  해결: " + string.Join(", ", _data.ResolvedRules.Select(x => x.RuleName)) : "";
                var intro = _data.IntroducedRules.Count > 0 ? "  ·  새 위반: " + string.Join(", ", _data.IntroducedRules.Select(x => x.RuleName)) : "";
                _summary.ForeColor = _data.Violations.After > 0 ? Theme.Warn : Theme.Text;
                _summary.Text = _data.Same
                    ? $"{la} → {lb}: GA_ 레이어·선박 정보에 바뀐 것이 없습니다."
                    : $"{la} → {lb}   미충족 요소 {_data.Violations.Before} → {_data.Violations.After}   변경 {_data.Changes.Count}건{resolved}{intro}"
                      + (eb != null ? $"\n{lb} 작성자 {eb.Author} · 사유 {VersionStore.ReasonText(eb.Reason)}" + (string.IsNullOrWhiteSpace(eb.Reference) ? "" : $" — {eb.Reference}") : "\n이후: 저장 전 현재 도면");
                if (_drawOnDwg) _showOnDrawing(_data);
            }
            catch (Exception ex)
            {
                _data = null;
                _summary.ForeColor = Theme.Warn;
                _summary.Text = "비교할 수 없습니다 — " + ex.Message;
                _list.Controls.Clear();
            }
            finally { Cursor = Cursors.Default; }
        }

        private void FillList()
        {
            _list.SuspendLayout();
            _list.Controls.Clear();
            if (_data == null) { _list.ResumeLayout(); return; }
            string reason = _eb == null ? "저장 전 변경" : VersionStore.ReasonText(_eb.Reason) + (string.IsNullOrWhiteSpace(_eb.Reference) ? "" : " — " + _eb.Reference);
            int w = Math.Max(_list.ClientSize.Width - 24, 260);
            foreach (var c in _data.Changes)
            {
                var row = new ChangeRow(c, reason) { Width = w };
                row.Hovered += on => SetHighlight(on ? c.Number : null);
                row.Clicked += () => Select(c);
                _list.Controls.Add(row);
            }
            foreach (var x in _data.Indirect)
                _list.Controls.Add(new Label { Text = "※ " + x.Text, AutoSize = false, Width = w, Height = 36, ForeColor = Theme.Muted, Font = Theme.Small });
            _list.ResumeLayout();
            _list.Resize -= ListResize;
            _list.Resize += ListResize;
        }

        private void ListResize(object? s, EventArgs e)
        {
            int w = Math.Max(_list.ClientSize.Width - 24, 260);
            foreach (Control c in _list.Controls) { c.Width = w; if (c is ChangeRow r) r.Relayout(); }
        }

        private void SetMode(CanvasMode m)
        {
            _mode = m;
            bool side = m == CanvasMode.Before;
            _sideBySide.Visible = side;
            _main.Visible = !side;
            if (!side) _main.Mode = m;
            _opacity.Enabled = m == CanvasMode.Overlay;
            _opLabel.ForeColor = m == CanvasMode.Overlay ? Theme.Text : Theme.Muted;
            Mark(_side, side);
            Mark(_over, m == CanvasMode.Overlay);
            Mark(_diff, m == CanvasMode.Diff);
            _main.Invalidate();
            _left.Invalidate();
            _right.Invalidate();
        }

        private void SetHighlight(int? n)
        {
            foreach (var cv in new[] { _left, _right, _main })
            {
                if (cv.Highlight == n) continue;
                cv.Highlight = n;
                cv.Invalidate();
            }
            foreach (Control c in _list.Controls)
                if (c is ChangeRow r) r.SetActive(r.Change.Number == n);
        }

        /// 표식·목록 클릭: 그 변경 위치로 확대·이동(도면에 표시 중이면 AutoCAD 화면도).
        private void Select(ChangeDto c)
        {
            SetHighlight(c.Number);
            var cv = _mode == CanvasMode.Before ? _left : _main;
            cv.FocusChange(c);
            foreach (Control x in _list.Controls)
                if (x is ChangeRow r && r.Change.Number == c.Number) _list.ScrollControlIntoView(r);
            if (_drawOnDwg && _data?.UnitsScale is double sc && sc > 0) _focusInDrawing(c, sc);
        }

        private void Export()
        {
            if (_data == null) return;
            string dwgNo = DwgNumber();
            string baseName = $"{Safe(dwgNo)}_{Safe(_la)}-{Safe(_lb)}_변경";
            using var dlg = new SaveFileDialog
            {
                Title = "생산팀 전달 — 저장",
                Filter = "PDF (*.pdf)|*.pdf|PNG 그림 (*.png)|*.png",
                FileName = baseName,
                InitialDirectory = Path.GetDirectoryName(_doc.Name) is string d && Directory.Exists(d) ? d : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                AddExtension = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                using var bmp = ExportSheet.Render(_data, _ea, _eb, _la, _lb, dwgNo, Path.GetFileName(_doc.Name));
                if (dlg.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) bmp.Save(dlg.FileName, ImageFormat.Png);
                else RasterPdf.WriteImagePage(dlg.FileName, bmp, $"{dwgNo} {_la}→{_lb} 변경");
                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "내보내기 실패: " + ex.Message, "생산팀 전달", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string DwgNumber()
        {
            try
            {
                var p = DrawingProps.Read(_doc.Database);
                if (p.TryGetValue("GARC_DWG_NO", out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            catch { /* 특성 읽기 실패 → 파일 이름 */ }
            return Path.GetFileNameWithoutExtension(_doc.Name);
        }

        private static string Safe(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch));

        private static ComboBox Combo() => new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, BackColor = Theme.Input, ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 8, 0),
        };

        private static Button Toggle(string text)
        {
            var b = Theme.FlatButton(text);
            b.Margin = new Padding(0, 0, 4, 0);
            return b;
        }

        private static void Mark(Button b, bool on)
        {
            b.BackColor = on ? Theme.Info : Theme.Card;
            b.Font = on ? Theme.Bold : Theme.Body;
        }

        private static Control Spacer(int w) => new Panel { Width = w, Height = 1, Margin = Padding.Empty };

        private sealed record Item(string Text, string Key)
        {
            public override string ToString() => Text;
        }
    }

    /// 변경 목록 한 줄: 번호 표식(모양·색) + 설명 + 이전/이후 + 수정 사유 + 해결된 위반.
    public sealed class ChangeRow : Control
    {
        public ChangeDto Change { get; }
        public event Action<bool>? Hovered;
        public event Action? Clicked;
        private readonly string _reason;
        private bool _active, _hover;

        public ChangeRow(ChangeDto c, string reason)
        {
            Change = c;
            _reason = reason;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 0, 6);
            Font = Theme.Body;
            Relayout();
        }

        private string Body() =>
            $"이전: {Change.BeforeKo}\n이후: {Change.AfterKo}\n사유: {_reason}"
            + string.Concat(Change.Resolved.Select(x => "\n✔ 해결: " + x.Text))
            + string.Concat(Change.Introduced.Select(x => "\n✖ 새 위반: " + x.Text));

        public void Relayout()
        {
            int tw = Math.Max(Width - 52, 120);
            var h1 = TextRenderer.MeasureText(Change.DescKo, Theme.Bold, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
            var h2 = TextRenderer.MeasureText(Body(), Theme.Small, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
            Height = h1 + h2 + 18;
            Invalidate();
        }

        public void SetActive(bool on)
        {
            if (_active == on) return;
            _active = on;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Hovered?.Invoke(true); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Hovered?.Invoke(false); Invalidate(); }
        protected override void OnClick(EventArgs e) => Clicked?.Invoke();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(_active || _hover ? Theme.CardHover : Theme.Card);
            var col = CanvasPainter.KindColor(Change.Kind);
            using (var bar = new SolidBrush(col)) g.FillRectangle(bar, 0, 0, 4, Height);
            var mr = new RectangleF(12, 10, 24, 24);
            using (var f = new Font(Theme.FontName, 8.5f, FontStyle.Bold))
            {
                if (Change.Bbox != null) CanvasPainter.DrawMarker(g, PaintStyle.Screen, Change, mr, null, f, _active);
                else TextRenderer.DrawText(g, $"{Change.Number}.", Theme.Bold, new Point(12, 12), Theme.Text);
            }
            int tw = Math.Max(Width - 52, 120);
            var h1 = TextRenderer.MeasureText(Change.DescKo, Theme.Bold, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, Change.DescKo, Theme.Bold, new Rectangle(44, 8, tw, h1), Theme.Text, TextFormatFlags.WordBreak);
            TextRenderer.DrawText(g, Body(), Theme.Small, new Rectangle(44, 10 + h1, tw, Height - h1 - 10), Theme.Muted, TextFormatFlags.WordBreak);
        }
    }
}
