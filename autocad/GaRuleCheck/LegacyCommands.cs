// 옛 명령 이름(GARC*) — 이름 변경(2026-10-26, 명세 2절) 뒤에도 한동안 별칭으로 남긴다. 새 이름은 BRC*.
// 별칭으로 실행하면 처음 한 번 명령줄에 새 이름을 알려 준다. 동작은 새 명령과 같다(같은 메서드를 부른다).
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;

namespace GaRuleCheck
{
    public sealed partial class Commands
    {
        private static readonly HashSet<string> LegacyHinted = new();

        private static void LegacyHint(string oldName)
        {
            if (!LegacyHinted.Add(oldName)) return;
            Active?.Editor.WriteMessage($"\n{Product.Tag} {oldName} → BRC{oldName[4..]}(으)로 이름이 바뀌었어요. 옛 이름도 한동안 됩니다.");
        }

        [CommandMethod("GARC")] public void LegacyStart() { LegacyHint("GARC"); Start(); }
        [CommandMethod("GARCCHECK")] public void LegacyCheckNow() { LegacyHint("GARCCHECK"); CheckNow(); }
        [CommandMethod("GARCOFF")] public void LegacyStop() { LegacyHint("GARCOFF"); Stop(); }
        [CommandMethod("GARCHEALTH")] public void LegacyHealth() { LegacyHint("GARCHEALTH"); Health(); }
        [CommandMethod("GARCSETUP")] public void LegacySetup() { LegacyHint("GARCSETUP"); Setup(); }
        [CommandMethod("GARCSHIP")] public void LegacyShip() { LegacyHint("GARCSHIP"); Ship(); }
        [CommandMethod("GARCEYE")] public void LegacyEye() { LegacyHint("GARCEYE"); Eye(); }
        [CommandMethod("GARCPICK")] public void LegacyPick() { LegacyHint("GARCPICK"); Pick(); }
        [CommandMethod("GARCTOGGLE")] public void LegacyToggle() { LegacyHint("GARCTOGGLE"); Toggle(); }
        [CommandMethod("GARCLARGE")] public void LegacyLargeLabels() { LegacyHint("GARCLARGE"); LargeLabels(); }
        [CommandMethod("GARCREPORT")] public void LegacyReport() { LegacyHint("GARCREPORT"); Report(); }
        [CommandMethod("GARCVERSION")] public void LegacyVersion() { LegacyHint("GARCVERSION"); Version(); }
        [CommandMethod("GARCCOMPARE")] public void LegacyCompare() { LegacyHint("GARCCOMPARE"); Compare(); }
        [CommandMethod("GARCPANEL")] public void LegacyTogglePanel() { LegacyHint("GARCPANEL"); TogglePanel(); }
        [CommandMethod("GARCLOGO")] public void LegacyToggleLogo() { LegacyHint("GARCLOGO"); ToggleLogo(); }
        [CommandMethod("GARCLINK")] public void LegacyLink() { LegacyHint("GARCLINK"); Link(); }
        [CommandMethod("GARCROLE", CommandFlags.UsePickSet)] public void LegacyRole() { LegacyHint("GARCROLE"); Role(); }
        [CommandMethod("GARCROLESHOW")] public void LegacyRoleShow() { LegacyHint("GARCROLESHOW"); RoleShow(); }
    }
}
