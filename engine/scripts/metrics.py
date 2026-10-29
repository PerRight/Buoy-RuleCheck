"""정량지표 자동 측정 (3주차) — 사람이 할 측정과 따로, 코드로 다시 낼 수 있는 숫자만.

    cd engine
    python scripts/metrics.py                 # 결과를 화면에
    python scripts/metrics.py --out ../docs/metrics/자동측정.md

무엇을 재나(무엇을 주장할 수 있나):
1. 개발용 24건 기대값 일치 — '기능이 설계대로 동작한다'는 증거만. 개발 중에 보고 고친 자료라
   판정 정확도(성능)로는 말하지 않는다(2강 기준). 사람 검산(verified_by) 수도 함께 적는다.
2. 어림 검사(baseline)와 비교 — 설계 현장에서 흔히 쓸 만한 단순 검사 두 가지를 같은 입력에 돌려
   엔진이 무엇을 더 잡고, 언제 멈추는지 센다.
     가시거리 어림: "조종위치 눈높이보다 높은 장애물이 있으면 위반"(수면 사각 거리·흘수·트림을 안 봄)
     격벽 어림: "FP에서 격벽까지 0.05L~0.08L이면 문제없음"(10 m 하한·0.05L+3 m 상한·구상선수 기준점을 안 봄)
   어림 검사에 필요한 입력(눈 위치·L·격벽 1개)이 없으면 '입력 부족'으로 비교에서 뺀다.
3. 합성 샘플(S140) 판정·처리 시간 — DXF 읽기와 계약+판정을 나눠 잰다(ezdxf 필요, 없으면 건너뜀).
4. 변경 감지·캐시 — 같은 세션에서 rev0 → rev1 → Undo(rev0과 같은 도면) 순서로 검사할 때
   다시 계산한 규칙·캐시 재사용·시간.

시간은 이 스크립트를 돌린 컴퓨터 기준이다(결과 머리에 환경을 적는다). 설계자의 '판단 완료 시간'은
사람 측정(docs/metrics/측정계획)으로 따로 잰다 — 여기 숫자는 엔진 응답만이다.
"""
from __future__ import annotations

import argparse
import copy
import json
import platform
import statistics
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
ENGINE = HERE.parent
ROOT = ENGINE.parent
sys.path.insert(0, str(ENGINE))

from brc import __version__ as ENGINE_VERSION  # noqa: E402
from brc.contract import objects_by_role, raw_to_model, validate_model  # noqa: E402
from brc.engine import all_rules  # noqa: E402
from brc.session import Session  # noqa: E402

CASES = json.loads((ENGINE / "tests" / "cases" / "expected_24.json").read_text(encoding="utf-8"))["cases"]
RULES = {r.id: r for r in all_rules()}
V_ID, B_ID = "SOLAS-V-22.1.1", "SOLAS-II1-12"
SAMPLES = ["S140_rev0_base", "S140_rev1_deck_containers", "S140_rev2_bulkhead_moved", "S140_rev3_undo",
           "S140_rev4_three_tiers", "S140_v1_review", "S140_v2_fixed"]
KO = {"pass": "문제없음", "fail": "수정 소요", "na": "입력 없음", "review": "판정 멈춤", "skip": "적용 제외",
      None: "입력 부족"}


# ── 1. 개발용 24건 ───────────────────────────────────────────────────────────

def _close(a, b) -> bool:
    if a is None or b is None:
        return a is b
    if isinstance(b, list):
        return isinstance(a, list) and len(a) == len(b) and all(abs(x - y) < 5e-4 for x, y in zip(a, b))
    return abs(a - b) < 5e-4


