// 팔레트 미리보기: 플러그인과 같은 ResultPanel에 합성 샘플(S140)의 엔진 응답을 차례로 넣어 본다(AutoCAD 없이).
// 응답은 samples.json(make_samples.py로 만듦). 도면 쪽 동작(근거 표시·부표 이동)은 여기서는 글로만 알린다.
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
                b.Click += (_, _) => _panel.ShowResult(resp, "S140_" + name.Split(' ')[0] + ".dwg", record: true, seconds: 0.3);
                list.Controls.Add(b);
            }
            var idle = Theme.TextButton("검사 전 화면");
            idle.Click += (_, _) => _panel.SetIdle("검사 전", "S140.dwg");
            var down = Theme.TextButton("연결 끊김 화면");
            down.Click += (_, _) => _panel.ShowEngineDown("엔진 서버(127.0.0.1:8765)에 연결할 수 없습니다.");
            list.Controls.Add(idle);
            list.Controls.Add(down);

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
            if (_samples.Count > 2) _panel.ShowResult(_samples[2].Resp, "S140_rev2.dwg", seconds: 0.3);
        }

        private void Log(string s) => _log.Text = s;
    }
}
