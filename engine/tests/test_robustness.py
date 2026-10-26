"""2026-10-12 오류 수정 회귀 테스트 — '모르면 멈춘다'가 깨져 초록으로 새던 경로들.

H1 블록(INSERT)이 장애물·선체·격벽 레이어에 있으면 삽입점 1개로만 판정되던 문제
H2 선박 값 NaN·무한대·0 이하가 '기준 충족'으로 나오던 문제
H3 좌표가 빈 객체를 조용히 무시하던 문제
M2 수치 제안이 최악 조건 하나만 보고 계산돼 다른 조건에서 부족하던 문제
M3 폴리선 호(bulge)를 꼭짓점만 보던 문제
L1 같은 issue가 두 번 들어가던 문제 / L2 리포트 영문 PASS 배지 / L3 서버 Host·Origin 검사
"""
import http.client
import importlib.util
import json
import math
import random
import re
import sys
import tempfile
import threading
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from brc.contract import raw_to_model, validate_model, ContractError  # noqa: E402
from brc.engine import evaluate, all_rules  # noqa: E402
from brc.session import Session  # noqa: E402

RULES = {r.id: r for r in all_rules()}
VIS, BHD = "SOLAS-V-22.1.1", "SOLAS-II1-12"
PROPS = {"BRC_LOA": "140", "BRC_L_LL": "132", "BRC_X_AP": "0", "BRC_X_FP": "132",
         "BRC_CONDITIONS": "만재 출항:8.0/8.0; 밸러스트 도착:6.5/4.0"}
S = 1000.0  # mm


def raw(props=None, extra=(), insunits=4):
    ents = [
        {"handle": "H1", "layer": "GA_HULL", "type": "LWPOLYLINE",
         "points": [[x * S, z * S] for x, z in [(-3, 4), (-3, 13), (0, 12), (120, 12), (120, 14.5), (137, 14.5), (134, 8), (132, 0), (0, 0)]]},
        {"handle": "E1", "layer": "GA_EYE", "type": "POINT", "points": [[18 * S, 28 * S]]},
        {"handle": "B1", "layer": "GA_BHD_COLL", "type": "LINE", "points": [[124.5 * S, 0], [124.5 * S, 14.5 * S]]},
        {"handle": "U1", "layer": "GA_BULB", "type": "LWPOLYLINE", "points": [[132 * S, 1.5 * S], [135 * S, 3 * S], [132 * S, 5 * S]]},
        *extra,
    ]
    return {"schema": "brc.raw/1", "insunits": insunits, "props": dict(props or PROPS), "entities": ents}


def run(r):
    rep = evaluate(raw_to_model(r)).to_dict()
    return rep, {x["rule_id"]: x for x in rep["results"]}


CONTAINER = [[100 * S, 13.5 * S], [100 * S, 24 * S], [112 * S, 24 * S], [112 * S, 13.5 * S]]


class BlockInsert(unittest.TestCase):  # H1
    def test_insert_on_obstruction_is_review_not_pass(self):
        poly = {"handle": "S1", "layer": "GA_OBST", "type": "LWPOLYLINE", "points": CONTAINER}
        block = {"handle": "S1", "layer": "GA_OBST", "type": "INSERT", "points": [CONTAINER[0]]}
        self.assertEqual(run(raw(extra=[poly]))[1][VIS]["status"], "fail")
        rep, res = run(raw(extra=[block]))
        self.assertEqual(res[VIS]["status"], "review")
        self.assertIn("EXPLODE", res[VIS]["message_ko"])
        self.assertEqual(res[BHD]["status"], "pass")       # 격벽 규칙과 무관
        self.assertEqual(rep["overall"], "incomplete")     # 초록 아님

    def test_insert_on_other_roles(self):
        for layer, rule in (("GA_HULL", VIS), ("GA_BHD_COLL", BHD), ("GA_BULB", BHD)):
            ent = {"handle": "K1", "layer": layer, "type": "INSERT", "points": [[50 * S, 5 * S]]}
            with self.subTest(layer):
                self.assertEqual(run(raw(extra=[ent]))[1][rule]["status"], "review")

    def test_insert_on_eye_still_allowed(self):
        r = raw()
        r["entities"][1] = {"handle": "E1", "layer": "GA_EYE", "type": "INSERT", "points": [[18 * S, 28 * S]]}
        rep, res = run(r)
        self.assertEqual(rep["overall"], "pass")


class NonFiniteShipValues(unittest.TestCase):  # H2
    def test_bad_values_never_pass(self):
        cases = [("BRC_L_LL", v) for v in ("nan", "inf", "-inf", "0", "-120")]
        cases += [("BRC_X_FP", "nan"), ("BRC_X_AP", "inf"), ("BRC_LOA", "nan"), ("BRC_LOA", "0"),
                  ("BRC_Z_BASE", "nan"), ("BRC_EYE_HEIGHT", "-1"), ("BRC_EYE_HEIGHT", "nan")]
        for key, v in cases:
            with self.subTest(f"{key}={v}"):
                rep, res = run(raw(props={**PROPS, key: v}))
                self.assertNotEqual(rep["overall"], "pass")
                self.assertTrue(all(x["status"] == "review" for x in rep["results"]), rep["results"])
                self.assertIn(key, res[VIS]["message_ko"])
                json.dumps(rep, allow_nan=False)   # C# 파서가 NaN/Infinity를 못 읽음

    def test_model_path_also_checked(self):
        model = raw_to_model(raw())
        model["ship"]["l_ll"] = float("nan")
        model["issues"] = []
        rep = evaluate(model).to_dict()
        self.assertEqual(rep["overall"], "incomplete")

    def test_bulkhead_defence_line_without_contract(self):
        # 계약 검증을 거치지 않고 규칙 함수만 불러도 NaN이 '기준 충족'으로 새지 않아야 한다
        from brc.rules.collision_bulkhead import check_collision_bulkhead
        model = {"ship": {"l_ll": float("nan"), "x_fp": 120.0}, "conditions": [], "issues": [],
                 "objects": [{"handle": "B1", "role": "collision_bhd", "points": [[112, 0], [112, 15]]}]}
        self.assertEqual(check_collision_bulkhead(model, RULES[BHD]).status, "review")
        model["ship"] = {"l_ll": 120.0, "x_fp": float("nan")}
        self.assertEqual(check_collision_bulkhead(model, RULES[BHD]).status, "review")

    def test_nan_draft_is_review(self):
        rep, res = run(raw(props={**PROPS, "BRC_CONDITIONS": "A:nan/4"}))
        self.assertEqual(res[VIS]["status"], "review")


