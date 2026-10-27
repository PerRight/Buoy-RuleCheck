# -*- coding: utf-8 -*-
"""설치 패키지 시험(리눅스·CI에서도 돈다): 번들 정의, 내장 Python ._pth 처리, 설치·제거 흐름.

Windows 전용 부분(레지스트리·프로세스·차단 해제·서명·dotnet 빌드)은 여기서 돌리지 않는다 —
buoy_setup.py는 Windows가 아니면 환경변수를 JSON 파일에 적고, 엔진 자체 시험은 이 Python으로 한다.
실행: python -m unittest discover -s tools/package/tests -v   (저장소 루트에서)
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

PKG = Path(__file__).resolve().parent.parent
ROOT = PKG.parent.parent


def _load(name: str):
    spec = importlib.util.spec_from_file_location(name, PKG / f"{name}.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


mp = _load("make_package")


def fake_embed_zip(path: Path, minor: int = 14) -> Path:
    with zipfile.ZipFile(path, "w") as z:
        z.writestr("python.exe", b"MZ")
        z.writestr(f"python3{minor}.dll", b"MZ")
        z.writestr(f"python3{minor}.zip", b"PK")
        z.writestr(f"python3{minor}._pth", f"python3{minor}.zip\r\n.\r\n\r\n# Uncomment to run site.main() automatically\r\n#import site\r\n")
    return path


class BundleXml(unittest.TestCase):
    def _comps(self, targets):
        root = ET.fromstring(mp.package_contents("9.9.9", targets))
        top = root.find("RuntimeRequirements").attrib
        comps = [(c.find("RuntimeRequirements").attrib, c.find("ComponentEntry").attrib) for c in root.findall("Components")]
        return root, top, comps

    def test_both_targets(self):
        root, top, comps = self._comps(["R25", "R26"])
        self.assertEqual(root.attrib["AppVersion"], "9.9.9")
        self.assertEqual((top["SeriesMin"], top["SeriesMax"]), ("R25.0", "R26.0"))
        self.assertEqual(len(comps), 2)
        r25, r26 = comps
        self.assertEqual((r25[0]["SeriesMin"], r25[0]["SeriesMax"]), ("R25.0", "R25.1"))
        self.assertEqual(r25[1]["ModuleName"], "./Contents/Win64/R25/BuoyRuleCheck.dll")
        self.assertEqual((r26[0]["SeriesMin"], r26[0]["SeriesMax"]), ("R26.0", "R26.0"))
        self.assertEqual(r26[1]["ModuleName"], "./Contents/Win64/R26/BuoyRuleCheck.dll")
        for req, entry in comps:
            self.assertEqual(req["Platform"], "AutoCAD*")
            self.assertEqual(entry["LoadOnAutoCADStartup"], "True")
        # 개발용 번들과 같은 제품 코드(업그레이드로 인식)
        dev = (ROOT / "autocad" / "bundle" / "PackageContents.xml").read_text(encoding="utf-8")
        self.assertIn(root.attrib["ProductCode"], dev)
        self.assertIn(root.attrib["UpgradeCodes"], dev)

    def test_2027_only(self):
        _, top, comps = self._comps(["R26"])
        self.assertEqual((top["SeriesMin"], top["SeriesMax"]), ("R26.0", "R26.0"))
        self.assertEqual(len(comps), 1)


class EmbeddedPython(unittest.TestCase):
    def test_pth_gets_engine_once(self):
        with tempfile.TemporaryDirectory() as t:
            z = fake_embed_zip(Path(t) / "python-3.14.9-embed-amd64.zip")
            dest = Path(t) / "py"
            with contextlib.redirect_stdout(io.StringIO()):
                ver = mp.unpack_python(z, dest)
                self.assertEqual(ver, "3.14.9")
                lines = (dest / "python314._pth").read_text(encoding="utf-8").splitlines()
                self.assertEqual(lines.count("..\\engine"), 1)
                self.assertIn("#import site", lines)          # site는 켜지 않는다
                self.assertLess(lines.index("..\\engine"), lines.index("#import site"))
                mp.unpack_python(z, dest)                      # 다시 풀어도 한 줄
            self.assertEqual((dest / "python314._pth").read_text(encoding="utf-8").splitlines().count("..\\engine"), 1)

    def test_rejects_non_embeddable_zip(self):
        with tempfile.TemporaryDirectory() as t:
            z = Path(t) / "x.zip"
            with zipfile.ZipFile(z, "w") as zz:
                zz.writestr("python.exe", b"MZ")             # ._pth 없음 = 설치형 Python 조각 등
            with contextlib.redirect_stdout(io.StringIO()), self.assertRaises(SystemExit):
                mp.unpack_python(z, Path(t) / "py")

    def test_version_warning_outside_tested_range(self):
        with tempfile.TemporaryDirectory() as t:
            z = fake_embed_zip(Path(t) / "python-3.9.13-embed-amd64.zip", minor=9)
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                mp.unpack_python(z, Path(t) / "py")
            self.assertIn("시험 범위", out.getvalue())


class SetupFlow(unittest.TestCase):
    """가짜 Windows 폴더(APPDATA·LOCALAPPDATA)에 설치·재설치·제거."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.pkg = self.tmp / "pkg" / "app"
        (self.pkg / "python").mkdir(parents=True)
        (self.pkg / "python" / "python.exe").write_bytes(b"MZ")
        mp.copy_engine(self.pkg / "engine")
        (self.pkg / "engine" / "brc" / "__pycache__").mkdir()
        (self.pkg / "engine" / "brc" / "__pycache__" / "sentinel.pyc").write_bytes(b"x")   # 복사되면 안 됨
        mp.copy_samples(self.pkg / "samples")
        shutil.copy2(PKG / "buoy_setup.py", self.pkg / "buoy_setup.py")
        shutil.copy2(PKG / "payload" / "app_UNINSTALL.cmd", self.pkg / "UNINSTALL.cmd")
        b = self.pkg / "BuoyRuleCheck.bundle"
        (b / "Contents" / "Win64" / "R26").mkdir(parents=True)
        (b / "Contents" / "Win64" / "R26" / "BuoyRuleCheck.dll").write_bytes(b"MZ")
        (b / "PackageContents.xml").write_text(mp.package_contents("0.6.0", ["R26"]), encoding="utf-8")
        (self.pkg / "manifest.json").write_text(json.dumps({"plugin_version": "0.6.0", "engine_version": "0.5.0",
                                                            "python_version": "3.14.9"}), encoding="utf-8")
        self.env = dict(os.environ, APPDATA=str(self.tmp / "Roaming"), LOCALAPPDATA=str(self.tmp / "Local"),
                        BRC_SETUP_FAKE_ACAD="2025,2027")
        self.env.pop("BRC_SETUP_FAKE_ACAD_RUNNING", None)

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def run_setup(self, script: Path, action: str, **extra):
        env = dict(self.env, **extra)
        return subprocess.run([sys.executable, "-I", str(script), action, "--yes"], capture_output=True, text=True,
                              env=env, cwd=str(self.tmp), stdin=subprocess.DEVNULL, timeout=120)

    def test_install_reinstall_uninstall(self):
        r = self.run_setup(self.pkg / "buoy_setup.py", "install")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("엔진 0.5.0 응답 확인", r.stdout)
        self.assertIn("AutoCAD 2025", r.stdout)                 # 2025는 있지만 이 패키지에 R25 없음 → 주의
        self.assertIn("그 버전용 플러그인이 없음", r.stdout)
        app = self.tmp / "Local" / "BuoyRuleCheck" / "app"
        bundle = self.tmp / "Roaming" / "Autodesk" / "ApplicationPlugins" / "BuoyRuleCheck.bundle"
        self.assertTrue((app / "engine" / "brc" / "__init__.py").exists())
        self.assertTrue((app / "UNINSTALL.cmd").exists())
        self.assertTrue((bundle / "Contents" / "Win64" / "R26" / "BuoyRuleCheck.dll").exists())
        envf = self.tmp / "Local" / "BuoyRuleCheck" / "user_env.json"
        env = json.loads(envf.read_text(encoding="utf-8"))
        self.assertEqual(env["BRC_ENGINE_DIR"], str(app / "engine"))
        self.assertNotIn("GARC_PYTHON", env)
        self.assertFalse((app / "engine" / "brc" / "__pycache__" / "sentinel.pyc").exists(), "패키지의 캐시를 복사하지 않음")
        # 옛 이름 번들·옛 환경변수가 있으면 지운다
        old = bundle.parent / "GaRuleCheck.bundle"
        old.mkdir()
        env["GARC_PYTHON"] = "C:/old/python.exe"
        envf.write_text(json.dumps(env), encoding="utf-8")
        (bundle / "stale.txt").write_text("x")
        r = self.run_setup(self.pkg / "buoy_setup.py", "install")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertFalse(old.exists())
        self.assertFalse((bundle / "stale.txt").exists(), "재설치는 번들을 통째로 바꾼다")
        self.assertNotIn("GARC_PYTHON", json.loads(envf.read_text(encoding="utf-8")))
        # 설치된 쪽에서 제거
        r = self.run_setup(app / "buoy_setup.py", "uninstall")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertFalse(bundle.exists())
        self.assertFalse(app.exists())
        self.assertEqual(json.loads(envf.read_text(encoding="utf-8")), {})
        self.assertTrue((self.tmp / "Local" / "BuoyRuleCheck" / "install.log").exists())

    def test_autocad_running_stops_without_changes(self):
        r = self.run_setup(self.pkg / "buoy_setup.py", "install", BRC_SETUP_FAKE_ACAD_RUNNING="1")
        self.assertEqual(r.returncode, 2, r.stdout + r.stderr)
        self.assertIn("AutoCAD가 켜져 있습니다", r.stdout)
        self.assertFalse((self.tmp / "Local" / "BuoyRuleCheck" / "app").exists())
        self.assertFalse((self.tmp / "Roaming" / "Autodesk" / "ApplicationPlugins" / "BuoyRuleCheck.bundle").exists())

    def test_incomplete_package_refuses(self):
        shutil.rmtree(self.pkg / "BuoyRuleCheck.bundle" / "Contents")
        r = self.run_setup(self.pkg / "buoy_setup.py", "install")
        self.assertEqual(r.returncode, 1, r.stdout + r.stderr)
        self.assertIn("패키지가 온전하지 않습니다", r.stdout)

    def test_broken_engine_reports_failure(self):
        (self.pkg / "engine" / "brc" / "__init__.py").write_text("raise ImportError('깨진 엔진')\n", encoding="utf-8")
        r = self.run_setup(self.pkg / "buoy_setup.py", "install")
        self.assertEqual(r.returncode, 3, r.stdout + r.stderr)
        self.assertIn("엔진을 불러오지 못함", r.stdout)


