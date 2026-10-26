"""2026-10-18 버전 비교 데모 샘플 — S140_v1_review(위반 3건) → S140_v2_fixed(수정 완료)와 플러그인 버전 기록 폴더.

재생성: python scripts/make_samples.py --compare-demo
"""
import importlib.util
import json
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from brc.compare import compare_models, compare_raws  # noqa: E402


@unittest.skipUnless(importlib.util.find_spec("ezdxf"), "ezdxf 없음")
class DemoSamples(unittest.TestCase):
    def test_demo_files_and_version_store(self):
        from brc.cli import load_any
        samples = HERE.parents[1] / "samples"
        out = compare_models(load_any(str(samples / "S140_v1_review.dxf")), load_any(str(samples / "S140_v2_fixed.dxf")))
        self.assertEqual(out["violations"], {"before": 3, "after": 0})
        self.assertEqual([c["kind"] for c in out["changes"]], ["modified", "removed", "modified", "added"])
        store = samples / "S140_v2_fixed.brc"
        idx = json.loads((store / "versions.json").read_text(encoding="utf-8"))
        self.assertEqual(idx["schema"], "brc.versions/1")
        self.assertEqual([v["version"] for v in idx["versions"]], [1, 2])
        self.assertEqual([v["fail_elements"] for v in idx["versions"]], [3, 0])
        raws = [json.loads((store / v["file"]).read_text(encoding="utf-8")) for v in idx["versions"]]
        out2 = compare_raws(raws[0], raws[1])
        self.assertEqual([c["desc_ko"] for c in out2["changes"]], [c["desc_ko"] for c in out["changes"]])


if __name__ == "__main__":
    unittest.main()