class EmptyGeometry(unittest.TestCase):  # H3
    def test_empty_points_are_review(self):
        for layer, rule in (("GA_OBST", VIS), ("GA_HULL", VIS), ("GA_BHD_COLL", BHD), ("GA_BULB", BHD), ("GA_EYE", VIS)):
            ent = {"handle": "Z1", "layer": layer, "type": "POLYLINE", "points": []}
            with self.subTest(layer):
                rep, res = run(raw(extra=[ent]))
                self.assertEqual(res[rule]["status"], "review")
                self.assertNotEqual(rep["overall"], "pass")

    def test_unknown_units_do_not_add_empty_noise(self):
        m = raw_to_model(raw(insunits=0))
        self.assertEqual([i["code"] for i in m["issues"]], ["UNIT_UNKNOWN"])


class IssueDedup(unittest.TestCase):  # L1
    def test_condition_issue_once(self):
        m = raw_to_model(raw(props={**PROPS, "BRC_CONDITIONS": "A:-1/4"}))
        validate_model(m)
        out = Session().check(m)
        self.assertEqual([i["code"] for i in m["issues"]].count("CONDITION_RANGE"), 1)
        self.assertEqual(out["results"][0]["message_ko"].count("t_aft"), 1)

    def test_same_snapshot_hits_cache(self):
        s = Session()
        s.check(raw_to_model(raw(props={**PROPS, "BRC_CONDITIONS": "A:-1/4"})))
        out = s.check(raw_to_model(raw(props={**PROPS, "BRC_CONDITIONS": "A:-1/4"})))
        self.assertEqual(out["rerun_rules"], [])
        self.assertEqual(out["same_as_revision"], 1)


def _two_condition_model(top):
    model = {
        "ship": {"loa": 100, "x_ap": 0, "x_fp": 95, "z_base": 0, "eye_height": 1.8, "keel_laid": "2026-03-01"},
        "conditions": [{"name": "W", "t_aft": 8.5, "t_fwd": 6.6}, {"name": "C", "t_aft": 2.0, "t_fwd": 2.0}],
        "objects": [
            {"handle": "H1", "role": "hull", "points": [[0, 0], [0, 14], [100, 14], [100, 0]]},
            {"handle": "E1", "role": "eye", "points": [[10, 20.2]]},
            {"handle": "O1", "role": "obstruction", "points": [[64, 12], [64, top], [68, top], [68, 12]]},
        ],
    }
    validate_model(model)
    return model


class SuggestionAllConditions(unittest.TestCase):  # M2
    def test_suggested_height_passes_every_condition(self):
        r = RULES[VIS].evaluate(_two_condition_model(18.52))
        self.assertEqual(r.status, "fail")
        self.assertEqual(r.condition, "W")                                   # 사각 거리 최악은 W
        self.assertEqual(r.evidence["suggestion_governing_condition"], "C")  # 허용 상단은 C가 더 엄격
        zmax = r.evidence["max_obstacle_z_at_critical_x"]
        self.assertAlmostEqual(zmax, 18.0, places=3)                         # 손계산: 22 − 58·20/290
        self.assertIn("z=18.00", r.suggestion_ko)
        after = RULES[VIS].evaluate(_two_condition_model(zmax - 0.001))
        self.assertEqual(after.status, "pass", after.message_ko)

    def test_other_violating_objects_are_reported(self):
        model = _two_condition_model(18.52)
        model["objects"].append({"handle": "O2", "role": "obstruction",
                                 "points": [[80, 12], [80, 17.5], [84, 17.5], [84, 12]]})
        validate_model(model)
        r = RULES[VIS].evaluate(model)
        self.assertEqual(r.status, "fail")
        self.assertEqual(r.targets, ["O1", "O2"])   # 결정 장애물 먼저, 그다음 단독 위반 객체
        self.assertIn("다른 객체", r.suggestion_ko)

    def test_demo_number_unchanged(self):
        # 본선 데모 문구 "x=112 m 장애물 상단을 z=22.45 m 이하로"가 그대로인지
        rep, res = run(raw(extra=[{"handle": "S1", "layer": "GA_OBST", "type": "LWPOLYLINE", "points": CONTAINER}]))
        self.assertIn("x=112.00", res[VIS]["suggestion_ko"])
        self.assertIn("z=22.45", res[VIS]["suggestion_ko"])
        self.assertEqual(res[VIS]["targets"], ["S1"])


