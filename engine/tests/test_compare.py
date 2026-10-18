"""2026-10-18 요소별 판정(elements)·버전 비교(compare) 테스트.

- 요소 판정은 규칙 판정과 같은 계산을 쓴다 → 규칙이 위반이면 위반 요소가 반드시 있고, 충족이면 없다.
- 판정 못 한 규칙의 객체는 '확인 필요(unknown)' — 충족(파랑)으로 보이지 않는다.
- 버전 비교는 변경 항목(추가·삭제·수정·선박 정보·흘수 조건), 해결/새 위반, 표식 위치를 엔진이 계산한다.
"""
import json
import sys
import threading
import unittest
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from garc.compare import compare_models, compare_raws  # noqa: E402
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


class Compare(unittest.TestCase):
    def v1(self):
        return raw(bhd_x=122.5, extra=[box("C1", 100, 112, 13.5, 24.0), box("K1", 117, 119, 14.5, 25.0)])

    def v2(self):
        return raw(bhd_x=124.5, extra=[box("C1", 100, 112, 13.5, 21.4), box("M1", 126, 127, 14.5, 19.0)])

    def test_demo_v1_to_v2(self):
        out = compare_raws(self.v1(), self.v2(), ("v1", "v2"))
        self.assertEqual(out["violations"], {"before": 3, "after": 0})
        self.assertEqual((out["a"]["overall"], out["b"]["overall"]), ("fail", "pass"))
        kinds = [(c["number"], c["kind"], c["element_id"]) for c in out["changes"]]
        self.assertEqual(kinds, [(1, "modified", "C1"), (2, "removed", "K1"), (3, "modified", "B1"), (4, "added", "M1")])
        c1, k1, b1, m1 = out["changes"]
        self.assertIn("상단 z 24.00 → 21.40 m", c1["desc_ko"])
        self.assertEqual((c1["before_ko"], c1["after_ko"]), ("x 100.0~112.0 m, 상단 z 24.00 m", "x 100.0~112.0 m, 상단 z 21.40 m"))
        self.assertEqual([x["rule_id"] for x in c1["resolved"]], [VIS])
        self.assertIn("삭제", k1["desc_ko"])
        self.assertIn("객체 삭제로", k1["resolved"][0]["text"])
        self.assertEqual(k1["after_ko"], "없음")
        self.assertIn("기준점에서 11.00 m → 9.00 m", b1["desc_ko"])
        self.assertEqual([x["rule_id"] for x in b1["resolved"]], [BHD])
        self.assertEqual((m1["before_ko"], m1["resolved"], m1["introduced"]), ("없음", [], []))
        for c in out["changes"]:
            self.assertEqual(len(c["center"]), 2)
            self.assertEqual(len(c["bbox"]), 4)
        self.assertEqual({x["rule_id"] for x in out["resolved_rules"]}, {VIS, BHD})
        self.assertFalse(out["same"])
        json.dumps(out, allow_nan=False)
        # 형상: 두 버전 모두, 요소 상태 포함
        st_a = {g["handle"]: g["status"] for g in out["geometry"]["a"]}
        self.assertEqual((st_a["C1"], st_a["K1"], st_a["B1"]), ("fail", "fail", "fail"))
        self.assertEqual(len(out["bounds"]), 4)

    def test_reverse_introduces(self):
        out = compare_raws(self.v2(), self.v1())
        self.assertEqual(out["violations"], {"before": 0, "after": 3})
        added = next(c for c in out["changes"] if c["element_id"] == "K1")
        self.assertEqual(added["kind"], "added")
        self.assertEqual([x["rule_id"] for x in added["introduced"]], [VIS])

    def test_same(self):
        out = compare_raws(self.v2(), self.v2())
        self.assertTrue(out["same"])
        self.assertEqual(out["changes"], [])

    def test_ship_and_conditions(self):
        p2 = {**PROPS, "GARC_LOA": "141", "GARC_CONDITIONS": "만재 출항:8.0/8.0"}
        out = compare_raws(raw(), raw(props=p2))
        descs = [c["desc_ko"] for c in out["changes"]]
        self.assertTrue(any("LOA 140 m → 141 m" in d for d in descs), descs)
        cond = next(c for c in out["changes"] if c["kind"] == "conditions")
        self.assertIn("밸러스트 도착", cond["before_ko"])
        self.assertEqual(cond["related_rules"], [VIS])
        self.assertIsNone(cond["center"])
        self.assertEqual(out["changes"][-1]["kind"], "conditions")   # 형상 없는 항목은 끝에

    def test_indirect(self):
        """조종위치를 높이면 바뀌지 않은 컨테이너의 위반이 풀린다 → 간접 영향으로 따로 알린다."""
        a = raw(extra=[box("C1", 100, 112, 13.5, 24.0)])
        b = raw(extra=[box("C1", 100, 112, 13.5, 24.0)])
        b["entities"][1]["points"] = [[18 * S, 34 * S]]
        out = compare_raws(a, b)
        self.assertEqual([c["element_id"] for c in out["changes"]], ["E1"])
        self.assertIn("조종위치", out["changes"][0]["desc_ko"])
        self.assertEqual([(x["element_id"], x["from"], x["to"]) for x in out["indirect"]], [("C1", "fail", "pass")])

    # ── 2026-10-18 검증 지적(HIGH): 판정을 못 하게 된 것을 '위반 해결'로 세면 안 된다 ──
    def assert_not_resolved(self, out):
        texts = [x["text"] for c in out["changes"] for x in c["resolved"]] + [x["text"] for x in out["resolved_rules"]]
        self.assertEqual(texts, [], "판정 불가·적용 제외로 바뀐 것을 '해결'로 셈")
        self.assertFalse(out["same"])

    def test_unsupported_block_is_not_resolution(self):
        """B에 장애물 블록(INSERT)이 들어가 시야 규칙이 '검토 불가'가 되면, A의 위반은 '해결'이 아니라 '확인 필요'."""
        a = raw(extra=[box("C1", 100, 112, 13.5, 24.0)])
        b = raw(extra=[box("C1", 100, 112, 13.5, 24.0),
                       {"handle": "X9", "layer": "GA_OBST", "type": "INSERT", "points": [[60 * S, 15 * S]]}])
        out = compare_raws(a, b)
        self.assert_not_resolved(out)
        self.assertEqual(out["b"]["overall"], "incomplete")
        self.assertIn(VIS, [x["rule_id"] for x in out["needs_check_rules"]])
        self.assertTrue(any(x["element_id"] == "C1" and x["to"] == "unknown" for x in out["indirect"]), out["indirect"])
        self.assertGreater(out["unknown"]["after"], 0)

    def test_loa_typo_is_not_resolution(self):
        """LOA 140 → 14 오입력: 시야 규칙이 판정을 못 하게 되면 '해결'이 아니다(선박 정보 변경에 '확인 필요'를 붙인다)."""
        a = raw(extra=[box("C1", 100, 112, 13.5, 24.0)])
        b = raw(props={**PROPS, "GARC_LOA": "14"}, extra=[box("C1", 100, 112, 13.5, 24.0)])
        out = compare_raws(a, b)
        self.assert_not_resolved(out)
        loa = next(c for c in out["changes"] if c["kind"] == "ship")
        # 충돌격벽 규칙은 LOA를 직접 읽지 않지만 L_LL↔LOA 대조로 막히므로 LOA 변경에 함께 붙는다
        self.assertEqual(sorted(x["rule_id"] for x in loa["needs_check"]), sorted([VIS, BHD]))
        self.assertIn("확인", loa["needs_check"][0]["text"])
        self.assertNotIn("함께 바뀐", loa["needs_check"][0]["text"])

    def test_multi_ship_keys_do_not_guess_cause(self):
        p2 = {**PROPS, "GARC_LOA": "14", "GARC_X_FP": "133", "GARC_KEEL_LAID": "1990-01-01"}
        out = compare_raws(raw(extra=[box("C1", 100, 112, 13.5, 24.0)]), raw(props=p2, extra=[box("C1", 100, 112, 13.5, 24.0)]))
        self.assert_not_resolved(out)
        checks = [x for c in out["changes"] if c["kind"] == "ship" for x in c["needs_check"]]
        self.assertEqual(sorted(x["rule_id"] for x in checks), sorted([VIS, BHD]))   # 규칙마다 한 번
        for x in checks:
            self.assertIn("중 원인 확인", x["text"])

    def test_near_miss_layer_not_same(self):
        """형상 변경이 없어도 비슷한 레이어(GA-OBST)가 생겨 판정이 바뀌면 '같음'이 아니다."""
        a = raw(extra=[box("C1", 100, 112, 13.5, 24.0)])
        b = raw(extra=[box("C1", 100, 112, 13.5, 24.0)])
        b["layer_counts"] = {"GA-OBST": 1}
        out = compare_raws(a, b)
        self.assertEqual(out["changes"], [])
        self.assertNotEqual(out["a"]["overall"], out["b"]["overall"])
        self.assert_not_resolved(out)

    def test_delete_then_unjudgeable_is_not_resolution(self):
        """위반 충돌격벽을 지워 규칙이 '입력 누락'이 되면 삭제로 '해결'이 아니라 '확인 필요'."""
        a = raw(bhd_x=122.5)
        b = raw(bhd_x=122.5)
        b["entities"] = [e for e in b["entities"] if e["handle"] != "B1"]
        out = compare_raws(a, b)
        b1 = next(c for c in out["changes"] if c["element_id"] == "B1")
        self.assertEqual(b1["kind"], "removed")
        self.assertEqual(b1["resolved"], [])
        self.assertEqual([x["rule_id"] for x in b1["needs_check"]], [BHD])
        self.assert_not_resolved(out)
        self.assertTrue(any("입력 누락으로 바뀜" in x["text"] for x in out["needs_check_rules"]), out["needs_check_rules"])

    def test_indirect_unknown_grouped_per_rule(self):
        """LOA 오입력으로 여러 객체가 판정 못 함이 되면 간접 영향은 규칙마다 한 줄."""
        extra = [box(f"C{i}", 60 + i * 8, 66 + i * 8, 13.5, 20.0) for i in range(6)]
        a = raw(extra=extra)
        b = raw(props={**PROPS, "GARC_LOA": "14"}, extra=extra)
        out = compare_raws(a, b)
        vis = [x for x in out["indirect"] if x["rule_id"] == VIS]
        self.assertEqual(len(vis), 1, vis)
        self.assertGreaterEqual(vis[0]["count"], 6)
        self.assertEqual(len(vis[0]["element_ids"]), vis[0]["count"])
        self.assertIn("확인 필요", vis[0]["text"])

    def test_needs_check_key_always_present(self):
        out = compare_raws(self.v1(), self.v2())
        for c in out["changes"]:
            self.assertEqual(c["needs_check"], [])
        self.assertEqual(out["needs_check_rules"], [])
        self.assertEqual(out["unknown"], {"before": 0, "after": 0})

    def test_http(self):
        from http.server import ThreadingHTTPServer
        from garc.server import Handler
        httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        port = httpd.server_address[1]
        threading.Thread(target=httpd.serve_forever, daemon=True).start()
        try:
            def post(body):
                req = urllib.request.Request(f"http://127.0.0.1:{port}/v1/compare", method="POST",
                                             data=json.dumps(body).encode(), headers={"Content-Type": "application/json"})
                try:
                    return 200, json.loads(urllib.request.urlopen(req).read())
                except urllib.error.HTTPError as e:
                    return e.code, json.loads(e.read())
            code, out = post({"a": self.v1(), "b": self.v2(), "labels": ["v1", "v2"]})
            self.assertEqual(code, 200)
            self.assertEqual((out["a"]["label"], out["b"]["label"], len(out["changes"])), ("v1", "v2", 4))
            code, out = post({"a": self.v1()})
            self.assertEqual(code, 400)
        finally:
            httpd.shutdown()
            httpd.server_close()


if __name__ == "__main__":
    unittest.main()
