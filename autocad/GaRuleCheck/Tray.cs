// 상태 표시줄(오른쪽 아래) 아이콘: 팔레트를 닫아 둬도 판정 상태가 보이고, 새 위반이 생기면 풍선 알림.
// 아이콘을 누르면 팔레트가 열린다. 상태 표시줄 API가 실패해도 플러그인 본기능에는 영향 없게 전부 try로 감싼다.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = System.Drawing.Color;
using Icon = System.Drawing.Icon;

namespace GaRuleCheck
{
    public static class StatusTray
    {
        private static TrayItem? _item;
        private static readonly Dictionary<string, Icon> Icons = new();
        public static event Action? Clicked;

        public static void Install()
        {
            if (_item != null) return;
            try
            {
                _item = new TrayItem { ToolTipText = "GA RuleCheck — 대기 중 (클릭: 팔레트)", Icon = IconFor("idle"), Visible = true };
                _item.MouseDown += (_, _) => Clicked?.Invoke();
                AcApp.StatusBar.TrayItems.Add(_item);
                AcApp.StatusBar.Update();
            }
            catch { _item = null; }
        }

        public static void Remove()
        {
            try
            {
                if (_item != null) AcApp.StatusBar.TrayItems.Remove(_item);
                AcApp.StatusBar.Update();
            }
            catch { }
            _item = null;
        }

        /// state: pass / fail / incomplete / na / idle / down
        public static void Set(string state, string tooltip)
        {
            if (_item == null) return;
            try
            {
                _item.Icon = IconFor(state);
                _item.ToolTipText = "GA RuleCheck — " + tooltip;
                AcApp.StatusBar.Update();
            }
            catch { }
        }

        public static void Update(CheckResponse r, string docName)
        {
            int fail = r.Summary.TryGetValue("fail", out var f) ? f : 0;
            int warn = (r.Summary.TryGetValue("na", out var n) ? n : 0) + (r.Summary.TryGetValue("review", out var v) ? v : 0);
            var tip = r.Overall switch
            {
                "pass" => $"{docName}: 규칙 후보 기준 충족",
                "fail" => $"{docName}: 위반 {fail}건" + (warn > 0 ? $", 확인 필요 {warn}건" : ""),
                "incomplete" => $"{docName}: 확인 필요 {warn}건",
                _ => $"{docName}: 판정된 규칙 없음",
            };
            Set(r.Overall, tip);

            var news = r.Transitions.Where(t => t.Kind is "new_violation" or "needs_human").ToList();
            if (news.Count == 0) return;
            var lines = news.Select(t =>
            {
                var res = r.Results.FirstOrDefault(x => x.RuleId == t.RuleId);
                return $"{res?.TitleKo ?? t.RuleId}: {res?.MessageKo}";
            });
            Notify(news.Any(t => t.Kind == "new_violation") ? "새 위반 후보" : "사람 확인 필요", string.Join("\n", lines),
                   news.Any(t => t.Kind == "new_violation"));
        }

        public static void Notify(string title, string text, bool bad)
        {
            if (_item == null) return;
            try
            {
                var bw = new TrayItemBubbleWindow
                {
                    Title = "GA RuleCheck · " + title,
                    Text = text.Length > 240 ? text[..240] + "…" : text,
                    IconType = bad ? IconType.Warning : IconType.Information,
                };
                _item.ShowBubbleWindow(bw);
            }
            catch { }
        }

        private static Icon IconFor(string state)
        {
            if (Icons.TryGetValue(state, out var ic)) return ic;
            var color = state switch
            {
                "pass" => Theme.Pass,
                "fail" => Theme.Fail,
                "incomplete" or "na" or "down" => Theme.Warn,
                _ => Theme.Skip,
            };
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var b = new SolidBrush(color);
                g.FillEllipse(b, 1, 1, 13, 13);
                using var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.5f);
                if (state == "pass") g.DrawLines(pen, new[] { new PointF(4.5f, 8), new PointF(7, 10.5f), new PointF(11.5f, 5.5f) });
                else if (state == "fail") { g.DrawLine(pen, 5, 5, 11, 11); g.DrawLine(pen, 11, 5, 5, 11); }
                else if (state is "incomplete" or "na" or "down") { g.DrawLine(pen, 8, 4, 8, 9); g.DrawLine(pen, 8, 11, 8, 12); }
            }
            ic = Icon.FromHandle(bmp.GetHicon());
            Icons[state] = ic;
            return ic;
        }
    }
}