class BulgeArcs(unittest.TestCase):  # M3 (+ 1차 검증 N6: 외접 꺾은선으로 안전 쪽 근사)
    TOP = [[64, 12], [64, 18], [68, 18], [68, 12]]
    PROPS = {"BRC_LOA": "100", "BRC_X_AP": "0", "BRC_X_FP": "95", "BRC_CONDITIONS": "만재:6/6"}
    TOL = 1e-4   # contract.ARC_TOL_M (도면 단위 m)

    def _raw(self, obst):
        return {"schema": "brc.raw/1", "insunits": 6, "props": self.PROPS, "entities": [
            {"handle": "H1", "layer": "GA_HULL", "type": "LWPOLYLINE", "points": [[0, 0], [0, 14], [100, 14], [100, 0]]},
            {"handle": "E1", "layer": "GA_EYE", "type": "POINT", "points": [[10, 20.2]]},
            {"handle": "O1", "layer": "GA_OBST", "type": "LWPOLYLINE", **obst}]}

    def test_arc_direction_and_outside(self):
        up = raw_to_model(self._raw({"points": self.TOP, "bulges": [0, -1, 0, 0]}))
        down = raw_to_model(self._raw({"points": self.TOP, "bulges": [0, 1, 0, 0]}))
        zu = max(p[1] for p in up["objects"][2]["points"])
        zd = min(p[1] for p in down["objects"][2]["points"] if 64 < p[0] < 68)
        self.assertTrue(20.0 <= zu <= 20.0 + self.TOL, zu)      # 반원(bulge −1), +x 진행 → 위로 볼록
        self.assertTrue(16.0 - self.TOL <= zd <= 16.0, zd)      # bulge +1 → 아래로 볼록(반시계)
        for p in up["objects"][2]["points"]:                    # 분할점은 원 바깥 0~0.1 mm (외접)
            if 64 < p[0] < 68:
                self.assertTrue(2.0 <= math.hypot(p[0] - 66, p[1] - 18) <= 2.0 + self.TOL + 1e-12)

    def test_arc_top_changes_verdict(self):
        flat = run(self._raw({"points": self.TOP}))[1][VIS]
        arc = run(self._raw({"points": self.TOP, "bulges": [0, -1, 0, 0]}))[1][VIS]
        self.assertEqual(flat["status"], "pass")       # 꼭짓점만: D = 142 m
        self.assertEqual(arc["status"], "fail")
        # 손계산(정확값): 눈 (10, 22)에서 원(중심 (66, 18), r=2)에 그은 위쪽 접선이 수면 z=6과 만나는 점
        #   기울기 = tan(atan2(−4, 56) + asin(2/√3152)) → D = 358.2854 m. 외접 근사는 D ≥ 정확값(안전 쪽).
        exact = 10 + (6 - 22) / math.tan(math.atan2(-4, 56) + math.asin(2 / math.hypot(56, 4))) - 100
        self.assertGreaterEqual(arc["actual"], exact - 1e-6)
        self.assertLess(arc["actual"], exact + 0.05)

    def test_shallow_large_radius_arc_is_conservative(self):
        # 1차 검증 N6 재현: 현 50 m, bulge −0.002(위로 볼록, 새지타 50 mm)인 얕은 호. 예전 1° 분할은 내부 점이 없어 통과로 오판.
        slope = -0.0782
        for z0 in (26.47, 26.50, 26.55):
            pts = [[60 * S, z0 * S], [110 * S, (z0 + slope * 50) * S]]
            ent = {"handle": "O1", "layer": "GA_OBST", "type": "LWPOLYLINE", "points": pts, "bulges": [-0.002, 0]}
            res = run(raw(extra=[ent]))[1][VIS]
            exact = _dense_blind_distance(raw(extra=[ent]))
            with self.subTest(z0=z0):
                self.assertGreaterEqual(res["actual"], exact - 1e-6)          # 작게 보지 않음
                self.assertLess(res["actual"], exact + 0.05)
                self.assertEqual(res["status"], "fail" if exact > 280 + 1e-6 else res["status"])

    def test_closed_polyline_closing_arc(self):
        m = raw_to_model(self._raw({"points": [[64, 18], [68, 18]], "bulges": [0, 1], "closed": True}))
        pts = m["objects"][2]["points"]
        self.assertEqual(pts[0], pts[-1])
        self.assertTrue(20.0 <= max(p[1] for p in pts) <= 20.0 + self.TOL)   # 68→64 구간 bulge +1 → 위로

    def test_point_budget(self):
        # 극단 입력(모든 꼭짓점에 거의 원 한 바퀴인 호)은 응답 지연 대신 형식 오류로 멈춘다
        ents = [{"handle": f"{i + 100:X}", "layer": "GA_OBST", "type": "LWPOLYLINE",
                 "points": [[60 + k * 0.8, 15 + (k % 2)] for k in range(40)], "bulges": [1e3] * 40, "closed": True}
                for i in range(40)]
        r = self._raw({"points": self.TOP})
        r["entities"] += ents
        with self.assertRaises(ContractError):
            raw_to_model(r)

    def test_bad_bulges_rejected(self):
        with self.assertRaises(ContractError):
            raw_to_model(self._raw({"points": self.TOP, "bulges": [0, 1]}))
        with self.assertRaises(ContractError):
            raw_to_model(self._raw({"points": self.TOP, "bulges": [0, float("nan"), 0, 0]}))


