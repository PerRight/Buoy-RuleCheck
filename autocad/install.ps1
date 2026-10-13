# GA-RuleCheck 설치: 빌드 → AutoCAD 자동 로드 번들 설치 → 엔진 자동 실행 설정
# 사용: AutoCAD를 모두 닫고  powershell -ExecutionPolicy Bypass -File .\install.ps1
#       (빌드 생략: -NoBuild)
param([switch]$NoBuild)
$ErrorActionPreference = "Stop"

$root   = Split-Path -Parent $PSScriptRoot          # GA-RuleCheck
$proj   = Join-Path $PSScriptRoot "GaRuleCheck"
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
if (-not (Test-Path (Join-Path $out "GaRuleCheck.dll"))) { throw "GaRuleCheck.dll이 없습니다: $out" }

Write-Host "[2/3] 자동 로드 번들 설치..." -ForegroundColor Cyan
$bundle   = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\GaRuleCheck.bundle"
$contents = Join-Path $bundle "Contents\Win64"
New-Item -ItemType Directory -Force -Path $contents | Out-Null
Copy-Item (Join-Path $out "GaRuleCheck.*") $contents -Force
Copy-Item (Join-Path $PSScriptRoot "bundle\PackageContents.xml") $bundle -Force
Write-Host "    $bundle"

Write-Host "[3/3] 엔진 자동 실행 설정..." -ForegroundColor Cyan
$py = Get-Command python -ErrorAction SilentlyContinue | Where-Object { $_.Source -notlike "*WindowsApps*" } | Select-Object -First 1
if (-not $py) { $py = Get-Command py -ErrorAction SilentlyContinue | Select-Object -First 1 }
[Environment]::SetEnvironmentVariable("GARC_ENGINE_DIR", $engine, "User")
if ($py) {
    [Environment]::SetEnvironmentVariable("GARC_PYTHON", $py.Source, "User")
    Write-Host "    Python: $($py.Source)"
} else {
    Write-Host "    Python을 찾지 못했습니다. 엔진은 직접 켜야 합니다: cd engine; python -m garc serve" -ForegroundColor Yellow
}
Write-Host "    엔진 폴더: $engine"

Write-Host ""
Write-Host "완료. AutoCAD 2027을 새로 켜면 자동으로 로드됩니다. 도면에서 GARC 를 입력하세요." -ForegroundColor Green
Write-Host "처음 한 번 보안 경고가 뜨면 '항상 로드'를 선택하세요."
