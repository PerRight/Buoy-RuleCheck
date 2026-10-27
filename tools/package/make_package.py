# -*- coding: utf-8 -*-
"""Buoy-RuleCheck 통합 설치 패키지 만들기 — 개발 PC에서 한 번 실행한다(.NET SDK·AutoCAD 2027·인터넷 필요).

결과: dist/Buoy-RuleCheck_<버전>_setup.zip
  다른 PC에서는 압축을 풀고 INSTALL.cmd 더블클릭이면 끝(Python·.NET SDK·인터넷·관리자 권한 불필요).
  Buoy-RuleCheck_<버전>/
    INSTALL.cmd  UNINSTALL.cmd  README.txt
    app/
      buoy_setup.py  UNINSTALL.cmd  manifest.json
      python/                     Windows 내장(embeddable) Python — ._pth에 ..\\engine 추가
      engine/brc, engine/garc     규칙 엔진(표준 라이브러리만)
      samples/                    시연용 합성 도면
      BuoyRuleCheck.bundle/       AutoCAD 자동 로드 번들
        PackageContents.xml       R25(2025·2026, .NET 8)와 R26(2027, .NET 10)을 버전별로 로드
        Contents/Win64/R26/       AutoCAD 2027용 DLL
        Contents/Win64/R25/       AutoCAD 2025·2026용 DLL(만들 수 있을 때)

사용 예(저장소 루트에서):
  python tools/package/make_package.py
      AutoCAD 2027 DLL은 C:\\Program Files\\Autodesk\\AutoCAD 2027에서, 2025·2026용은 아래 순서로 찾는다:
        1) --acad25 <폴더>  2) C:\\Program Files\\Autodesk\\AutoCAD 2025 / 2026  3) tools/package/refs/R25
        4) Autodesk 공식 NuGet 참조(AutoCAD.NET 25.0.*, 인터넷)  — 모두 안 되면 실패(--allow-2027-only면 2027용만)
      내장 Python은 python.org에서 최신 3.14.x를 받아 tools/package/cache에 둔다(서명 확인).
  python tools/package/make_package.py --python-zip D:\\python-3.14.2-embed-amd64.zip   (미리 받은 파일)
  python tools/package/make_package.py --acad25 D:\\refs\\R25        (2025·2026 PC에서 복사한 DLL 4개:
      acmgd.dll · acdbmgd.dll · accoremgd.dll · AdWindows.dll — Autodesk 파일이므로 저장소에 올리지 않는다)

도면은 다루지 않는다. 인터넷은 Python·NuGet 내려받기에만 쓴다.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.request
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CSPROJ = ROOT / "autocad" / "BuoyRuleCheck" / "BuoyRuleCheck.csproj"
PAYLOAD = HERE / "payload"
CACHE = HERE / "cache"
IS_WIN = os.name == "nt"
ACAD_REF_DLLS = ("acmgd.dll", "acdbmgd.dll", "accoremgd.dll", "AdWindows.dll")
PY_FTP = "https://www.python.org/ftp/python/"
PY_TESTED = ((3, 10), (3, 14))   # 엔진 CI가 도는 범위
PRODUCT_CODE = "{4407E2A1-5A9B-4B16-9E78-8BCA66DA04C5}"
UPGRADE_CODE = "{BC4149B4-2FE9-43D7-A2E1-1ADA2E39588A}"

TARGETS = {
    # 이름: (대상 프레임워크, AutoCAD 릴리스 범위, 설명)
    "R25": ("net8.0-windows", ("R25.0", "R25.1"), "AutoCAD 2025·2026 (.NET 8)"),
    "R26": ("net10.0-windows", ("R26.0", "R26.0"), "AutoCAD 2027 (.NET 10)"),
}


def say(msg: str = "") -> None:
    print(msg, flush=True)


def fail(msg: str) -> "None":
    say(f"[실패] {msg}")
    raise SystemExit(1)


# ── 버전 ─────────────────────────────────────────────────────────────────────

def plugin_version() -> str:
    xml = (ROOT / "autocad" / "bundle" / "PackageContents.xml").read_text(encoding="utf-8")
    m = re.search(r'AppVersion="([^"]+)"', xml)
    return m.group(1) if m else "0.0.0"


def engine_version() -> str:
    src = (ROOT / "engine" / "brc" / "__init__.py").read_text(encoding="utf-8")
    m = re.search(r'__version__\s*=\s*"([^"]+)"', src)
    return m.group(1) if m else "?"


def git_commit() -> str:
    try:
        return subprocess.run(["git", "-C", str(ROOT), "rev-parse", "--short", "HEAD"], capture_output=True,
                              encoding="utf-8", errors="replace", timeout=20).stdout.strip()
    except (OSError, subprocess.SubprocessError):
        return ""


# ── 플러그인 빌드 ────────────────────────────────────────────────────────────

def has_refs(d: Path | None) -> bool:
    return bool(d) and all((d / x).exists() for x in ACAD_REF_DLLS)


def program_files_acad(year: int) -> Path:
    return Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Autodesk" / f"AutoCAD {year}"


def dotnet_build(target: str, out: Path, acad_dir: Path | None, nuget: str | None, extra: list[str]) -> bool:
    tfm = TARGETS[target][0]
    if out.exists():
        shutil.rmtree(out)
    # obj는 기본 위치 그대로(바꾸면 기존 obj/의 자동 생성 .cs가 컴파일에 섞인다). 대상 프레임워크별 하위 폴더로 나뉜다.
    # 대상 프레임워크는 BrcTfm으로(csproj가 TargetFramework에 넣음). -p:TargetFramework=는 NuGet 복원이 무시해
    # project.assets.json이 net10 기준으로 남고 net8 빌드가 NETSDK1005로 실패한다(2026-10-27 검증 H1).
    cmd = ["dotnet", "build", str(CSPROJ), "-c", "Release", "-o", str(out), "--nologo", "-v", "q",
           f"-p:BrcTfm={tfm}"]
    if nuget:
        cmd.append(f"-p:AcadNuGet={nuget}")
    else:
        cmd.append(f"-p:AcadDir={acad_dir}")
    cmd += extra
    say(f"    dotnet build ({TARGETS[target][2]}, {'NuGet ' + nuget if nuget else acad_dir})")
    try:
        # 출력은 영어·UTF-8로 받는다(한국어 SDK 메시지·cp949 경로에서도 오류 줄을 읽을 수 있게)
        env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en-US", DOTNET_CLI_FORCE_UTF8_ENCODING="true",
                   DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
        r = subprocess.run(cmd, capture_output=True, encoding="utf-8", errors="replace", timeout=900, env=env)
    except FileNotFoundError:
        fail("dotnet 명령이 없습니다 — .NET 10 SDK를 설치하세요(패키지를 만드는 PC에만 필요).")
    except subprocess.TimeoutExpired:
        say("    빌드가 15분 안에 끝나지 않음")
        return False
    lines = [x for x in (r.stdout + r.stderr).splitlines() if " error " in x or "error CS" in x or "error NU" in x
             or "error MSB" in x]
    if r.returncode != 0 or not (out / "BuoyRuleCheck.dll").exists():
        for x in lines[:15]:
            say("      " + x.strip())
        if not lines:
            say("      " + "\n      ".join((r.stdout + r.stderr).strip().splitlines()[-10:]))
        return False
    rc = out / "BuoyRuleCheck.runtimeconfig.json"
    want = tfm.split("-")[0]
    if rc.exists() and f'"tfm": "{want}"' not in rc.read_text(encoding="utf-8"):
        say(f"    빌드 결과의 대상 프레임워크가 {want}이 아님 — {rc}")
        return False
    return True


def build_plugins(a: argparse.Namespace, work: Path) -> dict[str, Path]:
    built: dict[str, Path] = {}
    extra = list(a.dotnet_arg or [])
    say("[1/4] 플러그인 빌드")
    d27 = Path(a.acad2027) if a.acad2027 else program_files_acad(2027)
    if not has_refs(d27):
        fail(f"AutoCAD 2027 참조 DLL이 없습니다: {d27}  (--acad2027 <폴더>로 지정)")
    # 2025·2026 (.NET 8)을 먼저, 2027을 마지막에 — 공유 obj/의 복원 상태가 평소(.NET 10)로 끝나게(검증 N5)
    if a.skip_r25:
        say("    [건너뜀] AutoCAD 2025·2026용(--skip-r25)")
    else:
        built.update(build_r25(a, work, extra))
    if not dotnet_build("R26", work / "R26", d27, None, extra):
        fail("AutoCAD 2027용 빌드 실패 — 위 오류를 확인하세요.")
    built["R26"] = work / "R26"
    say("    [OK] AutoCAD 2027용")
    return built


def build_r25(a: argparse.Namespace, work: Path, extra: list[str]) -> dict[str, Path]:
    cands: list[tuple[str, Path | None, str | None]] = []
    if a.acad25:
        cands.append(("지정 폴더", Path(a.acad25), None))
    for year in (2025, 2026):   # 2025 참조로 만든 DLL이 2026에서도 로드된다(반대는 2026에만 있는 API가 섞일 수 있음)
        cands.append((f"AutoCAD {year} 설치 폴더", program_files_acad(year), None))
    cands.append(("tools/package/refs/R25", HERE / "refs" / "R25", None))
    if not a.no_nuget:
        cands.append(("Autodesk NuGet 참조", None, a.acad_nuget))
    tried = False
    for label, d, nuget in cands:
        if nuget is None and not has_refs(d):
            continue
        tried = True
        say(f"    2025·2026용 참조: {label}")
        if dotnet_build("R25", work / "R25", d, nuget, extra):
            say("    [OK] AutoCAD 2025·2026용")
            return {"R25": work / "R25"}
        say(f"    [주의] {label}로 빌드하지 못함")
    why = ("참조 DLL은 찾았지만 빌드하지 못했습니다(위 오류 — NU1101이면 .NET 8 참조 팩을 받을 인터넷이 필요)"
           if tried else "참조 DLL을 찾지 못했습니다")
    msg = (f"AutoCAD 2025·2026용을 만들지 못했습니다: {why}. 2025·2026이 깔린 PC의 DLL 4개("
           + ", ".join(ACAD_REF_DLLS) + ")를 tools/package/refs/R25에 복사하거나 --acad25로 지정하세요.")
    if not a.allow_2027_only:
        fail(msg + " 2027용만 만들려면 --allow-2027-only.")
    say("    [주의] " + msg + " 2027용만 넣고 zip 이름에 _2027only를 붙입니다.")
    return {}


# ── 내장 Python ──────────────────────────────────────────────────────────────

def _open(url: str, method: str = "GET"):
    req = urllib.request.Request(url, method=method, headers={"User-Agent": "Buoy-RuleCheck-packager"})
    return urllib.request.urlopen(req, timeout=60)


def latest_python(minor: str) -> str:
    with _open(PY_FTP) as r:
        html = r.read().decode("utf-8", "replace")
    vers = sorted({tuple(int(x) for x in v.split(".")) for v in re.findall(rf'href="({re.escape(minor)}\.\d+)/"', html)},
                  reverse=True)
    for v in vers:
        s = ".".join(map(str, v))
        try:
            with _open(f"{PY_FTP}{s}/python-{s}-embed-amd64.zip", "HEAD") as r:
                if r.status == 200:
                    return s
        except OSError:
            continue
    fail(f"python.org에서 {minor}.x 내장 Python을 찾지 못했습니다 — --python-zip으로 미리 받은 파일을 지정하세요.")
    return ""


def get_python_zip(a: argparse.Namespace) -> Path:
    if a.python_zip:
        p = Path(a.python_zip)
        if not p.exists():
            fail(f"--python-zip 파일이 없습니다: {p}")
        return p
    v = a.python_version or latest_python(a.python_minor)
    CACHE.mkdir(parents=True, exist_ok=True)
    p = CACHE / f"python-{v}-embed-amd64.zip"
    if p.exists() and zipfile.is_zipfile(p):
        say(f"    내려받아 둔 파일 사용: {p.name}")
        return p
    url = f"{PY_FTP}{v}/python-{v}-embed-amd64.zip"
    say(f"    내려받기: {url}")
    tmp = p.with_suffix(".part")
    with _open(url) as r, open(tmp, "wb") as f:
        shutil.copyfileobj(r, f)
    tmp.replace(p)
    return p


def python_version_of(zip_path: Path, py_dir: Path) -> str:
    m = re.search(r"python-(\d+\.\d+\.\d+)-embed", zip_path.name)
    if m:
        return m.group(1)
    pth = next(py_dir.glob("python3*._pth"), None)
    if pth:
        d = re.search(r"python3(\d+)\._pth", pth.name)
        if d:
            return f"3.{d.group(1)}"
    return "?"


def check_signature(*files: Path) -> None:
    """python.exe·python3xx.dll이 Python Software Foundation 서명인지(Windows에서만). 아니면 멈춘다."""
    if not IS_WIN:
        say("    (서명 확인은 Windows에서만 — 건너뜀)")
        return
    for f in files:
        lit = str(f).replace("'", "''")   # PowerShell 작은따옴표 문자열 안의 '
        ps = (f"$s = Get-AuthenticodeSignature -LiteralPath '{lit}'; "
              "\"$($s.Status)|$($s.SignerCertificate.Subject)\"")
        out = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", ps], capture_output=True,
                             encoding="oem", errors="replace", timeout=60).stdout.strip()
        status, _, subject = out.partition("|")
        if status != "Valid" or "Python Software Foundation" not in subject:
            fail(f"{f.name} 서명이 올바르지 않습니다({out or '확인 실패'}). 받은 파일을 지우고 다시 실행하세요.")
    say("    [OK] 서명 확인: " + ", ".join(f.name for f in files) + " — Python Software Foundation")


def unpack_python(zip_path: Path, dest: Path) -> str:
    if not zipfile.is_zipfile(zip_path):
        fail(f"zip 파일이 아닙니다: {zip_path}")
    with zipfile.ZipFile(zip_path) as z:
        names = z.namelist()
        if "python.exe" not in names or not any(re.fullmatch(r"python3\d+\._pth", n) for n in names):
            fail(f"Windows 내장(embeddable) Python zip이 아닙니다: {zip_path.name}")
        z.extractall(dest)
    pth = next(dest.glob("python3*._pth"))
    lines = pth.read_text(encoding="utf-8").splitlines()
    if "..\\engine" not in lines:
        # 엔진 폴더를 경로에 넣는다(내장 Python은 PYTHONPATH·현재 폴더를 쓰지 않음). site는 켜지 않는다.
        lines.insert(2 if len(lines) >= 2 else len(lines), "..\\engine")
        pth.write_text("\r\n".join(lines) + "\r\n", encoding="utf-8")
    check_signature(dest / "python.exe", *sorted(dest.glob("python3*.dll")))
    ver = python_version_of(zip_path, dest)
    try:
        mm = tuple(int(x) for x in ver.split(".")[:2])
        if not (PY_TESTED[0] <= mm <= PY_TESTED[1]):
            say(f"    [주의] Python {ver}는 엔진 시험 범위(3.10~3.14) 밖입니다.")
    except ValueError:
        pass
    return ver


# ── 조립 ─────────────────────────────────────────────────────────────────────

def package_contents(version: str, targets: list[str]) -> str:
    lo = min(TARGETS[t][1][0] for t in targets)
    hi = max(TARGETS[t][1][1] for t in targets)
    comps = []
    for t in targets:
        smin, smax = TARGETS[t][1]
        comps.append(f"""  <Components Description="{TARGETS[t][2]}">
    <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="{smin}" SeriesMax="{smax}" />
    <ComponentEntry AppName="BuoyRuleCheck" Version="{version}"
        ModuleName="./Contents/Win64/{t}/BuoyRuleCheck.dll"
        AppDescription="Buoy-RuleCheck AutoCAD plugin ({TARGETS[t][2]})" LoadOnAutoCADStartup="True" />
  </Components>""")
    return f"""<?xml version="1.0" encoding="utf-8"?>
