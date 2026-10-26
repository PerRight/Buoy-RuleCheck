"""이름 변경(2026-10-26 GA-RuleCheck → Buoy-RuleCheck)의 옛 이름 읽기.

- 도면 특성: 새 이름 BRC_* — 옛 이름 GARC_*도 같은 뜻으로 읽는다. 둘 다 있고 값이 다르면 고르지 않고 '확인 필요'(PROP_DUPLICATE).
- 역할 지정 XRecord: 새 'BRC_ROLES' — 옛 'GARC_ROLES'도 읽는다(둘 다 있으면 새 것이 이긴다).
- 패키지: 새 brc — 옛 `import garc.…`·`python -m garc …`도 같은 모듈로.
- 스냅샷 schema: 옛 'garc.raw/1' 파일도 CLI가 raw로 읽는다.
"""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from brc.contract import raw_to_model  # noqa: E402
from brc.engine import evaluate  # noqa: E402

try:
    import ezdxf  # noqa: F401
    HAVE_EZDXF = True
except ImportError:
    HAVE_EZDXF = False

NEW = {"BRC_LOA": "140", "BRC_L_LL": "132", "BRC_X_AP": "0", "BRC_X_FP": "132",
       "BRC_CONDITIONS": "만재 출항:8.0/8.0; 밸러스트 도착:6.5/4.0"}
OLD = {"GARC_" + k[4:]: v for k, v in NEW.items()}
VIS, BHD = "SOLAS-V-22.1.1", "SOLAS-II1-12"


def raw(props, stack=True):
    s = 1000.0
    ents = [
        {"handle": "H1", "layer": "GA_HULL", "type": "LWPOLYLINE",
         "points": [[x * s, z * s] for x, z in [(-3, 4), (-3, 13), (0, 12), (120, 12), (120, 14.5), (137, 14.5), (134, 8), (132, 0), (0, 0)]]},
        {"handle": "E1", "layer": "GA_EYE", "type": "POINT", "points": [[18 * s, 28 * s]]},
        {"handle": "B1", "layer": "GA_BHD_COLL", "type": "LINE", "points": [[124.5 * s, 0], [124.5 * s, 14.5 * s]]},
        {"handle": "U1", "layer": "GA_BULB", "type": "LWPOLYLINE", "points": [[132 * s, 1.5 * s], [135 * s, 3 * s], [132 * s, 5 * s]]},
    ]
    if stack:
        ents.append({"handle": "S1", "layer": "GA_OBST", "type": "LWPOLYLINE",
                     "points": [[100e3, 13.5e3], [100e3, 24e3], [112e3, 24e3], [112e3, 13.5e3]]})
    return {"schema": "brc.raw/1", "insunits": 4, "props": dict(props), "entities": ents}


def judge(r):
    rep = evaluate(raw_to_model(r)).to_dict()
    return rep, {x["rule_id"]: x for x in rep["results"]}


