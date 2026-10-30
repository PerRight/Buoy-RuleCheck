"""생성형 AI 설명(빈칸 방식) — 보낼 본문에 도면 내용이 없는지, 기본 설명이 엔진 값을 그대로 쓰는지,
AI 답 검사·기본 설명으로 돌아가기, 외부 호출(가짜 응답), 서버 경로."""
from __future__ import annotations

import copy
import io
import json
import os
import random
import re
import sys
import threading
import unittest
import urllib.error
import urllib.request
from pathlib import Path
from unittest import mock

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from brc import ai, explain as ex  # noqa: E402
from brc.compare import compare_raws  # noqa: E402
from brc.engine import evaluate  # noqa: E402

VIS, BHD = "SOLAS-V-22.1.1", "SOLAS-II1-12"
CASES = json.loads((HERE / "cases" / "expected_24.json").read_text(encoding="utf-8"))["cases"]
SAMPLES = sorted((HERE.parent.parent / "samples").glob("S140_*.dxf"))
ALL_LITERALS = sorted({x for forms in ex.REF_FORMS.values() for x in forms}, key=len, reverse=True)

sys.path.insert(0, str(HERE))
from test_compare import raw, box  # noqa: E402


def sent_text(body: dict) -> str:
    """밖으로 나가는 글자(지시 + 사용자 내용). 모델 이름·max_tokens는 고정값이라 뺀다."""
    return body["system"] + "\n" + "\n".join(m["content"] for m in body["messages"])


def digits_left(text: str) -> list[str]:
    for lit in ALL_LITERALS:
        text = text.replace(lit, " ")
    return re.findall(r"\S*\d\S*", text)


def renamed(model: dict, rnd: random.Random) -> dict:
    """이름만 바꾼 모델(흘수 조건 이름·handle·레이어·블록) — 판정은 같아야 하고, 보낼 본문은 글자 하나 다르지 않아야 한다."""
    m = copy.deepcopy(model)
    hmap = {}
    for o in m["objects"]:
        hmap[o["handle"]] = "Z" + "".join(rnd.choice("QWXYZ") for _ in range(6))
        o["handle"] = hmap[o["handle"]]
        if "layer" in o:
            o["layer"] = "LAYER_" + "".join(rnd.choice("QWXYZ") for _ in range(5))
        if o.get("block"):
            o["block"] = "BLK_" + "".join(rnd.choice("QWXYZ") for _ in range(5))
    names = {}
    for c in m["conditions"]:
        names[c["name"]] = "비밀조건" + "".join(rnd.choice("가나다라마") for _ in range(4))
        c["name"] = names[c["name"]]
    return m


