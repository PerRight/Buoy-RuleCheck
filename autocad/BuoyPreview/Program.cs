// Buoy-RuleCheck 단독 미리보기 — 실행: dotnet run (Windows)
using System;
using System.Windows.Forms;

namespace GaRuleCheck
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PreviewForm());
        }
    }
}