class LegacyProps(unittest.TestCase):
    def test_old_names_give_same_judgment(self):
        for stack in (False, True):
            with self.subTest(stack=stack):
                a, b = judge(raw(NEW, stack)), judge(raw(OLD, stack))
                self.assertEqual(json.dumps(a[0], sort_keys=True, ensure_ascii=False), json.dumps(b[0], sort_keys=True, ensure_ascii=False))

    def test_mixed_old_and_new_names(self):
        mixed = {**{k: v for k, v in NEW.items() if k != "BRC_L_LL"}, "GARC_L_LL": "132"}
        self.assertEqual(judge(raw(mixed))[0], judge(raw(NEW))[0])

    def test_both_names_same_value_is_fine(self):
        both = {**NEW, "GARC_LOA": "140"}
        self.assertEqual(judge(raw(both, stack=False))[0]["overall"], "pass")

    def test_both_names_different_values_stops(self):
        for order in ("new_first", "old_first"):
            with self.subTest(order=order):
                props = {**NEW, "GARC_LOA": "150"} if order == "new_first" else {"GARC_LOA": "150", **NEW}
                rep, res = judge(raw(props, stack=False))
                self.assertNotEqual(rep["overall"], "pass")                     # 어느 쪽인지 고르지 않는다 — 문제없음으로 새지 않게
                self.assertEqual(res[VIS]["status"], "review")
                self.assertEqual(res[BHD]["status"], "review")
                self.assertIn("PROP_DUPLICATE", res[VIS]["evidence"]["issues"])
                msgs = " ".join(i["message_ko"] for i in raw_to_model(raw(props, stack=False))["issues"] if i["code"] == "PROP_DUPLICATE")
                self.assertIn("BRC_LOA", msgs)                                       # 두 이름을 모두 알려 준다
                self.assertIn("GARC_LOA", msgs)

    def test_same_number_written_differently_is_not_a_conflict(self):
        # 2026-10-26 Sonnet 검증 지적(낮음): "140"과 "140.0"은 같은 값 — 숫자로 같으면 멈추지 않는다
        both = {**NEW, "GARC_LOA": "140.0"}
        self.assertEqual(judge(raw(both, stack=False))[0]["overall"], "pass")
        for blank in ({**NEW, "GARC_LOA": ""}, {"BRC_LOA": "", **{k: v for k, v in NEW.items() if k != "BRC_LOA"}, "GARC_LOA": "140"}):
            with self.subTest(blank=blank):                                    # 빈 값은 '없음'과 같다 — 다른 쪽 값으로 판정
                self.assertEqual(judge(raw(blank, stack=False))[0]["overall"], "pass")
        for other in ("140.5", "1,40", "nan", "inf", "140m"):
            with self.subTest(other=other):
                props = {**NEW, "GARC_LOA": other}
                rep, res = judge(raw(props, stack=False))
                self.assertEqual(res[VIS]["status"], "review", other)
                self.assertIn("PROP_DUPLICATE", res[VIS]["evidence"]["issues"])

    def test_messages_use_new_names(self):
        rep, res = judge(raw({k: v for k, v in NEW.items() if k != "BRC_LOA"}, stack=False))
        self.assertEqual(res[VIS]["status"], "na")
        self.assertIn("BRC_LOA", res[VIS]["message_ko"])
        self.assertNotIn("GARC_", res[VIS]["message_ko"])


@unittest.skipUnless(HAVE_EZDXF, "ezdxf 없음")
class LegacyRolesXrecord(unittest.TestCase):
    def build(self, path, names):
        import ezdxf
        doc = ezdxf.new("R2018")
        doc.header["$INSUNITS"] = 6
        msp = doc.modelspace()
        hull = msp.add_lwpolyline([(0, 0), (0, 12), (137, 14.5), (132, 0)], dxfattribs={"layer": "선체"})
        cont = msp.add_lwpolyline([(100, 13.5), (100, 24), (112, 24), (112, 13.5)], close=True, dxfattribs={"layer": "컨테이너"})
        for name, role_of_cont in names:
            xr = doc.rootdict.add_xrecord(name)
            xr.reset([(1, "hull"), (330, hull.dxf.handle), (1, role_of_cont), (330, cont.dxf.handle)])
        doc.saveas(path)
        return hull.dxf.handle, cont.dxf.handle

    def roles(self, names):
        from brc.adapters.dxf import read_dxf
        with tempfile.TemporaryDirectory() as d:
            f = Path(d) / "roles.dxf"
            h, c = self.build(f, names)
            raw_ = read_dxf(f)
        by = {e["handle"]: e.get("role") for e in raw_["entities"]}
        return by[h], by[c]

    def test_old_xrecord_is_read(self):
        self.assertEqual(self.roles([("GARC_ROLES", "obstruction")]), ("hull", "obstruction"))

    def test_new_xrecord_wins(self):
        self.assertEqual(self.roles([("GARC_ROLES", "hull"), ("BRC_ROLES", "obstruction")]), ("hull", "obstruction"))


