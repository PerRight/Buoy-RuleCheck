"""합성 GA(측면도) DXF revision 세트 생성 — 계획서 '자체 제작 GA / Synthetic Revision'.

  python scripts/make_samples.py   → ../samples/*.dxf

S140 가상 일반화물선 (LOA 140 m, L 132 m), 도면 단위 mm, 원점 = AP·기선.
  rev0  기본 — 두 규칙 모두 통과
  rev1  선교 앞 갑판 컨테이너 4단 적재(상단 z=24.0 m) → 밸러스트 조건에서 가시거리 위반
  rev2  충돌격벽을 x=124.5 → 122.5 m로 이동 → 상한 초과
  rev3  rev2를 되돌림(Undo) → rev1과 동일 상태 (같은 handle)
  rev4  컨테이너 3단(상단 z=21.4 m)으로 낮춤 → 전부 통과
handle을 유지하기 위해 한 문서를 순서대로 고쳐 가며 저장한다.

버전 비교 데모(기능 1 — 수정 전후 비교): python scripts/make_samples.py --compare-demo
  S140_v1_review.dxf  위반 3건 — 컨테이너 4단(상단 24.0 m), 선수 크레인(상단 25.0 m), 충돌격벽 x=122.5 m
  S140_v2_fixed.dxf   수정 완료 — 컨테이너 3단(21.4 m), 크레인 삭제, 낮은 선수 마스트 추가(19.0 m), 격벽 x=124.5 m
  S140_v2_fixed.garc/ 플러그인 버전 기록(v1·v2 스냅샷 + 작성자·사유) — AutoCAD에서 S140_v2_fixed.dxf를 열고 GARCCOMPARE
"""
import json
import sys
from pathlib import Path

import ezdxf

OUT = Path(__file__).resolve().parents[2] / "samples"
MM = 1000.0


def mm(pts):
    return [(x * MM, y * MM) for x, y in pts]


def main():
    OUT.mkdir(exist_ok=True)
    doc = ezdxf.new("R2018", setup=True)
    doc.header["$INSUNITS"] = 4  # mm
    for k, v in {
        "GARC_LOA": "140", "GARC_L_LL": "132", "GARC_X_AP": "0", "GARC_X_FP": "132",
        "GARC_Z_BASE": "0", "GARC_EYE_HEIGHT": "1.8", "GARC_KEEL_LAID": "2026-03-02",
        "GARC_SHIP_TYPE": "general_cargo",
        "GARC_CONDITIONS": "만재 출항:8.0/8.0; 밸러스트 도착:6.5/4.0",
    }.items():
        doc.header.custom_vars.append(k, v)
    colors = {"GA_HULL": 7, "GA_EYE": 3, "GA_OBST": 30, "GA_BHD_COLL": 1, "GA_BULB": 5, "GA_REF": 8}
    for name, c in colors.items():
        doc.layers.add(name, color=c)
    msp = doc.modelspace()

    msp.add_lwpolyline(mm([(-3, 4), (-3, 13), (0, 12), (120, 12), (120, 14.5), (137, 14.5), (134, 8), (132, 0), (0, 0)]),
                       dxfattribs={"layer": "GA_HULL"})
    msp.add_point((18 * MM, 28 * MM), dxfattribs={"layer": "GA_EYE"})
    bhd = msp.add_line((124.5 * MM, 0), (124.5 * MM, 14.5 * MM), dxfattribs={"layer": "GA_BHD_COLL"})
    msp.add_lwpolyline(mm([(132, 1.5), (135, 3), (132, 5)]), dxfattribs={"layer": "GA_BULB"})
    # 참조선(판정에 쓰지 않음): 선교 블록, AP/FP
    msp.add_lwpolyline(mm([(-3, 13), (-3, 28), (22, 28), (22, 13)]), dxfattribs={"layer": "GA_REF"})
    for x in (0, 132):
        msp.add_line((x * MM, -2 * MM), (x * MM, 16 * MM), dxfattribs={"layer": "GA_REF"})
    doc.saveas(OUT / "S140_rev0_base.dxf")

    stack = msp.add_lwpolyline(mm([(100, 13.5), (100, 24.0), (112, 24.0), (112, 13.5)]), dxfattribs={"layer": "GA_OBST"})
    doc.saveas(OUT / "S140_rev1_deck_containers.dxf")

    bhd.dxf.start, bhd.dxf.end = (122.5 * MM, 0), (122.5 * MM, 14.5 * MM)
    doc.saveas(OUT / "S140_rev2_bulkhead_moved.dxf")

    bhd.dxf.start, bhd.dxf.end = (124.5 * MM, 0), (124.5 * MM, 14.5 * MM)
    doc.saveas(OUT / "S140_rev3_undo.dxf")

    stack.set_points(mm([(100, 13.5), (100, 21.4), (112, 21.4), (112, 13.5)]), format="xy")
    doc.saveas(OUT / "S140_rev4_three_tiers.dxf")
    print("생성:", *sorted(p.name for p in OUT.glob("S140_*.dxf")), sep="\n  ")


