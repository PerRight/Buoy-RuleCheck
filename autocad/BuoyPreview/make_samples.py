"""BuoyPreview 팔레트 미리보기용 엔진 응답(samples.json) 만들기 — 합성 샘플 S140만 쓴다(실도면 없음).

  cd autocad/BuoyPreview
  python make_samples.py          # ../../engine 과 ../../samples 를 읽어 samples.json 을 다시 쓴다(ezdxf 필요)

순서: rev0 → rev1(컨테이너) → rev2(격벽 이동) → rev3(되돌림) → rev4(3단) → 흘수 조건 지움 / LOA·L 없음 / v1 → v2.
버전 비교 응답(samples_compare.json): v1 → v2, rev0 → rev2.
표시에 쓰지 않는 근거(evidence)·그림(overlays)은 줄여서 넣는다.
"""
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(ROOT / "engine"))
from garc.adapters.dxf import read_dxf  # noqa: E402
from garc.server import handle_check  # noqa: E402

S = ROOT / "samples"
out = []


def run(doc, name, raw):
    r = handle_check({"doc_id": "preview-" + doc, "raw": raw})
    for x in r["results"]:
        x.pop("evidence", None)
        x["overlays"] = []
    out.append({"name": name, "resp": r})


for n, label in [("S140_rev0_base", "rev0 · 처음"), ("S140_rev1_deck_containers", "rev1 · 컨테이너 추가"),
                 ("S140_rev2_bulkhead_moved", "rev2 · 격벽 이동"), ("S140_rev3_undo", "rev3 · 되돌림"),
                 ("S140_rev4_three_tiers", "rev4 · 3단으로")]:
    run("A", label, read_dxf(S / f"{n}.dxf"))
raw = read_dxf(S / "S140_rev4_three_tiers.dxf")
raw["props"]["GARC_CONDITIONS"] = ""
run("A", "흘수 조건 지움 · 확인 필요", raw)
raw = read_dxf(S / "S140_rev0_base.dxf")
raw["props"].pop("GARC_LOA", None)
raw["props"].pop("GARC_L_LL", None)
run("B", "LOA·L 없음 · 준비 상태", raw)
for n, label in [("S140_v1_review", "v1 · 검토"), ("S140_v2_fixed", "v2 · 수정")]:
    run("C", label, read_dxf(S / f"{n}.dxf"))
(HERE / "samples.json").write_text(json.dumps(out, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
print(f"samples.json: {len(out)}개")

# 버전 비교 미리보기(비교 창·A4 전달 시트): v1 → v2(해결 3건), rev0 → rev2(새 수정 소요)
from garc.compare import compare_raws  # noqa: E402
cmp = [{"name": "v1 → v2", "resp": compare_raws(read_dxf(S / "S140_v1_review.dxf"), read_dxf(S / "S140_v2_fixed.dxf"), ("v1", "v2"))},
       {"name": "rev0 → rev2", "resp": compare_raws(read_dxf(S / "S140_rev0_base.dxf"), read_dxf(S / "S140_rev2_bulkhead_moved.dxf"), ("v1", "현재"))}]
(HERE / "samples_compare.json").write_text(json.dumps(cmp, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
print(f"samples_compare.json: {len(cmp)}개")
