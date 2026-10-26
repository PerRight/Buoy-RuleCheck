// 팔레트 미리보기: 플러그인과 같은 ResultPanel에 합성 샘플(S140)의 엔진 응답을 차례로 넣어 본다(AutoCAD 없이).
// 응답은 samples.json(make_samples.py로 만듦). 도면 쪽 동작(근거 표시·부표 이동)은 여기서는 글로만 알린다.
// 팔레트 왼쪽 아래에 떠 있는 로고(같은 LogoButton)가 뜨고, 누르면 수정 소요 목록, 해결이 생기면 토스트.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace GaRuleCheck
{
    public sealed class PalettePreview : Form
    {
        private readonly ResultPanel _panel = new() { Dock = DockStyle.Fill };
        private readonly Label _log = new() { Dock = DockStyle.Bottom, Height = 40, ForeColor = Theme.Faint, BackColor = Theme.Bg, Font = Theme.MonoSmall, Padding = new Padding(8, 4, 8, 4) };
        private readonly List<(string Name, CheckResponse Resp)> _samples = new();
        // 떠 있는 로고·목록·토스트(플러그인과 같은 부품)
        private readonly LogoButton _logo = new();
        private readonly LogoListPopup _list = new();
        private CheckResponse? _cur;
        private string _state = "idle";

        public PalettePreview()
        {
            Text = Product.Name + " — 팔레트 미리보기";
            BackColor = Theme.Bg;
            ClientSize = new Size(380 + 220, 1000);
            StartPosition = FormStartPosition.CenterScreen;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BuoyPreview.samples.json"))
            {
                if (s != null)
                    foreach (var o in JsonDocument.Parse(s).RootElement.EnumerateArray())
                        _samples.Add((o.GetProperty("name").GetString() ?? "", o.GetProperty("resp").Deserialize<CheckResponse>(Contract.Json)!));
            }
            var list = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 220, FlowDirection = FlowDirection.TopDown, BackColor = Theme.Card, Padding = new Padding(10), WrapContents = false };
            list.Controls.Add(Theme.Label("샘플 순서대로 누르세요", Theme.Small, Theme.Faint));
            foreach (var (name, resp) in _samples)
            {
                var b = Theme.RingButton(name);
                b.Margin = new Padding(0, 6, 0, 0);
                b.Click += (_, _) => Feed(resp, name);
                list.Controls.Add(b);
            }
            var idle = Theme.TextButton("검사 전 화면");
            idle.Click += (_, _) => { _panel.SetIdle("검사 전", "S140.dwg"); SetLogo(null, "idle"); };
            var down = Theme.TextButton("연결 끊김 화면");
            down.Click += (_, _) => { _panel.ShowEngineDown("엔진 서버(127.0.0.1:8765)에 연결할 수 없습니다."); SetLogo(null, "down"); };
            var link = Theme.TextButton("실도면 연결 창");
            link.Click += (_, _) => ShowLinkDialog();
            var strip = Theme.TextButton("2~5단계 안내 띠");
            strip.Click += (_, _) => ShowLinkStrip();
            var roles = Theme.TextButton("역할 표시 범례");
            roles.Click += (_, _) => _panel.ShowRoleLegend(new Dictionary<string, int> { ["hull"] = 4, ["obstruction"] = 7, ["collision_bhd"] = 1, ["eye"] = 1 });
            list.Controls.Add(idle);
            list.Controls.Add(down);
            list.Controls.Add(link);
            list.Controls.Add(strip);
            list.Controls.Add(roles);
            var cmp = Theme.TextButton("버전 비교 창");
            cmp.Click += (_, _) => ShowCompare();
            var a4 = Theme.TextButton("A4 전달 시트");
            a4.Click += (_, _) => ShowSheet();
            list.Controls.Add(cmp);
            list.Controls.Add(a4);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            host.Controls.Add(_panel);
            Controls.Add(host);
            Controls.Add(list);
            Controls.Add(_log);
            _panel.RuleHoverRequested += (r, el, on) => Log(on ? $"근거 표시: {r.RuleId} {el}" : "근거 지움");
            _panel.BuoyRequested += (r, el) => Log($"부표로 이동: {r.RuleId} {el?.ElementId} ({el?.Anchor?.FirstOrDefault()} m)");
            _panel.ElementHoverRequested += (e, on) => { if (on) Log($"객체 강조: {e.ElementId} {e.Status}"); };
            _panel.ElementFocusRequested += e => Log($"객체로 이동: {e.ElementId}");
            _panel.RecheckRequested += () => Log("다시 검사");
            _panel.LinkRequested += () => Log("실도면 연결 시작");
            _panel.ToggleOverlayRequested += () => Log("부표 표시 켜기/끄기");
            _panel.LargeLabelsRequested += () => Log("라벨 확대");
            _panel.ReportRequested += () => Log("리포트");
            if (_samples.Count > 2) Feed(_samples[2].Resp, _samples[2].Name);

            _logo.ToggleRequested += kb =>
            {
                if (_list.Visible) { _list.Hide(); _logo.SetExpanded(false); return; }
                _list.SetResult(_cur, _state);
                _list.PlaceNear(_logo.PlateBounds);
                _list.Open(this, kb);
                _logo.SetExpanded(true);
            };
            _list.RowPicked += r => { _list.Hide(); _logo.SetExpanded(false); Log($"목록 → 고칠 자리로: {r.RuleId}"); };
            _list.PaletteRequested += () => { _list.Hide(); _logo.SetExpanded(false); Log("팔레트에서 자세히"); };
            _list.CloseRequested += () => { _list.Hide(); _logo.SetExpanded(false); };
            Shown += (_, _) =>
            {
                _logo.Location = new Point(Left + 380 - LogoButton.W - 16, Bottom - LogoButton.H - 70);
                _logo.Show(this);
            };
            FormClosed += (_, _) => { _list.Dispose(); _logo.Dispose(); };
        }

        /// 샘플 하나를 검사 결과처럼 넣는다: 로고는 검사 중(최소 0.6 s) → 결과 상태, 해결이 있으면 토스트.
        private void Feed(CheckResponse resp, string name)
        {
            _logo.BeginCheck();
            _panel.ShowResult(resp, "S140_" + name.Split(' ')[0] + ".dwg", record: true, seconds: 0.3);
            SetLogo(resp, "idle");
            int solved = resp.Transitions.Count(t => t.Kind == "resolved");
            if (solved > 0 && _logo.Visible) new ToastForm(UiText.ToastResolved(solved), "pass").ShowNear(_logo.PlateBounds, this);
        }

        private void SetLogo(CheckResponse? r, string fallback)
        {
            _cur = r;
            _state = UiText.LogoState(r, fallback);
            var (chip, n) = UiText.LogoChip(r, _state);
            _logo.SetState(OrbStates.ForState(_state), chip, UiText.LogoLabel(_state, n));
            if (_list.Visible) _list.SetResult(_cur, _state);
        }

        private void Log(string s) => _log.Text = s;

        /// 실도면 연결 1단계 창: 합성 치수표 후보(S140 숫자)로. 실제 도면 값 아님.
        private void ShowLinkDialog()
        {
            static System.Text.Json.JsonElement J(string v) => JsonDocument.Parse(v).RootElement.Clone();
            var found = new ParticularsResponse
            {
                Items = new()
                {
                    ["loa"] = new ParticularItemDto { NameKo = "전장 LOA", Status = "found", Value = J("140") },
                    ["lbp"] = new ParticularItemDto { NameKo = "LBP", Status = "found", Value = J("132") },
                    ["scale"] = new ParticularItemDto { NameKo = "축척", Status = "found", Value = J("1000") },
                    ["ship_type"] = new ParticularItemDto { NameKo = "선종", Status = "conflict" },
                },
                SuggestedProps = new() { ["GARC_LOA"] = "140", ["GARC_LBP"] = "132", ["GARC_SCALE"] = "1/1000" },
                SuggestedConditions = new() { new ConditionDto { Name = "설계 흘수", TAft = 7.2, TFwd = 7.2 }, new ConditionDto { Name = "구조 흘수", TAft = 8.0, TFwd = 8.0 } },
                HumanInputsKo = new() { "L(ICLL)은 선급 계산서 값", "밸러스트 도착 흘수는 복원성 계산서 값" },
            };
            using var dlg = new ParticularsDialog(found, new Dictionary<string, string>(), unitless: true, "S140_sample.dwg");
            var r = dlg.ShowDialog(this);
            Log($"실도면 연결 1단계: {r} — " + string.Join(", ", dlg.Result.Select(kv => $"{kv.Key}={kv.Value}")));
        }

        /// 비교 응답(samples_compare.json): 0 = v1 → v2, 1 = rev0 → rev2
        private static List<CompareResponse> CompareSamples()
        {
            var list = new List<CompareResponse>();
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BuoyPreview.samples_compare.json");
            if (s == null) return list;
            foreach (var o in JsonDocument.Parse(s).RootElement.EnumerateArray())
                list.Add(o.GetProperty("resp").Deserialize<CompareResponse>(Contract.Json)!);
            return list;
        }

        private static readonly VersionEntry V1 = new() { Version = 1, Created = "2026-10-18T14:32:00", Author = "설계자", Reason = "rule_violation", Reference = "SOLAS V/22.1.1, II-1/12" };
        private static readonly VersionEntry V2 = new() { Version = 2, Created = "2026-10-18T16:05:00", Author = "설계자", Reason = "rule_violation", Reference = "SOLAS V/22.1.1, II-1/12" };

        /// 버전 비교 창: 플러그인과 같은 CompareForm, 도면·엔진 대신 합성 샘플 비교 응답
        private void ShowCompare()
        {
            var samples = CompareSamples();
            if (samples.Count == 0) return;
            var src = new CompareSource
            {
                DrawingName = "S140_v2_fixed.dwg",
                LoadIndex = () => new VersionIndex { Versions = new() { V1, V2 } },
                Compare = (a, b, la, lb) => lb == "현재" && samples.Count > 1 ? samples[1] : samples[0],
                DwgNumber = () => "S140-GA-001",
            };
            var f = new CompareForm(src, d => { Log(d == null ? "도면 표시 지움" : $"도면에 표시: 변경 {d.Changes.Count}건"); return true; },
                                    (c, sc) => Log($"도면에서 {c.Number}번으로 이동"));
            f.Show(this);
        }

        /// A4 전달 시트: 플러그인과 같은 ExportSheet로 그린 그림을 창에 띄운다
        private void ShowSheet()
        {
            var samples = CompareSamples();
            if (samples.Count == 0) return;
            var bmp = ExportSheet.Render(samples[0], V1, V2, "v1", "v2", "S140-GA-001", "S140_v2_fixed.dwg");
            var f = new Form { Text = "A4 전달 시트 — 미리보기", ClientSize = new Size(1123, 794), BackColor = Color.White, StartPosition = FormStartPosition.CenterScreen };
            var pb = new PictureBox { Dock = DockStyle.Fill, Image = bmp, SizeMode = PictureBoxSizeMode.Zoom };
            f.Controls.Add(pb);
            f.FormClosed += (_, _) => bmp.Dispose();
            f.Show(this);
        }

        private LinkProgress? _strip;
        private int _stripStep = 1;

        /// 2~5단계 안내 띠: 누를 때마다 다음 단계로
        private void ShowLinkStrip()
        {
            string[] what = { "", "측면도 영역의 한쪽 구석을 클릭하세요 — 평면도·단면도는 빼고", "AP(선미 수선, 보통 타두재 중심) 위치를 클릭하세요",
                              "장애물을 고르세요 — 선교 앞 갑판 화물·크레인·마스트(조타실 자체는 빼고)", "조종위치를 클릭하세요 — 조타실 앞 창 쪽, 선교 갑판(바닥) 높이" };
            _stripStep = _stripStep >= 5 ? 2 : _stripStep + 1;
            _strip ??= new LinkProgress();
            _strip.ShowOver(this, Bounds);
            _strip.Set(_stripStep, what[_stripStep - 1]);
        }
    }
}
