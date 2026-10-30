// 화면 문구(판정 아님): 규칙 짧은 이름·규정 번호, 카드 숫자 표기. 문구 원칙(명세 3절): '적합'·'승인'을 쓰지 않고 통과는 '문제없음'.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace BuoyRuleCheck
{
    public static class UiText
    {
        public const string Disclaimer = "규칙 엔진의 후보 판정입니다. 선급 승인이 아니며 최종 판단은 설계자 검토로 합니다.";

        // ── AI 설명 (2026-10-30 팀 결정, docs/ai-explain.md) ─────────────────────
        // 판정·수치는 엔진만. AI에는 규칙 조항·판정 상태·고칠 방향과 빈칸 이름만 보내고, 빈칸 숫자는 이 PC에서 엔진 값으로 채운다.
        public const string AiAbout = "켜면 판정 설명과 생산팀 전달 '변경 설명'에 Claude API(api.anthropic.com)가 쓴 문단을 덧붙입니다 — " +
                                      "규칙이 왜 있는지·이 판정이 다루지 않는 범위·검토할 방향. 판정과 숫자 문장은 그대로 규칙 엔진이 씁니다. " +
                                      "인터넷이나 키가 없으면 엔진 문장만 보입니다.";
        public const string AiSends = "보내는 것: 규칙 조항 · 판정 상태 · 원인 객체 종류(장애물 등) · 고칠 방향 · 이 판정이 다루지 않는 범위. " +
                                      "변경 설명은 바뀐 항목 종류(장애물 추가 등) · 설계팀 확인이 필요한 규칙 이름";
        public const string AiKeeps = "보내지 않는 것: 숫자 전부(좌표·치수·거리·건수) · 흘수 조건 이름 · 객체 번호(handle) · 레이어·블록 이름 · 도면·버전 이름. " +
                                      "숫자가 든 판정 문장은 이 PC의 엔진이 씁니다.";
        public const string ExplainButton = "설명";
        public const string ExplainTip = "이 판정을 문장으로 풀어 봅니다(숫자는 엔진 값). AI 설명이 켜져 있으면 이유·검토할 것을 AI가 덧붙입니다 — BRCAI.";

        /// 설명 창 아래 출처 줄
        public static string ExplainSource(ExplainResponse r, bool aiOn)
        {
            if (r.Source == "ai")
                return $"위 문장은 엔진, 아래 문단은 AI({r.Model} · {(r.ElapsedMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)}초) — AI 문단에는 숫자·판정이 없음";
            if (!string.IsNullOrEmpty(r.FallbackKo)) return "엔진 문장만 — " + r.FallbackKo;
            return aiOn ? "엔진 문장" : "엔진 문장 (AI 설명 꺼짐 — 켜려면 BRCAI)";
        }

        /// 엔진 오류 응답({"error": "..."})에서 문장만
        public static string EngineError(string message)
        {
            int i = message.IndexOf('{');
            if (i >= 0)
            {
                try
                {
                    using var d = JsonDocument.Parse(message[i..]);
                    if (d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) return e.GetString() ?? message;
                }
                catch (JsonException) { /* 그대로 */ }
            }
            return message;
        }

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

        /// 규정 번호: 아는 규칙은 짧게, 모르는 규칙은 엔진이 준 문구
        public static string RefOf(RuleResultDto r) => Ref(r.RuleId) != r.RuleId ? Ref(r.RuleId) : (string.IsNullOrEmpty(r.Ref) ? r.RuleId : r.Ref);

        // ── 개수와 요약(팔레트 머리·로고·목록·명령줄이 같은 말을 쓰게) ─────────────────

        /// 규칙 판정 개수. 확인 필요 = 입력 누락(na) + 판정 멈춤(review)
        public readonly record struct Counts(int Fail, int Warn, int Pass, int Skip);

        /// 모르는 상태 문자열(새 엔진의 새 상태·오타)은 확인 필요로 센다 — 문제없음으로 세지 않는다(모르면 멈춘다).
        public static Counts Count(CheckResponse r) => new(
            r.Results.Count(x => x.Status == "fail"),
            r.Results.Count(x => x.Status is not ("fail" or "pass" or "skip")),
            r.Results.Count(x => x.Status == "pass"),
            r.Results.Count(x => x.Status == "skip"));

        /// 전체 상태 키: fail / warn / pass / none(판정한 항목 없음). '일부 문제없음 + 일부 빈 입력'은 warn — 파랑으로 보이지 않게.
        public static string StateKey(CheckResponse r)
        {
            var c = Count(r);
            return c.Fail > 0 ? "fail" : c.Warn > 0 ? "warn" : c.Pass > 0 ? "pass" : "none";
        }

        /// 팔레트 머리: 큰 글자(예: 수정 소요 2)와 옆 글자(예: 문제없음 0)
        public static (string Main, string Side) Summary(CheckResponse r)
        {
            var c = Count(r);
            string skip = c.Skip > 0 ? $" · 적용 제외 {c.Skip}" : "";
            if (c.Fail > 0) return ($"수정 소요 {c.Fail}", (c.Warn > 0 ? $"확인 필요 {c.Warn} · " : "") + $"문제없음 {c.Pass}" + skip);
            if (c.Warn > 0) return ($"확인 필요 {c.Warn}", $"문제없음 {c.Pass}" + skip);
            if (c.Pass > 0) return ("모두 기준 충족", $"문제없음 {c.Pass}" + skip);
            return ("판정한 항목 없음", c.Skip > 0 ? $"적용 제외 {c.Skip}" : "");
        }

        /// 떠 있는 로고 접근성 레이블(명세 5절)
        public static string LogoLabel(string state, int n) => state switch
        {
            "fail" => $"{Product.Name} — 수정 소요 {n}개, 목록 열기",
            "warn" => $"{Product.Name} — 확인 필요 {n}개, 목록 열기",
            "pass" => $"{Product.Name} — 모두 기준 충족, 목록 열기",
            "check" => $"{Product.Name} — 검사 중, 목록 열기",
            "down" => $"{Product.Name} — 연결 끊김, 판정하지 않음",
            "error" => $"{Product.Name} — 검사하지 못함, 판정하지 않음",
            _ => $"{Product.Name} — 감시 중, 목록 열기",
        };

        /// 로고 칩: 수정 소요면 남은 개수, 확인 필요면 '!', 그 밖은 없음. n = 레이블에 넣을 개수.
        public static (string? Chip, int N) LogoChip(CheckResponse? r, string state)
        {
            var c = r != null ? Count(r) : default;
            int n = state == "fail" ? c.Fail : state == "warn" ? c.Warn : 0;
            string? chip = state == "fail" ? (n > 99 ? "99+" : n.ToString(CultureInfo.InvariantCulture)) : state == "warn" ? "!" : null;
            return (chip, n);
        }

        /// 로고 상태 키: 판정이 있으면 fail/warn/pass(판정한 항목 없음은 idle), 없으면 idle/down
        public static string LogoState(CheckResponse? r, string fallback) =>
            r == null ? fallback : StateKey(r) switch { "none" => "idle", var k => k };

        // ── 항목 카드 ────────────────────────────────────────────────────────────

        /// 카드 제목을 정하는 객체: 수정 소요면 가장 많이 고쳐야 하는 것, 문제없음이면 여유가 가장 적은 것.
        public static ElementCheckDto? Decisive(RuleResultDto r)
        {
            string want = r.Status switch { "fail" => "fail", "pass" => "pass", _ => "unknown" };
            return r.Elements.Where(e => e.Status == want && e.Fix != null)
                       .OrderByDescending(e => e.Fix!.Delta ?? double.NegativeInfinity).ThenBy(e => e.ElementId, StringComparer.Ordinal)
                       .FirstOrDefault()
                   ?? r.Elements.FirstOrDefault(e => e.Status == want);
        }

        /// 카드 제목·조건 줄(예: 상단을 1.55 m 낮추세요 / x = 112 m 장애물 · z ≤ 22.45 m 이면 기준 충족)
        public static (string Title, string Sub) Headline(RuleResultDto r)
        {
            var d = Decisive(r);
            if (r.Status is "fail" or "pass" && d?.Fix is FixDto f && !string.IsNullOrEmpty(f.ActionKo))
            {
                string sub = f.ConditionKo;
                if (f.Quantity == "z" && d.Anchor is { Length: >= 2 } a)
                    sub = $"x = {a[0].ToString("0.##", CultureInfo.InvariantCulture)} m {d.RoleKo} · {sub}";
                return (f.ActionKo, sub);
            }
            return r.Status switch
            {
                "na" => (r.MessageKo, "빈 입력이 채워지기 전에는 판정하지 않아요"),
                "review" => (r.MessageKo, "입력이 모호해 판정을 멈췄어요 — 확인한 뒤 다시 검사"),
                "skip" => ("적용 대상이 아니에요", r.MessageKo),
                _ => (r.MessageKo, r.SuggestionKo ?? ""),
            };
        }

        /// 게이지 왼쪽 이름
        public static string GaugeName(string ruleId) => ruleId switch
        {
            "SOLAS-V-22.1.1" => "사각 거리",
            "SOLAS-II1-12" => "기준점 ~ 격벽",
            _ => "현재값",
        };

        /// 게이지 오른쪽 기준(예: 허용 ≤ min(2L, 500) = 280 m · 6.60 – 10.56 m)
        public static string GaugeLimit(string ruleId, IReadOnlyList<double> lim)
        {
            if (lim.Count >= 2) return $"{N(lim[0])} – {N(lim[1])} m";
            if (lim.Count == 1)
                return ruleId == "SOLAS-V-22.1.1"
                    ? $"허용 ≤ min(2L, 500) = {lim[0].ToString("0.##", CultureInfo.InvariantCulture)} m"
                    : $"허용 ≤ {N(lim[0])} m";
            return "";
        }

        /// 게이지 아래 현재값(예: 433.84 m · 9.00 – 9.50 m · ∞)
        public static string GaugeActual(IReadOnlyList<double> act) =>
            act.Count == 0 ? "∞" : act.Count == 1 || Math.Abs(act.Min() - act.Max()) < 1e-9 ? $"{N(act[0])} m" : $"{N(act.Min())} – {N(act.Max())} m";

        /// 게이지 아래 왼쪽(예: 최악 조건 밸러스트 도착)
        public static string GaugeNote(RuleResultDto r) => string.IsNullOrEmpty(r.Condition) ? "현재값" : "최악 조건 " + r.Condition;

        public static List<double> Numbers(JsonElement? v) => Nums(v);

        /// 기여 객체 한 줄(예: 장애물 (9A) · +1.55 m / 선체 윤곽 (1A0) · 여유)
        public static string RowLabel(ElementCheckDto e) => $"{e.RoleKo} ({e.ElementId})";

        public static string RowValue(ElementCheckDto e) => e.Status switch
        {
            "unknown" => "확인 필요",
            _ when e.Fix?.Delta is double => Delta(e.Fix!),
            "pass" => "여유",
            _ => e.Diff ?? "",
        };

        /// 접힘 섹션 머리(예: 좌우 10°, 평면 사각 섹터)
        public static string NotCoveredShort(string ruleId) => ruleId switch
        {
            "SOLAS-V-22.1.1" => "좌우 10°, 평면 사각 섹터",
            "SOLAS-II1-12" => "격벽 수밀 높이, 주관청 예외",
            _ => "",
        };

        // ── 변경 알림 ────────────────────────────────────────────────────────────

        /// 받침 있는 낱말이면 true(조사 이/가, 은/는 고르기). 숫자·영문 끝은 읽는 소리로 어림.
        public static bool HasBatchim(string word)
        {
            var w = word.TrimEnd();
            if (w.Length == 0) return false;
            char c = w[^1];
            if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28 != 0;
            if (char.IsDigit(c)) return "013678".Contains(c);
            return "LMNRlmnr".Contains(c);
        }

        public static string Ga(string word) => HasBatchim(word) ? "이" : "가";

        /// 변경 알림 한 줄: (앞, 굵게, 뒤, 상태). 예: 이번 변경으로 **충돌격벽 위치**가 수정 소요가 됐어요
        public static (string Pre, string Bold, string Post, string Status) Notice(TransitionDto t)
        {
            var name = Short(t.RuleId);
            return t.Kind switch
            {
                "new_violation" => ("이번 변경으로 ", name, Ga(name) + " 수정 소요가 됐어요", "fail"),
                "resolved" => ("이번 변경으로 ", name, Ga(name) + " 문제없음으로 바뀌었어요", "pass"),
                "needs_human" => ("이번 변경으로 ", name, Ga(name) + " 확인 필요가 됐어요 — 빈 입력은 판정하지 않아요", "warn"),
                _ => ("이번 변경으로 ", name, " 판정이 바뀌었어요", t.To),
            };
        }

        // ── 떠 있는 로고 목록 ───────────────────────────────────────────────────

        /// '…하세요' → '…하기'(목록 한 줄은 명사형으로: 상단을 1.55 m 낮추기)
        public static string Noun(string action) => action.EndsWith("세요", StringComparison.Ordinal) ? action[..^2] + "기" : action;

        /// 목록 한 줄: (항목명, 설명, 상태 키 fail/warn/pass/skip)
        public static (string Title, string Sub, string Status) ListRow(RuleResultDto r)
        {
            var name = Short(r.RuleId) != r.RuleId ? Short(r.RuleId) : r.TitleKo;
            switch (r.Status)
            {
                case "fail":
                    var f = Decisive(r)?.Fix;
                    return (name, f != null && f.ActionKo.Length > 0 ? Noun(f.ActionKo) : r.SuggestionKo ?? r.MessageKo, "fail");
                case "pass": return (name, "문제없음", "pass");
                case "skip": return (name, "적용 제외", "skip");
                default: return (name, r.Status == "na" ? "빈 입력 — 판정하지 않음" : "입력 확인 필요 — 판정 멈춤", "warn");
            }
        }

        public static string ToastResolved(int n) => $"해결 {n}건 · 저장하면 버전 기록에 남아요";

        // ── 버전 비교(명세 6-6) ──────────────────────────────────────────────────

        /// 변경 한 칸 제목(엔진 표시용 title_ko, 옛 엔진이면 desc_ko)
        public static string ChangeTitle(ChangeDto c) => string.IsNullOrEmpty(c.TitleKo) ? c.DescKo : c.TitleKo!;

        /// 수치 변화(앞, 뒤): 옛 엔진이면 before_ko/after_ko
        public static (string? From, string To) ChangeValue(ChangeDto c) =>
            c.TitleKo != null ? (c.ValueFromKo, c.ValueToKo ?? "") : (c.Kind == "added" ? null : c.BeforeKo, c.AfterKo);

        /// 덧붙임 줄(예: 4단 → 3단 · 선교 가시거리 수정 소요 해결 · 기준점 거리 11.00 → 9.00 m)
        public static string ChangeNote(ChangeDto c)
        {
            var parts = new List<string>();
            parts.AddRange(c.Resolved.Select(x => $"{Short(x.RuleId)} 수정 소요 해결"));
            parts.AddRange(c.Introduced.Select(x => $"{Short(x.RuleId)} 새 수정 소요"));
            parts.AddRange(c.NeedsCheck.Select(x => $"{Short(x.RuleId)} 확인 필요"));
            if (!string.IsNullOrEmpty(c.DetailKo)) parts.Add(c.DetailKo!);
            return string.Join(" · ", parts);
        }

        /// A4 전달 시트 덧붙임(짧게): 선교 가시거리 해결 · 기준점 거리 11.00 → 9.00 m / 기준 충족
        public static string ChangeNoteShort(ChangeDto c)
        {
            var parts = new List<string>();
            parts.AddRange(c.Resolved.Select(x => $"{Short(x.RuleId)} 해결"));
            parts.AddRange(c.Introduced.Select(x => $"{Short(x.RuleId)} 새 수정 소요"));
            parts.AddRange(c.NeedsCheck.Select(x => $"{Short(x.RuleId)} 확인 필요"));
            if (!string.IsNullOrEmpty(c.DetailKo)) parts.Add(c.DetailKo!);
            if (parts.Count == 0)
            {
                var s = c.StatusAfter switch { "pass" => "기준 충족", "fail" => "수정 소요", "unknown" => "확인 필요", _ => "" };
                if (s.Length > 0) parts.Add(s);
            }
            return string.Join(" · ", parts);
        }

        /// 변경 한 칸 오른쪽 상태: (글자, 상태 키 fail/warn/pass/muted). 해결은 이후가 충족이거나 판정할 수 있는 상태에서 지워진 경우만(엔진 계산).
        public static (string Text, string Key) ChangeStatus(ChangeDto c)
        {
            if (c.Introduced.Count > 0) return ("새 수정 소요", "fail");
            if (c.NeedsCheck.Count > 0) return ("확인 필요", "warn");
            if (c.Resolved.Count > 0) return ("해결", "pass");
            return c.StatusAfter switch
            {
                "fail" => ("수정 소요", "fail"),
                "unknown" => ("확인 필요", "warn"),
                "pass" => ("기준 충족", "muted"),
                _ => ("", "muted"),
            };
        }

        /// 요약 줄 개수: (수정 소요 이전, 이후, 변경 수, 확인 필요 이후)
        public static (int FailBefore, int FailAfter, int Changes, int WarnAfter) CompareCounts(CompareResponse d) =>
            (d.Violations.Before, d.Violations.After, d.Changes.Count, d.Unknown.After + d.NeedsCheckRules.Count);

        /// 전체 판정 키(엔진 overall → fail/warn/pass/none). 엔진의 'na'는 '판정한 항목 없음'(모두 적용 제외) — StateKey의 none과 같게.
        /// 모르는 값은 확인 필요(문제없음으로 보이지 않게).
        public static string OverallKey(string overall) => overall switch
        {
            "fail" => "fail",
            "pass" => "pass",
            "na" or "" => "none",
            _ => "warn",
        };

        public static string OverallName(string overall) => OverallKey(overall) switch
        {
            "fail" => "수정 소요",
            "warn" => "확인 필요",
            "pass" => "문제없음",
            _ => "판정 없음",
        };

        /// 변경 내역 머리 아래(예: 사유 · 규정 위반 — SOLAS V/22.1.1, II-1/12)
        public static string ReasonLine(string? reasonKo, string? reference) =>
            reasonKo == null ? "사유 · 저장 전 변경" : "사유 · " + reasonKo + (string.IsNullOrWhiteSpace(reference) ? "" : " — " + OneLine(reference!));

        public static string OneLine(string s) => string.Join(" / ", s.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries));

        /// 부표 카드: 그 객체가 도면에서 없어진 뒤(이 자리의 수정 소요가 없어짐 / 규칙이 입력 누락으로 판정 못 함)
        public const string ElementGonePass = "이 객체가 없어져 이 자리의 수정 소요가 없어졌어요";
        public const string ElementGoneWarn = "이 객체가 없어졌고, 이 항목은 지금 판정할 수 없어요(입력 누락·검토 불가)";

        public const string ResolvedRule = "'해결'은 이후가 충족이거나, 판정할 수 있는 상태에서 객체가 지워진 경우만입니다. 판정 못 함으로 바뀌면 확인 필요(주황)로 따로 적습니다.";

        // ── 명령줄(명세 6-4) ────────────────────────────────────────────────────

        private static string Names(IEnumerable<string> ruleIds) => string.Join(", ", ruleIds.Select(Short));

        /// 검사 한 번의 명령줄 문구(접두어 제외). 처음 검사·다시 계산·해결·남은 수정 소요.
        public static List<string> CommandLines(CheckResponse r, double? seconds)
        {
            var lines = new List<string>();
            string rev = $"rev {r.Revision}";
            var c = Count(r);
            if (!r.Diff.Initial)
            {
                if (r.RerunRules.Count > 0)
                    lines.Add($"{rev} · 다시 계산{(seconds is double s ? " " + s.ToString("0.0", CultureInfo.InvariantCulture) + " s" : "")} — {Names(r.RerunRules)}");
                else if (r.SameAsRevision is int same)
                    lines.Add($"{rev} · rev {same}와 같은 입력 — 다시 계산 없음");
            }
            var resolved = r.Transitions.Where(t => t.Kind == "resolved").Select(t => t.RuleId).ToList();
            foreach (var id in resolved) lines.Add($"{rev} · 문제없음 — {Short(id)} 기준 충족");
            var fails = r.Results.Where(x => x.Status == "fail").Select(x => x.RuleId).ToList();
            var warns = r.Results.Where(x => x.Status is not ("fail" or "pass" or "skip")).Select(x => x.RuleId).ToList();
            string warnTail = warns.Count > 0 ? $" · 확인 필요 {warns.Count} — {Names(warns)}" : "";
            if (resolved.Count > 0)
                lines.Add($"남은 수정 소요 {c.Fail}" + (c.Fail > 0 ? $" — {Names(fails)}" : "") + warnTail);
            else if (c.Fail > 0)
                lines.Add($"{rev} · 수정 소요 {c.Fail} — {Names(fails)}" + warnTail);
            else if (c.Warn > 0)
                lines.Add($"{rev} · 확인 필요 {c.Warn} — {Names(warns)} (빈 입력은 판정하지 않음)");
            else if (c.Pass > 0)
                lines.Add($"{rev} · 문제없음 — {Names(r.Results.Where(x => x.Status == "pass").Select(x => x.RuleId))} 기준 충족");
            else
                lines.Add($"{rev} · 판정한 항목 없음");
            return lines;
        }
    }
}
