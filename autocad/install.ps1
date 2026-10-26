# Buoy-RuleCheck 설치: 빌드 → AutoCAD 자동 로드 번들 설치 → 엔진 자동 실행 설정
# 사용: AutoCAD를 모두 닫고  powershell -ExecutionPolicy Bypass -File .\install.ps1
#       (빌드 생략: -NoBuild)
# 옛 이름(GA-RuleCheck)으로 설치한 번들·환경변수가 있으면 지운다 — 두 번들이 함께 로드되면 명령이 겹친다.
param([switch]$NoBuild)
$ErrorActionPreference = "Stop"

$root   = Split-Path -Parent $PSScriptRoot          # 저장소 루트
$proj   = Join-Path $PSScriptRoot "BuoyRuleCheck"
$engine = Join-Path $root "engine"
$out    = Join-Path $proj "bin\Release\net10.0-windows"

if (Get-Process acad -ErrorAction SilentlyContinue) {
    Write-Host "AutoCAD가 실행 중입니다. 모두 닫고 다시 실행하세요 (DLL이 잠겨 있어 복사할 수 없습니다)." -ForegroundColor Yellow
    exit 1
}

if (-not $NoBuild) {
    Write-Host "[1/3] 빌드 중..." -ForegroundColor Cyan
    dotnet build $proj -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패 — 위 오류 메시지를 확인하세요." }
}
if (-not (Test-Path (Join-Path $out "BuoyRuleCheck.dll"))) { throw "BuoyRuleCheck.dll이 없습니다: $out" }

Write-Host "[2/3] 자동 로드 번들 설치..." -ForegroundColor Cyan
$plugins  = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$old      = Join-Path $plugins "GaRuleCheck.bundle"
if (Test-Path $old) { Remove-Item $old -Recurse -Force; Write-Host "    옛 번들 삭제: $old" }
$bundle   = Join-Path $plugins "BuoyRuleCheck.bundle"
$contents = Join-Path $bundle "Contents\Win64"
New-Item -ItemType Directory -Force -Path $contents | Out-Null
Copy-Item (Join-Path $out "BuoyRuleCheck.*") $contents -Force
$fonts = Join-Path $out "fonts"                     # 워드마크 글꼴(있을 때만, 없으면 대체 글꼴)
if (Test-Path $fonts) { Copy-Item $fonts $contents -Recurse -Force }
Copy-Item (Join-Path $PSScriptRoot "bundle\PackageContents.xml") $bundle -Force
Write-Host "    $bundle"

Write-Host "[3/3] 엔진 자동 실행 설정..." -ForegroundColor Cyan
# 이미 떠 있는 엔진(옛 python -m garc serve 또는 이전 코드의 brc)은 끈다 — 남아 있으면 새 플러그인이 옛 코드에 붙는다.
# (플러그인도 엔진 버전을 보고 옛 엔진이면 판정하지 않지만, 설치 때 정리해 두면 다음 검사에서 새 엔진이 자동으로 켜진다)
$running = Get-CimInstance Win32_Process -Filter "Name LIKE 'python%'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match '-m\s+(garc|brc)\s+serve' }
foreach ($p in $running) {
    Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    Write-Host "    켜져 있던 엔진 종료: PID $($p.ProcessId)"
}
$py = Get-Command python -ErrorAction SilentlyContinue | Where-Object { $_.Source -notlike "*WindowsApps*" } | Select-Object -First 1
if (-not $py) { $py = Get-Command py -ErrorAction SilentlyContinue | Select-Object -First 1 }
[Environment]::SetEnvironmentVariable("BRC_ENGINE_DIR", $engine, "User")
[Environment]::SetEnvironmentVariable("GARC_ENGINE_DIR", $null, "User")
[Environment]::SetEnvironmentVariable("GARC_PYTHON", $null, "User")
if ($py) {
    [Environment]::SetEnvironmentVariable("BRC_PYTHON", $py.Source, "User")
    Write-Host "    Python: $($py.Source)"
} else {
    Write-Host "    Python을 찾지 못했습니다. 엔진은 직접 켜야 합니다: cd engine; python -m brc serve" -ForegroundColor Yellow
}
Write-Host "    엔진 폴더: $engine"

Write-Host ""
Write-Host "완료. AutoCAD 2027을 새로 켜면 자동으로 로드됩니다. 도면에서 BRC 를 입력하세요(옛 명령 GARC도 한동안 됩니다)." -ForegroundColor Green
Write-Host "처음 한 번 보안 경고가 뜨면 '항상 로드'를 선택하세요."
