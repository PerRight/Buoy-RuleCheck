"""생성형 AI 설명 — 빈칸 방식 (2026-10-30 팀 결정, docs/ai-explain.md).

판정·수치는 엔진만 정한다. 이 모듈은 이미 나온 결과(규칙 판정·버전 비교)를 세 가지로 나눈다.
  1) 빈칸 값(slots)   PC 안에서만 쓰는 실제 문자열 — 숫자·흘수 조건 이름·버전 이름이 들어갈 수 있다.
  2) 보낼 사실(public) 규칙 조항·판정 상태·고칠 방향처럼 이 파일에 적힌 정해진 어휘만. 숫자·handle·레이어·
                     블록·흘수 조건 이름·선명은 넣지 않는다.
  3) 기본 설명(template) AI 없이 바로 보여 줄 문장(빈칸을 써서 적고 PC 안에서 채운다).
AI는 (2)와 빈칸 이름만 받아 문장을 쓰고, 그 문장의 {빈칸}을 PC 안에서 (1)로 채운다.
AI 답은 검사한다 — 숫자(조항 번호 말고)·모르는 빈칸·쓰면 안 되는 말·판정과 반대되는 말이 있으면 버리고 기본 설명.
"""
from __future__ import annotations

import json
import re
import time
from dataclasses import dataclass, field
from typing import Any, Callable

from .engine import DISCLAIMER_KO, ROLE_KO, STATUS_KO

SCHEMA = "brc.explain/1"
MODES = ("template", "preview", "ai")

VIS, BHD = "SOLAS-V-22.1.1", "SOLAS-II1-12"
RULE_TITLE = {VIS: "선교 전방 수면 가시거리", BHD: "충돌격벽(선수격벽) 위치"}
RULE_REF = {VIS: "SOLAS Ch.V Reg.22.1.1", BHD: "SOLAS Ch.II-1 Reg.12.1·12.3·12.4"}
# AI 답에서 숫자를 허용하는 곳은 조항 번호뿐 — 이 문자열을 지운 뒤에도 숫자가 남으면 그 답은 쓰지 않는다.
REF_FORMS = {
    VIS: ["SOLAS Ch.V Reg.22.1.1", "SOLAS-V-22.1.1", "SOLAS V/22.1.1", "Reg.22.1.1", "V/22.1.1", "22.1.1"],
    BHD: ["SOLAS Ch.II-1 Reg.12.1·12.3·12.4", "SOLAS Ch.II-1 Reg.12", "SOLAS-II1-12", "SOLAS II-1/12", "II-1/12",
          "Ch.II-1", "Reg.12"],
}
# 규칙이 다루지 않는 범위 — 숫자 없이 적은 것(보낸다). 원문은 규칙 모듈의 LIMITATIONS.
SCOPE_KO = {
    VIS: ["측면도(선체 중심선 단면)만 검토 — 좌우 방향 시야 범위와 장애물 폭은 따로 확인",
          "사각 섹터 각도 요건은 검토하지 않음",
          "도면 특성에 입력된 흘수 조건만 검토 — 빠진 적재 상태가 없는지 사람이 확인"],
    BHD: ["구상선수 연장부는 윤곽의 최전단으로만 판단 — 수선 아래인지는 사람이 확인",
          "격벽 수밀 높이 요건은 검토하지 않음",
          "주관청이 허용하는 예외는 반영하지 않음"],
}
FINDING_KO = {
    "within_limit": "사각 거리가 허용 한계 안",
    "blind_distance_exceeds": "사각 거리가 허용 한계를 넘음",
    "sight_blocked": "장애물이 눈높이 이상이라 시선이 수면에 닿지 않음",
    "within_range": "격벽 거리가 허용 범위 안",
    "below_lower": "격벽이 기준점에 너무 가까움(하한 미달)",
    "above_upper": "격벽이 기준점에서 너무 멂(상한 초과) — 주관청 허용 시 예외 가능, 사람 확인",
    "step_too_wide": "계단형 격벽의 계단 폭이 허용 구간 폭보다 넓음 — 옮기기만으로는 해결 안 됨",
    "input_missing": "판정에 필요한 입력이 없어 판정하지 않음",
    "judgement_stopped": "입력이 모호하거나 서로 맞지 않아 엔진이 판정을 멈춤",
    "not_applicable": "적용 대상이 아니라고 판단함(적용 조건 입력 확인 필요)",
}
_STOP_FINDING = {"na": "input_missing", "review": "judgement_stopped", "skip": "not_applicable"}
_KIND_KO = {"added": "추가", "removed": "삭제", "modified": "수정"}
_SHIP_KO = {"LOA", "L(만재흘수선협약)", "AP 위치 x", "FP 위치 x", "기선 높이 z", "눈높이", "용골 거치일", "선종", "LBP"}