def _dense_blind_distance(r, n=200_000):
    """엔진과 독립적으로: 호를 촘촘히(n점) 펼쳐 S140 조건의 사각 거리를 직접 계산(검산용)."""
    props = r["props"]
    conds = [c.split(":") for c in props["BRC_CONDITIONS"].split(";")]
    conds = [(float(v.split("/")[0]), float(v.split("/")[1])) for _, v in conds]
    x_ap, x_fp = float(props["BRC_X_AP"]), float(props["BRC_X_FP"])
    pts = []
    for e in r["entities"]:
        q = [[x / S, z / S] for x, z in e["points"]]
        if e["layer"] == "GA_EYE":
            eye = (q[0][0], q[0][1] + 1.8)
            continue
        pts += q
        b = (e.get("bulges") or [0])[0]
        if b:
            (x0, z0), (x1, z1) = q[0], q[1]
            c = math.hypot(x1 - x0, z1 - z0)
            th = 4 * math.atan(b)
            rad = c / (2 * math.sin(th / 2))
            mx, mz = (x0 + x1) / 2, (z0 + z1) / 2
            ux, uz = (x1 - x0) / c, (z1 - z0) / c
            cx, cz = mx - uz * rad * math.cos(th / 2), mz + ux * rad * math.cos(th / 2)
            a0 = math.atan2(z0 - cz, x0 - cx)
            pts += [[cx + abs(rad) * math.cos(a0 + th * k / n), cz + abs(rad) * math.sin(a0 + th * k / n)] for k in range(n + 1)]
    hull_x = [x / S for x, _ in r["entities"][0]["points"]]
    x_bow = max(hull_x)
    worst = 0.0
    for ta, tf in conds:
        sl = (tf - ta) / (x_fp - x_ap)
        a = ta - sl * x_ap
        for x, z in pts:
            if x <= eye[0] or z <= a + sl * x:
                continue
            s_ = (z - eye[1]) / (x - eye[0])
            xh = (a - eye[1] + s_ * eye[0]) / (s_ - sl)
            worst = max(worst, xh - x_bow)
    return worst


class ServerOrigin(unittest.TestCase):  # L3
    def test_host_and_origin_checks(self):
        from http.server import ThreadingHTTPServer
        from brc.server import Handler
        httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        port = httpd.server_address[1]
        threading.Thread(target=httpd.serve_forever, daemon=True).start()

        def call(method, path, headers, body=None):
            c = http.client.HTTPConnection("127.0.0.1", port, timeout=5)
            c.putrequest(method, path, skip_host=True)
            for k, v in headers.items():
                c.putheader(k, v)
            data = json.dumps(body).encode() if body is not None else b""
            c.putheader("Content-Length", str(len(data)))
            c.endheaders(data)
            st = c.getresponse().status
            c.close()
            return st
        try:
            ok = {"Host": f"127.0.0.1:{port}"}
            self.assertEqual(call("GET", "/v1/health", ok), 200)
            self.assertEqual(call("GET", "/v1/health", {"Host": f"localhost:{port}"}), 200)
            self.assertEqual(call("GET", "/v1/health", {"Host": "evil.example:8765"}), 403)
            for h in ("[::1]x", "localhost:80@evil.com", "127.0.0.1.evil.com", "localhost.", "127.0.0.2:8765", ""):
                self.assertEqual(call("GET", "/v1/health", {"Host": h}), 403, h)
            self.assertEqual(call("GET", "/v1/health", {"Host": "[::1]:8765"}), 200)
            self.assertEqual(call("GET", "/v1/health", {"Host": "LOCALHOST"}), 200)
            self.assertEqual(call("GET", "/v1/health", {**ok, "Origin": "https://evil.example"}), 403)
            self.assertEqual(call("POST", "/v1/check", {**ok, "Origin": "null"}, {"doc_id": "x", "raw": raw()}), 403)
            self.assertEqual(call("POST", "/v1/check", ok, {"doc_id": "x", "raw": raw()}), 200)
            bad = {"doc_id": "y", "raw": raw(extra=[{"handle": "O9", "layer": "GA_OBST", "type": "LWPOLYLINE",
                                                     "points": [[0, 0], [1, 1]], "bulges": [0]}])}
            self.assertEqual(call("POST", "/v1/check", ok, bad), 400)
        finally:
            httpd.shutdown()
            httpd.server_close()


class ReportWording(unittest.TestCase):  # L2
    def test_no_english_pass_badge(self):
        from brc.report import build_from_session
        s = Session()
        s.check(raw_to_model(raw()))
        html = build_from_session(s, "S140.dwg")
        self.assertNotIn(">PASS<", html)
        self.assertIn(">문제없음<", html)                          # 통과는 '문제없음'(2026-10-26 Buoy UI 명세 3절)
        for word in ("적합", "GA RuleCheck", "GA-RuleCheck"):
            self.assertNotIn(word, html)
        self.assertEqual(html.count("승인"), html.count("승인이 아니"))   # '승인'은 '선급 승인이 아니며' 고지에만


