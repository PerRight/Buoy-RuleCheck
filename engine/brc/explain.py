"""생성형 AI 설명 (2026-10-30 팀 결정 A안 — 외부 LLM에 숫자·이름을 보내지 않는다, docs/ai-explain.md).

판정·수치는 엔진만 정한다. 설명 한 건은 두 부분이다.
  1) 엔진 문장(template) 판정과 숫자(사각 거리·한도·제안 높이·격벽 거리·이동량…)를 담은 문장. 빈칸으로 적고 PC 안에서
                       엔진이 판정 문장에 쓴 표시 문자열(evidence.display)로 채운다 — AI를 켜도 이 문장은 그대로다.
  2) AI 문단(ai_text)   AI 설명을 켰을 때만. 숫자·판정 표현 없이 '이 규칙이 왜 있는지 · 이 판정이 다루지 않는 범위 ·
                       검토할 방향'(변경 설명은 생산팀이 확인·반영할 것)을 쓴다. 엔진 문장 뒤에 붙인다.
AI에 보내는 것(public)은 이 파일에 적힌 정해진 어휘뿐이다 — 규칙 조항·판정 상태·원인 객체 종류·고칠 방향·다루지 않는 범위.
숫자·handle·레이어·블록·흘수 조건 이름·선명·버전 이름은 보내지 않는다(빈칸 값도 보내지 않는다).
AI 문단은 검사한다(check_ai_text) — 숫자·판정 표현·금지어·영문 낱말·링크가 있으면 버리고 엔진 문장만 보인다.
1차 설계(AI가 판정 문장을 빈칸으로 다시 쓰기)는 10-30 2차 검증에서 반대 판정 우회와 과차단이 함께 나와 이 구조로 바꿨다.
"""
from __future__ import annotations

import json
import re
import time
import unicodedata
from dataclasses import dataclass, field
from typing import Any, Callable

from .engine import DISCLAIMER_KO, ROLE_KO, STATUS_KO

SCHEMA = "brc.explain/1"
MODES = ("template", "preview", "ai")

VIS, BHD = "SOLAS-V-22.1.1", "SOLAS-II1-12"
RULE_TITLE = {VIS: "선교 전방 수면 가시거리", BHD: "충돌격벽(선수격벽) 위치"}
RULE_REF = {VIS: "SOLAS Ch.V Reg.22.1.1", BHD: "SOLAS Ch.II-1 Reg.12.1·12.3·12.4"}
# AI 답에서 숫자를 허용하는 곳은 조항 번호뿐 — 이 문자열을 (앞뒤가 숫자·영문·점이 아닐 때만) 지운 뒤에도 숫자가 남으면 버린다.
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
# 판정 분류의 뜻 — AI에 보내는 말이라 판정·비교 표현 없이 '무엇을 다루는 경우인지'만 적는다
FINDING_KO = {
    "within_limit": "선수 앞 수면이 가려지는 거리(사각 거리)를 확인한 경우",
    "blind_distance_exceeds": "사각 거리를 줄여야 하는 경우",
    "sight_blocked": "장애물이 눈높이까지 올라와 선교에서 선수 앞 수면이 보이지 않는 경우",
    "within_range": "충돌격벽 위치를 확인한 경우",
    "below_lower": "충돌격벽이 기준점에 가까워 선미 쪽으로 옮겨야 하는 경우",
    "above_upper": "충돌격벽이 기준점에서 멀어 선수 쪽으로 옮겨야 하는 경우(주관청 예외 가능성은 사람이 확인)",
    "step_too_wide": "계단형 충돌격벽의 계단 폭을 줄여야 하는 경우",
    "input_missing": "판정에 필요한 입력이 없어 엔진이 판정을 미룬 경우",
    "judgement_stopped": "입력이 모호하거나 서로 맞지 않아 엔진이 판정을 미룬 경우",
    "not_applicable": "적용 대상이 아니라고 본 경우(적용 조건 입력은 사람이 확인)",
}
_STOP_FINDING = {"na": "input_missing", "review": "judgement_stopped", "skip": "not_applicable"}
_KIND_KO = {"added": "추가", "removed": "삭제", "modified": "수정"}
_SHIP_KO = {"LOA", "L(만재흘수선협약)", "AP 위치 x", "FP 위치 x", "기선 높이 z", "눈높이", "용골 거치일", "선종", "LBP"}
_ROLE_ORDER = list(ROLE_KO.values()) + ["객체"]
_OVERALLS = {"pass", "fail", "incomplete", "na"}