# AI에 주는 지시(고정 문장, 도면 내용 없음). 숫자를 쓰지 않도록 이 지시 자체에도 숫자를 넣지 않는다.
SYSTEM_KO = (
    "너는 조선소 GA(일반배치도) 검토 플러그인 Buoy-RuleCheck의 설명 도우미다. "
    "결정론적 규칙 엔진이 이미 낸 판정을 설계자·생산팀이 읽기 쉬운 한국어 문장으로 옮긴다.\n"
    "반드시 지킬 것:\n"
    "- 판정을 바꾸거나 새로 판단하지 않는다. 주어진 status와 facts에 있는 내용만 쓴다.\n"
    "- 숫자를 한 글자도 쓰지 않는다(아라비아 숫자, 동그라미 숫자, 숫자를 글로 풀어 쓴 값과 단위 모두). "
    "값이 필요한 자리에는 slots에 있는 빈칸 이름을 중괄호 그대로({이름}) 쓴다. 빈칸은 프로그램이 나중에 채운다.\n"
    "- 조항을 말할 때는 facts.rule.ref 문자열을 그대로 쓴다.\n"
    "- slots에 없는 빈칸을 만들지 않는다.\n"
    "- '적합', '승인', '합격', '인증', '보증', '통과 확정'이라는 말을 쓰지 않는다. 기준 안이면 '문제없음'이라고 한다.\n"
    "- 고칠 방법은 facts.actions에 있는 것만 말한다. 없으면 고칠 방법을 지어내지 않는다.\n"
    "- task가 rule이면 세 문장에서 네 문장, change면 두 문장에서 세 문장. 머리말·목록·마크다운 없이 평문 한 단락.\n"
    "- 확실하지 않은 내용은 쓰지 않는다."
)

_SLOT = re.compile(r"\{([^{}\s]{1,24})\}")
FORBIDDEN = ("적합", "승인", "합격", "인증", "보증", "통과 확정")
_UNIT_WORDS = re.compile(r"(미터|센티|밀리|킬로|퍼센트)")
MAX_AI_CHARS = 700


@dataclass
class Brief:
    """설명 재료 한 벌. public·slot_help만 밖으로 나간다(request_body)."""
    kind: str                                   # "rule" | "change"
    status: str                                 # 규칙 판정 상태 또는 비교의 이후 전체 상태
    public: dict[str, Any] = field(default_factory=dict)
    slots: dict[str, str] = field(default_factory=dict)       # 빈칸 → PC 안 값
    slot_help: dict[str, str] = field(default_factory=dict)   # 빈칸 → 뜻(보냄, 숫자 없음)
    template: str = ""
    literals: list[str] = field(default_factory=list)        # AI 답에서 숫자를 허용하는 조항 문자열

    def slot(self, name: str, value: str | None, help_ko: str) -> None:
        if value is None or value == "":
            return
        self.slots[name] = value
        self.slot_help[name] = help_ko


# ── 규칙 판정 → 설명 재료 ───────────────────────────────────────────────────────

def _m(v: float) -> str:
    return f"{v:.2f} m"


def _decisive(r: dict[str, Any]) -> dict[str, Any] | None:
    """수정 소요의 결정 객체(대상 목록의 첫 handle) — 없으면 첫 위반 요소."""
    els = r.get("elements") or []
    first = (r.get("targets") or [None])[0]
    for e in els:
        if e.get("status") == "fail" and e.get("element_id") == first:
            return e
    return next((e for e in els if e.get("status") == "fail"), None)


