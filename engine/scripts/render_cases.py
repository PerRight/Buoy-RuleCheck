"""tests/cases/expected_24.json → docs/test-cases.md (멘토 제출용 24건 기대 결과표).

  python scripts/render_cases.py
케이스를 고치면 JSON만 고치고 이 스크립트로 표를 다시 만든다(표와 테스트가 어긋나지 않게).
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from garc.engine import all_rules, STATUS_KO  # noqa: E402

data = json.loads((ROOT / "tests" / "cases" / "expected_24.json").read_text(encoding="utf-8"))
rules = {r.id: r for r in all_rules()}


def fmt(v):
    if v is None:
        return "—"
    if isinstance(v, list):
        return "~".join(f"{x:g}" for x in v)
    return f"{v:g}" if isinstance(v, (int, float)) else str(v)


lines = ["# 24건 기대 결과표", "",
         "> 자동 생성: `python engine/scripts/render_cases.py` — 원본 `engine/tests/cases/expected_24.json`.",
         "> 엔진 실행 결과(실측)와 기대값이 모두 일치해야 `python -m unittest`가 통과한다.",
         "> **수기 계산 열은 Claude가 작성한 초안**이다. 검증 담당이 손으로 다시 풀고 '검증' 열을 채운다.", ""]
for rid, r in rules.items():
    lines += [f"## {rid} — {r.title_ko}", f"근거: {r.ref} / 판본: {r.edition}", "",
              "| ID | 분류 | 상황 | 기대 판정 | 기대값 [m] | 허용 [m] | 엔진 실측 | 수기 계산 | 검증 |",
              "|---|---|---|---|---|---|---|---|---|"]
    for c in data["cases"]:
        if c["rule"] != rid:
            continue
        m = json.loads(json.dumps(c["model"]))
        from garc.contract import validate_model
        validate_model(m)
        res = r.evaluate(m)
        e = c["expected"]
        ok = "✅" if res.status == e["status"] else "❌"
        lines.append(f"| {c['id']} | {c['category']} | {c['description']} | {STATUS_KO[e['status']]} | "
                     f"{fmt(e.get('actual'))} | {fmt(e.get('limit'))} | {ok} {res.status} {fmt(res.actual)} | "
                     f"{c['hand_calc']} | {c.get('verified_by') or '☐'} |")
    lines.append("")
out = ROOT.parent / "docs" / "test-cases.md"
out.write_text("\n".join(lines), encoding="utf-8")
print("작성:", out)
