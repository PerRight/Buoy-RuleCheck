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
                        z = f"z = {r['evidence']['suggestion_z']:.2f} m"
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
        self.assertIn("z = 22.45 m보다 높은 부분이 없게", out["text"])
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
            "도메인": self.ok + " evil.example 참고.",
            "마크업": self.ok + " <b>굵게</b>",
            "너무 짧음": "{판정}",
            "너무 긺": self.ok * 10,
            # 10-30 Sonnet 검증 V2·V3 우회 사례
            "판정 빈칸 없이 시작": self.ok.replace("{판정}: ", ""),
            "반대 판정(기준 안)": self.ok + " 지금은 기준 안입니다.",
            "반대 판정(공백)": self.ok + " 문제 없 습니다.",
            "충족": self.ok + " 현재 기준을 충족합니다.",
            "안전": self.ok + " 안전합니다.",
            "통과": self.ok + " 통과했습니다.",
            "금지어 공백": self.ok + " 적 합합니다.",
            "금지어 제로폭": self.ok + " 적\u200b합.",
            "합격 한자": self.ok + " 合格.",
            "영문": self.ok + " approved.",
            "PASS": self.ok + " PASS.",
            "⑩": self.ok + " ⑩ 위치.",
            "½": self.ok + " ½ 정도.",
            "로마 숫자": self.ok + " Ⅲ 구역.",
            "한자 숫자": self.ok + " 五百.",
            "한글 수사": self.ok + " 오백 정도 넘습니다.",
            "한글 수사 단위": self.ok + " 사백삼십삼 m.",
            "단위 기호": self.ok + " 약 cm 단위.",
            "조항 밀수 1": self.ok + " Reg.12 m 옮기세요.",
            "조항 밀수 2": self.ok + " SOLAS Ch.V Reg.22.1.15 참조.",
            "조항 밀수 3": self.ok + " 22.1.122.1.1",
            "조항 밀수 4": self.ok + " Reg.22.1.1m",
            "빈칸 맞바꿈": "{판정}: 사각 거리가 허용 한계 {사각거리}로 {한도}를 넘습니다. 상단을 {제안높이}로 낮추세요.",
        }
        for name, text in bad.items():
            with self.subTest(name=name):
                self.assertIsNotNone(ex.check_ai_text(text, self.b))

    def test_realistic_answers_pass(self):
        """그럴듯한 AI 답은 통과해야 한다(검사가 지나쳐서 늘 기본 설명만 나오지 않게)."""
        fail_txts = [
            "{판정}: 조종위치에서 바라본 선수 앞 수면의 사각 거리가 {사각거리}로, 허용 한계 {한도}를 넘어섭니다. "
            "가장 불리한 흘수 조건은 {최악조건}입니다. 시선을 가장 크게 가리는 장애물의 상단을 {제안높이} 이하로 낮추면 "
            "기준 안에 들어옵니다. 어렵다면 선수 트림을 줄이거나 선교 높이를 올리는 방법을 검토하세요.",
            "{판정}: 사각 거리 {사각거리}가 허용 한계 {한도}보다 깁니다. {제안위치} 부근 장애물 상단을 {제안높이} 이하로 낮추면 "
            "모든 흘수 조건에서 기준 안에 듭니다. 근거는 SOLAS Ch.V Reg.22.1.1입니다.",
        ]
        for t in fail_txts:
            with self.subTest(t=t[:30]):
                self.assertIsNone(ex.check_ai_text(t, self.b))
        rep = evaluate(next(c for c in CASES if c["expected"]["status"] == "pass" and c["rule"] == VIS)["model"]).to_dict()
        pb = ex.rule_brief(next(r for r in rep["results"] if r["rule_id"] == VIS))
        for t in ["{판정}: 선교에서 본 선수 앞 수면의 사각 거리가 {사각거리}로 허용 한계 {한도}를 넘지 않습니다. "
                  "가장 불리한 조건은 {최악조건}이며 한계까지 {남은거리}가 남아 있습니다. 측면도만 본 판정이므로 좌우 시야는 따로 확인하세요."]:
            self.assertIsNone(ex.check_ai_text(t, pb), ex.check_ai_text(t, pb))
        rep = evaluate(next(c for c in CASES if c["expected"]["status"] == "fail" and c["rule"] == BHD)["model"]).to_dict()
        bb = ex.rule_brief(next(r for r in rep["results"] if r["rule_id"] == BHD))
        t = ("{판정}: 충돌격벽이 기준점({기준점})에서 {거리} 떨어져 있어 허용 범위 {허용범위}를 벗어납니다. "
             "격벽을 {고칠양} 이상 옮기면 기준 안에 듭니다. 근거는 SOLAS Ch.II-1 Reg.12.1·12.3·12.4입니다.")
        self.assertIsNone(ex.check_ai_text(t, bb), ex.check_ai_text(t, bb))

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


