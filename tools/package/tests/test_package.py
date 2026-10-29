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

    def test_only_unsupported_autocad_is_not_reported_as_done(self):
        r = self.run_setup(self.pkg / "buoy_setup.py", "install", BRC_SETUP_FAKE_ACAD="2026")
        self.assertEqual(r.returncode, 4, r.stdout + r.stderr)
        self.assertNotIn("설치 완료", r.stdout)
        self.assertIn("동작하지 않아요", r.stdout)

    def test_locked_upgrade_changes_nothing(self):
        """옛 설치 폴더를 옮기지 못하면(파일 잠김) 엔진·Python·번들 모두 옛것 그대로, 임시 폴더도 남기지 않는다."""
        r = self.run_setup(self.pkg / "buoy_setup.py", "install")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        app = self.tmp / "Local" / "BuoyRuleCheck" / "app"
        (app / "engine" / "OLD_MARK").write_text("old")
        (self.pkg / "engine" / "NEW_MARK").write_text("new")
        code = (
            "import importlib.util, pathlib, sys\n"
            f"spec = importlib.util.spec_from_file_location('bs', r'{self.pkg / 'buoy_setup.py'}')\n"
            "bs = importlib.util.module_from_spec(spec); spec.loader.exec_module(bs)\n"
            "real = pathlib.Path.rename\n"
            "def rename(self, target):\n"
            "    if self.name == 'app' and '.old-' in str(target):\n"
            "        raise PermissionError(13, 'locked', str(self))\n"
            "    return real(self, target)\n"
            "pathlib.Path.rename = rename\n"
            "sys.exit(bs.main(['install', '--yes']))\n")
        r = subprocess.run([sys.executable, "-I", "-c", code], capture_output=True, text=True, env=self.env,
                           cwd=str(self.tmp), stdin=subprocess.DEVNULL, timeout=120)
        self.assertEqual(r.returncode, 1, r.stdout + r.stderr)
        self.assertIn("사용 중인 파일", r.stdout)
        self.assertIn("이전 설치는 그대로", r.stdout)
        self.assertTrue((app / "engine" / "OLD_MARK").exists(), "옛 설치가 그대로")
        self.assertFalse((app / "engine" / "NEW_MARK").exists())
        self.assertFalse(list(app.parent.glob("app.new*")), "임시 폴더를 남기지 않음")

    def _swap_with_failing_rename(self, should_fail) -> subprocess.CompletedProcess:
        """설치 중 특정 이름 바꾸기만 실패시키고 install을 돌린다(should_fail(src_name, target_name) -> bool)."""
        code = (
            "import importlib.util, pathlib, sys\n"
            f"spec = importlib.util.spec_from_file_location('bs', r'{self.pkg / 'buoy_setup.py'}')\n"
            "bs = importlib.util.module_from_spec(spec); spec.loader.exec_module(bs)\n"
            "bs.time.sleep = lambda s: None\n"
            "real = pathlib.Path.rename\n"
            f"FAIL = {should_fail}\n"
            "def rename(self, target):\n"
            "    if FAIL(self.name, pathlib.Path(target).name):\n"
            "        raise PermissionError(13, 'locked', str(self))\n"
            "    return real(self, target)\n"
            "pathlib.Path.rename = rename\n"
            "sys.exit(bs.main(['install', '--yes']))\n")
        return subprocess.run([sys.executable, "-I", "-c", code], capture_output=True, text=True, env=self.env,
                              cwd=str(self.tmp), stdin=subprocess.DEVNULL, timeout=120)

    def _mark_old_install(self):
        r = self.run_setup(self.pkg / "buoy_setup.py", "install")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        app = self.tmp / "Local" / "BuoyRuleCheck" / "app"
        bundle = self.tmp / "Roaming" / "Autodesk" / "ApplicationPlugins" / "BuoyRuleCheck.bundle"
        (app / "engine" / "OLD_MARK").write_text("old")
        (bundle / "OLD_MARK").write_text("old")
        (self.pkg / "engine" / "NEW_MARK").write_text("new")
        return app, bundle

    def _assert_old_intact(self, app, bundle):
        self.assertTrue((app / "engine" / "OLD_MARK").exists(), "설치 폴더는 옛것 그대로")
        self.assertFalse((app / "engine" / "NEW_MARK").exists())
        self.assertTrue((bundle / "OLD_MARK").exists(), "번들도 옛것 그대로")
        for d in (app.parent, bundle.parent):
            left = [p.name for p in d.iterdir() if ".new" in p.name or ".old-" in p.name or ".bad-" in p.name]
            self.assertEqual(left, [], "임시 폴더를 남기지 않음")

    def test_bundle_locked_after_app_swapped_rolls_back_both(self):
        """설치 폴더는 바꿨는데 번들을 못 바꾸면 설치 폴더도 되돌린다(엔진 새것·플러그인 옛것이 남지 않게)."""
        app, bundle = self._mark_old_install()
        r = self._swap_with_failing_rename("lambda a, b: a == 'BuoyRuleCheck.bundle' and '.old-' in b")
        self.assertEqual(r.returncode, 1, r.stdout + r.stderr)
        self._assert_old_intact(app, bundle)

    def test_new_into_place_fails_restores_old(self):
        """옛것을 비킨 뒤 새것을 넣지 못하면(백신이 새 파일을 잡음) 옛것을 제자리로 — 설치 폴더가 사라지지 않게."""
        app, bundle = self._mark_old_install()
        r = self._swap_with_failing_rename("lambda a, b: a.startswith('app.new-') and b == 'app'")
        self.assertEqual(r.returncode, 1, r.stdout + r.stderr)
        self._assert_old_intact(app, bundle)

    def test_uninstall_cleans_leftovers(self):
        r = self.run_setup(self.pkg / "buoy_setup.py", "install")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        root = self.tmp / "Local" / "BuoyRuleCheck"
        plugins = self.tmp / "Roaming" / "Autodesk" / "ApplicationPlugins"
        for p in (root / "app.old-1", root / "app.new-2", plugins / "BuoyRuleCheck.bundle.old-3"):
            p.mkdir()
        r = self.run_setup(self.pkg / "buoy_setup.py", "uninstall")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(sorted(p.name for p in root.iterdir() if p.is_dir()), [])
        self.assertEqual(list(plugins.iterdir()), [])

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