class Payload(unittest.TestCase):
    """보낼 본문 = 정해진 어휘 + 빈칸 이름. 숫자(조항 번호 말고)·handle·조건 이름이 없다."""

    def _bodies(self, model):
        rep = evaluate(model).to_dict()
        return rep, [ex.request_body(ex.rule_brief(r), "claude-haiku-5-5") for r in rep["results"]]

    def test_24_cases_no_digits_names_handles(self):
        for c in CASES:
            with self.subTest(case=c["id"]):
                rep, bodies = self._bodies(c["model"])
                for body in bodies:
                    t = sent_text(body)
                    self.assertEqual(digits_left(t), [], f"{c['id']} 숫자")
                    user = body["messages"][0]["content"]
                    for o in c["model"]["objects"]:
                        if len(o["handle"]) >= 2:
                            self.assertNotIn(o["handle"], user, f"{c['id']} handle")
                    for cond in c["model"]["conditions"]:
                        self.assertNotIn(f"'{cond['name']}'", user)
                    self.assertNotIn("temperature", body)

    def test_renaming_does_not_change_what_is_sent(self):
        rnd = random.Random(20261030)
        for c in CASES:
            with self.subTest(case=c["id"]):
                rep, bodies = self._bodies(c["model"])
                m2 = renamed(c["model"], rnd)
                rep2, bodies2 = self._bodies(m2)
                self.assertEqual([r["status"] for r in rep["results"]], [r["status"] for r in rep2["results"]])
                self.assertEqual(bodies, bodies2)

    def test_samples_dxf(self):
        try:
            import ezdxf  # noqa: F401
        except ImportError:
            self.skipTest("ezdxf 없음")
        from brc.cli import load_any
        self.assertGreater(len(SAMPLES), 5)
        for p in SAMPLES:
            with self.subTest(sample=p.name):
                model = load_any(str(p))
                rep, bodies = self._bodies(model)
                for body in bodies:
                    self.assertEqual(digits_left(sent_text(body)), [])
                    for cond in model["conditions"]:
                        self.assertNotIn(cond["name"], body["messages"][0]["content"])

    def test_random_drawings(self):
        """좌표·흘수·이름을 무작위로 바꾼 도면 300장 — 어떤 판정이든 보낼 본문에 숫자가 없다."""
        rnd = random.Random(7)
        statuses = set()
        for i in range(300):
            c = copy.deepcopy(rnd.choice(CASES)["model"])
            amp = rnd.choice((0.3, 2.0, 8.0))
            for o in c["objects"]:
                o["points"] = [[x + rnd.uniform(-amp, amp), z + rnd.uniform(-amp / 4, amp / 4)] for x, z in o["points"]]
            for cond in c["conditions"]:
                cond["t_aft"] = round(max(0.2, cond["t_aft"] + rnd.uniform(-1, 1)), 3)
                cond["t_fwd"] = round(max(0.2, cond["t_fwd"] + rnd.uniform(-1, 1)), 3)
                cond["name"] = f"조건{rnd.randint(100, 999)}"
            try:
                rep, bodies = self._bodies(c)
            except Exception:   # 계약 검증에서 막히는 입력은 여기서 다루지 않는다
                continue
            for r, body in zip(rep["results"], bodies):
                statuses.add((r["rule_id"], r["status"]))
                self.assertEqual(digits_left(sent_text(body)), [], f"#{i} {r['rule_id']} {r['status']}")
        self.assertTrue({(VIS, "pass"), (VIS, "fail"), (BHD, "pass"), (BHD, "fail")} <= statuses, statuses)

    def test_compare_payload(self):
        a = raw(bhd_x=122.5, extra=[box("C1", 100, 112, 13.5, 24.0), box("K1", 117, 119, 14.5, 25.0)])
        b = raw(bhd_x=124.5, extra=[box("C1", 100, 112, 13.5, 21.4), box("M1", 126, 127, 14.5, 19.0)])
        for e in a["entities"] + b["entities"]:
            e["block"] = "SECRET_CRANE"   # 블록 이름이 제목에 들어가도(묶음) 보낼 본문에는 없어야 한다
        cmp = compare_raws(a, b, ("비밀버전A", "비밀버전B"))
        body = ex.request_body(ex.change_brief(cmp), "m")
        t = sent_text(body)
        self.assertEqual(digits_left(t), [])
        for word in ("비밀버전", "SECRET", "C1", "K1", "M1", "밸러스트 도착", "만재 출항"):
            self.assertNotIn(word, t)


class Template(unittest.TestCase):
    def test_all_cases_filled_and_engine_numbers(self):
        for c in CASES:
            with self.subTest(case=c["id"]):
                rep = evaluate(c["model"]).to_dict()
                for r in rep["results"]:
                    out = ex.explain(ex.rule_brief(r))
                    self.assertNotIn("{", out["text"])
                    self.assertEqual(out["source"], "template")
                    self.assertIsNone(out["request"])
                    self.assertTrue(out["text"].startswith(ex.STATUS_KO[r["status"]]))
                    if r["status"] == "fail" and r["rule_id"] == VIS and r["evidence"].get("suggestion_z") is not None:
                        z = f"z = {r['evidence']['suggestion_z']:.2f} m 이하"
                        self.assertIn(z, out["text"])
                        self.assertIn(f"z={r['evidence']['suggestion_z']:.2f}", r["suggestion_ko"])   # 엔진 제안과 같은 값
                    if r["status"] == "fail" and r["rule_id"] == BHD:
                        m = re.search(r"([\d.]+) m 이상 옮기세요|폭을 ([\d.]+) m 줄이세요", r["elements"][0]["fix"]["action_ko"]) \
                            or re.search(r"([\d.]+) m", r["suggestion_ko"])
                        self.assertIsNotNone(m)

    def test_demo_rev1(self):
        try:
            import ezdxf  # noqa: F401
        except ImportError:
            self.skipTest("ezdxf 없음")
        from brc.cli import load_any
        rep = evaluate(load_any(str(HERE.parent.parent / "samples" / "S140_rev1_deck_containers.dxf"))).to_dict()
        out = ex.explain_rule(rep, VIS)
        self.assertIn("z = 22.45 m 이하", out["text"])
        self.assertIn("x = 112.00 m", out["text"])
        self.assertIn("'밸러스트 도착'", out["text"])

    def test_change_template(self):
        a = raw(bhd_x=122.5, extra=[box("C1", 100, 112, 13.5, 24.0), box("K1", 117, 119, 14.5, 25.0)])
        b = raw(bhd_x=124.5, extra=[box("C1", 100, 112, 13.5, 21.4), box("M1", 126, 127, 14.5, 19.0)])
        out = ex.explain_change(compare_raws(a, b, ("v1", "v2")))
        self.assertIn("v1 → v2", out["text"])
        self.assertIn("3건에서 0건으로 줄었습니다", out["text"])
        self.assertIn("해결", out["text"])
        same = ex.explain_change(compare_raws(a, a, ("v1", "v1b")))
        self.assertIn("모두 같습니다", same["text"])

    def test_bad_mode(self):
        with self.assertRaises(ValueError):
            ex.explain(ex.rule_brief(evaluate(CASES[0]["model"]).to_dict()["results"][0]), mode="send")