def compare_demo():
    """v1(위반 3건) → v2(수정 완료) 도면과, 플러그인이 저장할 때 남기는 버전 기록 폴더를 만든다."""
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
    from garc.adapters.dxf import read_dxf
    from garc.contract import raw_to_model
    from garc.engine import evaluate

    doc = ezdxf.new("R2018", setup=True)
    doc.header["$INSUNITS"] = 4  # mm
    for k, v in {
        "GARC_LOA": "140", "GARC_L_LL": "132", "GARC_X_AP": "0", "GARC_X_FP": "132",
        "GARC_Z_BASE": "0", "GARC_EYE_HEIGHT": "1.8", "GARC_KEEL_LAID": "2026-03-02",
        "GARC_SHIP_TYPE": "general_cargo", "GARC_DWG_NO": "S140-GA-001",
        "GARC_CONDITIONS": "만재 출항:8.0/8.0; 밸러스트 도착:6.5/4.0",
    }.items():
        doc.header.custom_vars.append(k, v)
    for name, c in {"GA_HULL": 7, "GA_EYE": 3, "GA_OBST": 30, "GA_BHD_COLL": 1, "GA_BULB": 5, "GA_REF": 8}.items():
        doc.layers.add(name, color=c)
    msp = doc.modelspace()
    msp.add_lwpolyline(mm([(-3, 4), (-3, 13), (0, 12), (120, 12), (120, 14.5), (137, 14.5), (134, 8), (132, 0), (0, 0)]),
                       dxfattribs={"layer": "GA_HULL"})
    msp.add_point((18 * MM, 28 * MM), dxfattribs={"layer": "GA_EYE"})
    bhd = msp.add_line((122.5 * MM, 0), (122.5 * MM, 14.5 * MM), dxfattribs={"layer": "GA_BHD_COLL"})
    msp.add_lwpolyline(mm([(132, 1.5), (135, 3), (132, 5)]), dxfattribs={"layer": "GA_BULB"})
    msp.add_lwpolyline(mm([(-3, 13), (-3, 28), (22, 28), (22, 13)]), dxfattribs={"layer": "GA_REF"})
    for x in (0, 132):
        msp.add_line((x * MM, -2 * MM), (x * MM, 16 * MM), dxfattribs={"layer": "GA_REF"})
    stack = msp.add_lwpolyline(mm([(100, 13.5), (100, 24.0), (112, 24.0), (112, 13.5)]), close=True,
                               dxfattribs={"layer": "GA_OBST"})
    crane = msp.add_lwpolyline(mm([(117, 14.5), (117, 25.0), (119, 25.0), (119, 14.5)]), close=True,
                               dxfattribs={"layer": "GA_OBST"})
    v1 = OUT / "S140_v1_review.dxf"
    doc.saveas(v1)

    stack.set_points(mm([(100, 13.5), (100, 21.4), (112, 21.4), (112, 13.5)]), format="xy")
    msp.delete_entity(crane)
    msp.add_lwpolyline(mm([(126, 14.5), (126, 19.0), (127, 19.0), (127, 14.5)]), close=True,
                       dxfattribs={"layer": "GA_OBST"})
    bhd.dxf.start, bhd.dxf.end = (124.5 * MM, 0), (124.5 * MM, 14.5 * MM)
    v2 = OUT / "S140_v2_fixed.dxf"
    doc.saveas(v2)

    store = OUT / "S140_v2_fixed.garc"
    store.mkdir(exist_ok=True)
    meta = [
        (v1, "2026-10-20T10:12:00", "표인범", "other", "", "기본설계 GA 1차안 — 갑판 컨테이너 4단·선수 크레인 배치"),
        (v2, "2026-10-21T16:40:00", "손종민", "rule_violation", "SOLAS V/22.1.1 · SOLAS II-1/12",
         "가시거리 위반(컨테이너·크레인)과 충돌격벽 상한 초과 수정 — 생산설계 전달 전"),
    ]
    versions = []
    for n, (path, created, author, reason, ref, note) in enumerate(meta, 1):
        raw = read_dxf(path)
        raw["source"] = {"app": "dxf", "file": v2.name, "doc_id": v2.stem}
        (store / f"v{n}.raw.json").write_text(json.dumps(raw, ensure_ascii=False, indent=1), encoding="utf-8")
        rep = evaluate(raw_to_model(raw))
        fails = sum(1 for r in rep.results for e in r.elements if e["status"] == "fail")
        versions.append({"version": n, "file": f"v{n}.raw.json", "created": created, "author": author,
                         "reason": reason, "reference": ref, "note": note,
                         "overall": rep.overall, "fail_elements": fails})
    (store / "versions.json").write_text(json.dumps(
        {"schema": "garc.versions/1", "drawing": v2.name, "versions": versions}, ensure_ascii=False, indent=1), encoding="utf-8")
    print("생성:", v1.name, v2.name, store.name + "/", sep="\n  ")


if __name__ == "__main__":
    if "--compare-demo" in sys.argv:
        compare_demo()
    else:
        main()