class FirstReviewFindings(unittest.TestCase):
    """Sonnet 1차 검증(2026-10-12)에서 나온 N1~N10 회귀."""

    def test_n1_loa_typo_is_review_not_skip(self):
        for loa in ("14", "54", "1400"):
            with self.subTest(loa=loa):
                props = {k: v for k, v in PROPS.items() if k != "BRC_L_LL"}   # L 대조(SHIP_VALUE_MISMATCH)는 빼고 LOA만
                rep, res = run(raw(props={**props, "BRC_LOA": loa}))
                self.assertEqual(res[VIS]["status"], "review", res[VIS]["message_ko"])
                self.assertIn("LOA", res[VIS]["message_ko"])
                self.assertNotEqual(rep["overall"], "pass")

    def test_n2_eye_height_sanity(self):
        for v in ("1800", "18", "5.01"):
            with self.subTest(v=v):
                rep, res = run(raw(props={**PROPS, "BRC_EYE_HEIGHT": v}))
                self.assertEqual(res[VIS]["status"], "review")
                self.assertIn("BRC_EYE_HEIGHT", res[VIS]["message_ko"])
        self.assertEqual(run(raw(props={**PROPS, "BRC_EYE_HEIGHT": "5"}))[0]["overall"], "pass")
        self.assertEqual(run(raw(props={**PROPS, "BRC_EYE_HEIGHT": "0"}))[0]["overall"], "pass")

    def test_n2_l_ll_vs_loa(self):
        for v in ("1320", "13.2", "60", "144"):
            with self.subTest(v=v):
                rep, res = run(raw(props={**PROPS, "BRC_L_LL": v}))
                self.assertEqual(res[BHD]["status"], "review", res[BHD]["message_ko"])
                self.assertIn("SHIP_VALUE_MISMATCH", res[BHD]["evidence"]["issues"])
        for v in ("132", "140", "70"):
            with self.subTest(ok=v):
                self.assertNotEqual(run(raw(props={**PROPS, "BRC_L_LL": v}))[1][BHD]["status"], "review")

    def test_n2_perpendiculars_outside_hull(self):
        rep, res = run(raw(props={**PROPS, "BRC_X_AP": "100", "BRC_X_FP": "232"}))
        self.assertEqual(res[VIS]["status"], "review")
        self.assertIn("AP", res[VIS]["message_ko"])

    def test_n3_eye_must_be_single_point(self):
        for ent in ({"handle": "E1", "layer": "GA_EYE", "type": "LINE", "points": [[18 * S, 28 * S], [20 * S, 28 * S]]},
                    {"handle": "E1", "layer": "GA_EYE", "type": "LWPOLYLINE", "points": [[18 * S, 28 * S], [18 * S, 30 * S]]}):
            r = raw()
            r["entities"][1] = ent
            with self.subTest(ent["type"]):
                rep, res = run(r)
                self.assertEqual(res[VIS]["status"], "review")
                self.assertIn("점 1개", res[VIS]["message_ko"])

    def test_n4_infinite_case_independent_of_vertex_order(self):
        z = []
        for pts in ([[64, 12], [64, 40], [68, 40], [68, 12]], [[68, 12], [68, 40], [64, 40], [64, 12]]):
            m = _two_condition_model(18.52)
            m["objects"][2]["points"] = pts
            r = RULES[VIS].evaluate(m)
            self.assertIsNone(r.actual)                       # 눈높이 이상 → 무한대
            zs = float(re.search(r"z=([0-9.]+) m 이하", r.suggestion_ko).group(1))
            z.append(zs)
            after = _two_condition_model(18.52)
            after["objects"][2]["points"] = [[x, min(zz, zs)] for x, zz in pts]
            self.assertEqual(RULES[VIS].evaluate(after).status, "pass")
        self.assertEqual(z[0], z[1])

    def test_n5_displayed_suggestion_is_sufficient(self):
        rng = random.Random(20261012)
        checked = 0
        for _ in range(400):
            ox, top = rng.uniform(30, 90), rng.uniform(16, 23)
            m = _two_condition_model(18.0)
            m["objects"][2]["points"] = [[ox, 12], [ox, top]]
            r = RULES[VIS].evaluate(m)
            if r.status != "fail" or r.evidence.get("other_violating_handles"):
                continue
            zs = float(re.search(r"z=([0-9.]+) m 이하", r.suggestion_ko).group(1))
            self.assertLessEqual(zs, r.evidence["max_obstacle_z_at_critical_x"] + 5e-4)
            after = _two_condition_model(18.0)
            after["objects"][2]["points"] = [[ox, 12], [ox, min(top, zs)]]
            self.assertEqual(RULES[VIS].evaluate(after).status, "pass", (ox, top, zs))
            checked += 1
        self.assertGreater(checked, 50)

    def test_n5_bulkhead_move_is_rounded_up(self):
        model = {"ship": {"l_ll": 120.0, "x_fp": 120.0}, "conditions": [],
                 "objects": [{"handle": "B1", "role": "collision_bhd", "points": [[114.004, 0], [114.004, 15]]}]}
        validate_model(model)
        r = RULES[BHD].evaluate(model)
        self.assertEqual(r.status, "fail")
        self.assertIn("0.01 m 이상", r.suggestion_ko)     # 필요 0.004 m → '0.00'이 아니라 '0.01'

    def test_n7_model_path_insert(self):
        m = raw_to_model(raw())
        m["objects"].append({"handle": "K1", "layer": "GA_OBST", "role": "obstruction", "type": "INSERT", "points": [[100, 13.5]]})
        m["issues"] = []
        rep = evaluate(m).to_dict()
        self.assertEqual(rep["results"][0]["status"], "review")

    def test_n8_nonfinite_payload_not_encoded(self):
        from brc.server import encode_payload
        self.assertIsNone(encode_payload({"x": float("inf")}))
        self.assertIsNone(encode_payload({"x": [float("nan")]}))
        self.assertIsNotNone(encode_payload({"x": 1.5}))

    def test_n10_eye_ahead_of_bow_and_layer_spaces(self):
        r = raw()
        r["entities"][1] = {"handle": "E1", "layer": "GA_EYE", "type": "POINT", "points": [[137 * S, 28 * S]]}
        self.assertEqual(run(r)[1][VIS]["status"], "review")
        spaced = {"handle": "S1", "layer": " ga_obst ", "type": "LWPOLYLINE", "points": CONTAINER}
        self.assertEqual(run(raw(extra=[spaced]))[1][VIS]["status"], "fail")