def rule_brief(r: dict[str, Any]) -> Brief:
    """RuleResult.to_dict() 하나 → 설명 재료."""
    rid, st = r["rule_id"], r["status"]
    ev = r.get("evidence") or {}
    b = Brief(kind="rule", status=st, literals=list(REF_FORMS.get(rid, [])))
    b.public = {
        "rule": {"id": rid, "title_ko": RULE_TITLE.get(rid, "규칙"), "ref": RULE_REF.get(rid, "")},
        "status": st, "status_ko": STATUS_KO.get(st, st),
        "scope_ko": list(SCOPE_KO.get(rid, [])),
        "actions": [],
    }
    b.slot("판정", STATUS_KO.get(st, st), "판정 상태 이름")
    b.slot("엔진결과", r.get("message_ko"), "엔진의 판정 문장(수치 포함)")
    b.slot("엔진제안", r.get("suggestion_ko"), "엔진의 수치 제안 문장")
    if st in _STOP_FINDING:
        f = _STOP_FINDING[st]
        b.public["finding"], b.public["finding_ko"] = f, FINDING_KO[f]
        b.slot("멈춤사유", r.get("message_ko"), "엔진이 판정하지 않은 이유와 고칠 입력(엔진 문장)")
        b.public["actions"] = {
            "na": ["{멈춤사유}에 적힌 빠진 입력을 넣고 다시 검사(팔레트 준비 상태 카드 또는 실도면 연결 마법사)"],
            "review": ["{멈춤사유}에 적힌 입력을 확인하고 고친 뒤 다시 검사"],
            "skip": ["적용 조건(LOA·용골 거치일) 입력이 맞는지 확인"],
        }[st]
        b.template = {
            "na": "{판정}: {멈춤사유} 엔진은 빈 입력을 추측해 채우지 않으므로 이 규칙은 아직 판정하지 않았습니다. "
                  "빠진 값을 도면 특성이나 연결 마법사로 넣으면 바로 다시 검사합니다.",
            "review": "{판정}: {멈춤사유} 엔진은 모호하거나 서로 맞지 않는 입력으로는 판정을 내리지 않습니다. "
                      "적힌 입력을 확인해 고치면 바로 다시 검사합니다.",
            "skip": "{판정}: {멈춤사유} 적용 제외는 입력값에 달려 있으니 LOA·용골 거치일이 맞게 들어갔는지 확인하세요.",
        }[st]
        return b
    finding = ev.get("finding")
    if finding in FINDING_KO:
        b.public["finding"], b.public["finding_ko"] = finding, FINDING_KO[finding]
    if rid == VIS:
        _vis(r, ev, b)
    elif rid == BHD:
        _bhd(r, ev, b)
    else:
        b.template = "{판정}: {엔진결과}" + (" {엔진제안}" if "엔진제안" in b.slots else "")
    return b


