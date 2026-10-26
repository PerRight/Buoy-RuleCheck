// 도면 특성(GARC_*)의 이름표와 흘수 조건 문자열 다루기 — AutoCAD 없이 쓰는 부분(선박 정보 탭·단독 미리보기).
// 값의 해석(단위 변환·검증)은 엔진이 한다. 읽기/쓰기·초기 설정은 DrawingProps.cs.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GaRuleCheck
{
    public sealed record ConditionRow(string Name, string TAft, string TFwd);

    public static partial class DrawingProps
    {
        public static readonly (string Key, string Label, string Hint)[] Fields =
        {
            ("GARC_LOA", "LOA 전장 [m]", "가시거리 규칙의 L (SOLAS V/2.4)"),
            ("GARC_L_LL", "L 만재흘수선 길이 [m]", "충돌격벽 규칙의 L (ICLL) — LOA와 다름"),
            ("GARC_X_AP", "AP 위치 x [m]", "도면 좌표계 기준, m"),
            ("GARC_X_FP", "FP 위치 x [m]", "도면 좌표계 기준, m"),
            ("GARC_Z_BASE", "기선 높이 z [m]", "보통 0"),
            ("GARC_EYE_HEIGHT", "눈높이 [m]", "선교 갑판 위, 기본 1.8"),
            ("GARC_KEEL_LAID", "용골 거치일", "YYYY-MM-DD"),
            ("GARC_LBP", "LBP 수선간 길이 [m]", "참고값 — AP–FP 간격과 대조해 축척·기준점 실수를 잡는다(실도면)"),
            ("GARC_SCALE", "도면 축척 1/N", "실도면: mm 시트에 1/1000로 그렸으면 1/1000. GA_ 규약 도면(1:1)은 비워 둔다"),
        };

        public static List<ConditionRow> ParseConditions(string? text)
        {
            var rows = new List<ConditionRow>();
            foreach (var chunk in (text ?? "").Replace("\n", ";").Split(';'))
            {
                var c = chunk.Trim();
                if (c.Length == 0) continue;
                int colon = c.LastIndexOf(':');
                if (colon < 0) { rows.Add(new ConditionRow(c, "", "")); continue; }
                var drafts = c[(colon + 1)..].Split('/');
                rows.Add(new ConditionRow(c[..colon].Trim(), drafts[0].Trim(), drafts.Length > 1 ? drafts[1].Trim() : ""));
            }
            return rows;
        }

        public static string FormatConditions(IEnumerable<ConditionRow> rows)
        {
            var parts = new List<string>();
            foreach (var r in rows)
            {
                if (string.IsNullOrWhiteSpace(r.Name)) continue;
                parts.Add($"{r.Name.Trim()}:{r.TAft.Trim()}/{r.TFwd.Trim()}");
            }
            return string.Join("; ", parts);
        }

        public static string M(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
