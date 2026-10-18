// 화면 상태 기억: 로고 위치·패널 열림·'미충족만 보기'. %APPDATA%\GaRuleCheck\ui.json (도면 내용 없음).
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GaRuleCheck
{
    public sealed class UiState
    {
        public int? LogoX { get; set; }
        public int? LogoY { get; set; }
        public bool PanelOpen { get; set; }
        public bool LogoVisible { get; set; } = true;
        public bool FailOnly { get; set; }

        private static UiState? _current;
        public static UiState Current => _current ??= Load();

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GaRuleCheck", "ui.json");

        private static UiState Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<UiState>(File.ReadAllText(FilePath, Encoding.UTF8), Contract.Json) ?? new UiState();
            }
            catch { /* 깨진 파일은 무시하고 기본값 */ }
            return new UiState();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Contract.Json), new UTF8Encoding(false));
            }
            catch { /* 저장 실패는 기능에 영향 없음 */ }
        }

        /// '미충족만 보기'가 바뀌면 열린 패널(팔레트·떠 있는 패널)이 같이 바뀌게.
        public static event Action? FailOnlyChanged;

        public static void SetFailOnly(bool on)
        {
            if (Current.FailOnly == on) return;
            Current.FailOnly = on;
            Current.Save();
            FailOnlyChanged?.Invoke();
        }
    }
}
