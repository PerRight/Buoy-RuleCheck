"""2026-10-26 Buoy UI 표시용 필드(anchor·fix) — 판정과 무관한 표시 값이 판정·제안 문구와 맞는지.

anchor = 부표를 띄울 '고칠 자리'(m), fix = 카드 숫자(현재·기준·차이·제목·조건). 판정(status·actual·limit)은 바꾸지 않는다.
"""
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from garc.engine import evaluate  # noqa: E402
from garc.contract import raw_to_model  # noqa: E402

try:
    import ezdxf  # noqa: F401
    from garc.adapters.dxf import read_dxf
    HAVE_EZDXF = True
except ImportError:
    HAVE_EZDXF = False

SAMPLES = HERE.parent.parent / "samples"


def elems(path):
    rep = evaluate(raw_to_model(read_dxf(path)))
    return {r.rule_id: r for r in rep.results}


@unittest.skipUnless(HAVE_EZDXF, "ezdxf 없음")
class DisplayFields(unittest.TestCase):
    def test_container_fix_matches_design_numbers(self):
        r = elems(SAMPLES / "S140_rev1_deck_containers.dxf")["SOLAS-V-22.1.1"]
        bad = [e for e in r.elements if e["status"] == "fail"]
        self.assertEqual(len(bad), 1)
        fx = bad[0]["fix"]
        self.assertEqual((fx["current"], fx["limit"], fx["delta"], fx["op"]), (24.0, 22.45, 1.55, "≤"))
        self.assertEqual(fx["action_ko"], "상단을 1.55 m 낮추세요")
        self.assertEqual(fx["condition_ko"], "z ≤ 22.45 m 이면 기준 충족")
        self.assertEqual(fx["condition"], r.condition)              # 이 샘플은 최악 조건이 허용 상단도 정함
        self.assertEqual(bad[0]["anchor"], [112.0, 24.0])          # 결정 꼭짓점(컨테이너 앞 윗모서리)

    def test_fixed_container_shows_room(self):
        r = elems(SAMPLES / "S140_v2_fixed.dxf")["SOLAS-V-22.1.1"]
        ok = next(e for e in r.elements if e["anchor"] == [112.0, 21.4])
        self.assertEqual(ok["status"], "pass")
        self.assertEqual(ok["fix"]["action_ko"], "상단 z 21.40 m — 기준 안쪽")
        self.assertEqual(ok["fix"]["condition_ko"], "여유 1.05 m · 기준 z ≤ 22.45 m")

    def test_bulkhead_fix_direction(self):
        r = elems(SAMPLES / "S140_rev2_bulkhead_moved.dxf")["SOLAS-II1-12"]
        e = r.elements[0]
        self.assertEqual(r.status, "fail")
        self.assertEqual(e["fix"]["action_ko"], "격벽을 선수 쪽으로 0.44 m 옮기세요")
        self.assertIn("0.44", r.suggestion_ko)                       # 규칙 제안 문구와 같은 양
        self.assertEqual(e["anchor"], [122.5, 14.5])

    def test_fail_delta_brings_inside(self):
        """'낮추세요' 양만큼 낮추면 기준 안(올림 처리) — 모든 위반 요소에서."""
        for f in ("S140_rev1_deck_containers.dxf", "S140_rev2_bulkhead_moved.dxf", "S140_rev4_three_tiers.dxf"):
            for r in elems(SAMPLES / f).values():
                for e in r.elements:
                    fx = e.get("fix")
                    if not fx or e["status"] != "fail" or fx["op"] != "≤":
                        continue
                    self.assertLessEqual(round(fx["current"] - fx["delta"], 6), fx["limit"], (f, e["element_id"]))

    def test_unknown_elements_have_no_anchor(self):
        """입력 누락으로 판정 못 한 객체에는 부표 자리가 없다(팔레트·로고에서 '확인 필요'로만)."""
        raw = read_dxf(SAMPLES / "S140_rev0_base.dxf")
        raw["props"] = {k: v for k, v in raw["props"].items() if k != "GARC_CONDITIONS"}
        rep = evaluate(raw_to_model(raw))
        vis = next(r for r in rep.results if r.rule_id == "SOLAS-V-22.1.1")
        self.assertIn(vis.status, ("na", "review"))
        self.assertTrue(vis.elements)
        self.assertTrue(all(e["status"] == "unknown" and e["anchor"] is None and e["fix"] is None for e in vis.elements))


if __name__ == "__main__":
    unittest.main()


@unittest.skipUnless(HAVE_EZDXF, "ezdxf 없음")
class CompareDisplayFields(unittest.TestCase):
    """버전 비교 변경 목록의 표시용 필드(title_ko·value_from/to_ko·detail_ko·status_before/after) — 판정·diff와 같은 숫자인지."""

    @classmethod
    def setUpClass(cls):
        from garc.compare import compare_raws
        cls.d = compare_raws(read_dxf(SAMPLES / "S140_v1_review.dxf"), read_dxf(SAMPLES / "S140_v2_fixed.dxf"), ("v1", "v2"))
        cls.by = {c["element_id"]: c for c in cls.d["changes"]}

    def test_modified_obstruction_top(self):
        c = self.by["97"]
        self.assertEqual((c["title_ko"], c["value_from_ko"], c["value_to_ko"]), ("장애물 상단 높이", "z 24.00", "21.40 m"))
        self.assertEqual((c["status_before"], c["status_after"]), ("fail", "pass"))
        self.assertIn("21.40", c["after_ko"])                       # 기존 문구와 같은 숫자

    def test_removed_and_added(self):
        rm, add = self.by["98"], self.by["9C"]
        self.assertEqual((rm["title_ko"], rm["value_from_ko"], rm["value_to_ko"], rm["status_after"]), ("장애물 삭제", "상단 z 25.00 m", "없음", None))
        self.assertEqual((add["title_ko"], add["value_from_ko"], add["value_to_ko"], add["status_before"], add["status_after"]),
                         ("장애물 추가", None, "상단 z 19.00 m", None, "pass"))

    def test_bulkhead_distance_detail(self):
        c = self.by["92"]
        self.assertEqual((c["title_ko"], c["value_from_ko"], c["value_to_ko"]), ("충돌격벽 위치", "x 122.50", "124.50 m"))
        self.assertEqual(c["detail_ko"], "기준점 거리 11.00 → 9.00 m")
        self.assertIn("11.00 m → 9.00 m", c["desc_ko"])                # 기존 설명과 같은 거리

    def test_ship_and_conditions(self):
        from garc.compare import compare_raws
        a = read_dxf(SAMPLES / "S140_v2_fixed.dxf")
        b = read_dxf(SAMPLES / "S140_v2_fixed.dxf")
        b["props"]["GARC_LOA"] = "150"
        b["props"]["GARC_CONDITIONS"] = "밸러스트 도착:6.5/3.0"
        d = compare_raws(a, b, ("A", "B"))
        kinds = {c["kind"]: c for c in d["changes"]}
        self.assertEqual((kinds["ship"]["title_ko"], kinds["ship"]["value_from_ko"], kinds["ship"]["value_to_ko"]), ("선박 정보 LOA", "140 m", "150 m"))
        self.assertEqual(kinds["conditions"]["title_ko"], "흘수 조건")
        self.assertIsNone(kinds["ship"]["status_after"])