def _vis(r: dict[str, Any], ev: dict[str, Any], b: Brief) -> None:
    st = r["status"]
    lim = r.get("limit")
    if isinstance(lim, (int, float)):
        b.slot("한도", f"{lim:g} m", "허용 한계 — 전장(LOA)의 두 배와 고정 상한 중 작은 값")
    act = r.get("actual")
    b.slot("사각거리", "무한대(시선이 수면에 닿지 않음)" if act is None else _m(act), "선수 앞 수면이 가려지는 거리")
    if r.get("condition"):
        b.slot("최악조건", f"'{r['condition']}'", "가장 불리한 흘수 조건의 이름")
    eye = ev.get("eye")
    if isinstance(eye, list) and len(eye) == 2:
        b.slot("눈높이", f"z = {eye[1]:.2f} m", "조종위치 눈높이")
    b.public["criterion_ko"] = ("선교(조종위치)에서 본 선수 앞 수면의 사각 거리가 {한도} 이하 — "
                                "입력된 모든 흘수 조건 중 가장 불리한 값으로 판정")
    if st == "pass":
        if isinstance(lim, (int, float)) and isinstance(act, (int, float)):
            b.slot("여유", _m(max(lim - act, 0.0)), "허용 한계까지 남은 거리")
        trimless = any("등흘수" in str(n) for n in ev.get("notes") or [])
        b.public["trim_condition_missing"] = trimless
        b.template = ("{판정}: 선교에서 본 선수 앞 수면의 사각 거리가 {사각거리}로 허용 한계 {한도} 안입니다"
                      + ("(가장 불리한 조건 {최악조건}" if "최악조건" in b.slots else "(")
                      + (", 여유 {여유})." if "여유" in b.slots else ").")
                      + " 이 판정은 측면도만 본 후보 판정이라 좌우 시야 범위와 사각 섹터 요건은 따로 확인해야 합니다.")
        if trimless:
            b.public["actions"] = ["트림이 있는 흘수 조건(밸러스트 등)을 추가해 다시 검사"]
            b.template += " 입력된 흘수 조건이 모두 등흘수라, 트림이 있는 밸러스트 상태를 넣어 한 번 더 확인하세요."
        return
    # 수정 소요
    dec = _decisive(r)
    cause = (dec or {}).get("role_ko") or "장애물"
    if cause not in ROLE_KO.values():
        cause = "장애물"
    b.public["cause_role_ko"] = cause
    others = bool(ev.get("other_violating_handles"))
    b.public["other_objects_also_exceed"] = others
    if isinstance(ev.get("suggestion_x"), (int, float)):
        b.slot("제안위치", f"x = {ev['suggestion_x']:.2f} m", "고칠 꼭짓점의 위치")
    if isinstance(ev.get("suggestion_z"), (int, float)):
        b.slot("제안높이", f"z = {ev['suggestion_z']:.2f} m 이하", "그 지점 상단의 허용 높이(입력된 모든 흘수 조건 기준)")
    acts = []
    if "제안위치" in b.slots and "제안높이" in b.slots:
        acts.append(f"시선을 가장 크게 가리는 {cause}의 {{제안위치}} 지점 상단을 {{제안높이}}로 낮추기")
    else:
        acts.append(f"시선을 가리는 {cause} 상단을 낮추기({{엔진제안}})" if "엔진제안" in b.slots else f"시선을 가리는 {cause} 상단을 낮추기")
    acts += ["선수 트림 줄이기", "선교(조종위치) 높이 올리기"]
    if others:
        acts.append("단독으로 한계를 넘는 다른 객체도 함께 조정")
    b.public["actions"] = acts
    if act is None:
        head = "{판정}: " + cause + "이(가) 조종위치 눈높이 이상이라 선교에서 본 시선이 수면에 닿지 않습니다"
    else:
        head = "{판정}: 선교에서 본 선수 앞 수면의 사각 거리가 {사각거리}로 허용 한계 {한도}를 넘습니다"
    head += "(가장 불리한 조건 {최악조건})." if "최악조건" in b.slots else "."
    fix = (f" 시선을 가장 크게 가리는 {cause}의 {{제안위치}} 지점 상단을 {{제안높이}}로 낮추면 입력된 모든 흘수 조건에서 기준 안에 듭니다."
           if "제안위치" in b.slots and "제안높이" in b.slots else (" {엔진제안}" if "엔진제안" in b.slots else ""))
    tail = " 낮추기 어렵다면 선수 트림을 줄이거나 선교를 높이는 방법도 있습니다."
    if others:
        tail += " 다른 객체도 단독으로 한계를 넘으므로 함께 조정해야 합니다."
    b.template = head + fix + tail + " (근거: SOLAS V/22.1.1)"


