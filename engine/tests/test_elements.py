"""2026-10-18 요소별 판정(elements) 테스트 — 마우스 오버 툴팁·체크리스트의 근거 데이터.

- 요소 판정은 규칙 판정과 같은 계산을 쓴다 → 규칙이 위반이면 위반 요소가 반드시 있고, 충족이면 없다.
- 판정 못 한 규칙의 객체는 '확인 필요(unknown)' — 충족(파랑)으로 보이지 않는다.
- 버전 비교는 변경 항목(추가·삭제·수정·선박 정보·흘수 조건), 해결/새 위반, 표식 위치를 엔진이 계산한다.
"""
import json
import random
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from garc.contract import raw_to_model  # noqa: E402
from garc.engine import evaluate  # noqa: E402

VIS, BHD = "SOLAS-V-22.1.1", "SOLAS-II1-12"
S = 1000.0
PROPS = {"GARC_LOA": "140", "GARC_L_LL": "132", "GARC_X_AP": "0", "GARC_X_FP": "132",
         "GARC_CONDITIONS": "만재 출항:8.0/8.0; 밸러스트 도착:6.5/4.0"}


def box(handle, x0, x1, z0, z1):
    return {"handle": handle, "layer": "GA_OBST", "type": "LWPOLYLINE", "closed": True,
            "points": [[x0 * S, z0 * S], [x0 * S, z1 * S], [x1 * S, z1 * S], [x1 * S, z0 * S]]}


def raw(props=None, extra=(), bhd_x=124.5):
    ents = [
        {"handle": "H1", "layer": "GA_HULL", "type": "LWPOLYLINE",
         "points": [[x * S, z * S] for x, z in [(-3, 4), (-3, 13), (0, 12), (120, 12), (120, 14.5), (137, 14.5), (134, 8), (132, 0), (0, 0)]]},
        {"handle": "E1", "layer": "GA_EYE", "type": "POINT", "points": [[18 * S, 28 * S]]},
        {"handle": "B1", "layer": "GA_BHD_COLL", "type": "LINE", "points": [[bhd_x * S, 0], [bhd_x * S, 14.5 * S]]},
        {"handle": "U1", "layer": "GA_BULB", "type": "LWPOLYLINE", "points": [[132 * S, 1.5 * S], [135 * S, 3 * S], [132 * S, 5 * S]]},
        *extra,
    ]
    return {"schema": "garc.raw/1", "insunits": 4, "props": dict(PROPS if props is None else props), "entities": ents}


def elements(rep, rule_id=None):
    return [e for r in rep.results if rule_id in (None, r.rule_id) for e in r.elements]


def by_id(rep, handle, rule_id):
    return next(e for e in elements(rep, rule_id) if e["element_id"] == handle)


class ElementChecks(unittest.TestCase):
    def test_container_fail_with_guide(self):
        rep = evaluate(raw_to_model(raw(extra=[box("C1", 100, 112, 13.5, 24.0)])))
        c = by_id(rep, "C1", VIS)
        self.assertEqual(c["status"], "fail")
        self.assertIn("22.45", c["required"])          # 데모 제안값과 같은 허용 상단
        self.assertIn("24.00", c["actual"])
        self.assertEqual(c["diff"], "1.55 m 초과")
        self.assertIn("22.45", c["guide"])
        self.assertEqual(c["rule_name"], "선교 전방 수면 가시거리")
        # 조종위치는 판정 기준점 — 위반 개수에 넣지 않는다
        self.assertEqual(by_id(rep, "E1", VIS)["status"], "pass")
        self.assertEqual(by_id(rep, "H1", VIS)["status"], "pass")
        self.assertEqual(by_id(rep, "B1", BHD)["status"], "pass")

    def test_pass_shows_required_and_actual(self):
        rep = evaluate(raw_to_model(raw(extra=[box("C1", 100, 112, 13.5, 21.4)])))
        c = by_id(rep, "C1", VIS)
        self.assertEqual(c["status"], "pass")
        self.assertIn("≤ 22.45", c["required"])
        self.assertIn("여유 1.05", c["actual"])
        self.assertIsNone(c["guide"])

    def test_aft_object_unrelated(self):
        rep = evaluate(raw_to_model(raw(extra=[box("A1", 2, 10, 13, 40)])))   # 선교 뒤 높은 구조
        self.assertEqual(by_id(rep, "A1", VIS)["status"], "pass")
        self.assertIn("선교 뒤", by_id(rep, "A1", VIS)["actual"])

    def test_bulkhead_upper(self):
        rep = evaluate(raw_to_model(raw(bhd_x=122.5)))
        b = by_id(rep, "B1", BHD)
        self.assertEqual((b["status"], b["diff"]), ("fail", "상한보다 0.44 m 김"))
        self.assertIn("6.60~10.56", b["required"])
        self.assertEqual(b["actual"], "11.00 m")

    def test_unknown_when_not_judged(self):
        props = {k: v for k, v in PROPS.items() if k != "GARC_CONDITIONS"}
        rep = evaluate(raw_to_model(raw(props=props, extra=[box("C1", 100, 112, 13.5, 24.0)])))
        vis = elements(rep, VIS)
        self.assertTrue(vis)
        self.assertEqual({e["status"] for e in vis}, {"unknown"})   # 판정 안 된 객체가 충족으로 보이지 않게
        self.assertEqual({e["element_id"] for e in vis}, {"H1", "E1", "C1"})
        # 블록 등 계약 위반(review)도 마찬가지
        ins = {"handle": "I1", "layer": "GA_OBST", "type": "INSERT", "points": [[100 * S, 13 * S]]}
        rep = evaluate(raw_to_model(raw(extra=[ins])))
        self.assertEqual({e["status"] for e in elements(rep, VIS)}, {"unknown"})

    def test_rule_and_elements_agree(self):
        """무작위 장애물 1~3개: 규칙 위반 ⇔ 위반 요소 ≥ 1 (조종위치 제외)."""
        rnd = random.Random(20261018)
        for _ in range(400):
            extra = []
            for i in range(rnd.randint(1, 3)):
                x0 = rnd.uniform(20, 130)
                extra.append(box(f"R{i}", x0, x0 + rnd.uniform(0.5, 8), rnd.uniform(12, 15), rnd.uniform(15, 31)))
            rep = evaluate(raw_to_model(raw(extra=extra)))
            vis = next(r for r in rep.results if r.rule_id == VIS)
            fails = [e for e in vis.elements if e["status"] == "fail"]
            self.assertEqual(vis.status == "fail", bool(fails), (vis.status, vis.message_ko, extra))
            self.assertTrue(all(e["role"] != "eye" for e in fails))
            # 규칙이 지목한 대상(targets)은 모두 위반 요소다
            self.assertTrue(set(vis.targets) <= {e["element_id"] for e in fails})

    def test_json_safe(self):
        tall = box("T1", 100, 112, 13.5, 40.0)   # 눈높이 이상 → 사각 무한대
        rep = evaluate(raw_to_model(raw(extra=[tall])))
        json.dumps(rep.to_dict(), allow_nan=False)
        self.assertEqual(by_id(rep, "T1", VIS)["status"], "fail")


if __name__ == "__main__":
    unittest.main()