# AI에 주는 지시(고정 문장, 도면 내용 없음). 숫자를 쓰지 않도록 이 지시 자체에도 숫자를 넣지 않는다.
SYSTEM_KO = (
    "너는 조선소 GA(일반배치도) 검토 플러그인 Buoy-RuleCheck의 설명 도우미다. "
    "결정론적 규칙 엔진이 판정과 수치를 담은 문장을 이미 썼고, 너는 그 문장 뒤에 붙일 짧은 설명 문단을 쓴다.\n"
    "쓸 내용:\n"
    "- task가 rule이면: 이 규칙이 왜 있는지(무엇을 막으려는 규정인지), facts.scope_ko에 있는 이 판정이 다루지 않는 범위, "
    "facts.actions에 있는 검토 방향을 자연스럽게 두세 문장으로. status가 na·review면 엔진이 왜 판정을 미루는지와 무엇을 채워야 하는지.\n"
    "- task가 change면: 생산팀이 도면에서 확인하고 반영할 것을 facts.change_kinds_ko와 facts.actions 중심으로 한두 문장으로.\n"
    "반드시 지킬 것:\n"
    "- 판정과 수치는 앞 문장에 이미 있다. 판정을 다시 말하거나 바꾸지 않는다. 기준을 넘는지·안에 드는지·해결됐는지·늘었는지 줄었는지, "
    "값끼리의 비교를 쓰지 않는다. 다음 말을 쓰지 않는다: 문제없음, 문제가 있다, 이상 없음, 수정 소요, 확인 필요, 위반, 충족, 만족, 미달, 초과, "
    "넘다, 이내, 기준 안, 범위 안이다, 벗어나다, 정상, 양호, 준수, 부합, 규정에 맞다, 필요 없다, 지장, 무방, 웃돌다, 밑돌다, 모자라다, 부족, "
    "위험, 여유, 해결, 늘다, 줄다(줄이다는 써도 됨), 증가, 감소, 비교하는 '보다', 적합, 승인, 합격, 인증, 보증, 통과, 확정, 안전, 적용 제외, "
    "인정되다, 허가되다, 틀리다.\n"
    "- 숫자를 쓰지 않는다(아라비아 숫자, 동그라미 숫자, 로마 숫자, 한자, 숫자를 글로 쓴 말, 단위 기호 모두).\n"
    "- 영어 낱말을 쓰지 않는다. 조항을 말할 때만 facts.rule.ref 문자열을 그대로 쓴다.\n"
    "- 고칠 방법은 facts.actions에 있는 것만 말한다. 확실하지 않은 내용은 쓰지 않는다.\n"
    "- 한글과 기본 문장 부호만 쓴다. 머리말·목록·마크다운·링크·중괄호·이모지 없이 한국어 평문 한 단락, 마지막 문장까지 끝맺는다."
)

MAX_AI_CHARS = 450
# 보이지 않는 글자(서식·결합·사용자 정의 문자, 한글 채움 문자, 변이 선택자)는 아예 받지 않는다
_INVISIBLE_EXTRA = set("\u3164\u115f\u1160\uffa0\u034f") | {chr(c) for c in range(0xFE00, 0xFE10)}
# 판정·비교 표현(공백을 지운 NFKC 소문자에서 찾는다) — 엔진 문장이 맡는 말이라 AI 문단에는 없어야 한다
# 판정·비교 표현 — 엔진 문장이 맡는 말이라 AI 문단에는 없어야 한다. (이름, 정규식) — 글자 사이에 띄어쓰기·문장 부호를
# 끼운 우회('적 합', '문-제-없-음')도 잡는다. 이름이 '~'로 시작하면 띄어쓰기를 끼운 일치는 보지 않는다(2음절 일반 낱말의 우연 일치 방지:
# '이 내용'→'이내', '넘어가기'는 제외 등). 검사는 NFKC·소문자·문장 부호를 지운 글에서 한다.
_VERDICT_SRC = [
    "문제없", "문제가없", "문제는없", "문제될것이없", "문제되지않", "문제있", "문제가있", "이상없", "이상이없", "이상은없",
    "수정소요", "수정이필요", "수정해야", "고쳐야", "고칠필요", "필요가없", "필요없", "확인필요", "적용제외",
    "위반", "충족", "만족", "미달", "초과", "~넘습", "~넘었", "~넘는", "~넘지", "~넘을", "~넘으", "~넘친", "~넘어(?!가|간|갈|서)",
    "기준안", "기준이하", "기준이상", "기준에맞", "규정에맞", "범위안(?:입|이다|이며|에\\s*있|에\\s*듭|에\\s*들)", "한계안(?:입|이다|이며|에\\s*있|에\\s*듭|에\\s*들)",
    "범위에\\s*듭", "범위에\\s*들", "~이내(?![용부려리])", "~벗어", "~정상", "~양호", "~준수", "~부합", "~지장", "~무방", "~웃돌", "~웃돕", "~웃돈", "~밑돌", "~밑돕", "~밑돈",
    "~상회", "~하회", "~모자(?:랍|라요|라다|랐|라서)", "~부족(?:합|하다|해|했)", "~위험(?:합|하다|해요)", "~여유", "~틀렸", "~틀릴", "~틀린",
    "~해결", "~줄었", "~줄어(?!들게)", "~늘었", "~늘어", "~증가", "~감소", "~괜찮",
    "보다길", "보다깁", "보다짧", "보다멀", "보다가깝", "보다높", "보다낮", "보다크", "보다큽", "보다작", "보다많", "보다적",
    "적합", "승인", "합격", "인증", "보증", "통과", "확정", "~안전", "~인정되", "~인정됩", "~인정받", "~허가되", "~허가됩", "~허가받",
    "그대로진행", "合格", "承認", "認證", "許可", "適合",
]