def check_24() -> dict:
    rows, ok = [], 0
    for c in CASES:
        model = copy.deepcopy(c["model"])
        validate_model(model)
        res = RULES[c["rule"]].evaluate(model)
        exp = c["expected"]
        same = res.status == exp["status"]
        for k in ("actual", "limit"):
            if k in exp:
                same = same and _close(getattr(res, k), exp[k])
        if "condition" in exp:
            same = same and res.condition == exp["condition"]
        if "targets" in exp:
            same = same and res.targets == exp["targets"]
        ok += same
        rows.append({"id": c["id"], "category": c["category"], "expected": exp["status"], "engine": res.status,
                     "same": same})
    verified = sum(1 for c in CASES if c.get("verified_by"))
    return {"total": len(CASES), "same": ok, "verified_by": verified, "rows": rows}


# ── 2. 어림 검사(baseline) ───────────────────────────────────────────────────

def baseline_visibility(model: dict) -> str | None:
    """눈높이보다 높은 장애물이 있으면 fail, 없으면 pass. 눈 위치가 하나로 정해지지 않으면 None(입력 부족)."""
    eyes = [p for o in objects_by_role(model, "eye") for p in o["points"]]
    eh = model.get("ship", {}).get("eye_height")
    if len(eyes) != 1 or eh is None:
        return None
    eye_z = eyes[0][1] + eh
    tops = [p[1] for o in objects_by_role(model, "obstruction") for p in o["points"]]
    return "fail" if tops and max(tops) > eye_z else "pass"


def baseline_bulkhead(model: dict) -> str | None:
    """FP에서 격벽(앞·뒤 꼭짓점 모두)까지 0.05L~0.08L이면 pass. L·FP·격벽 1개가 없으면 None."""
    ship = model.get("ship", {})
    L, x_fp = ship.get("l_ll"), ship.get("x_fp")
    bhds = objects_by_role(model, "collision_bhd")
    if L is None or x_fp is None or len(bhds) != 1 or not bhds[0]["points"]:
        return None
    xs = [p[0] for p in bhds[0]["points"]]
    d_min, d_max = x_fp - max(xs), x_fp - min(xs)
    return "pass" if 0.05 * L - 1e-6 <= d_min and d_max <= 0.08 * L + 1e-6 else "fail"


BASELINES = {V_ID: baseline_visibility, B_ID: baseline_bulkhead}


def compare_kind(engine: str, base: str | None) -> str:
    if base is None:
        return "입력 부족(어림 검사 불가)"
    if engine in ("na", "review"):
        return "엔진은 멈춤, 어림 검사는 판정"
    if engine == "fail" and base == "pass":
        return "어림 검사가 놓친 위반"
    if engine in ("pass", "skip") and base == "fail":
        return "어림 검사의 거짓 경보"
    return "같음"


def baseline_on_cases() -> list[dict]:
    rows = []
    for c in CASES:
        model = copy.deepcopy(c["model"])
        validate_model(model)
        eng = RULES[c["rule"]].evaluate(model).status
        base = BASELINES[c["rule"]](model)
        rows.append({"id": c["id"], "desc": c["description"], "engine": eng, "baseline": base,
                     "kind": compare_kind(eng, base)})
    return rows


# ── 3·4. 합성 샘플 ───────────────────────────────────────────────────────────

def _median_ms(fn, n: int) -> float:
    ts = []
    for _ in range(n):
        t0 = time.perf_counter()
        fn()
        ts.append((time.perf_counter() - t0) * 1000)
    return statistics.median(ts)


