// 화면 상태 기억: 로고 위치·패널 열림·'수정 소요만'·동작 줄이기·부표 표시. %APPDATA%\BuoyRuleCheck\ui.json (도면 내용 없음).
// 옛 이름(GA RuleCheck)의 %APPDATA%\GaRuleCheck\ui.json이 있으면 처음 한 번 읽어 이어 쓴다.
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BuoyRuleCheck
{
    public sealed class UiState
    {
        public int? LogoX { get; set; }
        public int? LogoY { get; set; }
        public bool PanelOpen { get; set; }
        public bool LogoVisible { get; set; } = true;
        public bool FailOnly { get; set; }
        /// 동작 줄이기(플러그인 설정). Windows '애니메이션 표시'가 꺼져 있어도 줄인다(Motion.Reduced)
        public bool ReduceMotion { get; set; }
        /// 도면 위 부표 표시
        public bool BuoysVisible { get; set; } = true;

        private static UiState? _current;
        public static UiState Current => _current ??= Load();

        private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static string FilePath => Path.Combine(AppData, "BuoyRuleCheck", "ui.json");
        private static string OldFilePath => Path.Combine(AppData, "GaRuleCheck", "ui.json");

        private static UiState Load()
        {
            foreach (var f in new[] { FilePath, OldFilePath })
            {
                try
                {
                    if (File.Exists(f))
                        return JsonSerializer.Deserialize<UiState>(File.ReadAllText(f, Encoding.UTF8), Contract.Json) ?? new UiState();
                }
                catch { /* 깨진 파일은 무시하고 다음 후보 → 기본값 */ }
            }
            return new UiState();
        }

        /// '수정 소요만'을 이번 실행 동안만 바꿨을 때 ui.json에 쓸 원래 값(다른 설정을 저장해도 잠깐 바꾼 값이 남지 않게)
        private bool? _persistedFailOnly;

        public void Save()
        {
            bool now = FailOnly;
            if (_persistedFailOnly is bool keep) FailOnly = keep;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Contract.Json), new UTF8Encoding(false));
            }
            catch { /* 저장 실패는 기능에 영향 없음 */ }
            finally { FailOnly = now; }
        }

        /// '미충족만 보기'가 바뀌면 열린 패널(팔레트·떠 있는 패널)이 같이 바뀌게.
        public static event Action? FailOnlyChanged;

        public static event Action? MotionChanged;

        public static void SetReduceMotion(bool on)
        {
            if (Current.ReduceMotion == on) return;
            Current.ReduceMotion = on;
            Current.Save();
            MotionChanged?.Invoke();
        }

        /// persist=false: 이번 실행 동안만(예: '근거 ›'로 숨긴 항목을 보이려고 잠깐 끔 — 사용자가 정한 값은 ui.json에 그대로)
        public static void SetFailOnly(bool on, bool persist = true)
        {
            if (Current.FailOnly == on)
            {
                // 잠깐 바꾼 값과 같은 값을 사용자가 정함 → 이제 그 값이 저장할 값(검증 F-J)
                if (persist && Current._persistedFailOnly != null) { Current._persistedFailOnly = null; Current.Save(); }
                return;
            }
            if (persist) Current._persistedFailOnly = null;
            else Current._persistedFailOnly ??= Current.FailOnly;
            Current.FailOnly = on;
            if (persist) Current.Save();
            FailOnlyChanged?.Invoke();
        }
    }
}
