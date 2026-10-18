"""SOLAS II-1/12 — 충돌격벽(선수격벽) 위치.

원문 12.1: "... located at a distance from the forward perpendicular of not less than 0.05L or 10 m,
whichever is the less, and, except as may be permitted by the Administration, not more than 0.08L
or 0.05L + 3 m, whichever is the greater."
원문 12.3: 수선 아래 선체가 FP 앞으로 연장된 경우(구상선수 등) 거리는 다음 중 '측정값이 가장 작아지는'
기준점에서 잰다 — .1 연장부 길이의 중점, .2 FP 전방 0.015L, .3 FP 전방 3 m.
원문 12.4: 계단·리세스는 위 한계 안에 있으면 허용 → 격벽의 모든 꼭짓점이 범위 안이어야 한다.
L: SOLAS II-1/2 — 만재흘수선협약(ICLL)의 길이. LOA가 아님에 주의.
"""
from __future__ import annotations

import math
from typing import Any

from ..contract import objects_by_role
from ..engine import rule, Rule, RuleResult, EPS, element

LIMITATIONS = [
    "구상선수 연장부는 GA_BULB 윤곽의 최전단으로만 판단 — '수선 아래' 여부는 사람 확인",
    "격벽 수밀 높이(건현갑판까지) 요건 미검토",
    "주관청 허용 예외(상한 초과 허용)는 반영하지 않음",
]


def _ceil_cm(v: float) -> float:
    """'이상 옮기세요' 값은 올림해서 보여 준다(그대로 옮겨도 기준을 넘게). 1e-7 cm 여유는 부동소수점 잡음 흡수용."""
    return math.ceil(v * 100 - 1e-7) / 100


@rule(id="SOLAS-II1-12", title_ko="충돌격벽(선수격벽) 위치",
      ref="SOLAS Ch.II-1 Reg.12.1·12.3·12.4", edition="SOLAS 통합본 (II-1: MSC.216(82) 개정 체계)",
      roles=["collision_bhd", "bulb"], ship_keys=["l_ll", "x_fp"], uses_conditions=False,
      limitations=LIMITATIONS,
      formula_ko="min(0.05L, 10 m) ≤ d ≤ max(0.08L, 0.05L + 3 m),  d = x_ref − x_bhd,  "
                 "x_ref = FP + min(돌출/2, 0.015L, 3 m) (구상선수 있을 때)")