def _verdict_rx(src: str) -> tuple[str, re.Pattern[str], bool]:
    tight = src.startswith("~")
    body = src.lstrip("~")
    m = re.match(r"^([가-힣a-z]+)(.*)$", body)
    head, tail = (m.group(1), m.group(2)) if m else (body, "")
    gap = "" if tight else r"\s*"
    return head, re.compile(gap.join(map(re.escape, head)) + tail), tight


_VERDICT_RE = [_verdict_rx(x) for x in _VERDICT_SRC]
_HANGUL_NUM = re.compile(r"[일이삼사오육륙칠팔구][십백천]|십[일이삼사오육칠팔구]|[백천]만|다섯|여섯|일곱|여덟|아홉|스물|스무|서른|마흔|쉰|예순|일흔|여든|아흔"
                         r"|열(?:한|두|세|네|다섯|여섯|일곱|여덟|아홉)|(?:영|공|일|이|삼|사|오|육|칠|팔|구|하나|둘|셋|넷)점(?:영|공|일|이|삼|사|오|육|칠|팔|구)"
                         r"|절반|반절")
# 쓸 수 있는 글자: 한글 음절, ASCII 인쇄 문자, 기본 문장 부호·공백 — 그 밖(자모·키릴·가나·라틴 확장 등)은 숫자·판정을 다른 글자로 쓰는 길이라 받지 않는다
_ALLOWED_EXTRA = set(" ·…—–‘’“”「」『』")
_UNIT = re.compile(r"%|°|㎜|㎝|㎞|㎡|미터|센티|센치|밀리|킬로|퍼센트|피트|인치|도씨")
_MARKUP = re.compile(r"://|www\.|[<>\[\]`#*|]")
# 허용하는 영문 낱말(조항 문자열을 지운 뒤) — 그 밖의 영문은 숫자·승인 표현을 영어로 쓰는 길이라 받지 않는다
_LATIN_OK = {"solas", "ch", "reg", "v", "ii", "loa", "l", "lbp", "fp", "ap", "ga", "icll", "imo", "buoy", "rulecheck"}


@dataclass
class Brief:
    """설명 재료 한 벌. public만 밖으로 나간다(request_body). slots·slot_help·template은 PC 안에서만."""
    kind: str                                   # "rule" | "change"
    status: str                                 # 규칙 판정 상태 또는 비교의 이후 전체 상태
    public: dict[str, Any] = field(default_factory=dict)
    slots: dict[str, str] = field(default_factory=dict)       # 빈칸 → PC 안 값
    slot_help: dict[str, str] = field(default_factory=dict)   # 빈칸 → 뜻(설명용, 보내지 않음)
    template: str = ""
    literals: list[str] = field(default_factory=list)        # AI 답에서 숫자를 허용하는 조항 문자열

    def slot(self, name: str, value: str | None, help_ko: str) -> None:
        if value is None or value == "":
            return
        self.slots[name] = value
        self.slot_help[name] = help_ko


def _sentence(text: str | None) -> str | None:
    """엔진 문장을 한 문장으로 끝맺는다(뒤에 다른 문장이 붙어도 읽히게)."""
    if not text:
        return text
    t = text.strip()
    return t if t.endswith((".", "!", "?", "。")) else t + "."


