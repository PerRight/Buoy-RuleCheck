// 떠 있는 로고(명세 5절, 디자인 보드 03): 화면 오른쪽 아래 판(지름 52) 위에 오브(36)가 떠서 돈다. 부표 등 색 = 전체 상태.
//   감시 중 8 s · 흰 등 / 검사 중 1.4 s(최소 0.6 s) / 수정 소요 6 s · 빨간 등 · 남은 개수 칩 / 확인 필요 빈 조각 · 주황 등 · '!' 칩
//   / 모두 충족 정지 · 파란 등 / 연결 끊김 45% · 정지 · 등 꺼짐(판정하지 않음). 동작 줄이기면 멈춘 자세, 색은 그대로.
// 누르면(또는 Ctrl+.) 수정 소요 목록이 로고 위로 열린다 — 한 줄을 누르면 그 고칠 자리로 이동해 부표 카드를 고정한다.
// 로고는 드래그로 옮길 수 있고 위치는 ui.json에 남는다. 해결이 생기면 로고 옆에 토스트('해결 N건 · 저장하면 버전 기록에 남아요').
// 판정은 하지 않는다 — 엔진 결과를 옮겨 보여 주기만.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace GaRuleCheck
{
    public static class FloatingUi
    {
        private static LogoButton? _logo;
        private static LogoListPopup? _list;
        private static ToastForm? _toast;
        private static bool _hooked;
        private static CheckResponse? _last;
        private static string _state = "idle";
        private static long _listClosedAt;

        /// 목록 한 줄 → 그 항목의 고칠 자리로(부표 카드 고정)
        public static event Action<RuleResultDto>? RowPicked;
        /// 목록의 '팔레트에서 자세히'
        public static event Action? PaletteRequested;

        public static bool ListOpen => _list is { IsDisposed: false, Visible: true };

        /// 플러그인 로드 후(메인 창이 준비된 다음) 한 번 부른다.
        public static void Init()
        {
            if (!_hooked)
            {
                try { AcApp.PreTranslateMessage += OnPreTranslate; _hooked = true; } catch { /* 훅 실패 시 단축키만 안 됨 */ }
            }
            if (UiState.Current.LogoVisible) ShowLogo();
        }

        public static void Shutdown()
        {
            if (_hooked) { try { AcApp.PreTranslateMessage -= OnPreTranslate; } catch { } _hooked = false; }
            _toast?.Dispose(); _toast = null;
            _list?.Dispose(); _list = null;
            _logo?.Dispose(); _logo = null;
        }

        private static IWin32Window Owner() => new WindowWrapper(AcApp.MainWindow.Handle);

        public static void ShowLogo()
        {
            if (_logo == null || _logo.IsDisposed)
            {
                _logo = new LogoButton();
                _logo.ToggleRequested += kb => Toggle(kb);
                _logo.Moved += () => { if (ListOpen) _list!.PlaceNear(_logo.PlateBounds); };
                _logo.Location = InitialLogoLocation();
                _logo.Show(Owner());
                Apply();
            }
            _logo.Visible = true;
            UiState.Current.LogoVisible = true;
            UiState.Current.Save();
        }

        public static void ToggleLogo()
        {
            if (_logo is { Visible: true })
            {
                CloseList();
                _logo.Visible = false;
                UiState.Current.LogoVisible = false;
                UiState.Current.Save();
            }
            else ShowLogo();
        }

        private static long _lastToggle;

        /// 목록 열기/닫기. keyboard = 키보드로 열었으면 첫 줄에 초점.
        public static void Toggle(bool keyboard = false)
        {
            // Ctrl+. 이 AutoCAD 메시지 훅과 로고·목록 KeyDown 양쪽에서 들어와도 한 번만(200 ms 안 중복 무시)
            long now = Environment.TickCount64;
            if (now - _lastToggle < 200) return;
            _lastToggle = now;
            if (ListOpen) { CloseList(); return; }
            // 목록이 바깥 클릭(로고 포함)으로 방금 닫혔으면 다시 열지 않는다
            if (now - _listClosedAt < 500) return;
            OpenList(keyboard);
        }

        public static void OpenList(bool keyboard)
        {
            if (_logo == null || !_logo.Visible) ShowLogo();
            if (_list == null || _list.IsDisposed)
            {
                _list = new LogoListPopup();
                _list.RowPicked += r => { CloseList(); RowPicked?.Invoke(r); };
                _list.PaletteRequested += () => { CloseList(); PaletteRequested?.Invoke(); };
                _list.CloseRequested += CloseList;
                _list.Deactivated += () => { if (ListOpen) { CloseList(); _listClosedAt = Environment.TickCount64; } };
            }
            _list.SetResult(_last, _state);
            _list.PlaceNear(_logo!.PlateBounds);
            _list.Open(Owner(), keyboard);
            _logo.SetExpanded(true);
        }

        public static void CloseList()
        {
            if (_list is { IsDisposed: false, Visible: true }) _list.Hide();
            _logo?.SetExpanded(false);
        }

        /// 검사를 시작함(부표 등 1.4 s로, 최소 0.6 s 보여 줌)
        public static void BeginCheck()
        {
            _logo?.BeginCheck();
        }

        /// 새 판정: 로고 상태·칩, 열린 목록 갱신
        public static void SetResult(CheckResponse r)
        {
            _last = r;
            _state = UiText.LogoState(r, "idle");
            Apply();
        }

        /// 판정이 없는 상태: idle(감시 중·검사 전) / down(연결 끊김 — 판정하지 않음)
        public static void SetState(string state)
        {
            _last = null;
            _state = state;
            Apply();
        }

        private static void Apply()
        {
            var (chip, n) = UiText.LogoChip(_last, _state);
            _logo?.SetState(OrbStates.ForState(_state), chip, UiText.LogoLabel(_state, n));
            if (ListOpen) _list!.SetResult(_last, _state);
        }

        /// 로고 옆 토스트(해결 등). 로고가 없으면 AutoCAD 창 오른쪽 아래.
        public static void Toast(string text, string status = "pass")
        {
            try
            {
                _toast?.Dispose();
                _toast = new ToastForm(text, status);
                Rectangle anchor = _logo is { Visible: true } ? _logo.PlateBounds : MainCorner();
                _toast.ShowNear(anchor, Owner());
            }
            catch { /* 토스트는 부가 기능 */ }
        }

        private static Rectangle MainCorner()
        {
            if (GetWindowRect(AcApp.MainWindow.Handle, out var r)) return new Rectangle(r.Right - 80, r.Bottom - 120, 52, 52);
            var a = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            return new Rectangle(a.Right - 80, a.Bottom - 120, 52, 52);
        }

        private static Point InitialLogoLocation()
        {
            var st = UiState.Current;
            if (st.LogoX is int x && st.LogoY is int y)
            {
                var wa = Screen.FromPoint(new Point(x + 30, y + 30)).WorkingArea;
                if (wa.Contains(x + 30, y + 30)) return new Point(x, y);
            }
            try
            {
                if (GetWindowRect(AcApp.MainWindow.Handle, out var r))
                    return new Point(r.Right - LogoButton.W - 64, r.Bottom - LogoButton.H - 60);   // 오른쪽 아래(상태 표시줄 위)
            }
            catch { }
            var a = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            return new Point(a.Right - LogoButton.W - 64, a.Bottom - LogoButton.H - 60);
        }

        private const int WM_KEYDOWN = 0x0100, VK_OEM_PERIOD = 0xBE;

        private static void OnPreTranslate(object? sender, Autodesk.AutoCAD.ApplicationServices.PreTranslateMessageEventArgs e)
        {
            var m = e.Message;
            if (m.message == WM_KEYDOWN && (int)m.wParam == VK_OEM_PERIOD && (Control.ModifierKeys & Keys.Control) != 0)
            {
                Toggle(keyboard: true);
                e.Handled = true;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        private sealed class WindowWrapper : IWin32Window
        {
            public WindowWrapper(IntPtr h) { Handle = h; }
            public IntPtr Handle { get; }
        }
    }
}
