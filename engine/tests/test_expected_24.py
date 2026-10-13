"""멘토링 1차 요구: 두 핵심 규칙 × 정상/경계/위반/누락 = 24건 기대값 테스트.

케이스 데이터: cases/expected_24.json (수기 계산 근거 hand_calc 포함)
표 문서: python scripts/render_cases.py → docs/test-cases.md
"""
import json
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from garc.engine import all_rules  # noqa: E402
from garc.contract import validate_model  # noqa: E402

CASES = json.loads((HERE / "cases" / "expected_24.json").read_text(encoding="utf-8"))["cases"]
RULES = {r.id: r for r in all_rules()}


class Expected24(unittest.TestCase):
    def test_case_count_and_balance(self):
        self.assertEqual(len(CASES), 24)
        for rid in RULES:
            cats = [c["category"] for c in CASES if c["rule"] == rid]
            self.assertEqual(len(cats), 12, rid)
            for cat in ("정상", "경계", "위반", "누락"):
                self.assertEqual(cats.count(cat), 3, f"{rid} {cat}")

    def test_cases(self):
        for c in CASES:
            with self.subTest(c["id"]):
                model = json.loads(json.dumps(c["model"]))
                validate_model(model)
                res = RULES[c["rule"]].evaluate(model)
                exp = c["expected"]
                self.assertEqual(res.status, exp["status"], f"{c['id']}: {res.message_ko}")
                if "actual" in exp:
                    if exp["actual"] is None:
                        self.assertIsNone(res.actual)
                    elif isinstance(exp["actual"], list):
                        self.assertEqual(len(res.actual), len(exp["actual"]), c["id"])
                        for a, b in zip(res.actual, exp["actual"]):
                            self.assertAlmostEqual(a, b, places=3)
                    else:
                        self.assertAlmostEqual(res.actual, exp["actual"], places=3)
                if "limit" in exp:
                    if isinstance(exp["limit"], list):
                        self.assertEqual(len(res.limit), len(exp["limit"]), c["id"])
                        for a, b in zip(res.limit, exp["limit"]):
                            self.assertAlmostEqual(a, b, places=3)
                    else:
                        self.assertAlmostEqual(res.limit, exp["limit"], places=3)
                if "condition" in exp:
                    self.assertEqual(res.condition, exp["condition"])
                if "targets" in exp:
                    self.assertEqual(res.targets, exp["targets"])


if __name__ == "__main__":
    unittest.main()
