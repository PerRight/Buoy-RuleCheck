// 이벤트 처리기(AutoCAD 이벤트·WinForms 타이머·클릭)에서 난 예외를 AutoCAD의 '처리되지 않은 예외' 창으로 올리지 않고
// 명령줄 한 줄 + 로그 파일(%APPDATA%\BuoyRuleCheck\error.log, 전체 스택)로 남긴다. 판정과는 무관한 화면 쪽 오류용.
// 2026-10-26 사용자 PC: 부표 카드가 뜬 뒤 'Operation is not valid due to the current state of the object' 창이 떠서 추가.
// AutoCAD에 의존하지 않는다(미리보기에서도 링크) — 명령줄 출력은 플러그인이 Sink로 꽂는다.
using System;
using System.Collections.Generic;
using System.IO;

namespace BuoyRuleCheck
{
    internal static class Guard
    {
        private static readonly HashSet<string> Seen = new();

        /// 명령줄에 한 줄 쓰기(플러그인이 로드 때 지정). 없으면 로그 파일만.
        public static Action<string>? Sink { get; set; }

        public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BuoyRuleCheck", "error.log");

        public static void Run(string where, Action action)
        {
            try { action(); }
            catch (Exception ex) { Report(where, ex); }
        }

        public static EventHandler Wrap(string where, EventHandler handler) => (s, e) => Run(where, () => handler(s, e));

        public static void Report(string where, Exception ex)
        {
            string line = $"{Product.Tag} 내부 오류({where}) — {Short(ex)}";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {where}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { /* 로그를 못 써도 계속 */ }
            if (!Seen.Add(line)) return;   // 같은 오류는 한 번만 명령줄에
            try { Sink?.Invoke(line + $" (자세한 내용: {LogPath})"); } catch { }
        }

        /// 예외 종류: 메시지 [첫 스택 줄]
        public static string Short(Exception ex)
        {
            string at = "";
            foreach (var l in (ex.StackTrace ?? "").Split('\n'))
                if (l.Trim().Length > 0) { at = l.Trim(); break; }
            return $"{ex.GetType().Name}: {ex.Message}" + (at.Length > 0 ? $" [{at}]" : "");
        }
    }
}