def _bhd(r: dict[str, Any], ev: dict[str, Any], b: Brief) -> None:
    st = r["status"]
    ref = ev.get("ref_point")
    b.slot("기준점", str(ref) if ref else None, "거리를 재는 기준점 이름")
    lo, hi = ev.get("lower"), ev.get("upper")
    if isinstance(lo, (int, float)) and isinstance(hi, (int, float)):
        b.slot("허용범위", f"{lo:.2f}~{hi:.2f} m", "허용 거리 범위 — 만재흘수선협약 길이 L로 계산한 하한·상한")
    dmin, dmax = ev.get("d_min"), ev.get("d_max")
    if isinstance(dmin, (int, float)) and isinstance(dmax, (int, float)):
        b.slot("거리", _m(dmin) if abs(dmax - dmin) < 1e-3 else f"{dmin:.2f}~{dmax:.2f} m", "기준점에서 격벽까지의 거리")
    b.public["criterion_ko"] = ("기준점에서 충돌격벽까지의 거리가 {허용범위} 안 — 길이는 만재흘수선협약 길이 L, "
                                "계단형 격벽은 모든 부분이 범위 안이어야 함")
    where = "기준점({기준점})" if "기준점" in b.slots else "기준점"
    finding = ev.get("finding")
    dec = _decisive(r)
    delta = ((dec or {}).get("fix") or {}).get("delta")
    if st == "pass":
        if isinstance(lo, (int, float)) and isinstance(dmin, (int, float)) and isinstance(dmax, (int, float)):
            b.slot("여유", _m(max(min(dmin - lo, hi - dmax), 0.0)), "허용 범위 경계까지 남은 거리")
        b.template = (f"{{판정}}: 충돌격벽이 {where}에서 {{거리}} 떨어져 허용 범위 {{허용범위}} 안입니다"
                      + (f"(여유 {{여유}})." if "여유" in b.slots else ".")
                      + " 격벽 수밀 높이와 주관청 예외는 이 판정에 들어 있지 않으니 따로 확인하세요.")
        return
    if isinstance(delta, (int, float)):
        b.slot("고칠양", _m(delta), "고쳐야 할 양(엔진 계산값, 올림)")
    move = "{고칠양} 이상" if "고칠양" in b.slots else "기준 안에 들 때까지"
    if finding == "below_lower":
        b.public["actions"] = [f"격벽을 선미 쪽으로 {move} 옮기기"]
        b.template = (f"{{판정}}: 충돌격벽이 {where}에서 {{거리}} 떨어져 있어 허용 범위 {{허용범위}}의 하한에 못 미칩니다. "
                      f"격벽을 선미 쪽으로 {move} 옮기면 기준 안에 듭니다.")
    elif finding == "above_upper":
        b.public["actions"] = [f"격벽을 선수 쪽으로 {move} 옮기기", "주관청 허용 예외에 해당하는지 사람이 확인"]
        b.template = (f"{{판정}}: 충돌격벽이 {where}에서 {{거리}} 떨어져 있어 허용 범위 {{허용범위}}의 상한을 넘습니다. "
                      f"격벽을 선수 쪽으로 {move} 옮기면 기준 안에 들고, 주관청이 허용하는 예외인지는 사람이 확인합니다.")
    elif finding == "step_too_wide":
        b.public["actions"] = [f"계단·리세스 폭을 {('{고칠양}' if '고칠양' in b.slots else '허용 구간 폭 이하로')} 줄이기"]
        b.template = (f"{{판정}}: 계단형 충돌격벽이 {where}에서 {{거리}}에 걸쳐 있어 계단 폭이 허용 구간 폭보다 넓습니다. "
                      "어느 쪽으로 옮겨도 한쪽이 벗어나므로 계단·리세스 폭을 "
                      + ("{고칠양} 줄이세요." if "고칠양" in b.slots else "줄이세요."))
    else:
        b.template = "{판정}: {엔진결과}" + (" {엔진제안}" if "엔진제안" in b.slots else "")
    b.template += " (근거: SOLAS II-1/12)"


# ── 버전 비교 → 변경 설명(A4 '변경 설명' 초안) ─────────────────────────────────

def _change_kind_ko(c: dict[str, Any]) -> str:
    kind = c.get("kind")
    if kind == "conditions":
        return "흘수 조건 변경"
    if kind == "ship":
        name = str(c.get("title_ko") or "").removeprefix("선박 정보 ").strip()
        return f"선박 정보({name}) 변경" if name in _SHIP_KO else "선박 정보 변경"
    role = c.get("role_ko") if c.get("role_ko") in ROLE_KO.values() else ROLE_KO.get(c.get("role") or "", "객체")
    return f"{role} {_KIND_KO.get(kind, '변경')}"


def _rule_names(refs: list[dict[str, Any]]) -> list[str]:
    out = []
    for x in refs or []:
        n = RULE_TITLE.get(x.get("rule_id"))
        if n and n not in out:
            out.append(n)
    return out


