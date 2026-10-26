"""입력 계약(단위·좌표·누락), revision diff, Undo 캐시 동기화, 서버, DXF 경로."""
import importlib.util
import json
import sys
import threading
import unittest
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from brc.contract import raw_to_model, parse_conditions  # noqa: E402
from brc.engine import evaluate  # noqa: E402
from brc.session import Session  # noqa: E402

PROPS = {"BRC_LOA": "140", "BRC_L_LL": "132", "BRC_X_AP": "0", "BRC_X_FP": "132",
         "BRC_CONDITIONS": "만재 출항:8.0/8.0; 밸러스트 도착:6.5/4.0"}


def raw(insunits=4, scale=1000.0, props=None, extra=(), bhd_x=124.5):
    s = scale
    ents = [
        {"handle": "H1", "layer": "GA_HULL", "type": "LWPOLYLINE",
         "points": [[x * s, z * s] for x, z in [(-3, 4), (-3, 13), (0, 12), (120, 12), (120, 14.5), (137, 14.5), (134, 8), (132, 0), (0, 0)]]},
        {"handle": "E1", "layer": "GA_EYE", "type": "POINT", "points": [[18 * s, 28 * s]]},
        {"handle": "B1", "layer": "GA_BHD_COLL", "type": "LINE", "points": [[bhd_x * s, 0], [bhd_x * s, 14.5 * s]]},
        {"handle": "U1", "layer": "GA_BULB", "type": "LWPOLYLINE", "points": [[132 * s, 1.5 * s], [135 * s, 3 * s], [132 * s, 5 * s]]},
        {"handle": "X1", "layer": "0", "type": "LINE", "points": [[0, 0], [1, 1]]},  # 규약 밖 레이어 → 무시
        *extra,
    ]
    return {"schema": "brc.raw/1", "insunits": insunits, "props": dict(props or PROPS), "entities": ents}


STACK = {"handle": "S1", "layer": "GA_OBST", "type": "LWPOLYLINE",
         "points": [[100e3, 13.5e3], [100e3, 24e3], [112e3, 24e3], [112e3, 13.5e3]]}


class Contract(unittest.TestCase):
    def test_mm_and_m_give_same_result(self):
        a = evaluate(raw_to_model(raw(4, 1000.0))).to_dict()
        b = evaluate(raw_to_model(raw(6, 1.0))).to_dict()
        self.assertEqual([r["actual"] for r in a["results"]], [r["actual"] for r in b["results"]])
        self.assertEqual(a["overall"], "pass")

    def test_unknown_units_stop_everything(self):
        rep = evaluate(raw_to_model(raw(0))).to_dict()
        self.assertEqual({r["status"] for r in rep["results"]}, {"review"})
        self.assertEqual(rep["overall"], "incomplete")

    def test_units_override_prop(self):
        m = raw_to_model(raw(0, props={**PROPS, "BRC_UNITS": "mm"}))
        self.assertEqual(m["units_scale"], 0.001)

    def test_wrong_scale_caught_by_loa_check(self):
        # 실제는 m인데 mm로 선언 → 선체 길이 0.14 m ≠ LOA 140 m
        rep = evaluate(raw_to_model(raw(4, 1.0))).to_dict()
        vis = rep["results"][0]
        self.assertEqual(vis["status"], "review")
        self.assertIn("LOA", vis["message_ko"])

    def test_partial_pass_is_not_green(self):
        r = raw()
        r["entities"] = [e for e in r["entities"] if e["layer"] != "GA_EYE"]
        rep = evaluate(raw_to_model(r)).to_dict()
        self.assertEqual(rep["summary"]["pass"], 1)
        self.assertEqual(rep["overall"], "incomplete")

    def test_flipped_drawing(self):
        rep = evaluate(raw_to_model(raw(props={**PROPS, "BRC_X_AP": "132", "BRC_X_FP": "0"}))).to_dict()
        self.assertTrue(all(r["status"] == "review" for r in rep["results"]))

    def test_unsupported_entity(self):
        bad = {"handle": "C1", "layer": "GA_OBST", "type": "CIRCLE", "points": []}
        rep = evaluate(raw_to_model(raw(extra=[bad]))).to_dict()
        self.assertEqual(rep["results"][0]["status"], "review")   # 가시거리
        self.assertEqual(rep["results"][1]["status"], "pass")     # 격벽은 영향 없음

    def test_conditions_parse(self):
        self.assertEqual(parse_conditions("A:1/2; B:3.5/4"),
                         [{"name": "A", "t_aft": 1.0, "t_fwd": 2.0}, {"name": "B", "t_aft": 3.5, "t_fwd": 4.0}])
        rep = evaluate(raw_to_model(raw(props={**PROPS, "BRC_CONDITIONS": "잘못된형식"}))).to_dict()
        self.assertEqual(rep["results"][0]["status"], "review")


