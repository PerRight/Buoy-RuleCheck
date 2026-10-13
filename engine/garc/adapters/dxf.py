"""DXF → raw. AutoCAD 없이 시험·시연하는 대체 경로 (계획서 '개발범위: DXF 대체 경로').

AutoCAD 플러그인(Extractor.cs)과 같은 raw를 만든다. 즉 DXF로 통과한 테스트는 플러그인 경로에도 유효하다.
- 좌표는 WCS로 보낸다(Extractor.cs의 GetPoint3dAt·Position과 같게). 폴리선·블록의 OCS(돌출 방향 −Z 등)는 여기서 풀어 준다.
- 폴리선 호(bulge)·닫힘(closed)은 그대로 전달하고, 호 분할은 엔진(contract.py)이 한다.
- 스플라인 맞춤 폴리선의 '제어점'은 곡선 위의 점이 아니므로 뺀다.
- 3D 메시(폴리페이스·폴리곤 메시)는 지원하지 않는 타입으로 보낸다 → 엔진이 '검토 불가'로 표시.
- 레이어 이름은 앞뒤 공백을 지우고 대문자로 맞춘다(Extractor.cs와 같게). 레이어별 객체 수(layer_counts)도 보내
  엔진이 GA_ 이름과 비슷한 레이어(GA-OBST 등)를 '검토 불가'로 멈추게 한다.
의존성: ezdxf (pip install ezdxf) — 엔진 코어는 이 모듈 없이 동작한다.
"""
from __future__ import annotations

from pathlib import Path
from typing import Any

from ..contract import LAYER_ROLES, SCHEMA_RAW

_SPLINE_FRAME_CONTROL_POINT = 16  # DXF VERTEX 플래그


def _xy(v) -> list[float]:
    return [float(v[0]), float(v[1])]


def _entity(e) -> dict[str, Any]:
    t = e.dxftype()
    out: dict[str, Any] = {"handle": e.dxf.handle, "type": t, "points": []}
    if t == "LINE":
        out["points"] = [_xy(e.dxf.start), _xy(e.dxf.end)]
    elif t == "LWPOLYLINE":
        out["points"] = [_xy(p) for p in e.vertices_in_wcs()]
        flip = -1.0 if e.dxf.extrusion[2] < 0 else 1.0   # OCS 법선이 −Z면 WCS에서 회전 방향이 반대
        bulges = [flip * float(p[2]) for p in e.get_points("xyb")]
        if any(b != 0.0 for b in bulges):
            out["bulges"] = bulges
        out["closed"] = bool(e.closed)
    elif t == "POLYLINE":
        if e.is_poly_face_mesh or e.is_polygon_mesh:
            out["type"] = "POLYFACEMESH" if e.is_poly_face_mesh else "POLYGONMESH"
            return out
        verts = [v for v in e.vertices if not (v.dxf.flags & _SPLINE_FRAME_CONTROL_POINT)]
        if e.is_2d_polyline:
            ocs = e.ocs()
            elev = e.dxf.elevation[2] if hasattr(e.dxf.elevation, "__getitem__") else 0.0
            out["points"] = [_xy(ocs.to_wcs((v.dxf.location[0], v.dxf.location[1], elev))) for v in verts]
            flip = -1.0 if e.dxf.extrusion[2] < 0 else 1.0
            bulges = [flip * float(v.dxf.get("bulge", 0.0)) for v in verts]
            if any(b != 0.0 for b in bulges):
                out["bulges"] = bulges
        else:  # 3D 폴리선: 꼭짓점이 WCS
            out["points"] = [_xy(v.dxf.location) for v in verts]
        out["closed"] = bool(e.is_closed)
    elif t == "POINT":
        out["points"] = [_xy(e.dxf.location)]
    elif t == "INSERT":
        out["points"] = [_xy(e.ocs().to_wcs(e.dxf.insert))]
    return out


def read_dxf(path: str | Path) -> dict[str, Any]:
    import ezdxf  # 선택 의존

    doc = ezdxf.readfile(str(path))
    msp = doc.modelspace()
    entities = []
    layer_counts: dict[str, int] = {}
    for e in msp:
        layer = e.dxf.layer.strip().upper()
        layer_counts[e.dxf.layer] = layer_counts.get(e.dxf.layer, 0) + 1
        if layer not in LAYER_ROLES:
            continue
        ent = _entity(e)
        ent["layer"] = layer
        entities.append(ent)

    props = {k: v for k, v in doc.header.custom_vars}
    return {
        "schema": SCHEMA_RAW,
        "source": {"app": "dxf", "file": Path(path).name, "doc_id": Path(path).stem},
        "insunits": doc.header.get("$INSUNITS", 0),
        "props": props,
        "entities": entities,
        "layer_counts": layer_counts,
    }
