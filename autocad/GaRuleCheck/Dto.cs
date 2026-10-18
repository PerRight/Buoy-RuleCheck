// 엔진(Python)과 주고받는 JSON. 필드 이름은 snake_case로 자동 변환된다(JsonNamingPolicy.SnakeCaseLower).
// 명세: docs/input-contract.md, engine/garc/contract.py
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaRuleCheck
{
    public static class Contract
    {
        // 레이어 → 역할 (engine/garc/contract.py LAYER_ROLES와 같아야 한다)
        public static readonly Dictionary<string, string> LayerRoles = new()
        {
            ["GA_HULL"] = "hull",
            ["GA_EYE"] = "eye",
            ["GA_OBST"] = "obstruction",
            ["GA_BHD_COLL"] = "collision_bhd",
            ["GA_BULB"] = "bulb",
        };

        public static bool IsWatchedLayer(string layer) => LayerRoles.ContainsKey(layer.Trim().ToUpperInvariant());

        public static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public sealed class RawEntity
    {
        public string Handle { get; set; } = "";
        public string Layer { get; set; } = "";
        public string Type { get; set; } = "";
        public List<double[]> Points { get; set; } = new();
        /// 꼭짓점 i→i+1 구간의 bulge (호가 하나도 없으면 null → JSON에서 생략)
        public List<double>? Bulges { get; set; }
        /// 닫힌 폴리선 여부 (폴리선이 아니면 null)
        public bool? Closed { get; set; }
    }

    public sealed class RawDrawing
    {
        public string Schema { get; set; } = "garc.raw/1";
        public Dictionary<string, string> Source { get; set; } = new();
        public int Insunits { get; set; }
        public Dictionary<string, string> Props { get; set; } = new();
        public List<RawEntity> Entities { get; set; } = new();
        /// 모델 공간의 레이어별 객체 수 (GA_ 이름과 비슷한 레이어를 엔진이 감지하도록, 판단은 엔진)
        public Dictionary<string, int> LayerCounts { get; set; } = new();
    }

    public sealed class CheckRequest
    {
        public string DocId { get; set; } = "";
        public RawDrawing Raw { get; set; } = new();
    }

    public sealed class OverlayDto
    {
        public string Kind { get; set; } = "";
        public string? Status { get; set; }
        public List<double[]> Points { get; set; } = new();
        public string? Text { get; set; }
        public double? Height { get; set; }   // 라벨 글자 높이 [m]
    }

    public sealed class ReportResponse
    {
        public string Html { get; set; } = "";
    }

    public sealed class RuleResultDto
    {
        public string RuleId { get; set; } = "";
        public string TitleKo { get; set; } = "";
        public string Ref { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusKo { get; set; } = "";
        public string MessageKo { get; set; } = "";
        public JsonElement? Actual { get; set; }
        public JsonElement? Limit { get; set; }
        public string Unit { get; set; } = "m";
        public string? Condition { get; set; }
        public List<string> Targets { get; set; } = new();
        public string? SuggestionKo { get; set; }
        public List<string> Limitations { get; set; } = new();
        public List<OverlayDto> Overlays { get; set; } = new();
        /// 객체별 판정(마우스 오버 툴팁·체크리스트). 판정은 엔진이 한다.
        public List<ElementCheckDto> Elements { get; set; } = new();
        public bool Cached { get; set; }
    }

    /// 요소(도면 객체) 하나의 판정 — 상태는 pass / fail / unknown(확인 필요) 셋뿐.
    public sealed class ElementCheckDto
    {
        public string ElementId { get; set; } = "";
        public string RuleId { get; set; } = "";
        public string RuleName { get; set; } = "";
        public string Role { get; set; } = "";
        public string RoleKo { get; set; } = "";
        public string Status { get; set; } = "";
        public string Required { get; set; } = "";
        public string Actual { get; set; } = "";
        public string? Diff { get; set; }
        public string? Guide { get; set; }
        public double? Margin { get; set; }
    }

    public sealed class DiffDto
    {
        public bool Initial { get; set; }
        public List<string> Added { get; set; } = new();
        public List<string> Removed { get; set; } = new();
        public List<string> Modified { get; set; } = new();
        public List<string> ShipChanged { get; set; } = new();
        public bool ConditionsChanged { get; set; }
    }

    public sealed class TransitionDto
    {
        public string RuleId { get; set; } = "";
        public string? From { get; set; }
        public string To { get; set; } = "";
        public string Kind { get; set; } = "";
    }

    public sealed class CheckResponse
    {
        public string Overall { get; set; } = "";
        public string OverallKo { get; set; } = "";
        public string DisclaimerKo { get; set; } = "";
        public Dictionary<string, int> Summary { get; set; } = new();
        public List<RuleResultDto> Results { get; set; } = new();
        public int Revision { get; set; }
        public int? SameAsRevision { get; set; }
        public DiffDto Diff { get; set; } = new();
        public List<string> AffectedRules { get; set; } = new();
        public List<string> RerunRules { get; set; } = new();
        public List<string> CachedRules { get; set; } = new();
        public List<TransitionDto> Transitions { get; set; } = new();
        public double? UnitsScale { get; set; }
    }

    // ── 버전 비교 (POST /v1/compare) ─────────────────────────────────────────

    public sealed class CompareRequest
    {
        public RawDrawing A { get; set; } = new();
        public RawDrawing B { get; set; } = new();
        public List<string> Labels { get; set; } = new();
    }

    public sealed class RuleBriefDto
    {
        public string RuleId { get; set; } = "";
        public string TitleKo { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusKo { get; set; } = "";
        public string MessageKo { get; set; } = "";
    }

    public sealed class VersionSummaryDto
    {
        public string Label { get; set; } = "";
        public string Overall { get; set; } = "";
        public string OverallKo { get; set; } = "";
        public Dictionary<string, int> Summary { get; set; } = new();
        public int FailElements { get; set; }
        public int UnknownElements { get; set; }
        public List<RuleBriefDto> Rules { get; set; } = new();
        public double? UnitsScale { get; set; }
    }

    public sealed class ChangeRefDto
    {
        public string RuleId { get; set; } = "";
        public string RuleName { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// 변경 항목 한 건. kind = added / removed / modified / ship / conditions. 좌표는 m(엔진 좌표).
    public sealed class ChangeDto
    {
        public int Number { get; set; }
        public string Kind { get; set; } = "";
        public string? ElementId { get; set; }
        public string? Role { get; set; }
        public string RoleKo { get; set; } = "";
        public string DescKo { get; set; } = "";
        public string BeforeKo { get; set; } = "";
        public string AfterKo { get; set; } = "";
        public List<ChangeRefDto> Resolved { get; set; } = new();
        public List<ChangeRefDto> Introduced { get; set; } = new();
        public List<string> RelatedRules { get; set; } = new();
        public double[]? Center { get; set; }
        public double[]? Bbox { get; set; }
    }

    public sealed class IndirectDto
    {
        public string ElementId { get; set; } = "";
        public string RoleKo { get; set; } = "";
        public string RuleId { get; set; } = "";
        public string RuleName { get; set; } = "";
        public string? From { get; set; }
        public string? To { get; set; }
        public string Text { get; set; } = "";
    }

    public sealed class GeomDto
    {
        public string Handle { get; set; } = "";
        public string Role { get; set; } = "";
        public List<double[]> Points { get; set; } = new();
        public bool Closed { get; set; }
        public string? Status { get; set; }
    }

    public sealed class GeometryPairDto
    {
        public List<GeomDto> A { get; set; } = new();
        public List<GeomDto> B { get; set; } = new();
    }

    public sealed class ViolationsDto
    {
        public int Before { get; set; }
        public int After { get; set; }
    }

    public sealed class CompareResponse
    {
        public string Schema { get; set; } = "";
        public VersionSummaryDto A { get; set; } = new();
        public VersionSummaryDto B { get; set; } = new();
        public List<ChangeDto> Changes { get; set; } = new();
        public List<ChangeRefDto> ResolvedRules { get; set; } = new();
        public List<ChangeRefDto> IntroducedRules { get; set; } = new();
        public List<IndirectDto> Indirect { get; set; } = new();
        public ViolationsDto Violations { get; set; } = new();
        public GeometryPairDto Geometry { get; set; } = new();
        public double[]? Bounds { get; set; }
        public double? UnitsScale { get; set; }
        public bool Same { get; set; }
    }

    // ── 버전 기록 (<도면이름>.garc/versions.json) ─────────────────────────────

    public sealed class VersionEntry
    {
        public int Version { get; set; }
        public string File { get; set; } = "";
        public string Created { get; set; } = "";
        public string Author { get; set; } = "";
        /// rule_violation / feedback / other
        public string Reason { get; set; } = "other";
        /// 관련 규정 조항 또는 피드백 내용
        public string Reference { get; set; } = "";
        public string Note { get; set; } = "";
        public string? Overall { get; set; }
        public int? FailElements { get; set; }
        /// 스냅샷 내용 지문 — 바뀐 게 없으면 새 버전을 만들지 않는다
        public string? Hash { get; set; }
    }

    public sealed class VersionIndex
    {
        public string Schema { get; set; } = "garc.versions/1";
        public string Drawing { get; set; } = "";
        public List<VersionEntry> Versions { get; set; } = new();
    }
}