def fail_brief():
    rep = evaluate(next(c for c in CASES if c["expected"]["status"] == "fail" and c["rule"] == VIS)["model"]).to_dict()
    return ex.rule_brief(next(r for r in rep["results"] if r["rule_id"] == VIS))


class Guard(unittest.TestCase):
    def setUp(self):
        self.b = fail_brief()
        self.ok = ("{판정}: 선교에서 본 선수 앞 수면의 사각 거리가 {사각거리}로 허용 한계 {한도}를 넘습니다. "
                   "SOLAS Ch.V Reg.22.1.1 기준이며, {제안위치} 지점 상단을 {제안높이}로 낮추면 기준 안에 듭니다.")

    def test_accepts_slots_and_ref(self):
        self.assertIsNone(ex.check_ai_text(self.ok, self.b))

    def test_rejects(self):
        bad = {
            "숫자": self.ok + " 약 3 m 낮추세요.",
            "전각 숫자": self.ok + " ３ 미만.",
            "동그라미 숫자": self.ok + " ① 위치.",
            "모르는 빈칸": self.ok + " {선명} 선박.",
            "깨진 빈칸": self.ok + " {한도",
            "쓰면 안 되는 말": self.ok + " 선급 승인 대상입니다.",
            "적합": self.ok.replace("넘습니다", "넘지만 적합합니다"),
            "반대 판정": self.ok + " 따라서 문제없음입니다.",
            "단위 글": self.ok + " 이 미터 정도 낮추세요.",
            "링크": self.ok + " http://x.example",
            "너무 짧음": "{판정}",
            "너무 긺": self.ok * 10,
        }
        for name, text in bad.items():
            with self.subTest(name=name):
                self.assertIsNotNone(ex.check_ai_text(text, self.b))

    def test_status_words_by_status(self):
        rep = evaluate(next(c for c in CASES if c["expected"]["status"] == "na")["model"]).to_dict()
        r = next(x for x in rep["results"] if x["status"] == "na")
        b = ex.rule_brief(r)
        self.assertIsNotNone(ex.check_ai_text("{판정}: 입력이 없어 판정하지 않았지만 수정 소요로 보입니다. {멈춤사유}", b))
        self.assertIsNone(ex.check_ai_text("{판정}: 엔진은 입력이 없어 판정하지 않았습니다. {멈춤사유} 빠진 값을 넣고 다시 검사하세요.", b))