def samples(repeat: int) -> dict:
    try:
        from brc.adapters.dxf import read_dxf
    except ImportError as e:   # ezdxf 없음
        return {"skipped": f"DXF 어댑터를 불러오지 못함({e}) — pip install ezdxf"}
    rows, models = [], {}
    for name in SAMPLES:
        path = ROOT / "samples" / f"{name}.dxf"
        if not path.exists():
            continue
        read_ms = _median_ms(lambda: read_dxf(path), max(3, repeat // 4))
        raw = read_dxf(path)
        contract_ms = _median_ms(lambda: raw_to_model(copy.deepcopy(raw)), repeat)
        model = raw_to_model(copy.deepcopy(raw))
        models[name] = model
        judge_ms = _median_ms(lambda: [r.evaluate(model) for r in RULES.values()], repeat)
        res = {rid: RULES[rid].evaluate(model).status for rid in (V_ID, B_ID)}
        rows.append({"name": name, "objects": len(model.get("objects", [])), "read_ms": read_ms,
                     "contract_ms": contract_ms, "judge_ms": judge_ms,
                     "v": res[V_ID], "v_base": baseline_visibility(model),
                     "b": res[B_ID], "b_base": baseline_bulkhead(model)})
    seq = []
    if all(n in models for n in ("S140_rev0_base", "S140_rev1_deck_containers")):
        s = Session()
        order = [("rev0 기본", "S140_rev0_base"), ("rev1 컨테이너 추가", "S140_rev1_deck_containers"),
                 ("Undo(rev0과 같은 도면)", "S140_rev0_base")]
        for label, name in order:
            t0 = time.perf_counter()
            out = s.check(copy.deepcopy(models[name]))
            ms = (time.perf_counter() - t0) * 1000
            seq.append({"step": label, "revision": out["revision"], "overall": out["overall"],
                        "affected": out["affected_rules"], "rerun": out["rerun_rules"], "cached": out["cached_rules"],
                        "same_as": out["same_as_revision"], "ms": ms})
    return {"rows": rows, "sequence": seq}


# ── 출력 ─────────────────────────────────────────────────────────────────────

def _git() -> str:
    try:
        return subprocess.run(["git", "-C", str(ROOT), "rev-parse", "--short", "HEAD"], capture_output=True,
                              text=True, timeout=20).stdout.strip()
    except (OSError, subprocess.SubprocessError):
        return "?"


def render(c24: dict, base: list[dict], smp: dict, repeat: int) -> str:
    L = []
    L.append("# 정량지표 — 자동 측정 결과")
    L.append("")
    L.append(f"`engine/scripts/metrics.py`로 만든 문서다(직접 고치지 말고 다시 실행). "
             f"{datetime.now():%Y-%m-%d %H:%M} · 엔진 {ENGINE_VERSION} · 커밋 {_git()} · "
             f"Python {platform.python_version()} · {platform.system()} {platform.machine()} · 반복 {repeat}회 중앙값")
    L.append("")
    L.append("> 여기 숫자는 **엔진이 설계대로 동작하는지**와 **엔진 응답 시간**이다. 판정 정확도(봉인한 확인용 입력)와 "
             "설계자의 판단 완료 시간(기존 방식과 비교)은 사람 측정으로 따로 낸다 — `docs/metrics/2026-10-29_정량지표_측정계획.md`.")
    L.append("")

    L.append("## 1. 개발용 24건 기대값 일치 (기능 동작 증거)")
    L.append("")
    L.append(f"- 엔진 결과가 기대값과 같은 건: **{c24['same']} / {c24['total']}** (상태·수치·결정 조건·대상 객체)")
    L.append(f"- 사람이 따로 검산한 건(`verified_by`): **{c24['verified_by']} / {c24['total']}**")
    L.append("- 이 24건은 개발 중에 보고 고친 자료다. 발표에서는 '판정 정확도'가 아니라 '설계대로 동작함'으로만 말한다.")
    cats: dict[str, list[int]] = {}
    for r in c24["rows"]:
        cats.setdefault(r["category"], [0, 0])
        cats[r["category"]][0] += r["same"]
        cats[r["category"]][1] += 1
    L.append("- 분류별: " + " · ".join(f"{k} {v[0]}/{v[1]}" for k, v in cats.items()))
    bad = [r["id"] for r in c24["rows"] if not r["same"]]
    if bad:
        L.append(f"- **불일치: {', '.join(bad)}**")
    L.append("")

    L.append("## 2. 어림 검사(baseline)와 비교 — 개발용 24건")
    L.append("")
    L.append("| 어림 검사 | 규칙 |")
    L.append("|---|---|")
    L.append("| 가시거리 어림 | 조종위치 눈높이보다 높은 장애물이 있으면 위반 — 수면까지의 사각 거리·흘수·트림은 안 봄 |")
    L.append("| 격벽 어림 | FP에서 격벽까지 0.05L~0.08L이면 문제없음 — 10 m 하한·0.05L+3 m 상한·구상선수 기준점은 안 봄 |")
    L.append("")
    kinds: dict[str, list[str]] = {}
    for r in base:
        kinds.setdefault(r["kind"], []).append(r["id"])
    order = ["어림 검사가 놓친 위반", "어림 검사의 거짓 경보", "엔진은 멈춤, 어림 검사는 판정", "입력 부족(어림 검사 불가)", "같음"]
    L.append("| 결과 | 건수 | 사례 |")
    L.append("|---|---|---|")
    for k in order:
        ids = kinds.get(k, [])
        L.append(f"| {k} | {len(ids)} | {', '.join(ids) if ids else '—'} |")
    L.append("")
    L.append("다른 사례:")
    L.append("")
    L.append("| 사례 | 내용 | 엔진 | 어림 검사 | 차이 |")
    L.append("|---|---|---|---|---|")
    for r in base:
        if r["kind"] != "같음":
            L.append(f"| {r['id']} | {r['desc']} | {KO[r['engine']]} | {KO[r['baseline']]} | {r['kind']} |")
    L.append("")

    L.append("## 3. 합성 샘플 S140 — 판정과 처리 시간")
    L.append("")
    if "skipped" in smp:
        L.append(f"건너뜀: {smp['skipped']}")
    else:
        L.append("| 샘플 | 객체 | 가시거리 엔진 / 어림 | 격벽 엔진 / 어림 | DXF 읽기 ms | 계약 ms | 판정 ms |")
        L.append("|---|---|---|---|---|---|---|")
        for r in smp["rows"]:
            L.append(f"| {r['name']} | {r['objects']} | {KO[r['v']]} / {KO[r['v_base']]} | {KO[r['b']]} / {KO[r['b_base']]} | "
                     f"{r['read_ms']:.1f} | {r['contract_ms']:.2f} | {r['judge_ms']:.2f} |")
        L.append("")
        L.append("플러그인 경로에서는 DXF 읽기 대신 AutoCAD가 객체를 추출해 보내므로, 엔진 쪽 시간은 계약 + 판정이다. "
                 "실도면(저장소 밖, 장애물 7천~1만여 객체)의 계약 + 판정은 0.3~1.1 초였다(10-18, `docs/review/2026-10-18_실도면연결_검증기록.md`).")
        L.append("")
        L.append("## 4. 변경 감지·캐시 (같은 세션)")
        L.append("")
        L.append("| 단계 | rev | 전체 | 입력이 바뀐 규칙 | 다시 계산 | 캐시 재사용 | 같은 상태 | ms |")
        L.append("|---|---|---|---|---|---|---|---|")
        for r in smp["sequence"]:
            L.append(f"| {r['step']} | {r['revision']} | {r['overall']} | {', '.join(r['affected']) or '—'} | "
                     f"{', '.join(r['rerun']) or '—'} | {', '.join(r['cached']) or '—'} | "
                     f"{('rev ' + str(r['same_as'])) if r['same_as'] else '—'} | {r['ms']:.2f} |")
        L.append("")
    return "\n".join(L) + "\n"


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="정량지표 자동 측정")
    ap.add_argument("--out", help="결과 Markdown 파일")
    ap.add_argument("--repeat", type=int, default=20, help="시간 측정 반복 횟수(중앙값)")
    a = ap.parse_args(argv)
    text = render(check_24(), baseline_on_cases(), samples(a.repeat), a.repeat)
    if a.out:
        Path(a.out).parent.mkdir(parents=True, exist_ok=True)
        Path(a.out).write_text(text, encoding="utf-8")
        print(f"[OK] {a.out}")
    else:
        sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
