// 생산팀 전달용 한 장(A4 가로, 200 dpi): 도면 번호·버전 비교·날짜·작성자·사유, 차이 강조 그림, 번호 매긴 변경 목록, 범례.
// 흑백 인쇄에서도 구분되게 색과 함께 선 모양·빗금·표식 모양(○ □ ◇)과 기호(+ − ~)를 쓴다(CanvasPainter 공용).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace GaRuleCheck
{
    public static class ExportSheet
    {
        public const int Width = 2339, Height = 1654;   // A4 가로 @200 dpi
        private const float Px = 2.4f;

        public static Bitmap Render(CompareResponse d, VersionEntry? va, VersionEntry? vb, string labelA, string labelB,
                                    string dwgNo, string drawingName)
        {
            var bmp = new Bitmap(Width, Height);
            bmp.SetResolution(200, 200);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.White);
            var st = PaintStyle.Paper(Px);
            using var title = F(15, FontStyle.Bold);
            using var h2 = F(9.5f, FontStyle.Bold);
            using var body = F(8.5f);
            using var small = F(7.2f);
            using var black = new SolidBrush(Color.Black);
            using var gray = new SolidBrush(Color.FromArgb(90, 90, 90));
            using var line = new Pen(Color.FromArgb(150, 150, 150), 1.2f);

            float m = 70;   // 여백
            // ── 머리말 ──
            g.DrawString("도면 변경 비교 — 생산팀 전달", title, black, m, m - 10);
            string reason = vb == null ? "" : VersionStore.ReasonText(vb.Reason) + (string.IsNullOrWhiteSpace(vb.Reference) ? "" : $" — {vb.Reference}");
            var meta = new (string k, string v)[]
            {
                ("도면 번호", dwgNo),
                ("도면", drawingName),
                ("버전 비교", $"{labelA} → {labelB}" + (va != null && vb != null ? $"   ({When(va)} → {When(vb)})" : "")),
                ("작성자", vb?.Author ?? "-"),
                ("수정 사유", string.IsNullOrEmpty(reason) ? "-" : reason),
                ("출력일", DateTime.Now.ToString("yyyy-MM-dd HH:mm")),
            };
            float mx = Width * 0.52f, my = m - 6;
            float vw = Width - m - mx - 210;
            foreach (var (k, v) in meta)
            {
                g.DrawString(k, h2, gray, mx, my);
                // 긴 사유·조항은 두 줄까지 감싼다(그 이상은 줄임표)
                float vh = Math.Min(g.MeasureString(v, body, (int)vw).Height, body.GetHeight(g) * 2 + 2);
                using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                    g.DrawString(v, body, black, new RectangleF(mx + 210, my, vw, vh + 1), fmt);
                my += Math.Max(38, vh + 8);
            }
            float headerBottom = Math.Max(my, m + 110) + 14;
            g.DrawLine(line, m, headerBottom, Width - m, headerBottom);

            // ── 요약 줄 ──
            // 비교 창과 같은 요약(미충족·확인 필요 개수, 해결/새 위반/확인 필요 규칙, 두 버전의 전체 판정). 작성자 줄은 머리말에 있으므로 뺀다.
            var lines = CompareForm.SummaryText(d, labelA, labelB, vb).text.Split('\n');
            string summary = string.Join("\n", lines.Take(Math.Max(lines.Length - 1, 1)));
            var sumSize = g.MeasureString(summary, body, (int)(Width - 2 * m));
            g.DrawString(summary, body, black, new RectangleF(m, headerBottom + 14, Width - 2 * m, sumSize.Height + 2));
            float top = headerBottom + 14 + sumSize.Height + 22;

            // ── 차이 강조 그림 ──
            float footer = 150;
            var img = new RectangleF(m, top, Width * 0.62f - m, Height - top - footer - 20);
            g.DrawRectangle(line, img.X, img.Y, img.Width, img.Height);
            var view = new ViewState();
            if (d.Bounds is double[] b) view.Fit(b, Size.Round(img.Size), 0.06);
            CanvasPainter.Paint(g, img, d, CanvasMode.Diff, view, st, 1f, null);

            // ── 변경 목록 ──
            float lx = img.Right + 50, lw = Width - m - lx, ly = top;
            g.DrawString("변경 목록", h2, black, lx, ly);
            ly += 46;
            int shown = 0;
            foreach (var c in d.Changes)
            {
                var text = $"{c.DescKo}\n이전: {c.BeforeKo}   이후: {c.AfterKo}"
                           + string.Concat(c.Resolved.Select(x => $"\n✔ 해결: {x.Text}"))
                           + string.Concat(c.Introduced.Select(x => $"\n✖ 새 위반: {x.Text}"))
                           + string.Concat(c.NeedsCheck.Select(x => $"\n⚠ 확인 필요: {x.Text}"));
                var sz = g.MeasureString(text, small, (int)(lw - 70));
                if (ly + sz.Height > img.Bottom - 40)
                {
                    g.DrawString($"… 외 {d.Changes.Count - shown}건 — 비교 창에서 확인", small, gray, lx, ly);
                    break;
                }
                var mr = new RectangleF(lx, ly + 2, 40, 40);
                if (c.Bbox != null)
                    using (var mf = F(7.5f, FontStyle.Bold)) CanvasPainter.DrawMarker(g, st, c, mr, null, mf, false);
                else
                    g.DrawString($"{c.Number}.", h2, black, lx + 2, ly + 4);
                g.DrawString(text, small, black, new RectangleF(lx + 62, ly, lw - 70, sz.Height + 4));
                ly += Math.Max(sz.Height, 44) + 22;
                shown++;
            }
            if (d.Indirect.Count > 0 && ly < img.Bottom - 80)
            {
                ly += 6;
                g.DrawString("다른 변경의 영향", h2, black, lx, ly);
                ly += 42;
                foreach (var x in d.Indirect.Take(4))
                {
                    g.DrawString((x.To is null or "unknown" ? "⚠ " : "※ ") + x.Text, small, gray, new RectangleF(lx, ly, lw, 60));
                    ly += 40;
                }
            }

            // ── 범례·면책 ──
            float fy = Height - footer;
            g.DrawLine(line, m, fy, Width - m, fy);
            CanvasPainter.PaintLegend(g, new RectangleF(m, fy + 16, Width - 2 * m, 50), st);
            g.DrawString("규칙 엔진의 후보 판정입니다. 선급 승인이나 적합 증명이 아니며, 최종 판단은 설계자·선급 검토로 합니다.  "
                         + "번호 표식: ○ 추가  □ 삭제  ◇ 변경", small, gray, m, fy + 82);
            g.DrawString("GA-RuleCheck", small, gray, Width - m - 220, fy + 82);
            return bmp;
        }

        private static Font F(float pt, FontStyle style = FontStyle.Regular) => new(Theme.FontName, pt * Px * 1.35f, style, GraphicsUnit.Pixel);

        private static string When(VersionEntry v) => DateTime.TryParse(v.Created, out var t) ? t.ToString("yyyy-MM-dd HH:mm") : v.Created;
    }
}
