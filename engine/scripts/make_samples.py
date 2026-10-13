"""합성 GA(측면도) DXF revision 세트 생성 — 계획서 '자체 제작 GA / Synthetic Revision'.

  python scripts/make_samples.py   → ../samples/*.dxf

S140 가상 일반화물선 (LOA 140 m, L 132 m), 도면 단위 mm, 원점 = AP·기선.
  rev0  기본 — 두 규칙 모두 통과
  rev1  선교 앞 갑판 컨테이너 4단 적재(상단 z=24.0 m) → 밸러스트 조건에서 가시거리 위반
  rev2  충돌격벽을 x=124.5 → 122.5 m로 이동 → 상한 초과
  rev3  rev2를 되돌림(Undo) → rev1과 동일 상태 (같은 handle)
  rev4  컨테이너 3단(상단 z=21.4 m)으로 낮춤 → 전부 통과
handle을 유지하기 위해 한 문서를 순서대로 고쳐 가며 저장한다.
"""
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


if __name__ == "__main__":
    main()