class SonnetFindings(unittest.TestCase):
    """10-30 Sonnet 독립 검증 지적(V1·V4·V5·V6·V8·V9) 회귀."""

    def test_v1_numbers_match_engine_strings(self):
        """빈칸 숫자 = 엔진 판정 문장·제안·카드 숫자(반올림을 두 번 하지 않음) — 소수 넷째 자리 좌표로 퍼징."""
        rnd = random.Random(1030)
        checked = 0
        for i in range(600):
            c = copy.deepcopy(rnd.choice(CASES)["model"])
            for o in c["objects"]:
                o["points"] = [[round(x + rnd.uniform(-3, 3), 4), round(z + rnd.uniform(-1, 1), 4)] for x, z in o["points"]]
            for k in ("loa", "l_ll"):
                if isinstance(c["ship"].get(k), (int, float)):
                    c["ship"][k] = round(c["ship"][k] + rnd.uniform(-5, 5), 4)
            try:
                rep = evaluate(c).to_dict()
            except Exception:
                continue
            for r in rep["results"]:
                if r["status"] not in ("pass", "fail"):
                    continue
                b = ex.rule_brief(r)
                engine_text = " ".join(filter(None, [r["message_ko"], r.get("suggestion_ko") or ""] +
                                              [str((e.get("fix") or {}).get(k, "")) for e in r["elements"]
                                               for k in ("action_ko", "condition_ko")]))
                for name in ("사각거리", "한도", "제안위치", "제안높이", "거리", "허용범위", "여유", "고칠양"):
                    v = b.slots.get(name)
                    if not v or v.startswith("무한대"):
                        continue
                    for num in re.findall(r"\d+(?:\.\d+)?", v):
                        self.assertIn(num, engine_text, f"#{i} {r['rule_id']} {{{name}}}={v}")
                        checked += 1
        self.assertGreater(checked, 1000)

    def test_v4_unjudged_counts_not_compared(self):
        a = raw(bhd_x=122.5, extra=[box("C1", 100, 112, 13.5, 24.0), box("K1", 117, 119, 14.5, 25.0)])
        b = copy.deepcopy(a)
        b["entities"] = [e for e in b["entities"] if e["layer"] != "GA_EYE"]
        cmp = compare_raws(a, b, ("v1", "v2"))
        br = ex.change_brief(cmp)
        self.assertFalse(br.public["counts_comparable"])
        self.assertFalse(br.public["violation_free_after"])
        self.assertEqual(br.public["violation_trend_ko"], "비교 불가")
        text = ex.explain(br)["text"]
        self.assertIn("비교하지 않았습니다", text)
        self.assertNotIn("줄었습니다", text)
        self.assertIsNotNone(ex.check_ai_text("{버전} 변경으로 수정 소요가 {이전수정소요}에서 {이후수정소요}로 줄었습니다. 표식 위치를 반영하세요.", br))

    def test_v5_any_client_failure_falls_back(self):
        import http.client
        for exc in (http.client.BadStatusLine("x"), RecursionError(), RuntimeError("boom")):
            with self.subTest(exc=type(exc).__name__), mock.patch.object(ai, "_opener", return_value=_FakeOpener(exc=exc)):
                with self.assertRaises(ai.AiUnavailable):
                    ai.call({}, "k" * 30)
        deep = {"content": [{"type": "text", "text": "x"}]}
        for _ in range(3000):
            deep = {"a": deep}
        with mock.patch.object(ai, "_opener", return_value=_FakeOpener(deep)):
            with self.assertRaises(ai.AiUnavailable):
                ai.call({}, "k" * 30)
        out = ex.explain(fail_brief(), "ai", key="k" * 30, call=lambda b, k: (_ for _ in ()).throw(RuntimeError("x")))
        self.assertEqual(out["source"], "template")

    def test_v6_total_deadline(self):
        import time as _t

        class Drip:
            def __enter__(self):
                return self

            def __exit__(self, *a):
                pass

            def read1(self, n):
                _t.sleep(0.05)
                return b" "

        class Op:
            def open(self, req, timeout=None):
                return Drip()
        t0 = _t.monotonic()
        with mock.patch.object(ai, "_opener", return_value=Op()):
            with self.assertRaises(ai.AiUnavailable) as cm:
                ai.call({}, "k" * 30, timeout=0.3)
        self.assertLess(_t.monotonic() - t0, 1.0)
        self.assertIn("시간", cm.exception.reason_ko)

    def test_v8_kinds_order_does_not_follow_handles(self):
        def pair(h_mod, h_add):
            a = raw(extra=[box(h_mod, 100, 112, 13.5, 24.0)])
            b = raw(extra=[box(h_mod, 100, 112, 13.5, 21.0), box(h_add, 60, 62, 14.5, 18.0)])
            return ex.change_brief(compare_raws(a, b, ("v1", "v2"))).public["change_kinds_ko"]
        self.assertEqual(pair("A1", "Z1"), pair("Z1", "A1"))
        self.assertEqual(pair("A1", "Z1"), ["장애물 추가", "장애물 수정"])

    def test_v9_unknown_values_not_sent(self):
        r = evaluate(CASES[0]["model"]).to_dict()["results"][0]
        r = dict(r, status="SECRETSTATUS", rule_id="SECRET-RULE")
        body = ex.request_body(ex.rule_brief(r), "m")
        self.assertNotIn("SECRET", sent_text(body))

    def test_header_key_wrong_shape_does_not_use_env(self):
        with mock.patch.dict(os.environ, {"BRC_AI_KEY": "e" * 30}):
            self.assertIsNone(ai.resolve_key("짧은키"))
            self.assertEqual(ai.resolve_key(""), "e" * 30)


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