class Revisions(unittest.TestCase):
    def test_add_delete_undo_cycle(self):
        s = Session()
        r0 = s.check(raw_to_model(raw()))
        self.assertEqual(r0["overall"], "pass")

        r1 = s.check(raw_to_model(raw(extra=[STACK])))             # add
        self.assertEqual(r1["diff"]["added"], ["S1"])
        self.assertEqual(r1["affected_rules"], ["SOLAS-V-22.1.1"])
        self.assertEqual(r1["cached_rules"], ["SOLAS-II1-12"])
        self.assertEqual(r1["transitions"][0]["kind"], "new_violation")
        self.assertEqual(r1["results"][0]["targets"], ["S1"])

        r2 = s.check(raw_to_model(raw()))                           # delete (= add의 Undo)
        self.assertEqual(r2["diff"]["removed"], ["S1"])
        self.assertEqual(r2["same_as_revision"], 1)
        self.assertEqual(r2["rerun_rules"], [])                     # 전부 캐시
        self.assertEqual(r2["overall"], "pass")
        self.assertEqual(r2["transitions"][0]["kind"], "resolved")

        r3 = s.check(raw_to_model(raw(extra=[STACK])))              # Redo
        self.assertEqual(r3["same_as_revision"], 2)
        self.assertEqual(r3["overall"], "fail")

    def test_move_bulkhead_only_reruns_bulkhead(self):
        s = Session()
        s.check(raw_to_model(raw()))
        r = s.check(raw_to_model(raw(bhd_x=122.5)))
        self.assertEqual(r["diff"]["modified"], ["B1"])
        self.assertEqual(r["rerun_rules"], ["SOLAS-II1-12"])
        self.assertEqual(r["results"][1]["status"], "fail")

    def test_condition_change_reruns_visibility(self):
        s = Session()
        s.check(raw_to_model(raw()))
        r = s.check(raw_to_model(raw(props={**PROPS, "BRC_CONDITIONS": "만재 출항:8.0/8.0"})))
        self.assertTrue(r["diff"]["conditions_changed"])
        self.assertEqual(r["affected_rules"], ["SOLAS-V-22.1.1"])

    def test_no_infinite_in_json(self):
        tall = dict(STACK, points=[[100e3, 13.5e3], [100e3, 40e3], [112e3, 40e3], [112e3, 13.5e3]])
        out = Session().check(raw_to_model(raw(extra=[tall])))
        json.dumps(out, allow_nan=False)  # C# 쪽 JSON 파서가 Infinity를 못 읽으므로
        self.assertEqual(out["results"][0]["status"], "fail")


class Server(unittest.TestCase):
    def test_http_roundtrip(self):
        from http.server import ThreadingHTTPServer
        from brc.server import Handler
        httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        port = httpd.server_address[1]
        threading.Thread(target=httpd.serve_forever, daemon=True).start()
        try:
            base = f"http://127.0.0.1:{port}"
            health = json.loads(urllib.request.urlopen(base + "/v1/health").read())
            self.assertTrue(health["ok"])
            req = urllib.request.Request(base + "/v1/check", method="POST",
                                         data=json.dumps({"doc_id": "t", "raw": raw()}).encode(),
                                         headers={"Content-Type": "application/json"})
            out = json.loads(urllib.request.urlopen(req).read())
            self.assertEqual(out["overall"], "pass")
            self.assertEqual(out["units_scale"], 0.001)
        finally:
            httpd.shutdown()
            httpd.server_close()


@unittest.skipUnless(importlib.util.find_spec("ezdxf"), "ezdxf 없음")
class Dxf(unittest.TestCase):
    def test_samples(self):
        from brc.cli import load_any
        samples = HERE.parents[1] / "samples"
        files = sorted(samples.glob("S140_rev*.dxf"))
        if not files:
            self.skipTest("samples 없음 — python scripts/make_samples.py")
        s = Session()
        overall = [s.check(load_any(str(f)))["overall"] for f in files]
        self.assertEqual(overall, ["pass", "fail", "fail", "fail", "pass"])
        self.assertEqual(s.revisions[3].hashes, s.revisions[1].hashes)  # rev3(Undo) == rev1



class ReportAndOverlays(unittest.TestCase):
    def test_overlays_have_labels_and_band(self):
        out = Session().check(raw_to_model(raw(extra=[STACK])))
        vis, bhd = out["results"]
        kinds_v = {o["kind"] for o in vis["overlays"]}
        kinds_b = {o["kind"] for o in bhd["overlays"]}
        self.assertTrue({"sightline", "waterline", "limit_marker", "label", "tick"} <= kinds_v)
        self.assertTrue({"band_line", "ref_point", "label"} <= kinds_b)
        for o in vis["overlays"] + bhd["overlays"]:
            if o["kind"] == "label":
                self.assertTrue(o["text"] and o["height"] > 0)
        json.dumps(out, allow_nan=False)

    def test_report_html(self):
        from brc.report import build_from_session
        s = Session()
        s.check(raw_to_model(raw()))
        s.check(raw_to_model(raw(extra=[STACK])))
        s.check(raw_to_model(raw()))
        html = build_from_session(s, "S140.dwg")
        for must in ("규칙 후보", "선급 승인", "SOLAS-V-22.1.1", "<svg", "변경 이력", "Undo", "판정식" if False else "min(2·LOA, 500 m)"):
            self.assertIn(must, html)
        self.assertEqual(len(s.history()), 3)

    def test_report_endpoint(self):
        from http.server import ThreadingHTTPServer
        from brc.server import Handler
        httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        port = httpd.server_address[1]
        threading.Thread(target=httpd.serve_forever, daemon=True).start()
        try:
            def post(path, body):
                req = urllib.request.Request(f"http://127.0.0.1:{port}{path}", method="POST",
                                             data=json.dumps(body).encode(), headers={"Content-Type": "application/json"})
                return json.loads(urllib.request.urlopen(req).read())
            post("/v1/check", {"doc_id": "rep", "raw": raw()})
            out = post("/v1/report", {"doc_id": "rep", "title": "t.dwg"})
            self.assertIn("<html", out["html"])
        finally:
            httpd.shutdown()
            httpd.server_close()


if __name__ == "__main__":
    unittest.main()