def check_collision_bulkhead(model: dict[str, Any], r: Rule) -> RuleResult:
    ship = model["ship"]
    missing = [k for k in ("l_ll", "x_fp") if ship.get(k) is None]
    if missing:
        return r.result("na", f"선박 정보 누락: {', '.join(missing)} (도면 특성 GARC_L_LL/GARC_X_FP)")
    L, x_fp = ship["l_ll"], ship["x_fp"]

    bhds = objects_by_role(model, "collision_bhd")
    if not bhds:
        return r.result("na", "충돌격벽(GA_BHD_COLL 레이어)이 없습니다.")
    if len(bhds) > 1:
        return r.result("review", f"충돌격벽 객체가 {len(bhds)}개입니다. 계단형이면 폴리선 하나로 그리세요.",
                        targets=[b["handle"] for b in bhds])
    xs = [p[0] for p in bhds[0]["points"]]
    if not xs:
        return r.result("na", "충돌격벽 좌표가 없습니다.")

    # 12.3 기준점
    bulb_x = [p[0] for o in objects_by_role(model, "bulb") for p in o["points"]]
    ext = max(bulb_x) - x_fp if bulb_x else 0.0
    if ext > EPS:
        options = {"연장부 중점": ext / 2.0, "FP 전방 0.015L": 0.015 * L, "FP 전방 3 m": 3.0}
        ref_name, offset = min(options.items(), key=lambda kv: kv[1])
    else:
        options, ref_name, offset = {}, "FP", 0.0
    x_ref = x_fp + offset

    lower = min(0.05 * L, 10.0)
    upper = max(0.08 * L, 0.05 * L + 3.0)
    d_min = x_ref - max(xs)   # 가장 앞쪽 꼭짓점까지 거리
    d_max = x_ref - min(xs)   # 가장 뒤쪽 꼭짓점까지 거리
    if not all(math.isfinite(v) for v in (L, x_fp, x_ref, lower, upper, d_min, d_max)) or L <= 0:
        # 계약 검증(contract.validate_model)을 통과했다면 생기지 않음 — NaN이 '기준 충족'으로 새지 않게 막는 방어선
        return r.result("review", "충돌격벽 계산값이 유한하지 않습니다 — GARC_L_LL·GARC_X_FP·격벽 좌표를 확인하세요.",
                        targets=[bhds[0]["handle"]])
    evidence = {
        "L": L, "x_fp": x_fp, "bulb_extension": round(ext, 3),
        "ref_options": {k: round(v, 3) for k, v in options.items()},
        "ref_point": ref_name, "x_ref": round(x_ref, 3),
        "lower_rule": "min(0.05L, 10 m)", "upper_rule": "max(0.08L, 0.05L + 3 m)",
        "lower": round(lower, 3), "upper": round(upper, 3),
        "d_min": round(d_min, 3), "d_max": round(d_max, 3),
    }
    rng = f"{d_min:.2f} m" if abs(d_max - d_min) < 1e-3 else f"{d_min:.2f}~{d_max:.2f} m"
    base = f"기준점({ref_name})에서 {rng}, 허용 {lower:.2f}~{upper:.2f} m"
    actual = round(d_min, 3) if abs(d_max - d_min) < 1e-3 else [round(d_min, 3), round(d_max, 3)]
    zs = [p[1] for p in bhds[0]["points"]] or [0.0]
    z_lo, z_hi = min(zs), max(zs)
    h = round(max(L * 0.012, 0.5), 3)
    ok = d_min >= lower - EPS and d_max <= upper + EPS
    st = "pass" if ok else "fail"
    x_far, x_near = x_ref - upper, x_ref - lower
    overlays = [
        {"kind": "band_line", "status": "info", "points": [[x_far, z_lo], [x_far, z_hi]]},
        {"kind": "band_line", "status": "info", "points": [[x_near, z_lo], [x_near, z_hi]]},
        {"kind": "band", "status": "info", "points": [[x_far, z_hi], [x_near, z_hi]]},
        {"kind": "label", "status": "info", "points": [[x_far, z_hi + h * 1.5]], "text": f"허용 {lower:.2f}~{upper:.2f} m", "height": h},
        {"kind": "ref_point", "status": "info", "points": [[x_ref, z_lo]]},
        {"kind": "label", "status": "info", "points": [[x_ref, z_lo - h * 2]], "text": f"기준점({ref_name})", "height": h},
        {"kind": "label", "status": st, "points": [[max(xs), (z_lo + z_hi) / 2]], "text": f"d={rng}", "height": h},
    ]
    common = dict(actual=actual, limit=[round(lower, 3), round(upper, 3)],
                  targets=[bhds[0]["handle"]], evidence=evidence, overlays=overlays)
    req = f"기준점({ref_name})에서 {lower:.2f}~{upper:.2f} m"

    def el(status: str, diff: str | None = None, guide: str | None = None, margin: float | None = None):
        return [element(r, bhds[0], status, req, f"{rng}", diff=diff, guide=guide, margin=margin)]

    if ok:   # 통과는 '위반 조건이 아님'이 아니라 '범위 안임'이 참일 때만 (NaN 비교는 항상 거짓)
        common["targets"] = []
        return r.result("pass", base, elements=el("pass", margin=min(d_min - lower, upper - d_max)), **common)
    if (d_max - d_min) > (upper - lower) + EPS:
        # 계단·리세스 폭이 허용 구간보다 넓으면 어느 쪽으로 옮겨도 한쪽이 벗어난다
        sugg = (f"격벽 계단 폭 {d_max - d_min:.2f} m가 허용 구간 폭 {upper - lower:.2f} m보다 넓어 "
                "이동만으로는 해결되지 않습니다. 계단·리세스 폭을 줄이세요.")
        return r.result("fail", base + " — 계단 폭이 허용 구간보다 넓음", suggestion_ko=sugg,
                        elements=el("fail", f"계단 폭 {d_max - d_min:.2f} m > 허용 구간 폭 {upper - lower:.2f} m", sugg),
                        **common)
    if d_min < lower - EPS:
        need = _ceil_cm(lower - d_min)
        sugg = f"격벽을 선미 쪽으로 {need:.2f} m 이상 옮기세요."
        return r.result("fail", base + " — 하한 미달", suggestion_ko=sugg,
                        elements=el("fail", f"하한보다 {lower - d_min:.2f} m 짧음", sugg, d_min - lower), **common)
    need = _ceil_cm(d_max - upper)
    sugg = f"격벽을 선수 쪽으로 {need:.2f} m 이상 옮기세요."
    return r.result("fail", base + " — 상한 초과 (주관청 허용 시 예외 가능 → 사람 확인)", suggestion_ko=sugg,
                    elements=el("fail", f"상한보다 {d_max - upper:.2f} m 김", sugg, upper - d_max), **common)