class WindowsCommands(unittest.TestCase):
    """Windows에서만 도는 명령 줄을 문자열로 확인(실행은 못 함)."""

    def test_detached_delete_is_one_string_with_retry_loop(self):
        bs = _load("buoy_setup")
        calls = []
        orig_win, orig_popen = bs.IS_WIN, bs.subprocess.Popen
        bs.IS_WIN = True
        bs.subprocess.Popen = lambda *a, **k: calls.append((a, k))
        try:
            bs.detached_delete(Path(r"C:/Users/홍 길동/AppData/Local/BuoyRuleCheck/app"))
        finally:
            bs.IS_WIN, bs.subprocess.Popen = orig_win, orig_popen
        (args, kw), = calls
        cmd = args[0]
        self.assertIsInstance(cmd, str, "목록으로 넘기면 따옴표가 \\\"로 바뀐다")
        self.assertTrue(cmd.startswith('cmd /d /c "for /l %i in (1,1,60) do @('))
        self.assertTrue(cmd.endswith(')"'))
        self.assertIn('rmdir /s /q "app"', cmd)
        self.assertTrue(cmd.isascii(), "한글·공백 경로는 명령 줄이 아니라 cwd로")
        self.assertTrue(kw["cwd"].endswith("BuoyRuleCheck"))


class NuGetFallback(unittest.TestCase):
    """10-29 사용자 PC: AutoCAD.NET 25.0.2가 net10.0 전용이라 25.0.* 빌드가 NU1202로 실패 → 버전을 하나씩 확인."""

    def setUp(self):
        self._open, self._build, self._say = mp._open, mp.dotnet_build, mp.say
        self.calls: list[str] = []
        self.nuspec = {
            "25.0.2": '<package><metadata><dependencies><group targetFramework="net10.0" /></dependencies></metadata></package>',
            "25.0.1": '<package><metadata><dependencies><group targetFramework="net8.0-windows7.0" /></dependencies></metadata></package>',
            "25.0.0": '<package><metadata><dependencies><group targetFramework="net8.0" /></dependencies></metadata></package>',
        }
        index = {"versions": ["24.3.0", "25.0.0", "25.0.1", "25.0.2", "25.0.3-beta", "25.1.0", "26.0.0"]}

        def fake_open(url, method="GET"):
            if url.endswith("index.json"):
                body = json.dumps(index)
            else:
                v = url.rsplit("/", 2)[-2]
                if v not in self.nuspec:
                    raise OSError("404")
                body = self.nuspec[v]
            return contextlib.closing(io.BytesIO(body.encode("utf-8")))

        mp._open = fake_open
        mp.say = lambda *_a, **_k: None
        self.ok_versions: set[str] = {"25.0.1"}

        def fake_build(target, out, acad_dir, nuget, extra):
            self.calls.append(nuget)
            return nuget in self.ok_versions

        mp.dotnet_build = fake_build

    def tearDown(self):
        mp._open, mp.dotnet_build, mp.say = self._open, self._build, self._say

    def test_versions_newest_first_in_series_without_prerelease(self):
        self.assertEqual(mp.nuget_versions("25.0.*"), ["25.0.2", "25.0.1", "25.0.0"])
        self.assertEqual(mp.nuget_versions("25.0.1"), ["25.0.1"])

    def test_skips_net10_only_and_builds_first_net8(self):
        self.assertTrue(mp.build_r25_nuget("25.0.*", Path("unused"), []))
        self.assertEqual(self.calls, ["25.0.1"])          # 25.0.2(net10 전용)는 빌드하지 않음

    def test_unknown_nuspec_is_tried_and_build_count_is_capped(self):
        self.nuspec.clear()                                # nuspec을 못 읽으면 빌드해 보고 판단
        self.ok_versions = set()
        self.assertFalse(mp.build_r25_nuget("25.0.*", Path("unused"), []))
        self.assertEqual(self.calls, ["25.0.2", "25.0.1", "25.0.0"][: mp.NUGET_MAX_BUILDS])

    def test_index_unreachable_falls_back_to_spec(self):
        def down(url, method="GET"):
            raise OSError("offline")
        mp._open = down
        self.ok_versions = {"25.0.*"}
        self.assertTrue(mp.build_r25_nuget("25.0.*", Path("unused"), []))
        self.assertEqual(self.calls, ["25.0.*"])


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

    def test_zip_name_marks_2027_only(self):
        with tempfile.TemporaryDirectory() as t:
            stage = Path(t) / "Buoy-RuleCheck_9.9.9"
            (stage / "app").mkdir(parents=True)
            (stage / "INSTALL.cmd").write_bytes(b"@echo off\r\n")
            self.assertEqual(mp.make_zip(stage, "_2027only").name, "Buoy-RuleCheck_9.9.9_2027only_setup.zip")
            z = mp.make_zip(stage)
            self.assertEqual(z.name, "Buoy-RuleCheck_9.9.9_setup.zip")
            with zipfile.ZipFile(z) as zz:
                self.assertIn("Buoy-RuleCheck_9.9.9/INSTALL.cmd", zz.namelist())

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
