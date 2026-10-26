# Buoy-RuleCheck 제거: 자동 로드 번들과 환경변수 삭제 (AutoCAD를 닫고 실행). 옛 이름(GA-RuleCheck) 번들·환경변수도 함께 지운다.
$plugins = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
foreach ($name in @("BuoyRuleCheck.bundle", "GaRuleCheck.bundle")) {
    $bundle = Join-Path $plugins $name
    if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force; Write-Host "삭제: $bundle" }
}
foreach ($v in @("BRC_ENGINE_DIR", "BRC_PYTHON", "GARC_ENGINE_DIR", "GARC_PYTHON")) {
    [Environment]::SetEnvironmentVariable($v, $null, "User")
}
Write-Host "Buoy-RuleCheck 자동 로드를 해제했습니다. 화면 설정(%APPDATA%\BuoyRuleCheck\ui.json)과 도면 옆 버전 기록(<도면>.brc)은 그대로 둡니다."
