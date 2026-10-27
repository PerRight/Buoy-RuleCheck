# -*- coding: utf-8 -*-
"""Buoy-RuleCheck 설치·제거 — 통합 설치 패키지 안의 내장 Python으로 실행한다.

  INSTALL.cmd    →  app\\python\\python.exe app\\buoy_setup.py install
  UNINSTALL.cmd  →  ...\\python\\python.exe ...\\buoy_setup.py uninstall

대상 PC에 Python·.NET SDK·인터넷·관리자 권한이 없어도 된다(전부 사용자 폴더에 설치).
  - 엔진·내장 Python  → %LOCALAPPDATA%\\BuoyRuleCheck\\app
  - AutoCAD 자동 로드 번들 → %APPDATA%\\Autodesk\\ApplicationPlugins\\BuoyRuleCheck.bundle
    (2027용 .NET 10 DLL과 2025·2026용 .NET 8 DLL 중 AutoCAD가 자기 버전에 맞는 것을 로드한다)
  - 사용자 환경변수 BRC_ENGINE_DIR·BRC_PYTHON → 플러그인이 엔진을 자동으로 켠다
  - 인터넷에서 받은 zip의 '차단'(Zone.Identifier) 표시를 지운다 — 남아 있으면 AutoCAD가 DLL을 로드하지 못한다
  - 화면 설정(%APPDATA%\\BuoyRuleCheck\\ui.json)·도면 옆 버전 기록(<도면>.brc)은 건드리지 않는다

도면 내용은 다루지 않는다. 네트워크는 설치 뒤 자체 시험에서 127.0.0.1(이 PC 안)에만 접속한다.
Windows가 아닌 곳(개발자 시험)에서는 레지스트리·프로세스·차단 해제를 건너뛰고 환경변수를 JSON 파일에 적는다.
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import socket
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

PRODUCT = "Buoy-RuleCheck"
BUNDLE = "BuoyRuleCheck.bundle"
OLD_BUNDLES = ("GaRuleCheck.bundle",)
ENV_NAMES = ("BRC_ENGINE_DIR", "BRC_PYTHON")
OLD_ENV_NAMES = ("GARC_ENGINE_DIR", "GARC_PYTHON")
APP_ITEMS = ("engine", "python", "samples", "buoy_setup.py", "UNINSTALL.cmd", "manifest.json")
IS_WIN = os.name == "nt"

# AutoCAD 릴리스 번호 → 연도. 번들의 R25(.NET 8)는 2025·2026, R26(.NET 10)은 2027.
SERIES_YEAR = {"R25.0": 2025, "R25.1": 2026, "R26.0": 2027}
TARGET_OF_YEAR = {2025: "R25", 2026: "R25", 2027: "R26"}

HERE = Path(__file__).resolve().parent


# ── 경로 ──────────────────────────────────────────────────────────────────────

def _env_dir(name: str) -> Path:
    v = os.environ.get(name)
    if not v:
        raise SystemExit(f"[{PRODUCT}] 환경변수 {name}가 없습니다 — Windows 사용자 계정에서 실행하세요.")
    return Path(v)


def install_root() -> Path:
    return _env_dir("LOCALAPPDATA") / "BuoyRuleCheck"


def install_dir() -> Path:
    return install_root() / "app"


def plugins_dir() -> Path:
    return _env_dir("APPDATA") / "Autodesk" / "ApplicationPlugins"


def log_path() -> Path:
    return install_root() / "install.log"


# ── 출력·기록 ────────────────────────────────────────────────────────────────

_log_lines: list[str] = []


def say(msg: str = "", *, mark: str = "") -> None:
    line = f"{mark} {msg}" if mark else msg
    try:
        print(line, flush=True)
    except UnicodeEncodeError:   # 콘솔이 한글을 못 쓰면(드묾) 깨지지 않게
        print(line.encode("ascii", "replace").decode("ascii"), flush=True)
    _log_lines.append(line)


def save_log() -> None:
    try:
        log_path().parent.mkdir(parents=True, exist_ok=True)
        with open(log_path(), "a", encoding="utf-8") as f:
            f.write(time.strftime("\n=== %Y-%m-%d %H:%M:%S ===\n"))
            f.write("\n".join(_log_lines) + "\n")
    except OSError:
        pass


# ── Windows 기능(다른 OS에서는 시험용 대체) ───────────────────────────────────

def _fake_env_file() -> Path:
    return install_root() / "user_env.json"   # 시험용(Windows가 아닐 때만)


def get_user_env(name: str) -> str | None:
    if IS_WIN:
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
                return str(winreg.QueryValueEx(k, name)[0])
        except OSError:
            return None
    try:
        return json.loads(_fake_env_file().read_text(encoding="utf-8")).get(name)
    except (OSError, ValueError):
        return None


def set_user_env(values: dict[str, str | None]) -> None:
    """사용자 환경변수(HKCU\\Environment)를 쓰거나(None이면) 지우고, 열린 프로그램에 바뀜을 알린다."""
    if IS_WIN:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment", 0, winreg.KEY_SET_VALUE) as k:
            for name, value in values.items():
                if value is None:
                    try:
                        winreg.DeleteValue(k, name)
                    except FileNotFoundError:
                        pass
                else:
                    winreg.SetValueEx(k, name, 0, winreg.REG_SZ, value)
        _broadcast_env_change()
        return
    f = _fake_env_file()
    try:
        data = json.loads(f.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        data = {}
    for name, value in values.items():
        if value is None:
            data.pop(name, None)
        else:
            data[name] = value
    f.parent.mkdir(parents=True, exist_ok=True)
    f.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def _broadcast_env_change() -> None:
    """WM_SETTINGCHANGE("Environment") — 새로 켜는 AutoCAD가 바뀐 환경변수를 받게."""
    try:
        import ctypes
        from ctypes import wintypes
        result = wintypes.DWORD()
        ctypes.windll.user32.SendMessageTimeoutW(0xFFFF, 0x001A, 0, "Environment", 0x0002, 5000, ctypes.byref(result))
    except Exception:
        pass


def autocad_running() -> bool:
    if not IS_WIN:
        return os.environ.get("BRC_SETUP_FAKE_ACAD_RUNNING") == "1"
    try:
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq acad.exe", "/NH"], capture_output=True, text=True,
                             creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0), timeout=20).stdout
        return "acad.exe" in out.lower()
    except (OSError, subprocess.SubprocessError):
        return False


def stop_engines() -> list[str]:
    """켜져 있는 엔진(python -m brc serve / 옛 python -m garc serve)을 끈다. 끈 PID 목록."""
    if not IS_WIN:
        return []
    ps = (
        "Get-CimInstance Win32_Process -Filter \"Name LIKE 'python%'\" -ErrorAction SilentlyContinue | "
        "Where-Object { $_.CommandLine -match '-m\\s+(garc|brc)\\s+serve' } | "
        "ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue; $_.ProcessId }"
    )
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", ps], capture_output=True,
                             text=True, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0), timeout=60).stdout
        pids = [x.strip() for x in out.splitlines() if x.strip().isdigit()]
        if pids:
            time.sleep(1.0)   # 파일 잠금이 풀리도록
        return pids
    except (OSError, subprocess.SubprocessError):
        return []


def unblock(root: Path) -> int:
    """인터넷에서 받은 표시(NTFS Zone.Identifier 스트림)를 지운다. 지운 개수."""
    if not IS_WIN or not root.exists():
        return 0
    n = 0
    for p in [root, *root.rglob("*")]:
        if p.is_file():
            try:
                os.remove(str(p) + ":Zone.Identifier")
                n += 1
            except OSError:
                pass
    return n


def detect_autocad() -> dict[int, str]:
    """설치된 AutoCAD: {연도: 설치 폴더}. 레지스트리와 Program Files를 함께 본다."""
    found: dict[int, str] = {}
    if IS_WIN:
        import winreg
        for view in (winreg.KEY_WOW64_64KEY, 0):
            try:
                with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Autodesk\AutoCAD", 0, winreg.KEY_READ | view) as k:
                    i = 0
                    while True:
                        try:
                            series = winreg.EnumKey(k, i)
                        except OSError:
                            break
                        i += 1
                        year = SERIES_YEAR.get(series)
                        if year is None:
                            continue
                        loc = ""
                        try:
                            with winreg.OpenKey(k, series) as sk:
                                j = 0
                                while not loc:
                                    try:
                                        prod = winreg.EnumKey(sk, j)
                                    except OSError:
                                        break
                                    j += 1
                                    try:
                                        with winreg.OpenKey(sk, prod) as pk:
                                            loc = str(winreg.QueryValueEx(pk, "AcadLocation")[0])
                                    except OSError:
                                        pass
                        except OSError:
                            pass
                        found.setdefault(year, loc)
            except OSError:
                pass
        pf = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Autodesk"
        for year in (2025, 2026, 2027):
            exe = pf / f"AutoCAD {year}" / "acad.exe"
            if exe.exists():
                found[year] = found.get(year) or str(exe.parent)
    else:
        for y in os.environ.get("BRC_SETUP_FAKE_ACAD", "").split(","):
            if y.strip().isdigit():
                found[int(y)] = f"(시험) AutoCAD {y}"
    return dict(sorted(found.items()))


def python_exe(app: Path) -> Path:
    if IS_WIN:
        return app / "python" / "python.exe"
    return Path(sys.executable)   # 시험: 이 Python으로 엔진을 돌려 본다


def detached_delete(path: Path) -> None:
    """지금 실행 중인 내장 Python이 들어 있는 폴더를, 이 프로그램이 끝난 뒤 지운다."""
    if not IS_WIN:
        shutil.rmtree(path, ignore_errors=True)
        return
    cmd = f'ping -n 4 127.0.0.1 >nul & rmdir /s /q "{path}"'
    flags = getattr(subprocess, "DETACHED_PROCESS", 0x8) | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0x200) \
        | getattr(subprocess, "CREATE_NO_WINDOW", 0)
    subprocess.Popen(["cmd", "/c", cmd], creationflags=flags, close_fds=True, cwd=str(path.parent))


# ── 공통 단계 ────────────────────────────────────────────────────────────────

def wait_autocad_closed(assume_yes: bool) -> None:
    while autocad_running():
        say("AutoCAD가 켜져 있습니다. 플러그인 파일이 잠겨 있어 설치·제거할 수 없어요.", mark="[주의]")
        if assume_yes or not sys.stdin or not sys.stdin.isatty():
            raise SystemExit(2)
        ans = input("  AutoCAD를 모두 닫은 뒤 Enter를 누르세요(그만두려면 q + Enter): ").strip().lower()
        if ans == "q":
            raise SystemExit(2)


def read_manifest(app: Path) -> dict:
    try:
        return json.loads((app / "manifest.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def bundle_targets(bundle: Path) -> list[str]:
    return [t for t in ("R25", "R26") if (bundle / "Contents" / "Win64" / t / "BuoyRuleCheck.dll").exists()]


def replace_dir(src: Path, dst: Path) -> None:
    if dst.exists():
        shutil.rmtree(dst)
    shutil.copytree(src, dst, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))


def free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def self_test(app: Path) -> tuple[bool, str]:
    """설치된 내장 Python으로 엔진을 잠깐 켜 /v1/health를 확인한다(127.0.0.1만)."""
    py = python_exe(app)
    engine = app / "engine"
    env = dict(os.environ)
    env.pop("PYTHONPATH", None)
    env.pop("PYTHONHOME", None)
    if not IS_WIN:
        env["PYTHONPATH"] = str(engine)   # 시험용 Python에는 ._pth가 없다
    flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
    # 1) 다른 폴더에서도 엔진을 찾는지(내장 Python의 ._pth에 ..\engine)
    try:
        r = subprocess.run([str(py), "-c", "import brc, garc; print(brc.__version__)"], cwd=str(app),
                           capture_output=True, text=True, env=env, creationflags=flags, timeout=60)
    except (OSError, subprocess.SubprocessError) as ex:
        return False, f"내장 Python을 실행하지 못함: {ex}"
    if r.returncode != 0:
        return False, "엔진을 불러오지 못함: " + (r.stderr.strip().splitlines() or ["?"])[-1]
    version = r.stdout.strip()
    # 2) 서버를 잠깐 켜서 응답 확인
    port = free_port()
    out = install_root() / "selftest.log"
    with open(out, "w", encoding="utf-8", errors="replace") as lf:
        try:
            p = subprocess.Popen([str(py), "-m", "brc", "serve", "--port", str(port)], cwd=str(engine), env=env,
                                 stdout=lf, stderr=subprocess.STDOUT, creationflags=flags)
        except OSError as ex:
            return False, f"엔진 서버를 켜지 못함: {ex}"
        try:
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))   # 프록시 설정과 무관하게 이 PC 안으로만
            deadline = time.time() + 20
            while time.time() < deadline:
                if p.poll() is not None:
                    break
                try:
                    with opener.open(f"http://127.0.0.1:{port}/v1/health", timeout=2) as resp:
                        body = json.loads(resp.read().decode("utf-8"))
                        server = resp.headers.get("Server", "")
                    if body.get("ok") and str(server).startswith("brc/"):
                        return True, f"엔진 {body.get('version', version)} 응답 확인(규칙 {len(body.get('rules', []))}개)"
                    return False, f"엔진 응답이 이상함: {server} {body}"
                except OSError:
                    time.sleep(0.3)
            return False, f"엔진이 20초 안에 응답하지 않음 — {out} 확인"
        finally:
            if p.poll() is None:
                p.terminate()
                try:
                    p.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    p.kill()


# ── 설치 ─────────────────────────────────────────────────────────────────────

def install(assume_yes: bool) -> int:
    src = HERE
    man = read_manifest(src)
    say(f"{PRODUCT} 설치 — 플러그인 {man.get('plugin_version', '?')} · 엔진 {man.get('engine_version', '?')} · Python {man.get('python_version', '?')}")
    say("관리자 권한·인터넷·Python·.NET SDK가 없어도 됩니다. 이 사용자 계정에만 설치합니다.")
    say()

    # 0) 패키지 확인
    bundle_src = src / BUNDLE
    targets = bundle_targets(bundle_src)
    missing = [x for x in ("engine", "python", BUNDLE) if not (src / x).exists()] if src != install_dir() else []
    if missing or not targets:
        say(f"패키지가 온전하지 않습니다(없음: {', '.join(missing) or '플러그인 DLL'}). zip을 다시 풀어 주세요.", mark="[실패]")
        return 1

    # 1) AutoCAD
    say("[1/6] AutoCAD 확인")
    acad = detect_autocad()
    if not acad:
        say("AutoCAD 2025·2026·2027을 찾지 못했어요. 설치는 계속하고, AutoCAD를 설치한 뒤 켜면 자동으로 로드됩니다.", mark="[주의]")
    for year, loc in acad.items():
        ok = TARGET_OF_YEAR[year] in targets
        say(f"AutoCAD {year}  {loc}  → {'지원' if ok else '이 패키지에 그 버전용 플러그인이 없음'}", mark="[OK]" if ok else "[주의]")
    if acad and not any(TARGET_OF_YEAR[y] in targets for y in acad):
        say("이 PC의 AutoCAD 버전에 맞는 플러그인이 패키지에 없습니다. 패키지를 만든 사람에게 알려 주세요.", mark="[주의]")
    wait_autocad_closed(assume_yes)

    # 2) 켜져 있던 엔진 끄기
    say("[2/6] 켜져 있던 검사 엔진 정리")
    pids = stop_engines()
    say(f"엔진 {len(pids)}개를 껐어요(PID {', '.join(pids)})" if pids else "켜져 있는 엔진 없음", mark="[OK]")

    # 3) 엔진·내장 Python 복사
    say("[3/6] 엔진·내장 Python 설치")
    app = install_dir()
    try:
        if src != app:
            app.mkdir(parents=True, exist_ok=True)
            for name in APP_ITEMS:
                s = src / name
                if not s.exists():
                    continue
                d = app / name
                if s.is_dir():
                    replace_dir(s, d)
                else:
                    shutil.copy2(s, d)
    except PermissionError as ex:
        say(f"파일이 잠겨 있어 바꾸지 못했어요({ex.filename}). 검사 엔진 창·AutoCAD를 닫고 다시 실행하세요.", mark="[실패]")
        return 1
    say(str(app), mark="[OK]")

    # 4) AutoCAD 자동 로드 번들
    say("[4/6] AutoCAD 자동 로드 번들 설치")
    plugins = plugins_dir()
    plugins.mkdir(parents=True, exist_ok=True)
    try:
        for old in OLD_BUNDLES:
            if (plugins / old).exists():
                shutil.rmtree(plugins / old)
                say(f"옛 번들 삭제: {plugins / old}", mark="[OK]")
        replace_dir(bundle_src, plugins / BUNDLE)
    except PermissionError as ex:
        say(f"번들 파일이 잠겨 있어요({ex.filename}). AutoCAD를 모두 닫고 다시 실행하세요.", mark="[실패]")
        return 1
    names = {"R25": "2025·2026(.NET 8)", "R26": "2027(.NET 10)"}
    say(f"{plugins / BUNDLE}  — {', '.join(names[t] for t in targets)}", mark="[OK]")
    n = unblock(app) + unblock(plugins / BUNDLE)
    if n:
        say(f"인터넷에서 받은 파일 표시 {n}개를 해제했어요(AutoCAD가 DLL을 막지 않게)", mark="[OK]")

    # 5) 환경변수
    say("[5/6] 엔진 자동 실행 설정(사용자 환경변수)")
    py = python_exe(app)
    set_user_env({"BRC_ENGINE_DIR": str(app / "engine"), "BRC_PYTHON": str(py),
                  **{k: None for k in OLD_ENV_NAMES}})
    say(f"BRC_ENGINE_DIR = {app / 'engine'}", mark="[OK]")
    say(f"BRC_PYTHON     = {py}", mark="[OK]")
    if get_user_env("BRC_ENGINE_URL"):
        say("BRC_ENGINE_URL이 따로 지정돼 있어요 — 엔진 주소를 바꾼 게 아니라면 지우세요.", mark="[주의]")

    # 6) 자체 시험
    say("[6/6] 자체 시험(엔진을 잠깐 켜서 이 PC 안에서만 응답 확인)")
    ok, msg = self_test(app)
    say(msg, mark="[OK]" if ok else "[실패]")

    say()
    if ok:
        say("설치 완료.", mark="[OK]")
    else:
        say("설치는 했지만 엔진 자체 시험이 실패했어요. 위 메시지와 install.log를 알려 주세요.", mark="[주의]")
    say("  1. AutoCAD를 켜면 리본에 'Buoy-RuleCheck' 탭이 생깁니다. 처음 한 번 보안 경고가 뜨면 '항상 로드'.")
    say("  2. 도면에서 BRC 를 입력하세요. 연결 확인은 BRCHEALTH.")
    say(f"  3. 시연용 샘플 도면: {app / 'samples'}")
    say(f"  제거: {app / 'UNINSTALL.cmd'}  (또는 받은 폴더의 UNINSTALL.cmd)")
    return 0 if ok else 3


# ── 제거 ─────────────────────────────────────────────────────────────────────

def uninstall(assume_yes: bool) -> int:
    say(f"{PRODUCT} 제거")
    wait_autocad_closed(assume_yes)
    pids = stop_engines()
    if pids:
        say(f"켜져 있던 엔진 {len(pids)}개를 껐어요", mark="[OK]")
    plugins = plugins_dir()
    for name in (BUNDLE, *OLD_BUNDLES):
        b = plugins / name
        if b.exists():
            try:
                shutil.rmtree(b)
                say(f"삭제: {b}", mark="[OK]")
            except PermissionError as ex:
                say(f"지우지 못함({ex.filename}) — AutoCAD를 닫고 다시 실행하세요.", mark="[실패]")
                return 1
    # 환경변수: 이 패키지가 설치한 값이거나 옛 이름이면 지운다(개발용 install.ps1이 저장소 엔진을 가리키게 한 값도 함께 정리)
    set_user_env({k: None for k in (*ENV_NAMES, *OLD_ENV_NAMES)})
    say("환경변수 BRC_ENGINE_DIR·BRC_PYTHON 삭제", mark="[OK]")
    app = install_dir()
    if app.exists():
        running_inside = Path(sys.executable).resolve().is_relative_to(app.resolve()) or HERE == app.resolve()
        if running_inside:
            for name in ("engine", "samples", "manifest.json"):
                p = app / name
                if p.is_dir():
                    shutil.rmtree(p, ignore_errors=True)
                elif p.exists():
                    p.unlink()
            detached_delete(app)   # 실행 중인 python.exe·이 스크립트는 끝난 뒤 지움
            say(f"삭제(잠시 뒤 마무리): {app}", mark="[OK]")
        else:
            shutil.rmtree(app, ignore_errors=True)
            say(f"삭제: {app}", mark="[OK]")
    say("제거 완료. 화면 설정(%APPDATA%\\BuoyRuleCheck)과 도면 옆 버전 기록(<도면>.brc)은 그대로 둡니다.", mark="[OK]")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="buoy_setup", description=f"{PRODUCT} 설치·제거")
    ap.add_argument("action", choices=("install", "uninstall"))
    ap.add_argument("--yes", action="store_true", help="묻지 않음(AutoCAD가 켜져 있으면 멈춤)")
    a = ap.parse_args(argv)
    try:
        return install(a.yes) if a.action == "install" else uninstall(a.yes)
    except SystemExit as ex:
        code = ex.code if isinstance(ex.code, int) else 1
        if not isinstance(ex.code, int) and ex.code:
            say(str(ex.code), mark="[실패]")
        say("설치·제거를 멈췄어요.", mark="[주의]")
        return code
    except Exception as ex:   # 예상 못 한 오류도 창이 바로 닫히지 않게 알리고 기록
        say(f"예상하지 못한 오류: {type(ex).__name__}: {ex}", mark="[실패]")
        return 1
    finally:
        save_log()


if __name__ == "__main__":
    sys.exit(main())
