// 팔레트 색·글꼴. AutoCAD 기본 다크 테마에 맞춘 값. 서용언 Figma 시안이 오면 여기만 바꾼다.
using System.Drawing;
using System.Windows.Forms;

namespace GaRuleCheck
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(33, 37, 43);
        public static readonly Color Card = Color.FromArgb(44, 49, 58);
        public static readonly Color CardHover = Color.FromArgb(52, 58, 68);
        public static readonly Color Line = Color.FromArgb(62, 69, 80);
        public static readonly Color Text = Color.FromArgb(230, 233, 238);
        public static readonly Color Muted = Color.FromArgb(150, 160, 172);
        public static readonly Color Pass = Color.FromArgb(63, 178, 127);
        public static readonly Color Fail = Color.FromArgb(229, 83, 75);
        public static readonly Color Warn = Color.FromArgb(224, 163, 58);
        public static readonly Color Info = Color.FromArgb(91, 155, 234);
        public static readonly Color Skip = Color.FromArgb(125, 133, 144);
        public static readonly Color Input = Color.FromArgb(28, 31, 36);

        public const string FontName = "Malgun Gothic";
        public static readonly Font Body = new(FontName, 9f);
        public static readonly Font Small = new(FontName, 8f);
        public static readonly Font Bold = new(FontName, 9f, FontStyle.Bold);
        public static readonly Font Title = new(FontName, 11f, FontStyle.Bold);
        public static readonly Font Big = new(FontName, 13f, FontStyle.Bold);

        /// 투명 배경을 건너뛰고 실제로 칠해진 조상 배경색. (Clear(Transparent)는 검게 칠해진다)
        public static Color EffectiveBack(Control? c)
        {
            for (var p = c; p != null; p = p.Parent)
                if (p.BackColor.A == 255) return p.BackColor;
            return Card;
        }

        public static Color Status(string? s) => s switch
        {
            "pass" => Pass,
            "fail" => Fail,
            "na" or "review" or "incomplete" => Warn,
            "skip" => Skip,
            _ => Info,
        };

        public static Button FlatButton(string text, Color? accent = null)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = accent ?? Card,
                ForeColor = Text,
                Font = Body,
                Cursor = Cursors.Hand,
                Padding = new Padding(6, 2, 6, 2),
                Margin = new Padding(0, 0, 6, 0),
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.MouseOverBackColor = CardHover;
            return b;
        }

        public static Label Label(string text, Font? font = null, Color? color = null) => new()
        {
            Text = text,
            AutoSize = true,
            Font = font ?? Body,
            ForeColor = color ?? Text,
            BackColor = Color.Transparent,
        };
    }
}
