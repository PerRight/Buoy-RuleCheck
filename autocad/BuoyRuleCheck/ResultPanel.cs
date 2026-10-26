// 팔레트 화면(명세 6-2, 디자인 보드 05). WinForms를 코드로 그린다.
//   머리:   오브(부표 등 = 전체 상태) · "수정 소요 2  문제없음 0" · "rev 2 · 파일 · 14:32" · 오른쪽 걸린 시간
//   도구:   [다시 검사](기본 버튼) · 부표 표시 ● · 라벨 확대 · 리포트
//   탭:     검토 / 선박 정보 / 이력, 오른쪽 '수정 소요만'(팔레트·로고 목록이 함께 바뀜, ui.json에 기억)
//   검토:   변경 알림 → 준비 상태(빈 입력) → 항목 카드 → '이 판정이 다루지 않는 것' 접힘
//   하단:   고지 문구 고정 — 문제없음(파랑)도 '규칙 후보' 판정일 뿐 승인 아님
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    public sealed class ResultPanel : UserControl
    {
        public event Action? RecheckRequested;
        /// 검사 시작(인자 true = '레이어 만들고 시작' — 사용자가 GA_ 레이어 만들기를 고른 경우만 도면을 바꾼다)
        public event Action<bool>? StartRequested;
        /// 실도면 연결 마법사 요청 — 준비 상태 카드·시작 화면의 버튼
        public event Action? LinkRequested;
        public event Action? ToggleOverlayRequested;
        public event Action? ReportRequested;
        public event Action? LargeLabelsRequested;
        /// 항목 카드 hover: 도면에 근거(규칙, 대표 객체 handle, 켜짐)
        public event Action<RuleResultDto, string?, bool>? RuleHoverRequested;
        /// [부표로 이동]
        public event Action<RuleResultDto, ElementCheckDto?>? BuoyRequested;
        public event Action<ElementCheckDto, bool>? ElementHoverRequested;
        public event Action<ElementCheckDto>? ElementFocusRequested;

        public ShipInfoView Ship { get; } = new() { Dock = DockStyle.Fill };

        private readonly HeaderView _header = new() { Dock = DockStyle.Top };
        private readonly FlowLayoutPanel _tools = new()
        {
            Dock = DockStyle.Top, Height = 50, BackColor = Theme.Bg, Padding = new Padding(16, 12, 16, 0), WrapContents = true,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 50),   // 폭이 모자라면 버튼이 다음 줄로(가려지지 않게)
        };
        private readonly Button _overlayBtn;
        private readonly Button _largeBtn;
        private readonly LinedStrip _tabs = new() { Dock = DockStyle.Top, Height = 34, Padding = new Padding(16, 0, 16, 0) };
        private readonly InkCheck _failOnly = new("수정 소요만");
        private readonly Dictionary<string, TabButton> _tabButtons = new();
        private string _tab = "review";
        private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        private readonly FlowLayoutPanel _cards = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            BackColor = Theme.Bg, Padding = new Padding(12, 12, 12, 4),
        };
        private readonly ListView _history = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Body,
        };
        private readonly FooterView _footer = new() { Dock = DockStyle.Bottom };
        private CheckResponse? _last;
        private string _lastDoc = "";
        private double? _lastSeconds;
        private Dictionary<string, int>? _roleCounts;

        public ResultPanel()
        {
            BackColor = Theme.Bg;
            Font = Theme.Body;

            var check = Theme.PrimaryButton("다시 검사");
            check.Click += (_, _) => RecheckRequested?.Invoke();
            _overlayBtn = Theme.RingButton("부표 표시 ●");
            _overlayBtn.Click += (_, _) => ToggleOverlayRequested?.Invoke();
            _largeBtn = Theme.TextButton("라벨 확대");
            _largeBtn.Click += (_, _) => LargeLabelsRequested?.Invoke();
            var report = Theme.TextButton("리포트");
            report.Click += (_, _) => ReportRequested?.Invoke();
            var tip = new ToolTip();
            tip.SetToolTip(check, "지금 도면으로 다시 검사합니다.");
            tip.SetToolTip(_overlayBtn, "도면 위 부표를 켜거나 끕니다(검사는 계속, 도면은 그대로).");
            tip.SetToolTip(_largeBtn, "도면 위 라벨 글자·선 두께·표식 크기만 2.5배로 키웁니다(프로젝터·화면 공유용). 판정과 도면 객체는 바뀌지 않습니다.");
            tip.SetToolTip(report, "근거·측면도·이력을 HTML로 저장하고 엽니다.");
            _tools.Controls.AddRange(new Control[] { check, _overlayBtn, _largeBtn, report });

            foreach (var (id, text) in new[] { ("review", "검토"), ("ship", "선박 정보"), ("history", "이력") })
            {
                var b = new TabButton(id, text);
                b.Picked += ShowTab;
                _tabButtons[id] = b;
                _tabs.Controls.Add(b);
            }
            _tabs.Controls.Add(_failOnly);
            _tabs.Resize += (_, _) => LayoutTabs();
            _failOnly.Toggled += () => UiState.SetFailOnly(!UiState.Current.FailOnly);
            tip.SetToolTip(_failOnly, "수정 소요(빨강) 항목·객체만 보여 줍니다. 숨긴 확인 필요 항목은 개수를 표시합니다.");
            UiState.FailOnlyChanged += OnFailOnlyChanged;
            SyncFailOnly();

            _history.Columns.Add("rev", 44);
            _history.Columns.Add("시각", 64);
            _history.Columns.Add("변경 · 판정 변화", 400);
            _cards.Resize += (_, _) => FitCards();

            Controls.Add(_content);
            Controls.Add(_footer);
            Controls.Add(_tabs);
            Controls.Add(_tools);
            Controls.Add(_header);
            ShowTab("review");
            SetIdle("검사 전");
            SetEngine(null);
        }

        private void LayoutTabs()
        {
            int x = _tabs.Padding.Left;
            foreach (var b in _tabButtons.Values) { b.Location = new Point(x, 0); x += b.Width + 4; }
            _failOnly.Location = new Point(_tabs.Width - _tabs.Padding.Right - _failOnly.Width, 0);
        }

        public void ShowTab(string id)
        {
            _tab = id;
            _content.Controls.Clear();
            _content.Controls.Add(id switch { "ship" => (Control)Ship, "history" => _history, _ => _cards });
            foreach (var (k, b) in _tabButtons) b.Selected = k == id;
            _failOnly.Visible = id == "review";
        }

        public void SetOverlayState(bool on)
        {
            _overlayBtn.Text = on ? "부표 표시 ●" : "부표 표시";
            if (_overlayBtn is PillButton pb) pb.Selected = false;
            _overlayBtn.AccessibleName = "부표 표시 " + (on ? "켜짐" : "꺼짐");
        }

        public void SetLargeLabelsState(bool on)
        {
            _largeBtn.Text = on ? "라벨 확대 ●" : "라벨 확대";
            _largeBtn.AccessibleName = "라벨 확대 " + (on ? "켜짐" : "꺼짐");
        }

        /// '근거 ›'(부표 카드) → 검토 탭에서 그 항목으로
        public void FocusRule(string ruleId)
        {
            ShowTab("review");
            foreach (Control c in _cards.Controls)
                if (c is RuleCard rc && rc.RuleId == ruleId) { _cards.ScrollControlIntoView(rc); rc.Flash(); break; }
        }

        private void OnFailOnlyChanged()
        {
            if (IsDisposed) return;
            SyncFailOnly();
            if (_last != null) ShowResult(_last, _lastDoc, record: false, seconds: _lastSeconds);
        }

        private void SyncFailOnly() => _failOnly.Checked = UiState.Current.FailOnly;

        /// 오른쪽 위 연결 상태: 연결(걸린 시간) · 끊김 · 대기
        public void SetEngine(bool? up)
        {
            switch (up)
            {
                case true:
                    _header.SetRight(_lastSeconds is double s ? $"{s:0.0} s" : "연결", Theme.Muted, Theme.Faint);
                    break;
                case false:
                    _header.SetRight("연결 끊김", Theme.Warn, Theme.Warn);
                    break;
                default:
                    _header.SetRight("대기", Theme.Faint, Theme.Faint);
                    break;
            }
        }

        public void SetIdle(string text, string? docName = null, bool showSetup = false)
        {
            _last = null;
            _header.Set("idle", text, "", docName ?? "");
            ClearCards();
            var card = new InfoCard("GA 도면이 바뀌면 고칠 자리에 부표가 떠요",
                "1) 선박 정보 탭에서 LOA·L·AP·FP·흘수 입력\n2) GA_ 레이어에 선체·조종위치·장애물·충돌격벽 그리기\n3) 검사 시작\n" +
                "실제 도면(GA_ 레이어 없음, 1/1000 축척 등)은 [실도면 연결]로 레이어와 객체를 바꾸지 않고 연결합니다.");
            card.Add(Theme.PrimaryButton(showSetup ? "레이어 만들고 시작" : "검사 시작")).Click += (_, _) => StartRequested?.Invoke(showSetup);
            card.Add(Theme.RingButton("실도면 연결")).Click += (_, _) => LinkRequested?.Invoke();
            _cards.Controls.Add(card);
            FitCards();
        }

        public void ShowEngineDown(string message)
        {
            _last = null;
            _header.Set("down", "연결 끊김", "판정하지 않음", message);
            SetEngine(false);
            ClearCards();
            var card = new InfoCard("검사 엔진에 연결할 수 없어요 — 판정하지 않습니다",
                message + "\n\n엔진 폴더에서 'python -m brc serve'를 실행한 뒤 [다시 검사]를 누르세요.", "down");
            card.Add(Theme.RingButton("다시 검사")).Click += (_, _) => RecheckRequested?.Invoke();
            _cards.Controls.Add(card);
            FitCards();
        }

        public void ShowResult(CheckResponse r, string docName, bool record = true, double? seconds = null)
        {
            _last = r;
            _lastDoc = docName;
            if (seconds != null || record) _lastSeconds = seconds;
            SetEngine(true);
            var (main, side) = UiText.Summary(r);
            _header.Set(UiText.StateKey(r), main, side,
                $"rev {r.Revision}{(r.SameAsRevision is int s ? $" (= rev {s})" : "")} · {docName} · {DateTime.Now:HH:mm}");
            _footer.Text = UiText.Disclaimer;

            _cards.SuspendLayout();
            ClearCards();
            if (r.Transitions.Count > 0) _cards.Controls.Add(new NoticeBox(r.Transitions));
            if (r.Readiness is { AllReady: false } rd)
            {
                var rc = new ReadinessCard(rd);
                rc.LinkRequested += () => LinkRequested?.Invoke();
                _cards.Controls.Add(rc);
            }
            if (_roleCounts != null) _cards.Controls.Add(new RoleLegendCard(_roleCounts));
            bool failOnly = UiState.Current.FailOnly;
            foreach (var x in r.Results)
            {
                if (failOnly && x.Status != "fail") continue;
                var card = new RuleCard(x, failOnly);
                card.HoverChanged += (rr, el, on) => RuleHoverRequested?.Invoke(rr, el, on);
                card.BuoyRequested += (rr, el) => BuoyRequested?.Invoke(rr, el);
                card.ElementHover += (e, on) => ElementHoverRequested?.Invoke(e, on);
                card.ElementClicked += e => ElementFocusRequested?.Invoke(e);
                _cards.Controls.Add(card);
            }
            if (failOnly)
            {
                // 숨긴 항목 개수는 꼭 보여 준다 — '확인 필요'를 숨겨서 문제없음처럼 보이면 안 되므로 주황으로
                var c = UiText.Count(r);
                var hidden = new List<string>();
                if (c.Warn > 0) hidden.Add($"확인 필요 {c.Warn}");
                if (c.Pass > 0) hidden.Add($"문제없음 {c.Pass}");
                if (c.Skip > 0) hidden.Add($"적용 제외 {c.Skip}");
                if (hidden.Count > 0)
                    _cards.Controls.Add(new NoteLabel("수정 소요만 보기 — 숨김: " + string.Join(" · ", hidden), c.Warn > 0 ? Theme.Warn : Theme.Faint));
                if (c.Fail == 0)
                    _cards.Controls.Add(c.Warn > 0
                        ? new NoteLabel($"수정 소요는 없지만 확인 필요 {c.Warn}건을 숨겼어요 — '수정 소요만'을 끄면 보여요.", Theme.Warn, Theme.Body)
                        : new NoteLabel("수정 소요가 없어요.", Theme.Text, Theme.Body));
            }
            _cards.Controls.Add(new NotCoveredView(r.Results));
            _cards.ResumeLayout();
            FitCards();
            if (record) AddHistory(r);
        }

        /// 역할 보기(BRCROLESHOW) 범례 카드 켜기(객체 수)/끄기(null)
        public void ShowRoleLegend(Dictionary<string, int>? counts)
        {
            _roleCounts = counts;
            if (_last != null) { ShowResult(_last, _lastDoc, record: false, seconds: _lastSeconds); return; }
            foreach (var old in _cards.Controls.OfType<RoleLegendCard>().ToList()) { _cards.Controls.Remove(old); old.Dispose(); }
            if (counts != null) { var c = new RoleLegendCard(counts); _cards.Controls.Add(c); _cards.Controls.SetChildIndex(c, 0); }
            FitCards();
        }

        private void ClearCards()
        {
            var old = _cards.Controls.Cast<Control>().ToList();
            _cards.Controls.Clear();
            foreach (var c in old) c.Dispose();   // 카드의 오브는 공용 박자(OrbClock)에 붙어 있으므로 바로 떼어 낸다
        }

        private void AddHistory(CheckResponse r)
        {
            var d = r.Diff;
            var parts = new List<string>();
            if (d.Initial) parts.Add("처음 검사");
            if (!d.Initial && d.Added.Count > 0) parts.Add("추가 " + string.Join(",", d.Added));
            if (d.Removed.Count > 0) parts.Add("삭제 " + string.Join(",", d.Removed));
            if (d.Modified.Count > 0) parts.Add("수정 " + string.Join(",", d.Modified));
            if (!d.Initial && d.ShipChanged.Count > 0) parts.Add("선박 정보");
            if (!d.Initial && d.ConditionsChanged) parts.Add("흘수 조건");
            if (r.SameAsRevision is int s) parts.Add($"= rev {s} (되돌림)");
            var trans = string.Join(", ", r.Transitions.Select(t =>
                $"{UiText.Short(t.RuleId)} {(t.From == null ? "-" : Theme.StatusName(t.From))}→{Theme.StatusName(t.To)}"));
            var text = string.Join(" / ", parts) + (trans.Length > 0 ? "  ⇒ " + trans : "");
            var item = new ListViewItem(new[] { r.Revision.ToString(), DateTime.Now.ToString("HH:mm:ss"), text })
            {
                ForeColor = UiText.StateKey(r) switch { "fail" => Theme.FailInk, "warn" => Theme.Warn, _ => Theme.Text },
            };
            _history.Items.Insert(0, item);
            if (_history.Items.Count > 300) _history.Items.RemoveAt(_history.Items.Count - 1);
        }

        public void ClearHistory() => _history.Items.Clear();

        protected override void Dispose(bool disposing)
        {
            if (disposing) UiState.FailOnlyChanged -= OnFailOnlyChanged;   // 정적 이벤트가 패널을 붙잡지 않게
            base.Dispose(disposing);
        }

        private void FitCards()
        {
            int w = Math.Max(_cards.ClientSize.Width - _cards.Padding.Horizontal - 2, 220);
            foreach (Control c in _cards.Controls)
                if (c is IFits f) f.FitTo(w);
                else c.Width = w;
        }
    }
}
