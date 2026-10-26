// 버전 비교 창(BRCCOMPARE, 명세 6-6, 디자인 보드 07): 이전/이후 버전 고르기 · 나란히 · 겹쳐 · 차이만 · 도면에 표시 · 생산팀 전달.
//   요약 줄: 수정 소요 3 → 0 · 변경 4건 · 전체 ● 수정 소요 → ● 문제없음 · 확인 필요 0
//   그림: 바뀌지 않은 형상 회색, 추가 초록 빗금 ○ · 삭제 빨강 파선 □ · 변경 노랑(이전 점선 · 이후 실선) ◇, 번호 표식
//   변경 내역: 번호 · 제목 · 해결/기준 충족 · 수치 변화(z 24.00 → 21.40 m) · 덧붙임. 고른 칸 ↔ 그림의 점선 틀
// 무엇이 바뀌었고 무엇이 해결됐는지는 엔진(/v1/compare)이 계산한다. '해결'은 이후가 충족이거나 판정할 수 있는 상태에서
// 객체가 지워진 경우만, 판정 못 함으로 바뀌면 '확인 필요'(주황) — 이 창은 고르고·그리고·내보내기만 한다.
// AutoCAD에 묶인 일(현재 도면 추출·버전 폴더·도면 번호)은 CompareSource로 받는다(단독 미리보기도 같은 창을 쓴다).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    /// 비교 창이 쓰는 바깥 일(AutoCAD 도면·버전 폴더·엔진)
    public sealed class CompareSource
    {
        public string DrawingName = "";
        /// 내보내기 기본 폴더(없으면 내 문서)
        public string? SaveDir;
        public Func<VersionIndex> LoadIndex = () => new VersionIndex();
        public Func<VersionEntry, RawDrawing> LoadRaw = _ => new RawDrawing();
        /// 저장 전 상태를 포함한 지금 도면
        public Func<RawDrawing> ExtractLive = () => new RawDrawing();
        public Func<RawDrawing, RawDrawing, string, string, CompareResponse> Compare = (a, b, la, lb) => EngineClient.Compare(a, b, la, lb);
        /// 도면 번호(특성 BRC_DWG_NO, 없으면 파일 이름)
        public Func<string> DwgNumber = () => "";
    }

    public sealed class CompareForm : Form
    {
        private const string Live = "__live__";
        private readonly CompareSource _src;
        private VersionIndex _index = new();
        /// 도면 위 임시 표시. false = 이 비교 창의 도면이 지금 활성 탭이 아니라 표시하지 못함.
        private readonly Func<CompareResponse?, bool> _showOnDrawing;
        private readonly Action<ChangeDto, double> _focusInDrawing;

        private readonly InkCombo _a = new("이전 버전"), _b = new("이후 버전");
        private readonly Segmented _modes = new("보기 방식", "나란히", "겹쳐", "차이만");
        private readonly InkSlider _opacity = new("겹쳐 보기 — 이후 투명도");
        private readonly Label _opLabel = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Faint, BackColor = Theme.Bg, Text = "이후 60%" };
        private readonly Button _onDwg, _export;
        private readonly LinedStrip _tools = new() { Dock = DockStyle.Top, Height = 58 };
        private readonly Label _labA = Lbl("이전"), _labB = Lbl("이후"), _arrow = Lbl("→");
        private readonly CompareSummaryBar _summary = new() { Dock = DockStyle.Top };
        private readonly CompareCanvas _left = new() { Dock = DockStyle.Fill, Mode = CanvasMode.Before };
        private readonly CompareCanvas _right = new() { Dock = DockStyle.Fill, Mode = CanvasMode.After };
        private readonly CompareCanvas _main = new() { Dock = DockStyle.Fill, Mode = CanvasMode.Diff };
        private readonly TableLayoutPanel _sideBySide = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Line, Margin = Padding.Empty };
        private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill, BackColor = PaintStyle.Screen.Back };
        private readonly LegendPill _legend = new();
        private readonly Button _zoomIn = Theme.RingButton("+"), _zoomOut = Theme.RingButton("−"), _zoomAll = Theme.RingButton("전체");
        private readonly Panel _side = new() { Dock = DockStyle.Right, Width = 380, BackColor = Theme.Bg };
        private readonly ListHeader _listHead = new() { Dock = DockStyle.Top };
        private readonly FlowLayoutPanel _list = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg,
            Padding = new Padding(12, 2, 12, 8),
        };
        private readonly FooterView _note = new() { Dock = DockStyle.Bottom, Text = UiText.ResolvedRule };
        private readonly ViewState _view = new();
        private CanvasMode _mode = CanvasMode.Diff;
        private CompareResponse? _data;
        private bool _drawOnDwg;
        private bool _loading;

        public CompareForm(CompareSource src, Func<CompareResponse?, bool> showOnDrawing, Action<ChangeDto, double> focusInDrawing)
        {
            _src = src;
            _showOnDrawing = showOnDrawing;
            _focusInDrawing = focusInDrawing;
            Text = $"버전 비교 — {src.DrawingName}";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Size = new Size(1280, 800);
            MinimumSize = new Size(960, 580);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            KeyPreview = true;

            // ── 도구 줄 ──
            _onDwg = Theme.RingButton("도면에 표시");
            _export = Theme.PrimaryButton("생산팀 전달");
            _modes.Selected = 2;
            var tip = new ToolTip();
            tip.SetToolTip(_modes, "나란히: 이전·이후를 좌우로(함께 확대·이동) · 겹쳐: 이전 위에 이후 · 차이만: 바뀐 것만 색으로");
            tip.SetToolTip(_onDwg, "변경 위치를 AutoCAD 도면 위에 임시 표시합니다(도면은 바뀌지 않음).");
            tip.SetToolTip(_export, "도면 번호·버전·날짜·작성자·차이 그림·변경 목록·범례·확인란을 A4 한 장(PDF 또는 PNG)으로.");
            _tools.Controls.AddRange(new Control[] { _labA, _a, _arrow, _labB, _b, _modes, _opLabel, _opacity, _onDwg, _export });
            _tools.Resize += (_, _) => LayoutTools();

            // ── 그림 ──
            _sideBySide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _sideBySide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _sideBySide.Controls.Add(_left, 0, 0);
            _sideBySide.Controls.Add(_right, 1, 0);
            _left.Margin = new Padding(0, 0, 1, 0);
            _right.Margin = new Padding(0);
            _canvasHost.Controls.Add(_main);
            _canvasHost.Controls.Add(_sideBySide);
            foreach (var b in new[] { _zoomIn, _zoomOut, _zoomAll })
            {
                b.Size = new Size(b.Text.Length > 1 ? 54 : 32, 32);
                _canvasHost.Controls.Add(b);
                b.BringToFront();
            }
            _zoomIn.AccessibleName = "확대";
            _zoomOut.AccessibleName = "축소";
            _zoomAll.AccessibleName = "전체 보기";
            _canvasHost.Controls.Add(_legend);
            _legend.BringToFront();
            _canvasHost.Resize += (_, _) => LayoutCanvasChrome();

            // ── 변경 내역 ──
            _side.Controls.Add(_list);
            _side.Controls.Add(_note);
            _side.Controls.Add(_listHead);
            _side.Paint += (_, e) => { using var p = new Pen(Theme.Line, 1f); e.Graphics.DrawLine(p, 0, 0, 0, _side.Height); };
            _side.Padding = new Padding(1, 0, 0, 0);

            Controls.Add(_canvasHost);
            Controls.Add(_side);
            Controls.Add(_summary);
            Controls.Add(_tools);
            Shown += (_, _) =>
            {
                LayoutTools();
                LayoutCanvasChrome();
                // 창이 보이기 전엔 그림 칸 크기가 0이라 맞춤이 안 된다 → 보인 뒤 한 번 더
                if (_data?.Bounds is double[] b) _view.Fit(b, _mode == CanvasMode.Before ? _left.ClientSize : _main.ClientSize);
            };

            // ── 동작 ──
            _modes.Changed += i => SetMode(i switch { 0 => CanvasMode.Before, 1 => CanvasMode.Overlay, _ => CanvasMode.Diff });
            _opacity.ValueChanged += () =>
            {
                _main.OverlayOpacity = _opacity.Value / 100f;
                _opLabel.Text = $"이후 {_opacity.Value}%";
                _main.Invalidate();
            };
            _onDwg.Click += (_, _) =>
            {
                _drawOnDwg = !_drawOnDwg;
                Mark(_onDwg, _drawOnDwg);
                if (!_showOnDrawing(_drawOnDwg ? _data : null)) NotActive();
            };
            _export.Click += (_, _) => Export();
            _a.SelectionChanged += Reload;
            _b.SelectionChanged += Reload;
            _zoomIn.Click += (_, _) => Zoom(1.25);
            _zoomOut.Click += (_, _) => Zoom(1 / 1.25);
            _zoomAll.Click += (_, _) => (_mode == CanvasMode.Before ? _left : _main).FitAll();
            foreach (var cv in new[] { _left, _right, _main })
            {
                cv.MarkerClicked += Select;
                cv.ChangeHovered += c => SetHighlight(c?.Number ?? _selected);
            }
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            FormClosed += (_, _) => _showOnDrawing(null);
            _list.Resize += (_, _) => FitList();

            LoadVersions();
            SetMode(CanvasMode.Diff);
        }

        private static Label Lbl(string t) => new() { AutoSize = true, Text = t, Font = Theme.Body, ForeColor = Theme.Faint, BackColor = Theme.Bg };

        private void LayoutTools()
        {
            int x = 16, cy = (_tools.Height - 1) / 2;
            void Put(Control c, int gap = 8) { c.Location = new Point(x, cy - c.Height / 2); x += c.Width + gap; }
            Put(_labA);
            Put(_a, 10);
            Put(_arrow, 10);
            Put(_labB);
            Put(_b, 18);
            Put(_modes, 12);
            if (_opacity.Visible) { Put(_opLabel, 4); Put(_opacity, 12); }
            int r = _tools.Width - 16;
            _export.Location = new Point(r - _export.Width, cy - _export.Height / 2);
            _onDwg.Location = new Point(_export.Left - 8 - _onDwg.Width, cy - _onDwg.Height / 2);
        }

        private void LayoutCanvasChrome()
        {
            int pad = 16, by = _canvasHost.Height - pad - 32;
            _legend.Location = new Point(pad, by);
            int x = _canvasHost.Width - pad;
            foreach (var b in new[] { _zoomAll, _zoomOut, _zoomIn }) { x -= b.Width; b.Location = new Point(x, by); x -= 6; }
        }

        private void Zoom(double f)
        {
            var cv = _mode == CanvasMode.Before ? _left : _main;
            if (cv.Data == null) return;
            _view.Scale *= f;
            _view.Notify();
        }

        private void LoadVersions()
        {
            _loading = true;
            try
            {
                _index = _src.LoadIndex();
                var items = _index.Versions.OrderBy(v => v.Version)
                    .Select(v => new InkCombo.Entry(VersionStore.Describe(v), v.Version.ToString(), VersionStore.DescribeLong(v))).ToList();
                items.Add(new InkCombo.Entry("현재 도면", Live, "현재 도면 (저장 전 상태 포함)"));
                int n = _index.Versions.Count;
                // 기본: 직전 버전 → 현재 버전. 버전이 하나면 그 버전 → 현재 도면.
                _a.SetItems(items, n >= 2 ? n - 2 : 0);
                _b.SetItems(items, n >= 2 ? n - 1 : items.Count - 1);
            }
            catch (Exception ex)
            {
                _summary.SetMessage("버전 기록을 읽지 못했어요: " + ex.Message, warn: true);
            }
            finally { _loading = false; }
            Reload();
        }

        private (RawDrawing raw, VersionEntry? entry, string label) Pick(InkCombo cb)
        {
            var it = cb.Selected!;
            if (it.Key == Live) return (_src.ExtractLive(), null, "현재");
            var v = _index.Versions.First(x => x.Version.ToString() == it.Key);
            return (_src.LoadRaw(v), v, VersionStore.Label(v));
        }

        private VersionEntry? _ea, _eb;
        private string _la = "", _lb = "";
        private int? _selected;

        private void Reload()
        {
            if (_loading || _a.Selected == null || _b.Selected == null) return;
            if (_index.Versions.Count == 0)
            {
                _summary.SetMessage("아직 버전 기록이 없어요. 도면을 저장하면(또는 BRCVERSION) 버전이 생깁니다. 지금은 '현재 도면'만 있습니다.", warn: false);
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                var (ra, ea, la) = Pick(_a);
                var (rb, eb, lb) = Pick(_b);
                _ea = ea; _eb = eb; _la = la; _lb = lb;
                _data = _src.Compare(ra, rb, la, lb);
                _selected = null;
                foreach (var cv in new[] { _left, _right, _main }) cv.Bind(_data, _view);
                _left.Caption = $"이전 · {_a.Selected!.Text}";
                _right.Caption = $"이후 · {_b.Selected!.Text}";
                _view.Fit(_data.Bounds ?? new double[] { 0, 0, 1, 1 }, _mode == CanvasMode.Before ? _left.ClientSize : _main.ClientSize);
                _summary.Set(_data);
                _listHead.Set(UiText.ReasonLine(eb == null ? null : VersionStore.ReasonText(eb.Reason), eb?.Reference));
                FillList();
                if (_drawOnDwg && !_showOnDrawing(_data)) NotActive();
            }
            catch (Exception ex)
            {
                _data = null;
                foreach (var cv in new[] { _left, _right, _main }) cv.Bind(null, _view);   // 이전 결과가 남아 보이지 않게
                _summary.SetMessage("비교할 수 없어요 — " + ex.Message, warn: true);
                ClearList();
                if (_drawOnDwg) _showOnDrawing(null);
            }
            finally { Cursor = Cursors.Default; }
        }

        /// 도면 탭이 다시 활성이 되면(PluginApp.OnActivated) 켜 둔 '도면에 표시'를 다시 그린다.
        public void ReapplyDrawing()
        {
            if (_drawOnDwg && _data != null) _showOnDrawing(_data);
        }

        /// 비교 창의 도면이 활성 탭이 아니면 표시·이동을 하지 않고, 버튼을 끄고 알린다(조용히 무시하지 않게).
        private void NotActive()
        {
            _drawOnDwg = false;
            Mark(_onDwg, false);
            MessageBox.Show(this, $"'{_src.DrawingName}' 도면 탭이 활성일 때만 도면에 표시할 수 있어요. 그 탭으로 바꾼 뒤 다시 누르세요.",
                            "도면에 표시", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearList()
        {
            var old = _list.Controls.Cast<Control>().ToList();
            _list.Controls.Clear();
            foreach (var c in old) c.Dispose();
        }

        private void FillList()
        {
            _list.SuspendLayout();
            ClearList();
            if (_data == null) { _list.ResumeLayout(); return; }
            foreach (var c in _data.Changes)
            {
                var row = new ChangeCard(c);
                row.Hovered += on => SetHighlight(on ? c.Number : _selected);
                row.Clicked += () => Select(c);
                _list.Controls.Add(row);
            }
            if (_data.Changes.Count == 0)
                _list.Controls.Add(new NoteLabel(_data.Same ? "바뀐 것이 없어요." : "형상·선박 정보는 같고 판정만 바뀌었어요 — 비슷한 레이어 이름·블록 등을 확인하세요.", Theme.Muted, Theme.Body));
            if (_data.Indirect.Count > 0)
            {
                _list.Controls.Add(new NoteLabel("다른 변경의 영향", Theme.Faint) { Margin = new Padding(4, 8, 0, 4) });
                foreach (var x in _data.Indirect)
                    _list.Controls.Add(new NoteLabel((x.To is null or "unknown" ? "확인 필요 · " : "") + x.Text, x.To is null or "unknown" ? Theme.Warn : Theme.Muted)
                    { Margin = new Padding(4, 0, 0, 6) });
            }
            _list.ResumeLayout();
            FitList();
        }

        private void FitList()
        {
            int w = Math.Max(_list.ClientSize.Width - _list.Padding.Horizontal - 2, 240);
            foreach (Control c in _list.Controls)
                if (c is ChangeCard r) r.FitTo(w);
                else if (c is IFits f) f.FitTo(w);
        }

        private void SetMode(CanvasMode m)
        {
            _mode = m;
            bool side = m == CanvasMode.Before;
            _sideBySide.Visible = side;
            _main.Visible = !side;
            if (!side) _main.Mode = m;
            _opacity.Visible = _opLabel.Visible = m == CanvasMode.Overlay;
            _modes.Selected = side ? 0 : m == CanvasMode.Overlay ? 1 : 2;
            LayoutTools();
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
                if (c is ChangeCard r) r.SetActive(r.Change.Number == n);
        }

        /// 표식·목록 클릭: 그 변경 위치로 확대·이동(도면에 표시 중이면 AutoCAD 화면도).
        private void Select(ChangeDto c)
        {
            _selected = c.Number;
            SetHighlight(c.Number);
            var cv = _mode == CanvasMode.Before ? _left : _main;
            cv.FocusChange(c);
            foreach (Control x in _list.Controls)
                if (x is ChangeCard r && r.Change.Number == c.Number) _list.ScrollControlIntoView(r);
            if (_drawOnDwg && _data?.UnitsScale is double sc && sc > 0) _focusInDrawing(c, sc);
        }

        private void Export()
        {
            if (_data == null) return;
            string dwgNo = _src.DwgNumber();
            string baseName = $"{Safe(dwgNo)}_{Safe(_la)}-{Safe(_lb)}_변경";   // 점(.)도 바꿔서 확장자로 오인되지 않게
            using var dlg = new SaveFileDialog
            {
                Title = "생산팀 전달 — 저장",
                Filter = "PDF (*.pdf)|*.pdf|PNG 그림 (*.png)|*.png",
                FileName = baseName,
                InitialDirectory = _src.SaveDir is string d && Directory.Exists(d) ? d : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                AddExtension = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            // 확장자가 .pdf/.png가 아니면 고른 형식에 맞춰 붙인다
            string path = dlg.FileName;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".pdf" && ext != ".png") path += dlg.FilterIndex == 2 ? ".png" : ".pdf";
            try
            {
                using var bmp = ExportSheet.Render(_data, _ea, _eb, _la, _lb, dwgNo, _src.DrawingName);
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) bmp.Save(path, ImageFormat.Png);
                else RasterPdf.WriteImagePage(path, bmp, $"{dwgNo} {_la}→{_lb} 변경");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "내보내기 실패: " + ex.Message, "생산팀 전달", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string Safe(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' || ch == '.' ? '_' : ch));

        private static void Mark(Button b, bool on)
        {
            if (b is PillButton pb) pb.Selected = on;
            b.AccessibleName = b.Text + (on ? " 켜짐" : " 꺼짐");
        }

        /// 변경 내역 머리: '변경 내역' + 사유 줄
        private sealed class ListHeader : Control
        {
            private string _sub = "";
            public ListHeader()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Bg;
                Height = 70;
                AccessibleRole = AccessibleRole.StaticText;
                AccessibleName = "변경 내역";
            }

            public void Set(string sub)
            {
                _sub = sub;
                AccessibleDescription = sub;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Bg);
                TextRenderer.DrawText(g, "변경 내역", Theme.Title, new Point(16, 16), Theme.Text, Ink.Line);
                TextRenderer.DrawText(g, _sub, Theme.Small, new Rectangle(16, 42, Width - 32, 18), Theme.Muted, Ink.Line);
            }
        }
    }
}