def change_brief(cmp: dict[str, Any]) -> Brief:
    """/v1/compare 결과 → 변경 설명 재료(생산팀 전달 A4의 '변경 설명' 초안)."""
    changes = cmp.get("changes") or []
    b = Brief(kind="change", status=(cmp.get("b") or {}).get("overall", "na"),
              literals=[x for forms in REF_FORMS.values() for x in forms])
    la, lb = (cmp.get("a") or {}).get("label", "이전"), (cmp.get("b") or {}).get("label", "이후")
    b.slot("버전", f"{la} → {lb}", "비교한 두 버전")
    vb, va = (cmp.get("violations") or {}).get("before", 0), (cmp.get("violations") or {}).get("after", 0)
    b.slot("이전수정소요", f"{vb}건", "이전 버전의 수정 소요 객체 수")
    b.slot("이후수정소요", f"{va}건", "이후 버전의 수정 소요 객체 수")
    b.slot("변경건수", f"{len(changes)}건", "바뀐 항목 수")
    lines = []
    for c in changes[:3]:
        t = c.get("title_ko") or c.get("desc_ko") or "변경"
        f, to = c.get("value_from_ko"), c.get("value_to_ko")
        lines.append(t + (f" ({f} → {to})" if f and to else f" ({to})" if to else ""))
    if len(changes) > 3:
        lines[-1] += f" 외 {len(changes) - 3}건"
    b.slot("주요변경", "; ".join(lines) if lines else None, "주요 변경 내용(최대 세 건, 위치·크기 포함)")
    resolved = _rule_names((cmp.get("resolved_rules") or []) + [x for c in changes for x in c.get("resolved") or []])
    introduced = _rule_names((cmp.get("introduced_rules") or []) + [x for c in changes for x in c.get("introduced") or []])
    check = _rule_names((cmp.get("needs_check_rules") or []) + [x for c in changes for x in c.get("needs_check") or []]
                        + [x for x in cmp.get("indirect") or [] if x.get("to") in ("unknown", None)])
    kinds = []
    for c in changes:
        k = _change_kind_ko(c)
        if k not in kinds:
            kinds.append(k)
    trend = "줄어듦" if va < vb else "늘어남" if va > vb else "같음"
    b.public = {
        "same": bool(cmp.get("same")),
        "overall_after": b.status,
        "change_kinds_ko": kinds,
        "violation_trend_ko": trend,
        "violation_free_after": va == 0,
        "resolved_rules_ko": resolved,
        "introduced_rules_ko": introduced,
        "needs_check_rules_ko": check,
        "indirect_effects": bool(cmp.get("indirect")),
        "rules": [{"title_ko": RULE_TITLE[k], "ref": RULE_REF[k]} for k in (VIS, BHD)],
        "actions": ["표식 번호 위치의 변경을 도면에 반영"] + (["새 수정 소요는 설계팀 확인 후 반영"] if introduced else []),
    }
    if cmp.get("same"):
        b.template = "{버전}: 형상·선박 정보·판정이 모두 같습니다."
        return b
    t = "{버전} 변경 — " + ("{주요변경}." if "주요변경" in b.slots else "{변경건수}.")
    t += " 수정 소요는 {이전수정소요}에서 {이후수정소요}" + ("으로 줄었습니다." if trend == "줄어듦" else "으로 늘었습니다." if trend == "늘어남" else "으로 같습니다.")
    if resolved:
        t += f" {'·'.join(resolved)} 수정 소요가 해결됐습니다."
    if introduced:
        t += f" {'·'.join(introduced)}에 새 수정 소요가 생겨 설계팀 확인이 필요합니다."
    if check:
        t += f" {'·'.join(check)}은(는) 판정할 수 없게 바뀌어 확인이 필요합니다."
    b.template = t
    return b


# ── 보낼 본문 · 검사 · 채우기 ─────────────────────────────────────────────────

def request_body(b: Brief, model: str) -> dict[str, Any]:
    """AI에 보낼 본문 전체(키 제외). 미리보기 화면도 이것을 그대로 보여 준다."""
    user = {"task": b.kind, "facts": b.public, "slots": b.slot_help}
    return {
        "model": model,
        "max_tokens": 600,
        "system": SYSTEM_KO,
        "messages": [{"role": "user", "content": "다음 JSON의 엔진 판정을 설명하세요.\n"
                                                 + json.dumps(user, ensure_ascii=False, indent=1)}],
    }


def fill(text: str, slots: dict[str, str]) -> str:
    return _SLOT.sub(lambda m: slots.get(m.group(1), m.group(0)), text)