class SecondReviewFindings(unittest.TestCase):
    """Sonnet 2차 검증(2026-10-12)에서 나온 F1~F12 + 유사 레이어 이름 회귀."""

    def _eye(self, x, z):
        r = raw()
        r["entities"][1] = {"handle": "E1", "layer": "GA_EYE", "type": "POINT", "points": [[x * S, z * S]]}
        return r

    def test_f1_eye_position_typos(self):
        for x, z in ((18, 280), (18, 1e6), (-50, 28)):
            with self.subTest(x=x, z=z):
                r = self._eye(x, z)
                r["entities"].append({"handle": "S1", "layer": "GA_OBST", "type": "LWPOLYLINE", "points": CONTAINER})
                rep, res = run(r)
                self.assertEqual(res[VIS]["status"], "review", res[VIS]["message_ko"])
                self.assertNotEqual(rep["overall"], "pass")
        self.assertEqual(run(self._eye(18, 28))[0]["overall"], "pass")   # 정상 위치는 그대로

    def test_f2_draft_and_baseline_typos(self):
        for props in ({"BRC_CONDITIONS": "만재:18/18"}, {"BRC_CONDITIONS": "만재:25/25; 밸러스트:6.5/4"},
                      {"BRC_Z_BASE": "10"}, {"BRC_Z_BASE": "14"}):
            with self.subTest(props=props):
                rep, res = run(raw(props={**PROPS, **props}))
                self.assertEqual(res[VIS]["status"], "review", res[VIS]["message_ko"])
        # 정상 범위(오타로 판단할 수 없는 값)는 막지 않는다
        for props in ({"BRC_CONDITIONS": "만재:9/9"}, {"BRC_Z_BASE": "2"}, {"BRC_Z_BASE": "-1"}):
            with self.subTest(ok=props):
                self.assertNotEqual(run(raw(props={**PROPS, **props}))[1][VIS]["status"], "review")

    def test_f3_keel_laid(self):
        for v in ("1926-03-02", "0001-01-01", "20260302", "2026-W10-1", "2026-3-2", "2026-13-01"):
            with self.subTest(v=v):
                rep, res = run(raw(props={**PROPS, "BRC_KEEL_LAID": v}))
                self.assertEqual(res[VIS]["status"], "review")
                self.assertNotEqual(rep["overall"], "pass")
        rep, res = run(raw(props={**PROPS, "BRC_KEEL_LAID": "1990-01-01"}))
        self.assertEqual(res[VIS]["status"], "skip")
        self.assertEqual(rep["overall"], "pass")
        self.assertIn("적용 제외 1건", rep["overall_ko"])          # 초록이어도 적용 제외가 있다는 사실을 남김
        self.assertEqual(run(raw(props={**PROPS, "BRC_KEEL_LAID": "2026-03-02"}))[1][VIS]["status"], "pass")

    def test_f5_stepped_bulkhead_too_wide(self):
        model = {"ship": {"l_ll": 132.0, "x_fp": 132.0}, "conditions": [],
                 "objects": [{"handle": "B1", "role": "collision_bhd",
                              "points": [[127, 0], [127, 6], [120, 6], [120, 14]]}]}   # d = 5 ~ 12 m, 허용 폭 3.96 m
        validate_model(model)
        r = RULES[BHD].evaluate(model)
        self.assertEqual(r.status, "fail")
        self.assertIn("이동만으로는 해결되지 않습니다", r.suggestion_ko)

    def test_f8_tiny_bulge_is_straight(self):
        e = {"handle": "O1", "layer": "GA_OBST", "type": "LWPOLYLINE",
             "points": [[100 * S, 13.5 * S], [112 * S, 13.5 * S]], "bulges": [1e-10, 0]}
        m = raw_to_model(raw(extra=[e]))
        self.assertEqual(len([o for o in m["objects"] if o["handle"] == "O1"][0]["points"]), 2)

    def test_f10_strict_numbers_and_duplicate_keys(self):
        for v in ("1_40", "１４０", "140,0", "nan", "infinity", "0x8C"):
            with self.subTest(v=v):
                rep, res = run(raw(props={**PROPS, "BRC_LOA": v}))
                self.assertEqual(res[VIS]["status"], "review")
        for v in ("140", " 140 ", "1.4e2", "+140", "140."):
            with self.subTest(ok=v):
                self.assertEqual(run(raw(props={**PROPS, "BRC_LOA": v}))[0]["overall"], "pass")
        rep, res = run(raw(props={**PROPS, "brc_loa": "14"}))
        self.assertEqual(res[BHD]["status"], "review")
        self.assertIn("PROP_DUPLICATE", res[BHD]["evidence"]["issues"])
        self.assertEqual(run(raw(props={**PROPS, "brc_loa": "140"}))[0]["overall"], "pass")   # 같은 값이면 문제 없음
        rep, res = run(raw(props={**PROPS, "BRC_CONDITIONS": "만재:8_0/8"}))
        self.assertEqual(res[VIS]["status"], "review")

    def test_f12_suggestion_stays_below_eye(self):
        rng = random.Random(7)
        checked = 0
        for _ in range(300):
            loa = rng.uniform(150, 280)
            x_fp = loa * 0.95
            t_aft, t_fwd = rng.uniform(1, 4), rng.uniform(5, 9)     # 큰 선수 트림 포함
            ex, deck = loa * rng.uniform(0.05, 0.3), rng.uniform(12, 22)
            ox, top = rng.uniform(ex + 5, loa * 0.9), rng.uniform(10, 30)
            m = {"ship": {"loa": loa, "x_ap": 0, "x_fp": x_fp, "z_base": 0, "eye_height": 1.8},
                 "conditions": [{"name": "B", "t_aft": t_aft, "t_fwd": t_fwd}],
                 "objects": [{"handle": "H1", "role": "hull", "points": [[0, 0], [0, 12], [loa, 12], [loa, 0]]},
                             {"handle": "E1", "role": "eye", "points": [[ex, deck]]},
                             {"handle": "O1", "role": "obstruction", "points": [[ox, 11], [ox, top]]}]}
            validate_model(m)
            r = RULES[VIS].evaluate(m)
            if r.status != "fail" or r.evidence.get("other_violating_handles"):
                continue
            zs = float(re.search(r"z=([0-9.]+) m 이하", r.suggestion_ko).group(1))
            self.assertLess(zs, deck + 1.8)
            m["objects"][2]["points"] = [[ox, 11], [ox, min(top, zs)]]
            self.assertEqual(RULES[VIS].evaluate(m).status, "pass", (loa, t_aft, t_fwd, ex, deck, ox, top, zs))
            checked += 1
        self.assertGreater(checked, 20)

    def test_near_miss_layer_names(self):
        for name in ("GA-OBST", "GA OBST", "ga_obst_crane", "GA_HULL2", "GA_EYE_OLD"):
            with self.subTest(name=name):
                r = raw()
                r["layer_counts"] = {name: 3, "0": 50}
                rep, res = run(r)
                self.assertEqual(rep["overall"], "incomplete")
                self.assertIn("LAYER_NEAR_MISS", [i for x in rep["results"] for i in x["evidence"].get("issues", [])])
        for counts in ({"GA_REF": 5, "0": 9}, {"GA-OBST": 0}, {"GA_OBST": 3}, {" GA_OBST ": 2}):
            with self.subTest(ok=counts):
                r = raw()
                r["layer_counts"] = counts
                self.assertEqual(run(r)[0]["overall"], "pass")


