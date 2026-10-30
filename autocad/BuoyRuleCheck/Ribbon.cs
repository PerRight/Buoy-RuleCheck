// 리본 'Buoy-RuleCheck' 탭(명세 6-3, 디자인 보드 03): 검토 · 입력 · 표시·기록 · 버전. 버튼은 명령 문자열을 실행만 한다(로직은 Commands에).
// 아이콘은 잉크 한 색의 선 그림(판정 색 없음) — 이미지 파일 없이 아래 경로 문자열로 그린다. 리본이 밝은 테마(어두움이 아닌
// 테마 — AutoCAD와 같은 기준)면 어두운 잉크, 어두운 테마면 흰 잉크로 칠한다(테마를 바꾸면 따라감 — 아래 '테마 따라가기'). '검사 시작'만 멈춘 자세의 오브(점으로 된 구).
// 자주 쓰지 않는 명령(역할 지정·역할 보기·초기 설정·로고·동작 줄이기·엔진 확인)은 패널 아래 펼침(slideout)에.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BuoyRuleCheck
{
    public static class RibbonUi
    {
        private const string TabId = "BRC_RIBBON_TAB";
        private static readonly List<(RibbonButton Button, Ico Icon)> Buttons = new();
        private static bool _themeHooked;

        /// 아이콘 하나: 32×32 칸의 선 경로(WPF 경로 문법 = SVG와 같은 M/L/H/V/A/Q/Z), 칠한 원·선 원("cx cy r; …").
        /// 'orb'면 오브를 그린다. 리눅스 확인용 스크립트(docs/review/ui)가 이 문자열을 그대로 읽어 그림으로 만든다.
        public sealed record Ico(string Path, string Fills = "", string Rings = "", bool Orb = false);

        public static void Install()
        {
            if (ComponentManager.Ribbon != null) { Create(); return; }
            ComponentManager.ItemInitialized += OnItemInitialized;
        }

        private static void OnItemInitialized(object? sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            Create();
        }

        public static void Create()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            foreach (var t in ribbon.Tabs)
                if (t.Id == TabId) return;
            Buttons.Clear();
            _inkLight = ThemeIsLight();   // 처음 칠할 잉크(이후 바뀌면 Repaint)

            var tab = new RibbonTab { Title = Product.Name, Id = TabId };
            ribbon.Tabs.Add(tab);

            tab.Panels.Add(Panel("검토",
                new[]
                {
                    Big("검사 시작", "BRC", new Ico("", Orb: true), "팔레트를 열고 이 도면을 감시합니다. 도면이 바뀌면 바뀐 입력에 걸린 규칙만 다시 검사해 고칠 자리에 부표를 띄웁니다."),
                    Big("지금 검사", "BRCCHECK", Icons.Recheck, "지금 도면으로 바로 다시 검사합니다."),
                    Big("감시 중지", "BRCOFF", Icons.Stop, "자동 검사를 멈추고 부표·표시를 지웁니다(도면은 그대로)."),
                },
                new[]
                {
                    Big("AI 설명", "BRCAI", Icons.Explain, "판정·변경 설명을 Claude API로 다듬기(기본 꺼짐). 숫자·도면 이름은 보내지 않고, 보낼 본문을 미리 볼 수 있습니다."),
                    Big("엔진 확인", "BRCHEALTH", Icons.Pulse, "로컬 검사 엔진(127.0.0.1)이 응답하는지 확인합니다."),
                }));
            tab.Panels.Add(Panel("입력",
                new[]
                {
                    Big("선박 정보", "BRCSHIP", Icons.Ship, "LOA·L·AP·FP·흘수 조건 편집(팔레트 '선박 정보' 탭)"),
                    Big("조종위치", "BRCEYE", Icons.Eye, "조종위치 점(선교 갑판 높이)을 찍습니다."),
                    Big("실도면 연결", "BRCLINK", Icons.Link,
                        "GA_ 레이어가 없는 실제 도면을 레이어와 객체를 바꾸지 않고 연결: 치수·축척 → 측면도 영역 → AP·FP·기선 → 역할 지정 → 조종위치."),
                },
                new[]
                {
                    Big("역할 지정", "BRCROLE", Icons.Tag, "고른 객체에 선체·장애물·충돌격벽·구상선수 역할을 붙이거나 해제(레이어는 그대로)"),
                    Big("역할 보기", "BRCROLESHOW", Icons.Layers, "엔진이 판정에 쓴 객체를 역할별 색으로 잠시 표시(도면은 그대로)"),
                    Big("초기 설정", "BRCSETUP", Icons.Plus, "GA_ 레이어 5개·라벨 문자 스타일·특성 틀 생성"),
                }));
            tab.Panels.Add(Panel("표시 · 기록",
                new[]
                {
                    Big("부표 표시", "BRCTOGGLE", Icons.Buoy, "도면 위 부표 켜기/끄기(검사는 계속, 도면은 그대로)"),
                    Big("라벨 확대", "BRCLARGE", Icons.Large, "도면 위 라벨 글자·선 두께·표식 크기만 2.5배로(프로젝터·화면 공유용, 판정·도면은 그대로)"),
                    Big("리포트", "BRCREPORT", Icons.Report, "근거·측면도·이력을 HTML로 저장하고 엽니다."),
                },
                new[]
                {
                    Big("로고 목록", "BRCPANEL", Icons.LogoList, "떠 있는 로고의 수정 소요 목록 열기/닫기 (Ctrl+.)"),
                    Big("로고 보이기", "BRCLOGO", Icons.LogoShow, "떠 있는 로고 보이기/숨기기"),
                    Big("동작 줄이기", "BRCMOTION", Icons.Motion, "오브·부표의 회전·물결·크기 변화를 멈춥니다(상태 색은 그대로). Windows '애니메이션 표시'를 끄면 자동으로 켜집니다."),
                }));
            tab.Panels.Add(Panel("버전",
                new[]
                {
                    Big("버전 기록", "BRCVERSION", Icons.Bookmark, "지금 상태를 버전으로 기록(작성자·사유·관련 규정). 저장할 때도 자동으로 묻습니다."),
                    Big("버전 비교", "BRCCOMPARE", Icons.Compare, "두 버전 비교: 나란히·겹쳐·차이만, 변경 목록, 생산팀 전달"),
                },
                Array.Empty<RibbonButton>()));

            HookTheme();
        }

        // ── 테마 따라가기 ──────────────────────────────────────────────────────
        // 10-29 사용자 PC(AutoCAD 2027): COLORTHEME을 바꿔도 아이콘 잉크가 그대로였다. 변수 변경 이벤트만 믿지 않고
        // ① COLORTHEME 변경 이벤트 ② AutoCAD 테마 변경 이벤트(ActiveThemeColor, 내부 API라 리플렉션으로) ③ Idle에서 1.5초마다 확인
        // 셋 중 무엇이든 오면 다음 Idle에 지금 테마를 다시 읽어 칠한다. 이벤트가 값이 바뀌기 전에 와도 Idle에서 다시 읽으므로 맞는다.
        private static bool _inkLight;                 // 지금 아이콘이 밝은 리본용(어두운 잉크)으로 칠해져 있나
        private static bool _repaintQueued;
        private static long _nextPoll;
        private static int _repaints, _sysvarHits, _themeHits, _pollHits;
        private static string _themeHook = "연결 전";

        private static void HookTheme()
        {
            if (_themeHooked) return;
            _themeHooked = true;
            var hooks = new List<string>();
            try { AcApp.SystemVariableChanged += OnSysVar; hooks.Add("변수"); } catch { }
            try { if (HookActiveTheme()) hooks.Add("테마"); } catch { }
            try { AcApp.Idle += OnIdlePoll; hooks.Add("확인"); } catch { }
            _themeHook = hooks.Count > 0 ? string.Join("·", hooks) : "없음";
        }

        /// Autodesk.AutoCAD.Internal.ActiveThemeColor.Instance().ColorThemeChanged — 공개 문서에 없는 API라
        /// 버전마다 없을 수 있어 리플렉션으로만 건다(없으면 변수 이벤트·Idle 확인으로 충분).
        private static bool HookActiveTheme()
        {
            var t = typeof(AcApp).Assembly.GetType("Autodesk.AutoCAD.Internal.ActiveThemeColor");
            var inst = t?.GetMethod("Instance", Type.EmptyTypes)?.Invoke(null, null);
            var ev = t?.GetEvent("ColorThemeChanged");
            if (inst == null || ev?.EventHandlerType == null) return false;
            var mi = typeof(RibbonUi).GetMethod(nameof(OnActiveTheme), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (mi == null) return false;
            ev.AddEventHandler(inst, Delegate.CreateDelegate(ev.EventHandlerType, mi));
            return true;
        }

        private static void OnActiveTheme(object? sender, EventArgs e)
        {
            _themeHits++;
            QueueRepaint();
        }

        private static void OnSysVar(object? sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
        {
            if (!string.Equals(e.Name, "COLORTHEME", StringComparison.OrdinalIgnoreCase)) return;
            _sysvarHits++;
            QueueRepaint();
        }

        private static void QueueRepaint()
        {
            if (_repaintQueued) return;
            _repaintQueued = true;
            try { AcApp.Idle += RepaintOnIdle; } catch { _repaintQueued = false; }
        }

        private static void RepaintOnIdle(object? sender, EventArgs e)
        {
            AcApp.Idle -= RepaintOnIdle;
            _repaintQueued = false;
            Guard.Run("리본 아이콘", () => Repaint(force: true));
        }

        /// 이벤트가 오지 않는 경로(옵션 창·프로필 바꿈 등)도 1.5초 안에 따라가게.
        private static void OnIdlePoll(object? sender, EventArgs e)
        {
            long now = Environment.TickCount64;
            if (now < _nextPoll) return;
            _nextPoll = now + 1500;
            if (Buttons.Count == 0 || ThemeIsLight() == _inkLight) return;
            _pollHits++;
            Guard.Run("리본 아이콘", () => Repaint(force: false));
        }

        private static void Repaint(bool force)
        {
            bool light = ThemeIsLight();
            if (!force && light == _inkLight) return;
            var ink = InkFor(light);
            foreach (var (b, ico) in Buttons)
            {
                b.LargeImage = Render(ico, 32, ink);
                b.Image = Render(ico, 16, ink);
            }
            _inkLight = light;
            _repaints++;
        }

        /// 리본이 밝은 테마인가. AutoCAD 리본 자신이 쓰는 값(ActiveThemeColor.CurrentTheme: Dark·Light·User)을 먼저,
        /// 못 읽으면 COLORTHEME으로. AutoCAD와 같게 '어두움(Dark·0)이 아니면 밝음'으로 본다 — User·2 같은 값도 밝은 리본.
        private static bool ThemeIsLight()
        {
            var name = ActiveThemeName();
            if (!string.IsNullOrEmpty(name)) return !string.Equals(name, "Dark", StringComparison.OrdinalIgnoreCase);
            var v = ColorThemeVar();
            return v != null && v != 0;
        }

        private static int? ColorThemeVar()
        {
            try { return Convert.ToInt32(AcApp.GetSystemVariable("COLORTHEME"), CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static string? ActiveThemeName()
        {
            try
            {
                var t = typeof(AcApp).Assembly.GetType("Autodesk.AutoCAD.Internal.ActiveThemeColor");
                return t?.GetProperty("CurrentTheme")?.GetValue(null)?.ToString();
            }
            catch { return null; }
        }

        private static Color InkFor(bool lightRibbon) => lightRibbon
            ? Color.FromRgb(0x23, 0x23, 0x27)    // 밝은 리본: 어두운 잉크
            : Color.FromRgb(0xED, 0xEF, 0xF3);   // 어두운 리본: 흰 잉크

        /// BRCHEALTH 한 줄: 아이콘이 테마를 따라가지 않을 때 어디서 막혔는지.
        public static string Diagnose()
        {
            string ink = Buttons.Count == 0 ? "리본 탭 없음" : _inkLight ? "어두운 잉크(밝은 리본용)" : "흰 잉크(어두운 리본용)";
            var v = ColorThemeVar();
            var name = ActiveThemeName();
            string by = !string.IsNullOrEmpty(name) ? "테마값" : v != null ? "COLORTHEME" : "못 읽음";
            return $"리본 아이콘: {ink} · 판단={by} · AutoCAD 테마={(string.IsNullOrEmpty(name) ? "?" : name)} · COLORTHEME={(v?.ToString() ?? "?")} · " +
                   $"감시 {_themeHook} · 다시 칠함 {_repaints}회(변수 {_sysvarHits} · 테마 {_themeHits} · 확인 {_pollHits})";
        }

        private static RibbonPanel Panel(string title, RibbonButton[] main, RibbonButton[] more)
        {
            var src = new RibbonPanelSource { Title = title };
            foreach (var b in main) src.Items.Add(b);
            if (more.Length > 0)
            {
                src.Items.Add(new RibbonPanelBreak());   // 아래는 패널 제목줄 ▾로 펼치는 칸
                foreach (var b in more) src.Items.Add(b);
            }
            return new RibbonPanel { Source = src };
        }

        private static RibbonButton Big(string text, string command, Ico icon, string tip)
        {
            var b = new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = Render(icon, 32, InkFor(_inkLight)),
                Image = Render(icon, 16, InkFor(_inkLight)),
                ToolTip = tip,
                CommandParameter = "_" + command + " ",
                CommandHandler = new Handler(),
            };
            Buttons.Add((b, icon));
            return b;
        }

        /// 선 경로 + 원을 잉크 한 색으로. 32 px는 선 2, 16 px는 선 1.25(같은 그림을 줄임).
        private static BitmapSource Render(Ico ico, int size, Color ink)
        {
            var brush = new SolidColorBrush(ink);
            brush.Freeze();
            double k = size / 32.0;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (ico.Orb) DrawOrb(dc, ink, size);
                else
                {
                    dc.PushTransform(new ScaleTransform(k, k));
                    var pen = new Pen(brush, size >= 32 ? 2.0 : 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    pen.Freeze();
                    if (!string.IsNullOrWhiteSpace(ico.Path)) dc.DrawGeometry(null, pen, Geometry.Parse(ico.Path));
                    foreach (var (cx, cy, r) in Circles(ico.Rings)) dc.DrawEllipse(null, pen, new Point(cx, cy), r, r);
                    foreach (var (cx, cy, r) in Circles(ico.Fills)) dc.DrawEllipse(brush, null, new Point(cx, cy), r, r);
                    dc.Pop();
                }
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static IEnumerable<(double, double, double)> Circles(string spec)
        {
            foreach (var part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var n = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                if (n.Length == 3) yield return (n[0], n[1], n[2]);
            }
        }

        /// '검사 시작': 멈춘 자세의 오브(잉크 점, 부표 등) — OrbModel의 같은 점 배치로.
        private static void DrawOrb(DrawingContext dc, Color ink, int size)
        {
            var geo = new OrbGeometry(new OrbSpec { Size = size, Lamp = true });
            var dots = new List<OrbDot>();
            geo.Frame(0.0, dots);
            foreach (var d in dots)
            {
                var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(d.Alpha, 0, 1)), ink.R, ink.G, ink.B));
                b.Freeze();
                dc.DrawEllipse(b, null, new Point(d.X, d.Y), d.D / 2, d.D / 2);
            }
            var lamp = new SolidColorBrush(ink);
            lamp.Freeze();
            dc.DrawEllipse(lamp, null, new Point(geo.LampX, geo.LampY), geo.LampD / 2, geo.LampD / 2);
        }

        private sealed class Handler : ICommand
        {
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter)
            {
                if (parameter is RibbonButton b && b.CommandParameter is string cmd)
                    AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute(cmd, true, false, true);
            }
        }

        /// 선 아이콘(32×32). 판정 색을 쓰지 않는다.
        public static class Icons
        {
            public static readonly Ico Recheck = new("M 25 17 A 9 9 0 1 1 21.5 9.2 M 22.5 3.5 L 22.5 9.8 L 16.2 9.8");
            public static readonly Ico Stop = new("M 10 9 H 22 A 1 1 0 0 1 23 10 V 22 A 1 1 0 0 1 22 23 H 10 A 1 1 0 0 1 9 22 V 10 A 1 1 0 0 1 10 9 Z");
            public static readonly Ico Pulse = new("M 3 17 H 9 L 12 9 L 17 24 L 20 14 L 22 17 H 29");
            public static readonly Ico Ship = new("M 3 19 H 29 L 25.5 26 H 7.5 Z M 9 19 V 12 H 16 V 19 M 11.5 12 V 7.5");
            public static readonly Ico Eye = new("M 3.5 16 Q 16 5 28.5 16 Q 16 27 3.5 16 Z", Fills: "16 16 2", Rings: "16 16 4.5");
            public static readonly Ico Link = new("M 13 19 L 19 13 M 11 15.5 L 8.2 18.3 A 4.2 4.2 0 0 0 13.7 23.8 L 16.5 21 M 15.5 11 L 18.3 8.2 A 4.2 4.2 0 0 1 23.8 13.7 L 21 16.5");
            public static readonly Ico Tag = new("M 5 5 H 15 L 27 17 L 17 27 L 5 15 Z", Fills: "10.5 10.5 1.8");
            public static readonly Ico Layers = new("M 16 5 L 28 11 L 16 17 L 4 11 Z M 4 16 L 16 22 L 28 16 M 4 21 L 16 27 L 28 21");
            public static readonly Ico Plus = new("M 16 8 V 24 M 8 16 H 24", Rings: "16 16 12");
            /// 부표: 고정점 → 지시선(45°) → 오브(점 몇 개)
            public static readonly Ico Buoy = new("M 9.5 22.5 L 14.4 17.6", Fills: "8 24 1.8; 20 6.9 1.3; 17.2 9.6 1.05; 20.6 10.4 1.05; 23.6 10.2 1.05; 18.4 13.6 1.05; 21.9 13.9 1.05; 20 16.8 0.9; 16.4 12.4 0.7; 24.1 13.6 0.7", Rings: "20 12 6.5");
            public static readonly Ico Large = new("M 4 26 L 11 7 L 18 26 M 6.6 19.5 H 15.4 M 24 9 V 19 M 19 14 H 29");
            public static readonly Ico Report = new("M 8 4 H 19 L 25 10 V 28 H 8 Z M 19 4 V 10 H 25 M 12 16 H 21 M 12 20 H 21 M 12 24 H 17");
            public static readonly Ico LogoList = new("M 4 23 H 15 M 4 27 H 11", Fills: "24.5 7.5 3", Rings: "15 14 8");
            public static readonly Ico LogoShow = new("", Fills: "16 9.5 1.6", Rings: "16 16 11");
            public static readonly Ico Motion = new("M 5 16 H 9 M 23 16 H 27 M 12.5 11 V 21 M 19.5 11 V 21", Rings: "16 16 12");
            public static readonly Ico Bookmark = new("M 9 4 H 23 V 28 L 16 22 L 9 28 Z");
            /// AI 설명: 말풍선 + 글줄
            public static readonly Ico Explain = new("M 5 6 H 27 V 21 H 15 L 9 26 V 21 H 5 Z M 10 11.5 H 22 M 10 16 H 18");
            public static readonly Ico Compare = new("M 6 11 H 25 M 20 6 L 25 11 L 20 16 M 26 21 H 7 M 12 16 L 7 21 L 12 26");
        }
    }
}