def _josa(word: str, with_batchim: str, without: str) -> str:
    """받침 있으면 '은/이/으로', 없으면 '는/가/로'."""
    ch = word.rstrip()[-1:] or "가"
    if "가" <= ch <= "힣":
        return with_batchim if (ord(ch) - 0xAC00) % 28 else without
    return with_batchim


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
        # 엔진이 만든 값이라도 정해진 목록에 없으면 보내지 않는다(방어)
        "rule": {"id": rid if rid in RULE_TITLE else "unknown", "title_ko": RULE_TITLE.get(rid, "규칙"),
                 "ref": RULE_REF.get(rid, "")},
        "status": st if st in STATUS_KO else "unknown",
        "scope_ko": list(SCOPE_KO.get(rid, [])),
        "actions": [],
    }
    b.slot("판정", STATUS_KO.get(st, "확인 필요"), "판정 상태 이름")
    b.slot("엔진결과", _sentence(r.get("message_ko")), "엔진의 판정 문장(수치 포함)")
    b.slot("엔진제안", _sentence(r.get("suggestion_ko")), "엔진의 수치 제안 문장")
    if st in _STOP_FINDING:
        f = _STOP_FINDING[st]
        b.public["finding"], b.public["finding_ko"] = f, FINDING_KO[f]
        b.slot("멈춤사유", _sentence(r.get("message_ko")), "엔진이 판정하지 않은 이유와 고칠 입력(엔진 문장)")
        b.public["actions"] = {
            "na": ["엔진이 알려 준 빠진 입력을 도면 특성이나 실도면 연결 마법사로 넣고 다시 검사"],
            "review": ["엔진이 알려 준 입력을 확인하고 고친 뒤 다시 검사"],
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
    if rid not in (VIS, BHD) or not ev.get("display"):
        # 표시 문자열이 없는 결과(새 규칙 등)는 엔진 판정 문장 그대로 — 숫자를 다시 만들지 않는다(반올림 어긋남 방지)
        b.template = "{판정}: {엔진결과}" + (" {엔진제안}" if "엔진제안" in b.slots else "")
    return b


def _vis(r: dict[str, Any], ev: dict[str, Any], b: Brief) -> None:
    st = r["status"]
    disp = ev.get("display") or {}
    lim, act = r.get("limit"), r.get("actual")
    # 숫자는 규칙이 판정 문장에 쓴 표시 문자열 그대로(없으면 같은 형식으로)
    b.slot("한도", disp.get("limit") or (f"{lim:g} m" if isinstance(lim, (int, float)) else None),
           "허용 한계 — 전장(LOA)의 두 배와 고정 상한 중 작은 값")
    b.slot("사각거리", disp.get("d") or ("무한대(시선이 수면에 닿지 않음)" if act is None else _m(act)),
           "선수 앞 수면이 가려지는 거리")
    if r.get("condition"):
        b.slot("최악조건", f"'{r['condition']}'", "가장 불리한 흘수 조건의 이름")
    b.public["criterion_ko"] = ("선교(조종위치)에서 본 선수 앞 수면이 가려지는 거리(사각 거리)를 전장의 두 배와 고정 상한 중 "
                                "작은 값 이하로 둠 — 입력된 모든 흘수 조건 중 가장 불리한 경우로 판정")
    if st == "pass":
        b.slot("남은거리", disp.get("room"), "허용 한계까지 남은 사각 거리")
        trimless = any("등흘수" in str(n) for n in ev.get("notes") or [])
        b.public["trim_condition_missing"] = trimless
        b.template = ("{판정}: 선교에서 본 선수 앞 수면의 사각 거리가 {사각거리}로 허용 한계 {한도} 안입니다"
                      + ("(가장 불리한 조건 {최악조건}" if "최악조건" in b.slots else "(")
                      + (", 한계까지 {남은거리})." if "남은거리" in b.slots else ").")
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
    b.slot("제안위치", ("x = " + disp["x"]) if disp.get("x") else None, "허용 높이가 가장 엄격한 꼭짓점의 위치")
    b.slot("제안높이", ("z = " + disp["z"]) if disp.get("z") else None,
           "그 장애물 상단의 허용 높이(입력된 모든 흘수 조건 기준, 이보다 높은 부분이 없게)")
    has_fix = "제안위치" in b.slots and "제안높이" in b.slots
    acts = [f"시선을 가장 크게 가리는 {cause}의 상단 낮추기", "선수 트림 줄이기", "선교(조종위치) 높이 올리기"]
    if others:
        acts.append("시선을 가리는 다른 객체도 함께 조정")
    b.public["actions"] = acts
    if act is None:
        head = f"{{판정}}: {cause}{_josa(cause, '이', '가')} 조종위치 눈높이 이상이라 선교에서 본 시선이 수면에 닿지 않습니다"
    else:
        head = "{판정}: 선교에서 본 선수 앞 수면의 사각 거리가 {사각거리}로 허용 한계 {한도}를 넘습니다"
    head += "(가장 불리한 조건 {최악조건})." if "최악조건" in b.slots else "."
    if has_fix:
        fix = (f" 시선을 가장 크게 가리는 {cause}에서 {{제안높이}}보다 높은 부분이 없게 낮추면(가장 엄격한 곳 {{제안위치}}) "
               + ("이 객체로 인한 초과는 풀립니다." if others else "입력된 모든 흘수 조건에서 기준 안에 듭니다."))
    else:
        fix = " {엔진제안}" if "엔진제안" in b.slots else ""
    tail = " 낮추기 어렵다면 선수 트림을 줄이거나 선교를 높이는 방법도 있습니다."
    if others:
        tail += " 다른 객체도 단독으로 한계를 넘으므로 함께 조정해야 합니다."
    b.template = head + fix + tail + " (근거: SOLAS V/22.1.1)"


def _bhd(r: dict[str, Any], ev: dict[str, Any], b: Brief) -> None:
    st = r["status"]
    disp = ev.get("display") or {}
    ref = ev.get("ref_point")
    b.slot("기준점", str(ref) if ref else None, "거리를 재는 기준점 이름")
    lo, hi = ev.get("lower"), ev.get("upper")
    b.slot("허용범위", disp.get("range") or (f"{lo:.2f}~{hi:.2f} m" if isinstance(lo, (int, float)) and isinstance(hi, (int, float)) else None),
           "허용 거리 범위 — 만재흘수선협약 길이 L로 계산한 하한·상한")
    dmin, dmax = ev.get("d_min"), ev.get("d_max")
    b.slot("거리", disp.get("distance") or (
        (_m(dmin) if abs(dmax - dmin) < 1e-3 else f"{dmin:.2f}~{dmax:.2f} m")
        if isinstance(dmin, (int, float)) and isinstance(dmax, (int, float)) else None),
        "기준점에서 격벽까지의 거리")
    b.public["criterion_ko"] = ("기준점에서 충돌격벽까지의 거리를 정해진 하한과 상한 사이에 둠 — 길이는 만재흘수선협약 길이 L, "
                                "계단형 격벽은 모든 부분이 그 사이에")
    where = "기준점({기준점})" if "기준점" in b.slots else "기준점"
    finding = ev.get("finding")
    dec = _decisive(r)
    delta = ((dec or {}).get("fix") or {}).get("delta")
    if st == "pass":
        b.slot("여유", disp.get("room"), "허용 범위 경계까지 남은 거리")
        b.template = (f"{{판정}}: 충돌격벽이 {where}에서 {{거리}} 떨어져 허용 범위 {{허용범위}} 안입니다"
                      + ("(여유 {여유})." if "여유" in b.slots else ".")
                      + " 격벽 수밀 높이와 주관청 예외는 이 판정에 들어 있지 않으니 따로 확인하세요.")
        return
    if isinstance(delta, (int, float)):
        b.slot("고칠양", _m(delta), "고쳐야 할 양(엔진 계산값, 올림)")   # 요소 판정 fix.delta·제안 문장과 같은 값
    move = "{고칠양} 이상" if "고칠양" in b.slots else "기준 안에 들 때까지"
    if finding == "below_lower":
        b.public["actions"] = ["격벽을 선미 쪽으로 옮기기"]
        b.template = (f"{{판정}}: 충돌격벽이 {where}에서 {{거리}} 떨어져 있어 허용 범위 {{허용범위}}의 하한에 못 미칩니다. "
                      f"격벽을 선미 쪽으로 {move} 옮기면 기준 안에 듭니다.")
    elif finding == "above_upper":
        b.public["actions"] = ["격벽을 선수 쪽으로 옮기기", "주관청 허용 예외에 해당하는지 사람이 확인"]
        b.template = (f"{{판정}}: 충돌격벽이 {where}에서 {{거리}} 떨어져 있어 허용 범위 {{허용범위}}의 상한을 넘습니다. "
                      f"격벽을 선수 쪽으로 {move} 옮기면 기준 안에 들고, 주관청이 허용하는 예외인지는 사람이 확인합니다.")
    elif finding == "step_too_wide":
        b.public["actions"] = ["계단·리세스 폭 줄이기"]
        b.template = (f"{{판정}}: 계단형 충돌격벽이 {where}에서 {{거리}}에 걸쳐 있어 계단 폭이 허용 구간 폭보다 넓습니다. "
                      "어느 쪽으로 옮겨도 한쪽이 벗어나므로 계단·리세스 폭을 "
                      + ("{고칠양} 줄이세요." if "고칠양" in b.slots else "줄이세요."))
    else:
        b.template = "{판정}: {엔진결과}" + (" {엔진제안}" if "엔진제안" in b.slots else "")
    b.template += " (근거: SOLAS II-1/12)"


# ── 버전 비교 → 변경 설명(A4 '변경 설명' 초안) ─────────────────────────────────

def _change_kind(c: dict[str, Any]) -> tuple[tuple[int, int], str]:
    """(정렬 키, 이름) — 정렬은 고정 순서(추가·삭제·수정 → 선박 정보 → 흘수 조건), handle·좌표 순서에 기대지 않는다."""
    kind = c.get("kind")
    if kind == "conditions":
        return (9, 0), "흘수 조건 변경"
    if kind == "ship":
        name = str(c.get("title_ko") or "").removeprefix("선박 정보 ").strip()
        return (8, 0), (f"선박 정보({name}) 변경" if name in _SHIP_KO else "선박 정보 변경")
    role = c.get("role_ko") if c.get("role_ko") in ROLE_KO.values() else ROLE_KO.get(c.get("role") or "", "객체")
    k = list(_KIND_KO).index(kind) if kind in _KIND_KO else 3
    return (k, _ROLE_ORDER.index(role) if role in _ROLE_ORDER else len(_ROLE_ORDER)), f"{role} {_KIND_KO.get(kind, '변경')}"


def _rule_names(refs: list[dict[str, Any]]) -> list[str]:
    out = []
    for rid in (RULE_TITLE[k] for k in (VIS, BHD) if any(x.get("rule_id") == k for x in refs or [])):
        out.append(rid)
    return out


def change_brief(cmp: dict[str, Any]) -> Brief:
    """/v1/compare 결과 → 변경 설명 재료(생산팀 전달 A4의 '변경 설명' 초안)."""
    changes = cmp.get("changes") or []
    oa, ob = (cmp.get("a") or {}).get("overall", "na"), (cmp.get("b") or {}).get("overall", "na")
    b = Brief(kind="change", status=ob if ob in _OVERALLS else "unknown",
              literals=[x for forms in REF_FORMS.values() for x in forms])
    la, lb = (cmp.get("a") or {}).get("label", "이전"), (cmp.get("b") or {}).get("label", "이후")
    b.slot("버전", f"{la} → {lb}", "비교한 두 버전")
    vb, va = (cmp.get("violations") or {}).get("before", 0), (cmp.get("violations") or {}).get("after", 0)
    # 수정 소요 건수는 두 버전 모두 모든 규칙을 판정했을 때만 비교한다(판정 못 해서 0건인 것을 '줄었다'로 읽지 않게).
    # 전체가 '수정 소요'여도 다른 규칙이 입력 없음·판정 멈춤이면 비교하지 않는다.
    def judged_all(v: dict[str, Any]) -> bool:
        s = v.get("summary") or {}
        return v.get("overall") in ("pass", "fail") and not s.get("na") and not s.get("review")
    comparable = judged_all(cmp.get("a") or {}) and judged_all(cmp.get("b") or {})
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
    # '해결'은 규칙 단위로만(규칙이 여전히 수정 소요인데 객체 하나가 풀린 것은 '일부 객체'로 따로)
    resolved = _rule_names(cmp.get("resolved_rules") or [])
    partial = [x for x in _rule_names([y for c in changes for y in c.get("resolved") or []]) if x not in resolved]
    introduced = _rule_names((cmp.get("introduced_rules") or []) + [x for c in changes for x in c.get("introduced") or []])
    check = _rule_names((cmp.get("needs_check_rules") or []) + [x for c in changes for x in c.get("needs_check") or []]
                        + [x for x in cmp.get("indirect") or [] if x.get("to") in ("unknown", None)])
    kinds = [name for _, name in sorted({_change_kind(c) for c in changes})]
    trend = ("비교 불가" if not comparable else "줄어듦" if va < vb else "늘어남" if va > vb else "같음")
    # AI에는 판정·증감을 보내지 않는다(문단에 쓰지 않을 말) — 바뀐 항목 종류와 설계팀 확인이 필요한 규칙 이름만
    design_check = list(dict.fromkeys(introduced + check))
    b.public = {
        "same": bool(cmp.get("same")),
        "change_kinds_ko": kinds,
        "design_check_rules_ko": design_check,
        "indirect_effects": bool(cmp.get("indirect")),
        "rules": [{"title_ko": RULE_TITLE[k], "ref": RULE_REF[k]} for k in (VIS, BHD)],
        "actions": ["표식 번호 위치의 변경을 도면에 반영"]
                   + (["설계팀 확인이 필요한 규칙은 확인을 받은 뒤 반영"] if design_check else [])
                   + (["다른 변경의 영향을 받은 객체도 함께 확인"] if cmp.get("indirect") else []),
    }
    if cmp.get("same"):
        b.template = "{버전}: 형상·선박 정보·판정이 모두 같습니다."
        return b
    t = "{버전} 변경 — " + ("{주요변경}." if "주요변경" in b.slots else "{변경건수}.")
    if not comparable:
        t += " 판정할 수 없는 규칙이 있어 수정 소요 건수는 비교하지 않았습니다."
    elif trend == "같음":
        t += " 수정 소요는 그대로 {이후수정소요}입니다."
    else:
        t += " 수정 소요는 {이전수정소요}에서 {이후수정소요}" + ("으로 줄었습니다." if trend == "줄어듦" else "으로 늘었습니다.")
    if resolved:
        t += f" {'·'.join(resolved)} 수정 소요가 해결됐습니다."
    if partial:
        names = "·".join(partial)
        t += f" {names}{_josa(names, '은', '는')} 일부 객체의 수정 소요가 풀렸습니다(규칙은 아직 수정 소요)."
    if introduced:
        t += f" {'·'.join(introduced)}에 새 수정 소요가 생겨 설계팀 확인이 필요합니다."
    if check:
        names = "·".join(check)
        t += f" {names}{_josa(names, '은', '는')} 판정할 수 없게 바뀌어 확인이 필요합니다."
    b.template = t
    return b


# ── 보낼 본문 · 검사 · 채우기 ─────────────────────────────────────────────────

_SLOT = re.compile(r"\{([^{}\s]{1,24})\}")


def request_body(b: Brief, model: str) -> dict[str, Any]:
    """AI에 보낼 본문 전체(키 제외). 미리보기 화면도 이것을 그대로 보여 준다. 빈칸 값은 물론 빈칸 이름도 보내지 않는다."""
    user = {"task": b.kind, "facts": b.public}
    return {
        "model": model,
        "max_tokens": 800,
        "system": SYSTEM_KO,
        "messages": [{"role": "user", "content": "다음 JSON의 엔진 판정 뒤에 붙일 설명 문단을 쓰세요.\n"
                                                 + json.dumps(user, ensure_ascii=False, indent=1)}],
    }


def fill(text: str, slots: dict[str, str]) -> str:
    """엔진 문장의 {빈칸}을 값으로 한 번만 바꾼다(값 안의 중괄호는 다시 바꾸지 않음)."""
    return _SLOT.sub(lambda m: slots.get(m.group(1), m.group(0)), text)


def _strip_literals(t: str, literals: list[str]) -> str:
    """조항 문자열을 지운다 — 앞뒤에 숫자·영문이 붙어 있거나 점 뒤에 글자가 이어지면 지우지 않는다(숫자 밀수 차단).
    문장 끝 마침표('…Reg.22.1.1.')는 허용."""
    for lit in sorted(set(literals), key=len, reverse=True):
        t = re.sub(r"(?<![0-9A-Za-z.])" + re.escape(lit) + r"(?![0-9A-Za-z]|\.[0-9A-Za-z])", " ", t, flags=re.I)
    return t


def check_ai_text(text: str, b: Brief) -> str | None:
    """AI 문단을 쓸 수 있으면 None, 아니면 버린 이유(한국어). 의심되면 버린다(엔진 문장만 보이는 쪽이 안전)."""
    t = text.strip()
    if not 10 <= len(t) <= MAX_AI_CHARS:
        return "길이가 맞지 않음"
    if any(unicodedata.category(ch) in ("Cc", "Cf", "Mn", "Me", "Co", "Cs", "Zl", "Zp") or ch in _INVISIBLE_EXTRA for ch in t):
        return "보이지 않는 글자·제어 문자가 있음"
    if any(not ("가" <= ch <= "힣" or " " <= ch <= "~" or ch in _ALLOWED_EXTRA) for ch in t):
        odd = next(ch for ch in t if not ("가" <= ch <= "힣" or " " <= ch <= "~" or ch in _ALLOWED_EXTRA))
        return f"쓸 수 없는 글자가 있음(U+{ord(odd):04X})"
    if not re.search(r"[.!?。]\s*$|[다요]\s*$", t):
        return "문장이 끝나지 않음(답이 중간에 잘렸을 수 있음)"
    n = unicodedata.normalize("NFKC", t)
    if any(ch in "{}" for ch in n):
        return "중괄호가 있음(빈칸은 엔진 문장만 씀)"
    rest_raw = _strip_literals(t, b.literals)
    rest = _strip_literals(n, b.literals)
    if any(unicodedata.category(ch).startswith("N") or ch.isnumeric() for ch in rest_raw + rest):
        return "숫자가 있음(수치는 엔진 문장만 씀)"
    if any(unicodedata.category(ch) == "So" for ch in rest_raw):   # 이모지·키캡 숫자(🔟)·둘러싼 글자 등
        return "기호·이모지가 있음"
    if any("\u3400" <= ch <= "\u9fff" or "\uf900" <= ch <= "\ufaff" for ch in rest):
        return "한자가 있음"
    flat = re.sub(r"\s+", "", rest).lower()
    if _HANGUL_NUM.search(flat) or _UNIT.search(flat):
        return "숫자를 글로 쓴 값이나 단위가 있음"
    if _MARKUP.search(rest):
        return "링크·마크업이 있음"
    latin = [w for w in re.findall(r"[A-Za-z]+", rest) if w.lower() not in _LATIN_OK]
    if latin:
        return f"영문 낱말이 있음('{latin[0]}')"
    # 한글 사이에 끼운 문장 부호를 지운다('문-제-없-음', '문제.없음' → '문제없음'). 띄어쓰기는 남긴다
    spaced = re.sub(r"\s+", " ", re.sub(r"(?<=[가-힣])[^\w\s]+(?=[가-힣])", "", rest)).lower()
    for w, rx, tight in _VERDICT_RE:
        for m in rx.finditer(spaced):
            # 띄어쓰기가 낀 일치는 낱말 첫머리에서 시작할 때만('다시 검사해 결과'의 '해 결'은 우연). '보다…'는 조사라 예외
            if " " not in m.group() or w.startswith("보다") or m.start() == 0 or not spaced[m.start() - 1].isalnum():
                return f"판정처럼 읽힐 수 있는 말이 있음('{w}' — 판정과 수치는 엔진 문장만 씀)"
    return None


def _clean(text: str) -> str:
    t = text.strip().replace("**", "").replace("__", "")
    return re.sub(r"\s*\n+\s*", " ", t)


def explain(b: Brief, mode: str = "template", key: str | None = None, model: str | None = None,
            call: Callable[[dict[str, Any], str], str] | None = None) -> dict[str, Any]:
    """mode: template(엔진 문장만) · preview(보낼 본문을 보여 주기만, 안 보냄) · ai(보내고, 쓸 수 있는 문단이면 엔진 문장 뒤에 붙임).

    text = 엔진 문장(+ AI 문단). template_text = 엔진 문장, ai_text = AI 문단(없으면 null)."""
    if mode not in MODES:
        raise ValueError(f"mode는 {', '.join(MODES)} 중 하나여야 합니다.")
    from . import ai
    t0 = time.perf_counter()
    base = fill(b.template, b.slots)
    out: dict[str, Any] = {
        "schema": SCHEMA, "kind": b.kind, "status": b.status, "source": "template", "text": base,
        "template_text": base, "ai_text": None, "request": None, "sent": False, "model": None, "fallback_ko": None,
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
        out["fallback_ko"] = "AI 키가 없거나 모양이 맞지 않아 엔진 문장만 보여 줍니다(BRCAI에서 키를 확인하세요)."
        return out
    out["sent"] = True
    try:
        raw = (call or ai.call)(body, key)
    except ai.AiUnavailable as e:
        out["fallback_ko"] = e.reason_ko
        return out
    except Exception:   # 어떤 실패도 판정 화면을 깨지 않게 — 엔진 문장만
        out["fallback_ko"] = "AI 응답을 처리하지 못해 엔진 문장만 보여 줍니다."
        return out
    finally:
        out["elapsed_ms"] = round((time.perf_counter() - t0) * 1000)
    text = _clean(raw) if isinstance(raw, str) else ""
    why = check_ai_text(text, b)
    if why:
        out["fallback_ko"] = f"AI 문단을 쓰지 않았습니다 — {why}."
        # 화면에 쓰지 않는 디버그용 — JSON으로 못 보내는 글자(짝 없는 서로게이트 등)는 지운다(서버 500 방지)
        out["rejected_text"] = "".join(ch for ch in text[:MAX_AI_CHARS]
                                       if unicodedata.category(ch) not in ("Cs", "Cc", "Co"))
        return out
    out.update(source="ai", ai_text=text, text=base + " " + text)
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
