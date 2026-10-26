// Theme v2 (Buoy-RuleCheck 디자인 명세 4절). 필드 이름은 명세 그대로.
// 원칙: "잉크는 진하기로, 색은 판정에만" — UI 크롬은 흑백 잉크 단계(Text → Muted → Faint → Line)로만 구분하고,
// 빨강·주황·파랑(Fail·Warn·Pass)은 판정 상태에만 쓴다. 옛 보라 강조(Info)는 흰 잉크로 바뀌었다.
// 대비(WCAG): Faint까지는 Bg·Card·CardHover·Input 모두에서 4.5:1 이상(가장 낮은 것 Faint/CardHover 4.53:1, 2026-10-26 계산).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    public static class Theme
    {
        private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        // ── 잉크 · 다크 (플러그인 화면) ─────────────────────────────────────────
        /// 켜진 것, 본문
        public static readonly Color Text = Hex(0xEDEFF3);
        /// 보조 글자, 윤곽
        public static readonly Color Muted = Hex(0xA3AAB5);
        /// 라벨, 단위
        public static readonly Color Faint = Hex(0x868E99);
        /// 구분선, 가이드 — 글자에 쓰지 않음
        public static readonly Color Line = Hex(0x2C3037);
        /// 줄 호버
        public static readonly Color CardHover = Hex(0x23272D);
        /// 카드(+ 1px 링 Ring)
        public static readonly Color Card = Hex(0x1D2025);
        /// 패널 바탕
        public static readonly Color Bg = Hex(0x17191D);
        /// 입력 칸
        public static readonly Color Input = Hex(0x121417);

        /// 카드 1px 링(흰 7%) — 바탕에 섞은 불투명 값도 같이 둔다(WinForms 테두리는 알파를 못 섞는 곳이 있다)
        public static readonly Color Ring = Color.FromArgb(18, 255, 255, 255);
        public static readonly Color RingOnCard = Hex(0x2B2E33);
        /// 보조 버튼 1px 링(흰 14%)
        public static readonly Color ButtonRing = Color.FromArgb(36, 255, 255, 255);
        public static readonly Color ButtonRingOnBg = Hex(0x3A3C40);
        /// 게이지 채움(판정이 아닌 진행 막대)
        public static readonly Color GaugeFill = Hex(0x4A5059);

        // ── 판정 색 (= 오브 색) ──────────────────────────────────────────────────
        /// Fail · 수정 소요 — 부표, 카드 머리, 근거 선
        public static readonly Color Fail = Hex(0xFF5F57);
        /// Warn · 확인 필요 — 입력 누락 · 검토 불가 · 엔진 연결 안 됨
        public static readonly Color Warn = Hex(0xFFB340);
        /// Pass · 문제없음
        public static readonly Color Pass = Hex(0x5A9CFF);
        /// Skip · 적용 제외 — 흑백 잉크 그대로
        public static readonly Color Skip = Hex(0x868E99);
        /// 다크 바탕 위 '수정 소요' 수치 글자(빨강보다 한 단계 밝게)
        public static readonly Color FailInk = Hex(0xFF8A84);

        /// 옛 보라 강조 자리 — 흰 잉크
        public static readonly Color Info = Text;

        // ── 버전 비교 전용 ───────────────────────────────────────────────────────
        public static readonly Color DiffAdded = Hex(0x3DD68C);
        /// 삭제는 색 대신 파선(색은 Fail)
        public static readonly Color DiffRemoved = Fail;
        public static readonly Color DiffModified = Hex(0xF5C84B);

        // ── 잉크 · 라이트 (A4 전달 시트 · 리포트 · 발표). 선 굵기 0.9 px ───────────
        public static class Paper
        {
            public static readonly Color Hi = Hex(0x232327);
            public static readonly Color Edge = Hex(0xA4A4AC);
            public static readonly Color Mid = Hex(0xC3C3C9);
            public static readonly Color Lo = Hex(0xE0E0E4);
            public static readonly Color Plate = Hex(0xFFFFFF);
            /// 흰 종이 위 보조 글자(대비 4.98:1)
            public static readonly Color Muted = Hex(0x6F6F78);
            /// 흰 종이에서는 판정 색을 한 단계 어둡게. Warn은 흰 종이 대비 4.12:1 → 글자보다 기호·선에 쓴다
            public static readonly Color Fail = Hex(0xD93B34);
            public static readonly Color Warn = Hex(0xB86A00);
            public static readonly Color Pass = Hex(0x2F6FE0);
            public const float LineWidth = 0.9f;
        }

        // ── 역할 보기(BRCROLESHOW) 색 — 판정 색(빨강·주황·파랑)과 겹치지 않게 ────────
        public static readonly Color RoleHull = Hex(0xEDEFF3);       // 선체: 흰 잉크
        public static readonly Color RoleObstruction = Hex(0xB392F0); // 장애물: 연보라
        public static readonly Color RoleBulkhead = Hex(0x4FD1C5);   // 충돌격벽: 청록
        public static readonly Color RoleBulb = Hex(0xA3AAB5);       // 구상선수: 회색(디자인 보드에 없음 — 잉크)
        public static readonly Color RoleEye = Hex(0xC3E88D);        // 조종위치: 연두

        // ── 글자 ────────────────────────────────────────────────────────────────
        public const string FontName = "Malgun Gothic";
        public const string MonoName = "Consolas";
        /// 본문·버튼 9pt
        public static readonly Font Body = new(FontName, 9f);
        /// 라벨·보조 8pt
        public static readonly Font Small = new(FontName, 8f);
        public static readonly Font Bold = new(FontName, 9f, FontStyle.Bold);
        /// 카드 제목 B 11pt (예: "상단을 1.55 m 낮추세요")
        public static readonly Font Title = new(FontName, 11f, FontStyle.Bold);
        /// 카드 머리 숫자 B 13pt (예: "수정 소요 2")
        public static readonly Font Big = new(FontName, 13f, FontStyle.Bold);
        /// 수치 Consolas 9pt (예: "z 24.00 m · +1.55 m")
        public static readonly Font Mono = new(MonoName, 9f);
        public static readonly Font MonoSmall = new(MonoName, 8f);

        private static PrivateFontCollection? _fonts;
        private static FontFamily? _wordmark;
        /// 워드마크 글꼴: Geist 500(DLL 옆 fonts\Geist-Medium.ttf) → 설치된 Geist → Segoe UI.
        public static FontFamily WordmarkFamily
        {
            get
            {
                if (_wordmark != null) return _wordmark;
                try
                {
                    var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                    foreach (var f in new[] { Path.Combine(dir, "fonts", "Geist-Medium.ttf"), Path.Combine(dir, "Geist-Medium.ttf") })
                    {
                        if (!File.Exists(f)) continue;
                        _fonts ??= new PrivateFontCollection();
                        _fonts.AddFontFile(f);
                        if (_fonts.Families.Length > 0) return _wordmark = _fonts.Families[0];
                    }
                }
                catch { /* 글꼴 파일이 깨졌으면 대체 글꼴 */ }
                foreach (var name in new[] { "Geist", "Geist Medium", "Segoe UI Semibold", "Segoe UI" })
                {
                    try
                    {
                        var fam = new FontFamily(name);
                        if (fam.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return _wordmark = fam;
                    }
                    catch (ArgumentException) { }
                }
                return _wordmark = FontFamily.GenericSansSerif;
            }
        }

        /// 워드마크 "Buoy RuleCheck": 'Buoy'는 굵게(밝은 잉크), 'RuleCheck'는 보통. 반환: 그린 너비.
        public static float DrawWordmark(Graphics g, PointF at, float sizePt, Color ink, Color? first = null)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var fam = WordmarkFamily;
            // 글꼴에 그 굵기가 없으면 GDI+가 ArgumentException — 있는 굵기로 바꾼다(Bold 없으면 Regular, Regular 없으면 Bold)
            var boldStyle = fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
            var regStyle = fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : boldStyle;
            using var bold = new Font(fam, sizePt, boldStyle);
            using var reg = new Font(fam, sizePt, regStyle);
            using var b1 = new SolidBrush(first ?? ink);
            using var b2 = new SolidBrush(ink);
            var fmt = StringFormat.GenericTypographic;
            g.DrawString(Product.First, bold, b1, at, fmt);
            float w1 = g.MeasureString(Product.First, bold, PointF.Empty, fmt).Width;
            float gap = sizePt * 0.32f;
            g.DrawString(Product.Second, reg, b2, new PointF(at.X + w1 + gap, at.Y), fmt);
            return w1 + gap + g.MeasureString(Product.Second, reg, PointF.Empty, fmt).Width;
        }

        /// 투명 배경을 건너뛰고 실제로 칠해진 조상 배경색. (Clear(Transparent)는 검게 칠해진다)
        public static Color EffectiveBack(Control? c)
        {
            for (var p = c; p != null; p = p.Parent)
                if (p.BackColor.A == 255) return p.BackColor;
            return Card;
        }

        /// 판정 상태 → 판정 색. 판정이 아닌 것(idle 등)은 흑백 잉크.
        public static Color Status(string? s) => s switch
        {
            "pass" => Pass,
            "fail" => Fail,
            "na" or "review" or "incomplete" or "unknown" or "down" or "warn" => Warn,
            "skip" => Skip,
            null or "" or "idle" or "none" or "check" or "muted" => Faint,   // 판정이 아닌 상태(검사 전·검사 중·바뀌지 않음)
            _ => Warn,                                                       // 모르는 상태 문자열은 확인 필요(파랑으로 보이지 않게)
        };

        /// 사용자에게 보이는 상태 이름(명세 3절: 통과는 '문제없음', '적합·승인'은 쓰지 않는다).
        public static string StatusName(string? s) => s switch
        {
            "pass" => "문제없음",
            "fail" => "수정 소요",
            "na" or "review" or "incomplete" or "unknown" or "warn" => "확인 필요",
            "down" => "연결 끊김",
            "skip" => "적용 제외",
            null or "" or "idle" or "none" => "검사 전",
            "check" => "검사 중",
            _ => "확인 필요",   // 모르는 상태 문자열
        };

        /// 기본 버튼(흰 잉크 채움)이면 accent에 Theme.Info, 나머지는 1px 링.
        public static Button FlatButton(string text, Color? accent = null) =>
            new PillButton(text, accent == Info ? PillKind.Primary : PillKind.Ring);

        public static Button PrimaryButton(string text) => new PillButton(text, PillKind.Primary);
        public static Button RingButton(string text) => new PillButton(text, PillKind.Ring);
        public static Button TextButton(string text) => new PillButton(text, PillKind.Text);

        /// 요소 판정 표시 문구(툴팁·체크리스트). 아이콘은 글꼴에 없을 수 있어 기호 + 글자를 같이 쓴다.
        public static string ElementMark(string status) => status switch
        {
            "pass" => "● 문제없음",
            "fail" => "● 수정 소요",
            _ => "● 확인 필요",
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

        internal static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(Math.Min(radius * 2, Math.Min(r.Width, r.Height)), 1);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// 다크 바탕에 알파 색을 섞은 불투명 색(WinForms 컨트롤은 알파 배경을 못 그리는 곳이 많다).
        public static Color Over(Color top, Color under)
        {
            double a = top.A / 255.0;
            return Color.FromArgb(255,
                (int)Math.Round(top.R * a + under.R * (1 - a)),
                (int)Math.Round(top.G * a + under.G * (1 - a)),
                (int)Math.Round(top.B * a + under.B * (1 - a)));
        }
    }

    /// 사용자에게 보이는 제품 이름(워드마크는 두 덩어리).
    public static class Product
    {
        public const string Name = "Buoy-RuleCheck";
        public const string First = "Buoy";
        public const string Second = "RuleCheck";
        /// 명령줄 접두어
        public const string Tag = "[BRC]";
    }

    public enum PillKind { Primary, Ring, Text }

    /// 둥근 알약 버튼: Primary = 흰 잉크 채움 + 어두운 글자, Ring = 1px 링, Text = 글자만.
    public sealed class PillButton : Button
    {
        public PillKind Kind { get; }
        private bool _hover, _down, _selected;
        /// 켜짐(토글·보기 모드 선택): 링 버튼을 호버 바탕 + 흰 잉크로
        public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

        public PillButton(string text, PillKind kind)
        {
            Kind = kind;
            Text = text;
            AutoSize = false;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = kind == PillKind.Primary ? Theme.Bold : Theme.Body;
            Margin = new Padding(0, 0, 6, 0);
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            ForeColor = kind == PillKind.Text ? Theme.Muted : Theme.Text;
            Size = PreferredPill();
        }

        private Size PreferredPill()
        {
            var sz = TextRenderer.MeasureText(Text, Font);
            int padX = Kind == PillKind.Text ? 10 : 14;
            return new Size(sz.Width + padX * 2 - 6, Math.Max(sz.Height + 10, 26));
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (Dock == DockStyle.None) Size = PreferredPill();
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize) => PreferredPill();

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var back = Theme.EffectiveBack(Parent);
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using var path = Theme.RoundRect(r, r.Height / 2);
            Color fg;
            switch (Kind)
            {
                case PillKind.Primary:
                    var fill = !Enabled ? Theme.Faint : _down ? Theme.Muted : _hover ? Color.White : Theme.Text;
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    fg = Theme.Bg;
                    break;
                case PillKind.Ring:
                    if (_hover || _down || _selected)
                        using (var hb = new SolidBrush(_down ? Theme.Line : Theme.CardHover)) g.FillPath(hb, path);
                    using (var p = new Pen(_selected ? Theme.Muted : Theme.Over(Theme.ButtonRing, back), 1f)) g.DrawPath(p, path);
                    fg = Enabled ? (_selected ? Theme.Text : ForeColor) : Theme.Faint;
                    break;
                default:
                    if (_hover) using (var hb = new SolidBrush(Theme.CardHover)) g.FillPath(hb, path);
                    fg = Enabled ? (_hover ? Theme.Text : ForeColor) : Theme.Faint;
                    break;
            }
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                using (var fp = new Pen(Theme.Muted, 1f) { DashStyle = DashStyle.Dot })
                using (var inner = Theme.RoundRect(RectangleF.Inflate(r, -2.5f, -2.5f), (r.Height - 5) / 2))
                    g.DrawPath(fp, inner);
        }
    }

    /// 상태 표시: 칠한 배지 대신 점 + 글자 (● 수정 소요). 적용 제외는 속 빈 점.
    public sealed class StatusTag : Control
    {
        private string _status;
        public StatusTag(string status, string? text = null)
        {
            _status = status;
            Text = text ?? Theme.StatusName(status);
            Font = Theme.Bold;
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = PreferredSize;
            Margin = new Padding(0);
        }

        public void Set(string status, string? text = null)
        {
            _status = status;
            Text = text ?? Theme.StatusName(status);
            Size = PreferredSize;
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var sz = TextRenderer.MeasureText(Text, Font);
            return new Size(sz.Width + 14, Math.Max(sz.Height, 16));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var col = _status == "skip" ? Theme.Muted : Theme.Status(_status);
            float cy = Height / 2f;
            var dot = new RectangleF(1, cy - 3.5f, 7, 7);
            if (_status == "skip") using (var p = new Pen(col, 1f)) g.DrawEllipse(p, dot);
            else using (var b = new SolidBrush(col)) g.FillEllipse(b, dot);
            TextRenderer.DrawText(g, Text, _status == "skip" ? Theme.Body : Font, new Point(12, (int)(cy - TextRenderer.MeasureText(Text, Font).Height / 2f)), col);
        }
    }
}