class Payload(unittest.TestCase):
    def test_cmd_files_ascii_commands_cp949_crlf(self):
        for name in ("INSTALL.cmd", "UNINSTALL.cmd", "app_UNINSTALL.cmd"):
            raw = (PKG / "payload" / name).read_bytes()
            text = raw.decode("cp949")                         # 한국어 Windows 콘솔 코드 페이지
            self.assertNotIn(b"\n", raw.replace(b"\r\n", b""), f"{name}: CRLF만")
            for line in text.splitlines():
                s = line.strip()
                if s and not s.lower().startswith(("rem", "echo")):
                    self.assertTrue(s.isascii(), f"{name}: 명령 줄은 ASCII만 — {s}")
            self.assertIn("-I -X utf8", text)

    def test_readme_template_fields(self):
        raw = (PKG / "payload" / "README.txt").read_bytes()
        self.assertTrue(raw.startswith(b"\xef\xbb\xbf"))
        text = raw.decode("utf-8-sig")
        filled = text.format(plugin_version="1", engine_version="2", python_version="3", acad_years="2027")
        self.assertIn("INSTALL.cmd", filled)
        self.assertNotIn("적합", filled)
        self.assertNotIn("승인이 아닙니다", filled.replace("선급 승인이 아닙니다", ""))


if __name__ == "__main__":
    unittest.main()
