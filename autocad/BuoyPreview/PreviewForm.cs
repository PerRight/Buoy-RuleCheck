// 미리보기 화면: 플러그인의 그리기 코드를 그대로 써서 오브 상태를 한 화면에 보여 준다(명세 7절 3단계 '단독 테스트 화면').
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    public sealed class PreviewForm : Form
    {
        private sealed class Cell
        {
            public string No = "", Name = "", Desc = "", Spec = "";
            public Func<BuoyAnim> Make = () => new BuoyAnim("fail");
            public Action<BuoyAnim>? Setup;
            public BuoyAnim Anim = null!;
            public double Loop;          // 0이면 반복 안 함(초)
            public double T;
            public string? Cluster;
        }

        private readonly Canvas _canvas = new() { Dock = DockStyle.Fill };
        private readonly CheckBox _reduce = new()
        {
            Text = "동작 줄이기", ForeColor = Theme.Text, BackColor = Theme.Bg, AutoSize = true, Font = Theme.Body,
        };
        private readonly List<Cell> _buoys = new();
        private readonly List<(OrbStates.LogoState st, OrbMotion m, string? chip)> _logos = new();
        private readonly List<(OrbGeometry g, OrbMotion m)> _ladder = new();
        private readonly Action<double> _tick;

        public PreviewForm()
        {
            Text = Product.Name + " — 미리보기";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            ClientSize = new Size(1240, 860);
            StartPosition = FormStartPosition.CenterScreen;
            _reduce.Checked = UiState.Current.ReduceMotion;
            _reduce.CheckedChanged += (_, _) => UiState.SetReduceMotion(_reduce.Checked);
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Bg, Padding = new Padding(0, 6, 16, 0) };
            var restart = Theme.RingButton("처음부터");
            restart.Click += (_, _) => Restart();
            var cards = Theme.PrimaryButton("부표 카드 보기");
            cards.Click += (_, _) => ShowSampleCards();
            var palette = Theme.RingButton("팔레트 보기");
            palette.Click += (_, _) => new PalettePreview().Show(this);
            top.Controls.Add(_reduce);
            top.Controls.Add(restart);
            top.Controls.Add(palette);
            top.Controls.Add(cards);
            Controls.Add(_canvas);
            Controls.Add(top);
            _canvas.Paint += (_, e) => PaintAll(e.Graphics);

            foreach (var (st, chip) in new (OrbStates.LogoState, string?)[]
                     {
                         (OrbStates.Watching, null), (OrbStates.Checking, null), (OrbStates.Fail, "2"),
                         (OrbStates.Warn, "!"), (OrbStates.Pass, null), (OrbStates.Down, null),
                     })
                _logos.Add((st, new OrbMotion(st.Period), chip));

            foreach (var s in new[] { 16, 20, 24, 32, 48, 64, 120 })
                _ladder.Add((new OrbGeometry(new OrbSpec { Size = s, Lamp = true, Sea = s >= 48 }), new OrbMotion(10)));

            _buoys.AddRange(new[]
            {
                new Cell { No = "01", Name = "등장", Desc = "고칠 자리로 모여들며 떠오름, 물결", Spec = "700 ms · (.32,.72,0,1)", Loop = 3.4 },
                new Cell { No = "02", Name = "대기", Desc = "고칠 자리 위에서 천천히 자전", Spec = "한 바퀴 5 s", Make = () => new BuoyAnim("fail", appear: false) },
                new Cell { No = "03", Name = "호버", Desc = "커지고 고칠 내용을 카드로", Spec = "×1.25 · 160 ms", Make = () => new BuoyAnim("fail", appear: false), Setup = a => a.SetHover(true) },
                new Cell { No = "04", Name = "수정 중", Desc = "명령 실행 동안 멈추고 흐려짐", Spec = "정지 · 투명도 35%", Make = () => new BuoyAnim("fail", appear: false), Setup = a => a.BeginEdit() },
                new Cell { No = "05", Name = "다시 검사", Desc = "명령이 끝나면 빠르게 돈다", Spec = "1.2 s · 최소 0.6 s", Make = () => new BuoyAnim("fail", appear: false), Setup = a => a.BeginRecheck() },
                new Cell { No = "06", Name = "문제없음", Desc = "파랑으로 바뀌며 멈춤", Spec = "정지 · 확인 대기", Make = () => new BuoyAnim("pass", appear: false) },
                new Cell { No = "07", Name = "사라짐", Desc = "확인하면 퍼지며 사라짐", Spec = "700 ms · 1.8배", Make = () => new BuoyAnim("pass", appear: false), Setup = a => a.Vanish(), Loop = 2.0 },
                new Cell { No = "08", Name = "확인 필요", Desc = "한쪽이 빈 채로 돈다 = 입력이 빔", Spec = "#FFB340 · 8 s · 280°–359°", Make = () => new BuoyAnim("warn", appear: false) },
                new Cell { No = "09", Name = "묶음", Desc = "28 px 안에 겹치면 숫자로", Spec = "커서를 올리면 목록·맞춰 확대", Make = () => new BuoyAnim("fail", appear: false), Cluster = "3" },
            });
            Restart();
            _tick = Step;
            OrbClock.Add(_tick);
            FormClosed += (_, _) => OrbClock.Remove(_tick);
        }

        /// S140 예시 숫자로 카드 네 가지(수정 소요·문제없음·확인 필요·묶음)를 띄운다. 숫자는 엔진 출력 형식 그대로.
        private void ShowSampleCards()
        {
            static System.Text.Json.JsonElement J(string s) => System.Text.Json.JsonDocument.Parse(s).RootElement.Clone();
            var fail = new ElementCheckDto
            {
                ElementId = "9A", RuleId = "SOLAS-V-22.1.1", RoleKo = "장애물", Status = "fail", Anchor = new[] { 112.0, 24.0 },
                Fix = new FixDto { Quantity = "z", Current = J("24.0"), Limit = J("22.45"), Op = "≤", Delta = 1.55, ActionKo = "상단을 1.55 m 낮추세요", ConditionKo = "z ≤ 22.45 m 이면 기준 충족", Condition = "밸러스트 도착" },
            };
            var ok = new ElementCheckDto
            {
                ElementId = "97", RuleId = "SOLAS-V-22.1.1", RoleKo = "장애물", Status = "pass", Anchor = new[] { 112.0, 21.4 },
                Fix = new FixDto { Quantity = "z", Current = J("21.4"), Limit = J("22.45"), Op = "≤", Delta = -1.05, ActionKo = "상단 z 21.40 m — 기준 안쪽", ConditionKo = "여유 1.05 m · 기준 z ≤ 22.45 m", Condition = "밸러스트 도착" },
            };
            var warn = new ElementCheckDto { ElementId = "9A", RuleId = "SOLAS-V-22.1.1", RoleKo = "장애물", Status = "unknown", Guide = "흘수 조건이 없습니다 — 선박 정보 탭에서 밸러스트 도착 흘수를 넣으세요." };
            var bhd = new ElementCheckDto
            {
                ElementId = "92", RuleId = "SOLAS-II1-12", RoleKo = "충돌격벽", Status = "fail", Anchor = new[] { 122.5, 14.5 },
                Fix = new FixDto { Quantity = "d", Current = J("11.0"), Limit = J("[6.6, 10.56]"), Op = "range", Delta = 0.44, ActionKo = "격벽을 선수 쪽으로 0.44 m 옮기세요", ConditionKo = "기준점(연장부 중점)에서 6.60~10.56 m 이면 기준 충족" },
            };
            var sets = new[]
            {
                new[] { new BuoyCardItem("fail", "SOLAS-V-22.1.1", "밸러스트 도착", fail) },
                new[] { new BuoyCardItem("pass", "SOLAS-V-22.1.1", "밸러스트 도착", ok) },
                new[] { new BuoyCardItem("warn", "SOLAS-V-22.1.1", null, warn) },
                new[] { new BuoyCardItem("fail", "SOLAS-V-22.1.1", "밸러스트 도착", fail), new BuoyCardItem("fail", "SOLAS-II1-12", null, bhd), new BuoyCardItem("warn", "SOLAS-V-22.1.1", null, warn) },
            };
            int x = Right + 8;
            foreach (var s in sets)
            {
                var card = new BuoyCard(s);
                card.Show(this);
                card.Location = new Point(Math.Min(x, Screen.FromControl(this).WorkingArea.Right - card.Width - 8), Top + 40 + Array.IndexOf(sets, s) * 10);
                card.Opacity = 1;
                card.Pinned2();
                x += card.Width + 8;
            }
        }

        private void Restart()
        {
            foreach (var c in _buoys) { c.Anim = c.Make(); c.Setup?.Invoke(c.Anim); c.T = 0; }
        }

        private void Step(double dt)
        {
            bool red = Motion.Reduced;
            foreach (var l in _logos) l.m.Step(dt, red);
            foreach (var l in _ladder) l.m.Step(dt, red);
            foreach (var c in _buoys)
            {
                c.Anim.Step(dt, red);
                c.T += dt;
                if (c.Loop > 0 && c.T >= c.Loop) { c.Anim = c.Make(); c.Setup?.Invoke(c.Anim); c.T = 0; }
            }
            _canvas.Invalidate();
        }

        private static readonly Font H1 = new(Theme.FontName, 15f, FontStyle.Bold);
        private static readonly Font H2 = new(Theme.FontName, 10f, FontStyle.Bold);

        private void PaintAll(Graphics g)
        {
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool red = Motion.Reduced;
            float wm = Theme.DrawWordmark(g, new PointF(32, 18), 20f, Theme.Text);
            TextRenderer.DrawText(g, "고칠 곳에 떠오르는 부표", Theme.Body, new Point(36 + (int)wm, 30), Theme.Muted);

            // ── 떠 있는 로고 ──
            TextRenderer.DrawText(g, "떠 있는 로고 · 판 52 px · 오브 36 px · 부표 등 색 = 전체 상태", H2, new Point(32, 76), Theme.Text);
            for (int i = 0; i < _logos.Count; i++)
            {
                var (st, m, chip) = _logos[i];
                int x = 40 + i * 190, y = 116;
                Card(g, new Rectangle(x - 8, y - 14, 176, 142));
                LogoPainter.Paint(g, new Point(x + 54, y), st, m.Turn, chip, m.Bob(red));
                TextRenderer.DrawText(g, st.Name, Theme.Bold, new Point(x, y + 66), Theme.Text);
                string spec = st.Key switch
                {
                    "watch" => "한 바퀴 8 s · 등 흰색", "check" => "1.4 s · 최소 0.6 s",
                    "fail" => "등 #FF5F57 · 6 s · 숫자", "warn" => "등 #FFB340 · 빈 조각 · !",
                    "pass" => "정지 · 등 #5A9CFF", _ => "45% · 정지 · 등 꺼짐 — 판정 안 함",
                };
                TextRenderer.DrawText(g, spec, Theme.MonoSmall, new Rectangle(x, y + 88, 162, 36), Theme.Faint, TextFormatFlags.WordBreak);
            }

            // ── 도면 위 부표 ──
            TextRenderer.DrawText(g, "도면 위 부표 · 30 px · 점 36 · 화면 크기 고정 · 고정점 → 지시선 16 px 45° → 오브", H2, new Point(32, 282), Theme.Text);
            for (int i = 0; i < _buoys.Count; i++)
            {
                var c = _buoys[i];
                int col = i % 5, row = i / 5;
                int x = 40 + col * 236, y = 324 + row * 196;
                Card(g, new Rectangle(x - 8, y - 12, 222, 180));
                // 도면 객체 모형(컨테이너): 1px 윤곽 Muted, 위반 객체는 호버 때 판정 색 1.5px
                var box = new Rectangle(x + 40, y + 52, 64, 48);
                using (var p = new Pen(c.Anim.Hover ? BuoyPainter.ColorOf(c.Anim.Status) : Theme.Faint, c.Anim.Hover ? 1.5f : 1f)) g.DrawRectangle(p, box);
                BuoyPainter.Paint(g, new PointF(box.Right, box.Top), c.Anim, red, c.Cluster);
                TextRenderer.DrawText(g, c.No, Theme.MonoSmall, new Point(x, y + 108), Theme.Faint);
                TextRenderer.DrawText(g, c.Name, Theme.Bold, new Point(x + 24, y + 106), Theme.Text);
                TextRenderer.DrawText(g, c.Desc, Theme.Small, new Rectangle(x, y + 126, 206, 18), Theme.Muted, TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, c.Spec, Theme.MonoSmall, new Rectangle(x, y + 144, 206, 18), Theme.Faint, TextFormatFlags.EndEllipsis);
            }

            // ── 크기별 ──
            TextRenderer.DrawText(g, "크기별 · 20 px 이하 24점 · 40 px 이하 36점 · 96 px 이하 80점 · 그 위 160점 · 수선 고리는 48 px부터", H2, new Point(32, 724), Theme.Text);
            float lx = 40;
            foreach (var (geo, m) in _ladder)
            {
                float y = 846 - (float)geo.Size - 20;
                OrbRenderer.Draw(g, geo, m.Turn, new PointF(lx, y), new OrbLook { Dots = OrbLook.Ink, Lamp = Color.White });
                TextRenderer.DrawText(g, $"{geo.Size:0}px · {geo.Count + (geo.Spec.Lamp ? 0 : 0)}점", Theme.MonoSmall, new Point((int)lx, 830), Theme.Faint);
                lx += (float)geo.Size + 56;
            }
            if (red) TextRenderer.DrawText(g, "동작 줄이기 켜짐 — 멈춘 자세, 상태 색은 그대로", Theme.Small, new Point(900, 830), Theme.Muted);
        }

        private static void Card(Graphics g, Rectangle r)
        {
            using var path = Theme.RoundRect(r, 12);
            using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
            using (var p = new Pen(Theme.RingOnCard, 1f)) g.DrawPath(p, path);
        }

        private sealed class Canvas : Panel
        {
            public Canvas() { DoubleBuffered = true; BackColor = Theme.Bg; }
        }
    }
}