@unittest.skipUnless(HAVE_EZDXF, "ezdxf 없음")
class LegacyLayerRolesDxf(unittest.TestCase):
    """2026-10-26 Sonnet 검증 지적(중간): DXF 어댑터가 옛 GARC_LAYER_ROLES의 레이어를 펼치지 않아
    '형상 객체가 없습니다'(LAYER_ROLES_EMPTY)로 잘못 안내하던 문제."""

    def judge_dxf(self, prefix):
        import ezdxf
        from brc.adapters.dxf import read_dxf
        doc = ezdxf.new("R2018")
        doc.header["$INSUNITS"] = 6
        msp = doc.modelspace()
        msp.add_lwpolyline([(x, z) for x, z in [(-3, 4), (-3, 13), (0, 12), (120, 12), (120, 14.5), (137, 14.5), (134, 8), (132, 0), (0, 0)]],
                           dxfattribs={"layer": "GA_HULL"})
        msp.add_point((18, 28), dxfattribs={"layer": "GA_EYE"})
        msp.add_line((124.5, 0), (124.5, 14.5), dxfattribs={"layer": "GA_BHD_COLL"})
        msp.add_lwpolyline([(100, 13.5), (100, 24), (112, 24), (112, 13.5)], close=True, dxfattribs={"layer": "컨테이너"})
        props = {**{prefix + k[4:]: v for k, v in NEW.items()}, prefix + "LAYER_ROLES": "컨테이너=장애물", prefix + "REGION": "-10,-5,150,40"}
        for k, v in props.items():
            doc.header.custom_vars.append(k, v)
        with tempfile.TemporaryDirectory() as d:
            f = Path(d) / "lr.dxf"
            doc.saveas(f)
            return judge(read_dxf(f))

    def test_old_layer_roles_name_is_expanded(self):
        new, old = self.judge_dxf("BRC_"), self.judge_dxf("GARC_")
        self.assertEqual(new[1][VIS]["status"], "fail")                    # 컨테이너가 장애물로 들어가 위반
        self.assertEqual(old[1][VIS]["status"], new[1][VIS]["status"])
        self.assertEqual(old[1][VIS]["actual"], new[1][VIS]["actual"])


class LegacyPackageAndSchema(unittest.TestCase):
    def test_import_alias_is_same_module(self):
        import brc.contract
        import garc.contract
        import garc.compare
        import brc.compare
        self.assertIs(garc.contract, brc.contract)
        self.assertIs(garc.compare.compare_raws, brc.compare.compare_raws)
        self.assertEqual(brc.contract.__spec__.name, "brc.contract")      # 별칭 import가 원래 모듈의 spec을 바꾸지 않는다
        import importlib
        import garc.adapters.dxf
        import brc.adapters.dxf
        self.assertIs(garc.adapters.dxf, brc.adapters.dxf)
        with self.assertRaises(ModuleNotFoundError) as cm:
            importlib.import_module("garc.nope")
        self.assertIn("garc.nope", str(cm.exception))

    def test_cli_old_and_new_and_old_schema(self):
        old_raw = {**raw(OLD, stack=True), "schema": "garc.raw/1"}
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "snap.json"
            p.write_text(json.dumps(old_raw, ensure_ascii=False), encoding="utf-8")
            outs = []
            for pkg in ("brc", "garc"):
                r = subprocess.run([sys.executable, "-m", pkg, "check", str(p), "--json"], cwd=HERE.parent,
                                   capture_output=True, text=True, encoding="utf-8")
                self.assertEqual(r.returncode, 1, r.stderr)                   # check의 종료 코드: 수정 소요 있음 = 1
                outs.append(json.loads(r.stdout))
        self.assertEqual(outs[0], outs[1])
        self.assertEqual(outs[0]["overall"], "fail")                         # 옛 schema·옛 키 스냅샷도 판정까지


if __name__ == "__main__":
    unittest.main()