def check_ai_text(text: str, b: Brief) -> str | None:
    """AI 답을 쓸 수 있으면 None, 아니면 버린 이유(한국어)."""
    t = text.strip()
    if not 15 <= len(t) <= MAX_AI_CHARS:
        return "길이가 맞지 않음"
    names = _SLOT.findall(t)
    unknown = sorted(set(names) - set(b.slots))
    if unknown:
        return "없는 빈칸을 씀: " + ", ".join("{" + n + "}" for n in unknown[:3])
    rest = _SLOT.sub(" ", t)
    for lit in sorted(set(b.literals), key=len, reverse=True):
        rest = rest.replace(lit, " ")
    if any(ch.isdigit() for ch in rest):
        return "빈칸 대신 숫자를 씀(엔진 값만 쓰게 하려고 버림)"
    if "{" in rest or "}" in rest:
        return "빈칸 표기가 깨짐"
    if _UNIT_WORDS.search(rest):
        return "숫자를 글로 풀어 쓴 값이 있음"
    if "http" in t.lower() or "```" in t:
        return "링크·코드 블록이 있음"
    bad = [w for w in FORBIDDEN if w in t]
    if bad:
        return f"쓰면 안 되는 말('{bad[0]}')"
    if b.kind == "rule":   # 판정과 반대되는 말(변경 설명은 여러 규칙이 섞여 있어 이 검사를 하지 않는다)
        st = b.status
        if st == "fail" and "문제없" in t:
            return "수정 소요를 '문제없음'처럼 씀"
        if st == "pass" and "수정 소요" in t:
            return "문제없음을 '수정 소요'처럼 씀"
        if st in ("na", "review", "skip") and ("문제없" in t or "수정 소요" in t):
            return "판정하지 않은 규칙을 판정한 것처럼 씀"
    return None


def _clean(text: str) -> str:
    t = text.strip().replace("**", "").replace("__", "")
    return re.sub(r"\s*\n+\s*", " ", t)


def explain(b: Brief, mode: str = "template", key: str | None = None, model: str | None = None,
            call: Callable[[dict[str, Any], str], str] | None = None) -> dict[str, Any]:
    """mode: template(기본 설명만) · preview(보낼 본문을 보여 주기만, 안 보냄) · ai(보내고, 안 되면 기본 설명)."""
    if mode not in MODES:
        raise ValueError(f"mode는 {', '.join(MODES)} 중 하나여야 합니다.")
    from . import ai
    t0 = time.perf_counter()
    base = fill(b.template, b.slots)
    out: dict[str, Any] = {
        "schema": SCHEMA, "kind": b.kind, "status": b.status, "source": "template", "text": base,
        "template_text": base, "request": None, "sent": False, "model": None, "fallback_ko": None,
        "rejected_text": None, "elapsed_ms": 0, "disclaimer_ko": DISCLAIMER_KO,
    }
    if mode == "template":
        return out
    model = model or ai.model_name()
    body = request_body(b, model)
    out["request"] = body
    out["model"] = model
    if mode == "preview":
        return out
    if not key:
        out["fallback_ko"] = "AI 키가 없어 기본 설명을 보여 줍니다(BRCAI에서 키를 넣으세요)."
        return out
    out["sent"] = True
    try:
        raw = (call or ai.call)(body, key)
    except ai.AiUnavailable as e:
        out["fallback_ko"] = e.reason_ko
        return out
    finally:
        out["elapsed_ms"] = round((time.perf_counter() - t0) * 1000)
    text = _clean(raw)
    why = check_ai_text(text, b)
    if why:
        out["fallback_ko"] = f"AI 답을 쓰지 않았습니다 — {why}. 기본 설명을 보여 줍니다."
        out["rejected_text"] = text[:MAX_AI_CHARS]
        return out
    out.update(source="ai", text=fill(text, b.slots))
    return out


def explain_rule(report_dict: dict[str, Any], rule_id: str, mode: str = "template", key: str | None = None,
                 call: Callable[[dict[str, Any], str], str] | None = None) -> dict[str, Any]:
    r = next((x for x in report_dict.get("results", []) if x.get("rule_id") == rule_id), None)
    if r is None:
        raise ValueError(f"규칙 {rule_id}의 판정이 없습니다.")
    out = explain(rule_brief(r), mode, key, call=call)
    out["rule_id"] = rule_id
    return out


def explain_change(cmp: dict[str, Any], mode: str = "template", key: str | None = None,
                   call: Callable[[dict[str, Any], str], str] | None = None) -> dict[str, Any]:
    return explain(change_brief(cmp), mode, key, call=call)