class AiPath(unittest.TestCase):
    def setUp(self):
        self.b = fail_brief()
        self.good = ("{판정}: 사각 거리 {사각거리}가 허용 한계 {한도}를 넘습니다. "
                     "{제안위치} 지점 상단을 {제안높이}로 낮추세요. 근거는 SOLAS Ch.V Reg.22.1.1 입니다.")

    def test_preview_does_not_send(self):
        called = []
        out = ex.explain(self.b, "preview", key="k" * 30, call=lambda *a: called.append(a))
        self.assertEqual(called, [])
        self.assertFalse(out["sent"])
        self.assertEqual(out["source"], "template")
        self.assertIsNotNone(out["request"])

    def test_no_key_falls_back(self):
        out = ex.explain(self.b, "ai", key=None, call=lambda *a: self.fail("보내면 안 됨"))
        self.assertEqual((out["source"], out["sent"]), ("template", False))
        self.assertIn("키", out["fallback_ko"])

    def test_good_answer_is_filled_locally(self):
        seen = {}

        def fake(body, key):
            seen["body"], seen["key"] = body, key
            return self.good
        out = ex.explain(self.b, "ai", key="sk-test-" + "x" * 30, call=fake)
        self.assertEqual(out["source"], "ai")
        self.assertTrue(out["sent"])
        self.assertNotIn("{", out["text"])
        self.assertIn(self.b.slots["사각거리"], out["text"])
        self.assertEqual(seen["body"], out["request"])
        self.assertEqual(digits_left(sent_text(seen["body"])), [])

    def test_bad_answer_falls_back(self):
        out = ex.explain(self.b, "ai", key="k" * 30, call=lambda b, k: self.good + " 약 2.5 m 낮추면 됩니다.")
        self.assertEqual(out["source"], "template")
        self.assertIn("숫자", out["fallback_ko"])
        self.assertIsNotNone(out["rejected_text"])
        self.assertEqual(out["text"], out["template_text"])

    def test_service_error_falls_back(self):
        def boom(b, k):
            raise ai.AiUnavailable("인터넷에 연결되지 않아 기본 설명을 보여 줍니다.")
        out = ex.explain(self.b, "ai", key="k" * 30, call=boom)
        self.assertEqual(out["source"], "template")
        self.assertTrue(out["sent"])
        self.assertIn("인터넷", out["fallback_ko"])


class _Resp(io.BytesIO):
    def __enter__(self):
        return self

    def __exit__(self, *a):
        self.close()


class _FakeOpener:
    def __init__(self, result=None, exc=None):
        self.result, self.exc, self.reqs = result, exc, []

    def open(self, req, timeout=None):
        self.reqs.append(req)
        if self.exc:
            raise self.exc
        return _Resp(json.dumps(self.result).encode())


class Client(unittest.TestCase):
    def test_fixed_endpoint_and_headers(self):
        self.assertEqual(ai.ENDPOINT, "https://api.anthropic.com/v1/messages")
        op = _FakeOpener({"content": [{"type": "text", "text": "안녕"}, {"type": "text", "text": "하세요"}]})
        with mock.patch.object(ai, "_opener", return_value=op):
            self.assertEqual(ai.call({"model": "m"}, "sk-" + "a" * 30), "안녕하세요")
        req = op.reqs[0]
        self.assertEqual(req.full_url, ai.ENDPOINT)
        self.assertEqual(req.get_header("Anthropic-version"), ai.API_VERSION)
        self.assertEqual(req.get_header("X-api-key"), "sk-" + "a" * 30)

    def test_errors(self):
        cases = [
            (urllib.error.HTTPError(ai.ENDPOINT, 401, "x", {}, None), "키"),
            (urllib.error.HTTPError(ai.ENDPOINT, 429, "x", {}, None), "한도"),
            (urllib.error.HTTPError(ai.ENDPOINT, 503, "x", {}, None), "서비스"),
            (urllib.error.URLError(OSError("no route")), "인터넷"),
            (TimeoutError(), "시간"),
        ]
        for exc, word in cases:
            with self.subTest(word=word), mock.patch.object(ai, "_opener", return_value=_FakeOpener(exc=exc)):
                with self.assertRaises(ai.AiUnavailable) as cm:
                    ai.call({}, "k" * 30)
                self.assertIn(word, cm.exception.reason_ko)
        for bad in ({"content": []}, {"content": "x"}, ["x"]):
            with self.subTest(bad=bad), mock.patch.object(ai, "_opener", return_value=_FakeOpener(bad)):
                with self.assertRaises(ai.AiUnavailable):
                    ai.call({}, "k" * 30)

    def test_redirect_refused(self):
        h = ai._NoRedirect()
        with self.assertRaises(ai.AiUnavailable):
            h.redirect_request(None, None, 302, "Found", {}, "https://evil.example/")

    def test_key_and_model(self):
        with mock.patch.dict(os.environ, {"BRC_AI_KEY": "", "ANTHROPIC_API_KEY": ""}):
            self.assertIsNone(ai.resolve_key(None))
            self.assertIsNone(ai.resolve_key("짧음"))
            self.assertEqual(ai.resolve_key(" sk-ant-" + "a" * 30 + " "), "sk-ant-" + "a" * 30)
        with mock.patch.dict(os.environ, {"BRC_AI_KEY": "e" * 30, "ANTHROPIC_API_KEY": "f" * 30}):
            self.assertEqual(ai.resolve_key("h" * 30), "h" * 30)   # 플러그인이 넘긴 키가 먼저
            self.assertEqual(ai.resolve_key(None), "e" * 30)
        with mock.patch.dict(os.environ, {"BRC_AI_MODEL": "claude-sonnet-5-5"}):
            self.assertEqual(ai.model_name(), "claude-sonnet-5-5")
        with mock.patch.dict(os.environ, {"BRC_AI_MODEL": "https://evil/"}):
            self.assertEqual(ai.model_name(), ai.DEFAULT_MODEL)