class ThirdReviewFindings(unittest.TestCase):
    """Sonnet 3차 검증(2026-10-12) N1~N8 회귀."""

    def _hull(self, pts):
        r = raw()
        r["entities"][0] = {"handle": "H1", "layer": "GA_HULL", "type": "LWPOLYLINE", "points": [[x * S, z * S] for x, z in pts]}
        return r

    def test_n1_raked_keel_not_blocked(self):
        # 선미 스케그가 기선 아래 4 m (중앙부 용골은 기선 위)
        skeg = [(-3, 0), (-3, 13), (0, 12), (120, 12), (120, 14.5), (137, 14.5), (134, 8), (132, 0), (0, 0), (-2.5, -4)]
        self.assertEqual(run(self._hull(skeg))[0]["overall"], "pass")
        # 용골 전체가 선미 −4 m → FP 0 m로 기울어진 선형
        raked = [(-3, 4), (-3, 13), (0, 12), (120, 12), (120, 14.5), (137, 14.5), (134, 8), (132, 0), (0, -4)]
        self.assertEqual(run(self._hull(raked))[0]["overall"], "pass")
        # 평평한 용골에서 기선 오타(3 m)는 여전히 멈춤
        self.assertEqual(run(raw(props={**PROPS, "BRC_Z_BASE": "3"}))[1][VIS]["status"], "review")

    def test_n3_near_miss_variants(self):
        from brc.contract import near_miss_layer
        flagged = ["GA_0BST", "GA_BHD", "GA_OBS", "GA_HUL", "ＧＡ＿ＯＢＳＴ", "GA_ОBST", "GA-OBST", "GA_OBST_2",
                   "GA_OBSTRUCTION", "GA__OBST", "ga.obst", "GA_EYE2", "GA_BULBOUS", "GA_BHD_COLLISION"]
        for n in flagged:
            with self.subTest(flag=n):
                self.assertIsNotNone(near_miss_layer(n))
        for n in ("GA_OBST", " ga_obst ", "GA_REF", "GA_DIM", "GA_TEXT", "GA_CL", "GA_O", "0", "DEFPOINTS", "장애물"):
            with self.subTest(ok=n):
                self.assertIsNone(near_miss_layer(n))

    def test_n4_layer_counts_robustness(self):
        for v in ("abc", None, float("inf"), "3", 2.5):
            with self.subTest(v=v):
                r = raw()
                r["layer_counts"] = {"GA-OBST": v}
                self.assertEqual(run(r)[0]["overall"], "incomplete")
        r = raw()
        r["layer_counts"] = {"GA-OBST": 0}
        self.assertEqual(run(r)[0]["overall"], "pass")
        r["layer_counts"] = ["GA-OBST"]
        with self.assertRaises(ContractError):
            raw_to_model(r)

    def test_n5_report_shows_skip_note(self):
        from brc.report import build_from_session
        s = Session()
        s.check(raw_to_model(raw(props={**PROPS, "BRC_KEEL_LAID": "1990-01-01"})))
        self.assertIn("적용 제외 1건", build_from_session(s, "t.dwg"))


class FourthReviewFindings(unittest.TestCase):
    """Sonnet 4차 재검증(2026-10-12) N-1~N-5 회귀."""

    def test_baseline_check_with_gapped_bottom(self):
        # 선체 바닥선이 LINE 두 개로 나뉘고 중앙에 7 m 틈 — 한 점이 갑판선으로 튀어도 기선 오타(3 m)는 멈춰야 한다
        r = raw(props={**PROPS, "BRC_Z_BASE": "3"})
        r["entities"][0] = {"handle": "H1", "layer": "GA_HULL", "type": "LWPOLYLINE",
                            "points": [[x * S, z * S] for x, z in [(-3, 4), (-3, 13), (0, 12), (120, 12), (120, 14.5), (137, 14.5), (134, 8), (132, 0)]]}
        r["entities"].append({"handle": "H2", "layer": "GA_HULL", "type": "LINE", "points": [[132 * S, 0], [70 * S, 0]]})
        r["entities"].append({"handle": "H3", "layer": "GA_HULL", "type": "LINE", "points": [[63 * S, 0], [0, 0]]})
        self.assertEqual(run(r)[1][VIS]["status"], "review")
        r["props"]["BRC_Z_BASE"] = "0"
        self.assertEqual(run(r)[0]["overall"], "pass")

    def test_huge_layer_count(self):
        r = raw()
        r["layer_counts"] = {"GA-OBST": 10 ** 400}
        self.assertEqual(run(r)[0]["overall"], "incomplete")

    def test_edit_rule_needs_ga_prefix(self):
        from brc.contract import near_miss_layer
        for n in ("A-HULL", "A-OBST", "A-BULB", "GB_OBST"):
            with self.subTest(ok=n):
                self.assertIsNone(near_miss_layer(n))
        for n in ("GA_0BST", "GA_FULL", "GA_ОBST"):
            with self.subTest(flag=n):
                self.assertIsNotNone(near_miss_layer(n))


