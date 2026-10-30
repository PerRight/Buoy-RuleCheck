// 생산팀 전달 A4 한 장(명세 6-7, 디자인 보드 08): A4 가로, 라이트 잉크 토큰(hi·edge·mid·lo·plate), 200 dpi 그림.
//   머리: 'GA 변경 전달 · 생산팀' · 도면 번호 v1 → v2 · (오른쪽) 날짜 · 작성자 · 수정 사유
//   왼쪽: 차이 그림(바뀌지 않은 형상 mid, 추가 ○ 빗금 · 삭제 □ 파선 · 변경 ◇ 이전 점선/이후 굵은 실선) + 범례
//   오른쪽: 변경 목록 ①②③④(기호 + 제목 · 수치 변화 · 해결/기준 충족) + 확인란(생산 ____ 일자 ____)
//   (선택) 왼쪽 아래: 변경 설명 — 작성자가 고른·고친 두세 문장(기본 설명 또는 AI 초안 바탕, 숫자는 엔진 비교 결과)
//   아래: 고지 문구 · 워드마크 'Buoy RuleCheck · 1 / 1'
// 흑백 인쇄에서도 구분되게 색과 함께 선 모양·빗금·표식 모양(○ □ ◇)과 기호(+ − ~)를 쓴다(CanvasPainter 공용). 판정은 엔진 결과 그대로.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace BuoyRuleCheck
{
    public static class ExportSheet
    {
        public const int Width = 2339, Height = 1654;   // A4 가로 @200 dpi
        /// 디자인 보드 08은 1123 × 794(96 dpi 기준 px) — 그 px에 곱해 200 dpi 그림 px로
        private const float S = Width / 1123f;
        private const float M = 44;                     // 좌우 여백(디자인 px)

        private static float P(float designPx) => designPx * S;

        public static Bitmap Render(CompareResponse d, VersionEntry? va, VersionEntry? vb, string labelA, string labelB,
                                    string dwgNo, string drawingName, string? changeNote = null, bool noteFromAi = false)
        {
            bool hasNote = !string.IsNullOrWhiteSpace(changeNote);
            float frameBottom = hasNote ? 600 : 695;   // 변경 설명이 있으면 그림을 줄여 아래에 자리를 낸다
            var bmp = new Bitmap(Width, Height);
            bmp.SetResolution(200, 200);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Theme.Paper.Plate);
            var st = PaintStyle.Paper(S);
            using var hi = new SolidBrush(Theme.Paper.Hi);
            using var muted = new SolidBrush(Theme.Paper.Muted);
            using var lo = new Pen(Theme.Paper.Lo, Math.Max(1f, 0.9f * S));
            using var rule = new Pen(Theme.Paper.Hi, 1f * S);
            float right = 1123 - M;

            // ── 머리 ──
            using (var kicker = F(11, mono: true)) Text(g, "GA 변경 전달 · 생산팀", kicker, muted, M, 40);
            using (var title = F(26, FontStyle.Bold, mono: true))
            using (var ver = F(18, mono: true))
            {
                Text(g, dwgNo, title, hi, M, 60);
                float x = M + Measure(g, dwgNo, title) + 14;
                Text(g, labelA, ver, muted, x, 68);
                x += Measure(g, labelA, ver) + 6;
                Text(g, "→", ver, muted, x, 68);
                x += Measure(g, "→", ver) + 6;
                Text(g, labelB, ver, hi, x, 68);
            }
            string reason = vb == null ? "저장 전 변경" : VersionStore.ReasonText(vb.Reason) + (string.IsNullOrWhiteSpace(vb.Reference) ? "" : " — " + UiText.OneLine(vb.Reference));
            var meta = new (string k, string v)[]
            {
                ("날짜", vb != null && DateTime.TryParse(vb.Created, out var t) ? t.ToString("yyyy-MM-dd") : DateTime.Now.ToString("yyyy-MM-dd")),
                ("작성자", vb?.Author ?? "—"),
                ("수정 사유", reason),
            };
            using (var mf = F(12))
            {
                float my = 38;
                foreach (var (k, v) in meta)
                {
                    Text(g, k, mf, muted, 815, my);
                    using var fmt = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
                    g.DrawString(v, mf, hi, new RectangleF(P(880), P(my), P(right - 880), P(18)), fmt);
                    my += 21;
                }
            }
            g.DrawLine(rule, P(M), P(113), P(right), P(113));

            // ── 차이 그림 ──
            var frame = new RectangleF(P(M), P(132), P(705 - M), P(frameBottom - 132));
            var img = RectangleF.Inflate(frame, -P(1), -P(1));
            var view = new ViewState();
            if (d.Bounds is double[] b) view.Fit(b, Size.Round(img.Size), 0.08);
            var clip = g.Clip;
            using (var fpath = Theme.RoundRect(img, P(6))) g.SetClip(fpath);
            CanvasPainter.Paint(g, img, d, CanvasMode.Diff, view, st, 1f, null);
            g.Clip = clip;
            using (var fpath = Theme.RoundRect(frame, P(6))) g.DrawPath(lo, fpath);
            CanvasPainter.PaintLegend(g, new RectangleF(P(M), P(frameBottom + 8), P(705 - M), P(20)), st);
            if (hasNote)
            {
                using var noteLab = F(11);
                Text(g, "변경 설명" + (noteFromAi ? " · AI 초안을 작성자가 확인" : ""), noteLab, muted, M, frameBottom + 36);
                var noteBox = new RectangleF(P(M), P(frameBottom + 52), P(705 - M), P(733 - (frameBottom + 52)));
                string text = changeNote!.Trim();
                // 상자에 들어갈 때까지 글자를 줄인다(12 → 9 px). 그래도 넘치면 말줄임
                float size = 12;
                while (size > 9 && g.MeasureString(text, FontPx(size), (int)noteBox.Width).Height > noteBox.Height) size -= 0.5f;
                using var noteFont = F(size);
                using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisWord };
                g.DrawString(text, noteFont, hi, noteBox, fmt);
            }

            // ── 변경 목록 ──
            float lx = 729, lw = right - lx;
            using (var h = F(15, FontStyle.Bold)) Text(g, "변경 목록", h, hi, lx, 131);
            var (fb, fa, n, _) = UiText.CompareCounts(d);
            using (var sm = F(11))
            {
                string sum = d.Same ? "바뀐 것 없음" : $"수정 소요 {fb} → {fa} · {n}건";
                Text(g, sum, sm, muted, right - Measure(g, sum, sm), 134);
            }
            g.DrawLine(lo, P(lx), P(160), P(right), P(160));
            float ly = 160, bottom = 655;
            using var num = F(18);
            using var tf = F(12, FontStyle.Bold);
            using var vf = F(12, mono: true);
            using var nf = F(11);
            int shown = 0;
            foreach (var c in d.Changes)
            {
                string title = $"{Glyph(c.Kind)} {UiText.ChangeTitle(c)}";
                var (from, to) = UiText.ChangeValue(c);
                string value = (from == null ? "" : from + " → ") + to;
                string note = UiText.ChangeNoteShort(c);
                float th = HeightOf(g, title, tf, lw - 36), vh = HeightOf(g, value, vf, lw - 36), nh = note.Length > 0 ? HeightOf(g, note, nf, lw - 36) : 0;
                float itemH = 12 + th + 5 + vh + (nh > 0 ? 5 + nh : 0) + 12;
                if (ly + itemH > bottom)
                {
                    Text(g, $"… 외 {d.Changes.Count - shown}건 — 비교 창에서 확인", nf, muted, lx, ly + 10);
                    break;
                }
                Text(g, Circled(c.Number), num, hi, lx, ly + 8);
                float cx = lx + 36, y = ly + 12;
                Wrap(g, title, tf, hi, cx, y, lw - 36); y += th + 5;
                Wrap(g, value, vf, hi, cx, y, lw - 36); y += vh + 5;
                if (nh > 0) Wrap(g, note, nf, muted, cx, y, lw - 36);
                ly += itemH;
                g.DrawLine(lo, P(lx), P(ly), P(right), P(ly));
                shown++;
            }
            if (d.Indirect.Count > 0 && ly + 24 < bottom)
                Text(g, $"다른 변경의 영향 {d.Indirect.Count}건 — 비교 창에서 확인", nf, muted, lx, ly + 10);

            // 확인란
            var box = new RectangleF(P(lx), P(665), P(lw), P(55));
            using (var bpath = Theme.RoundRect(box, P(6))) g.DrawPath(lo, bpath);
            using (var lab = F(11))
            using (var val = F(12))
            using (var ul = new Pen(Theme.Paper.Hi, 0.9f * S))
            {
                Text(g, "확인", lab, muted, lx + 12, 674);
                Text(g, "생산", val, hi, lx + 46, 673);
                float u0 = lx + 46 + Measure(g, "생산", val) + 4;
                g.DrawLine(ul, P(u0), P(688), P(u0 + 90), P(688));
                Text(g, "일자", lab, muted, lx + 12, 696);
                g.DrawLine(ul, P(lx + 46), P(710), P(lx + 136), P(710));
            }

            // ── 아래: 고지 · 워드마크 · 쪽 ──
            g.DrawLine(lo, P(M), P(739), P(right), P(739));
            using (var foot = F(11))
            {
                Text(g, UiText.Disclaimer, foot, muted, M, 748);
                const string page = " · 1 / 1";
                float pw = Measure(g, page, foot);
                Text(g, page, foot, muted, right - pw, 748);
                // 워드마크 'Buoy RuleCheck' — Buoy 굵게 + RuleCheck 보통(Theme.DrawWordmark, 글꼴 없으면 대체)
                float ww;
                using (var probe = new Bitmap(1, 1))
                {
                    probe.SetResolution(200, 200);   // 같은 해상도에서 재야 pt → px가 맞는다
                    using var pg = Graphics.FromImage(probe);
                    ww = Theme.DrawWordmark(pg, PointF.Empty, 9f, Color.Black);   // 폭만 잰다(12 px = 9 pt)
                }
                float wx = P(right) - pw * S - ww;
                Theme.DrawWordmark(g, new PointF(wx, P(747)), 9f, Theme.Paper.Hi, Theme.Paper.Hi);
                var orb = new OrbGeometry(new OrbSpec { Size = P(14) });
                OrbRenderer.Draw(g, orb, 0.0, new PointF(wx - P(20), P(747)), new OrbLook { Dots = Color.FromArgb(0x8E, 0x8E, 0x96) });
            }
            return bmp;
        }

        private static string Glyph(string kind) => kind switch { "added" => "+", "removed" => "−", "modified" => "~", _ => "·" };

        /// ①~⑳, 그 뒤는 (21)
        private static string Circled(int n) => n is >= 1 and <= 20 ? ((char)(0x2460 + n - 1)).ToString() : $"({n})";

        private static Font F(float designPx, FontStyle style = FontStyle.Regular, bool mono = false) =>
            new(mono ? Theme.MonoName : Theme.FontName, designPx * S, style, GraphicsUnit.Pixel);

        private static void Text(Graphics g, string s, Font f, Brush b, float x, float y) => g.DrawString(s, f, b, P(x), P(y));

        private static Font? _probe;
        private static float _probeSize;
        /// 크기 재기용 글꼴(하나만 두고 바꿔 쓴다)
        private static Font FontPx(float designPx)
        {
            if (_probe == null || _probeSize != designPx) { _probe?.Dispose(); _probe = F(designPx); _probeSize = designPx; }
            return _probe;
        }

        private static float Measure(Graphics g, string s, Font f) => g.MeasureString(s, f).Width / S;

        private static float HeightOf(Graphics g, string s, Font f, float w) => g.MeasureString(s, f, (int)P(w)).Height / S;

        private static void Wrap(Graphics g, string s, Font f, Brush b, float x, float y, float w) =>
            g.DrawString(s, f, b, new RectangleF(P(x), P(y), P(w), P(400)));
    }
}
