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
        // 판정 색 규칙(10-18): 충족 = 파랑, 미충족 = 빨강, 확인 필요 = 주황.
        // 버전 비교의 추가(초록)·삭제(빨강 점선)·변경(노랑)과 헷갈리지 않게 '충족'은 초록을 쓰지 않는다.
        public static readonly Color Pass = Color.FromArgb(59, 130, 246);
        public static readonly Color Fail = Color.FromArgb(229, 72, 77);
        public static readonly Color Warn = Color.FromArgb(240, 140, 40);
        /// 버튼·선택 강조색(판정 색과 겹치지 않게 보라 계열)
        public static readonly Color Info = Color.FromArgb(124, 106, 230);

        // 버전 비교 색 (흑백 인쇄용 패턴·아이콘과 함께 쓴다)
        public static readonly Color DiffAdded = Color.FromArgb(46, 170, 90);
        public static readonly Color DiffRemoved = Color.FromArgb(220, 60, 60);
        public static readonly Color DiffModified = Color.FromArgb(230, 190, 40);
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
            "na" or "review" or "incomplete" or "unknown" => Warn,
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

        /// 요소 판정 표시 문구(툴팁·체크리스트). 아이콘은 글꼴에 없을 수 있어 기호 + 글자를 같이 쓴다.
        public static string ElementMark(string status) => status switch
        {
            "pass" => "✔ 충족",
            "fail" => "✖ 미충족",
            _ => "⚠ 확인 필요",
        };

        public static int StatusRank(string? s) => s switch { "fail" => 0, "unknown" or "review" or "na" => 1, "pass" => 2, _ => 3 };

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