<!-- tools/package/make_package.py가 만든 통합 패키지용 번들 정의. AutoCAD가 자기 릴리스에 맞는 Components 하나만 로드한다.
     R25.0·R25.1 = AutoCAD 2025·2026(.NET 8), R26.0 = AutoCAD 2027(.NET 10). -->
<ApplicationPackage SchemaVersion="1.0" AppVersion="{version}" ProductType="Application"
    Name="Buoy-RuleCheck" Description="고칠 곳에 떠오르는 부표 — GA 변경 규칙 후보 검토 (홍대도면반, 해커톤 시제품)"
    Author="홍대도면반" ProductCode="{PRODUCT_CODE}" UpgradeCodes="{UPGRADE_CODE}">
  <CompanyDetails Name="홍대도면반" />
  <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="{lo}" SeriesMax="{hi}" />
{chr(10).join(comps)}
</ApplicationPackage>
"""


def copy_engine(dest: Path) -> None:
    for pkg in ("brc", "garc"):
        shutil.copytree(ROOT / "engine" / pkg, dest / pkg, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))


def copy_samples(dest: Path) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    s = ROOT / "samples"
    for f in sorted(s.glob("S140_*.dxf")):
        shutil.copy2(f, dest / f.name)
    for d in sorted(s.glob("S140_*.brc")):
        if d.is_dir():
            shutil.copytree(d, dest / d.name)
    ex = s / "S140_report_example.html"
    if ex.exists():
        shutil.copy2(ex, dest / ex.name)


def assemble(a: argparse.Namespace, built: dict[str, Path], py_zip: Path) -> tuple[Path, dict]:
    say("[3/4] 패키지 조립")
    ver = plugin_version()
    name = f"Buoy-RuleCheck_{ver}"
    out_root = Path(a.out)
    stage = out_root / name
    if stage.exists():
        shutil.rmtree(stage)
    app = stage / "app"
    app.mkdir(parents=True)
    py_ver = unpack_python(py_zip, app / "python")
    copy_engine(app / "engine")
    copy_samples(app / "samples")
    shutil.copy2(HERE / "buoy_setup.py", app / "buoy_setup.py")
    shutil.copy2(PAYLOAD / "app_UNINSTALL.cmd", app / "UNINSTALL.cmd")
    for f in ("INSTALL.cmd", "UNINSTALL.cmd"):
        shutil.copy2(PAYLOAD / f, stage / f)
    targets = [t for t in ("R25", "R26") if t in built]
    bundle = app / "BuoyRuleCheck.bundle"
    for t in targets:
        dst = bundle / "Contents" / "Win64" / t
        dst.mkdir(parents=True)
        for f in built[t].glob("BuoyRuleCheck.*"):
            shutil.copy2(f, dst / f.name)
        fonts = built[t] / "fonts"
        if fonts.is_dir():
            shutil.copytree(fonts, dst / "fonts")
    (bundle / "PackageContents.xml").write_text(package_contents(ver, targets), encoding="utf-8")
    years = {"R25": "2025·2026", "R26": "2027"}
    manifest = {
        "product": "Buoy-RuleCheck", "plugin_version": ver, "engine_version": engine_version(),
        "python_version": py_ver, "targets": targets, "autocad": [years[t] for t in targets],
        "commit": git_commit(), "built": time.strftime("%Y-%m-%d %H:%M"),
    }
    (app / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    readme = (PAYLOAD / "README.txt").read_bytes().decode("utf-8-sig").replace("\r\n", "\n")
    for k, v in {"plugin_version": ver, "engine_version": manifest["engine_version"], "python_version": py_ver,
                 "acad_years": " / ".join(years[t] for t in targets)}.items():
        readme = readme.replace("{" + k + "}", v)   # format()은 안내문에 다른 중괄호가 생기면 멈춘다
    (stage / "README.txt").write_bytes(("\ufeff" + readme).replace("\n", "\r\n").encode("utf-8"))
    say(f"    [OK] {stage}")
    return stage, manifest


def self_test(stage: Path) -> None:
    """만든 패키지의 내장 Python으로 엔진을 불러 본다(Windows에서만)."""
    app = stage / "app"
    if not IS_WIN:
        say("    (내장 Python 실행 시험은 Windows에서만 — 건너뜀)")
        return
    r = subprocess.run([str(app / "python" / "python.exe"), "-I", "-X", "utf8", "-c", "import brc, garc; print(brc.__version__)"],
                       cwd=str(app), capture_output=True, encoding="utf-8", errors="replace", timeout=120,
                       env=dict(os.environ, PYTHONUTF8="1", PYTHONIOENCODING="utf-8"))
    if r.returncode != 0:
        fail("패키지의 내장 Python이 엔진을 불러오지 못합니다: " + r.stderr.strip()[-400:])
    say(f"    [OK] 내장 Python이 엔진 {r.stdout.strip()}을 불러옴(다른 폴더에서, ._pth 경로)")


def make_zip(stage: Path, suffix: str = "") -> Path:
    z = stage.parent / f"{stage.name}{suffix}_setup.zip"
    if z.exists():
        z.unlink()
    with zipfile.ZipFile(z, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for p in sorted(stage.rglob("*")):
            if p.is_file():
                zf.write(p, Path(stage.name) / p.relative_to(stage))
    return z


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Buoy-RuleCheck 통합 설치 패키지 만들기")
    ap.add_argument("--out", default=str(ROOT / "dist"), help="결과 폴더(기본 dist)")
    ap.add_argument("--acad2027", help="AutoCAD 2027 폴더(참조 DLL 4개가 있는 곳)")
    ap.add_argument("--acad25", help="AutoCAD 2025·2026 참조 DLL 4개가 있는 폴더")
    ap.add_argument("--acad-nuget", default="25.0.*", help="마지막 수단 NuGet 참조 버전(기본 25.0.* = 2025 기준, 2026에서도 로드)")
    ap.add_argument("--no-nuget", action="store_true", help="NuGet 참조를 시도하지 않음")
    ap.add_argument("--skip-r25", action="store_true", help="2027용만 만듦(zip 이름에 _2027only)")
    ap.add_argument("--allow-2027-only", action="store_true",
                    help="2025·2026용을 못 만들어도 2027용만으로 계속(기본은 실패로 끝냄)")
    ap.add_argument("--python-zip", help="미리 받은 Windows 내장 Python zip(python-3.x.y-embed-amd64.zip)")
    ap.add_argument("--python-version", help="받을 Python 버전(예: 3.14.2). 없으면 --python-minor의 최신")
    ap.add_argument("--python-minor", default="3.14", help="최신을 찾을 Python 계열(기본 3.14)")
    ap.add_argument("--dotnet-arg", action="append", help="dotnet build에 그대로 넘길 인자(여러 번)")
    a = ap.parse_args(argv)

    say(f"Buoy-RuleCheck 설치 패키지 만들기 — 플러그인 {plugin_version()} · 엔진 {engine_version()}")
    work = CACHE / "build"
    built = build_plugins(a, work)
    say("[2/4] 내장 Python 준비")
    py_zip = get_python_zip(a)
    stage, manifest = assemble(a, built, py_zip)
    say("[4/4] 시험·압축")
    self_test(stage)
    z = make_zip(stage, "" if "R25" in built else "_2027only")   # 2027 전용이면 이름에 드러나게
    digest = hashlib.sha256(z.read_bytes()).hexdigest()
    (z.parent / (z.name + ".sha256.txt")).write_text(f"{digest}  {z.name}\n", encoding="utf-8")
    say()
    say(f"[OK] {z}  ({z.stat().st_size / 1e6:.1f} MB)")
    say(f"     SHA-256 {digest}")
    say(f"     AutoCAD {', '.join(manifest['autocad'])} · Python {manifest['python_version']} · 커밋 {manifest['commit'] or '?'}")
    say("     다른 PC: 압축을 모두 푼 뒤 INSTALL.cmd 더블클릭.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
