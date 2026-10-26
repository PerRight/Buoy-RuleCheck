// 상태 표시줄(오른쪽 아래) 아이콘: 팔레트를 닫아 둬도 판정 상태가 보이고, 새 수정 소요·확인 필요가 생기면 풍선 알림.
// 아이콘은 상태 점(판정 색) 하나 — 확인 필요는 속 빈 점이 아니라 주황 점, 연결 끊김은 흐린 빈 고리.
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

namespace BuoyRuleCheck
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
                _item = new TrayItem { ToolTipText = $"{Product.Name} — 감시 전 (클릭: 팔레트)", Icon = IconFor("idle"), Visible = true };
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

        /// state: pass / fail / warn / idle / down
        public static void Set(string state, string tooltip)
        {
            if (_item == null) return;
            try
            {
                _item.Icon = IconFor(state);
                _item.ToolTipText = $"{Product.Name} — {tooltip}";
                AcApp.StatusBar.Update();
            }
            catch { }
        }

        public static void Update(CheckResponse r, string docName)
        {
            var (main, side) = UiText.Summary(r);
            Set(UiText.StateKey(r), $"{docName}: {main}" + (string.IsNullOrEmpty(side) ? "" : $" · {side}"));

            var news = r.Transitions.Where(t => t.Kind is "new_violation" or "needs_human").ToList();
            if (news.Count == 0) return;
            var lines = news.Select(t =>
            {
                var res = r.Results.FirstOrDefault(x => x.RuleId == t.RuleId);
                var title = res != null ? UiText.Headline(res).Title : "";
                return $"{UiText.Short(t.RuleId)}: {title}";
            });
            Notify(news.Any(t => t.Kind == "new_violation") ? "새 수정 소요" : "확인 필요 — 빈 입력은 판정하지 않음", string.Join("\n", lines),
                   news.Any(t => t.Kind == "new_violation"));
        }

        public static void Notify(string title, string text, bool bad)
        {
            if (_item == null) return;
            try
            {
                var bw = new TrayItemBubbleWindow
                {
                    Title = $"{Product.Name} · {title}",
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
                "warn" or "incomplete" or "na" => Theme.Warn,
                _ => Theme.Faint,
            };
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                if (state is "down" or "idle")
                    using (var p = new Pen(color, 1.5f)) g.DrawEllipse(p, 3, 3, 10, 10);   // 판정 없음: 빈 고리
                else
                    using (var b = new SolidBrush(color)) g.FillEllipse(b, 2.5f, 2.5f, 11, 11);
            }
            ic = Icon.FromHandle(bmp.GetHicon());
            Icons[state] = ic;
            return ic;
        }
    }
}
