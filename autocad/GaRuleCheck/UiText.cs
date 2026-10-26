// 화면 문구(판정 아님): 규칙 짧은 이름·규정 번호, 카드 숫자 표기. 문구 원칙(명세 3절): '적합'·'승인'을 쓰지 않고 통과는 '문제없음'.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace GaRuleCheck
{
    public static class UiText
    {
        public const string Disclaimer = "규칙 엔진의 후보 판정입니다. 선급 승인이 아니며 최종 판단은 설계자 검토로 합니다.";

        /// 짧은 항목명(카드 머리·명령줄)
        public static string Short(string ruleId) => ruleId switch
        {
            "SOLAS-V-22.1.1" => "선교 가시거리",
            "SOLAS-II1-12" => "충돌격벽 위치",
            _ => ruleId,
        };

        /// 규정 번호 짧게(예: SOLAS V/22.1.1)
        public static string Ref(string ruleId) => ruleId switch
        {
            "SOLAS-V-22.1.1" => "SOLAS V/22.1.1",
            "SOLAS-II1-12" => "SOLAS II-1/12",
            _ => ruleId,
        };

        private static string N(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        private static List<double> Nums(JsonElement? v)
        {
            var list = new List<double>();
            if (v is not JsonElement e) return list;
            if (e.ValueKind == JsonValueKind.Number) list.Add(e.GetDouble());
            else if (e.ValueKind == JsonValueKind.Array)
                list.AddRange(e.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetDouble()));
            return list;
        }

        /// 현재값 표기(예: z 24.00 m · d 9.00~9.50 m)
        public static string Current(FixDto f)
        {
            var c = Nums(f.Current);
            if (c.Count == 0) return "—";
            string v = c.Count == 1 || Math.Abs(c[0] - c[^1]) < 1e-9 ? N(c[0]) : $"{N(c.Min())}~{N(c.Max())}";
            return $"{f.Quantity} {v} {f.Unit}";
        }

        /// 기준 표기(예: z ≤ 22.45 m · 6.60~10.56 m)
        public static string Limit(FixDto f)
        {
            var l = Nums(f.Limit);
            if (l.Count == 0) return "—";
            if (f.Op == "range" && l.Count >= 2) return $"{N(l[0])}~{N(l[1])} {f.Unit}";
            return $"{f.Quantity} {f.Op} {N(l[0])} {f.Unit}";
        }

        /// 차이 표기: 초과는 +, 여유는 '여유'
        public static string Delta(FixDto f)
        {
            if (f.Delta is not double d) return "—";
            return d > 0 ? $"+{N(d)} {f.Unit}" : $"여유 {N(-d)} {f.Unit}";
        }

        /// 근거 줄(예: SOLAS V/22.1.1 · 밸러스트 도착)
        public static string Basis(string ruleId, FixDto? f, string? ruleCondition)
        {
            var cond = f?.Condition ?? ruleCondition;
            return string.IsNullOrEmpty(cond) ? Ref(ruleId) : Ref(ruleId) + " · " + cond;
        }
    }
}