@unittest.skipUnless(importlib.util.find_spec("ezdxf"), "ezdxf 없음")
class DxfAdapter(unittest.TestCase):
    def test_bulge_ocs_spline_and_unsupported(self):
        import ezdxf
        from brc.adapters.dxf import read_dxf
        doc = ezdxf.new("R2018")
        doc.header["$INSUNITS"] = 6
        msp = doc.modelspace()
        msp.add_lwpolyline([(64, 18, 0, 0, -1), (68, 18)], format="xyseb", dxfattribs={"layer": "GA_OBST"})
        msp.add_lwpolyline([(0, 0), (2, 0)], dxfattribs={"layer": "GA_HULL", "extrusion": (0, 0, -1)})
        pl = msp.add_polyline2d([(0, 0), (5, 5), (10, 0)], dxfattribs={"layer": "GA_BULB"})
        pl.vertices[1].dxf.flags |= 16   # 스플라인 제어점
        doc.blocks.new("X")
        msp.add_blockref("X", (5, 6), dxfattribs={"layer": "GA_EYE", "extrusion": (0, 0, -1)})
        msp.add_polyface(dxfattribs={"layer": "GA_OBST"}).append_face([(0, 0, 0), (1, 0, 0), (1, 1, 0)])
        with tempfile.TemporaryDirectory() as d:
            f = Path(d) / "t.dxf"
            doc.saveas(f)
            raw_ = read_dxf(f)
        ents = {e["layer"] + ":" + e["type"]: e for e in raw_["entities"]}
        lw = ents["GA_OBST:LWPOLYLINE"]
        self.assertEqual(lw["bulges"], [-1.0, 0.0])
        self.assertFalse(lw["closed"])
        self.assertEqual(ents["GA_HULL:LWPOLYLINE"]["points"], [[0.0, 0.0], [-2.0, 0.0]])   # OCS −Z → WCS
        self.assertEqual(ents["GA_BULB:POLYLINE"]["points"], [[0.0, 0.0], [10.0, 0.0]])      # 제어점 제외
        self.assertEqual(ents["GA_EYE:INSERT"]["points"], [[-5.0, 6.0]])
        self.assertIn("GA_OBST:POLYFACEMESH", ents)
        m = raw_to_model(raw_)
        self.assertIn("ENTITY_UNSUPPORTED", [i["code"] for i in m["issues"]])
        self.assertEqual(raw_["layer_counts"]["GA_OBST"], 2)

    def test_layer_spaces_and_counts(self):
        import ezdxf
        from brc.adapters.dxf import read_dxf
        doc = ezdxf.new("R2018")
        doc.header["$INSUNITS"] = 4
        for k, v in PROPS.items():
            doc.header.custom_vars.append(k, v)
        msp = doc.modelspace()
        for e in raw()["entities"]:
            if e["type"] == "POINT":
                msp.add_point(e["points"][0], dxfattribs={"layer": e["layer"]})
            else:
                msp.add_lwpolyline(e["points"], dxfattribs={"layer": e["layer"]})
        msp.add_lwpolyline(CONTAINER, dxfattribs={"layer": "GA_OBST "})          # 뒤 공백
        msp.add_line((0, 0), (1, 1), dxfattribs={"layer": "GA-OBST"})           # 비슷한 이름
        with tempfile.TemporaryDirectory() as d:
            f = Path(d) / "t.dxf"
            doc.saveas(f)
            raw_ = read_dxf(f)
        rep = evaluate(raw_to_model(raw_)).to_dict()
        vis = rep["results"][0]
        self.assertEqual(vis["status"], "review")                  # GA-OBST 때문에 멈춤
        self.assertIn("GA-OBST", vis["message_ko"])
        raw_["layer_counts"].pop("GA-OBST")
        rep = evaluate(raw_to_model(raw_)).to_dict()
        self.assertEqual(rep["results"][0]["status"], "fail")      # 'GA_OBST ' 컨테이너는 읽힘 → 위반


class CliEncoding(unittest.TestCase):
    """CLI가 cp949 stdout(한국어 Windows 기본 콘솔·파이프·리다이렉트)에서 '—'·'→'·'⚠' 때문에 죽던 문제."""

    def test_check_survives_cp949_stdout(self):
        import os
        import subprocess
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "model.json"
            p.write_text(json.dumps(raw()), encoding="utf-8")
            env = dict(os.environ, PYTHONIOENCODING="cp949", PYTHONPATH=str(HERE.parent))
            env.pop("PYTHONUTF8", None)
            r = subprocess.run([sys.executable, "-m", "garc", "check", str(p)],
                               capture_output=True, env=env, cwd=td)
        err = r.stderr.decode("cp949", errors="replace")
        self.assertNotIn("UnicodeEncodeError", err)
        self.assertNotIn("Traceback", err)
        self.assertEqual(r.returncode, 0, err)                     # raw()는 pass → exit 0
        self.assertIn("전체", r.stdout.decode("cp949", errors="replace"))


if __name__ == "__main__":
    unittest.main()