class Http(unittest.TestCase):
    def test_routes(self):
        from http.server import ThreadingHTTPServer
        from brc.server import Handler
        httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        port = httpd.server_address[1]
        threading.Thread(target=httpd.serve_forever, daemon=True).start()

        def post(path, body, headers=None):
            req = urllib.request.Request(f"http://127.0.0.1:{port}{path}", method="POST", data=json.dumps(body).encode(),
                                         headers={"Content-Type": "application/json", **(headers or {})})
            try:
                return 200, json.loads(urllib.request.urlopen(req).read())
            except urllib.error.HTTPError as e:
                with e:
                    return e.code, json.loads(e.read())
        try:
            with mock.patch.dict(os.environ, {"BRC_AI_KEY": "", "ANTHROPIC_API_KEY": ""}):
                code, out = post("/v1/explain", {"doc_id": "x-none", "rule_id": VIS})
                self.assertEqual(code, 400)
                v1 = raw(bhd_x=122.5, extra=[box("C1", 100, 112, 13.5, 24.0)])
                self.assertEqual(post("/v1/check", {"doc_id": "x1", "raw": v1})[0], 200)
                code, out = post("/v1/explain", {"doc_id": "x1", "rule_id": VIS})
                self.assertEqual((code, out["source"], out["revision"]), (200, "template", 1))
                code, out = post("/v1/explain", {"doc_id": "x1", "rule_id": VIS, "mode": "preview"})
                self.assertEqual(digits_left(sent_text(out["request"])), [])
                # ai: 키가 없으면 보내지 않고 기본 설명
                code, out = post("/v1/explain", {"doc_id": "x1", "rule_id": BHD, "mode": "ai"})
                self.assertEqual((code, out["sent"], out["source"]), (200, False, "template"))
                # 헤더 키가 있으면 ai.call을 부른다(가짜)
                with mock.patch.object(ai, "call", side_effect=ai.AiUnavailable("인터넷에 연결되지 않아 기본 설명을 보여 줍니다.")) as m:
                    code, out = post("/v1/explain", {"doc_id": "x1", "rule_id": BHD, "mode": "ai"},
                                     {"X-BRC-AI-Key": "sk-" + "z" * 30})
                    self.assertEqual(m.call_args[0][1], "sk-" + "z" * 30)
                self.assertEqual((out["sent"], out["source"]), (True, "template"))
                code, out = post("/v1/explain", {"doc_id": "x1", "rule_id": VIS, "mode": "nope"})
                self.assertEqual(code, 400)
                code, out = post("/v1/explain/change", {"a": v1, "b": raw(), "labels": ["v1", "v2"]})
                self.assertEqual((code, out["kind"]), (200, "change"))
                self.assertIn("v1 → v2", out["text"])
                health = json.loads(urllib.request.urlopen(f"http://127.0.0.1:{port}/v1/health").read())
                self.assertEqual(health["ai"]["env_key"], False)
                self.assertEqual(health["ai"]["host"], "api.anthropic.com")
        finally:
            httpd.shutdown()
            httpd.server_close()


if __name__ == "__main__":
    unittest.main()
