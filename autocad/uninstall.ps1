# GA-RuleCheck 제거: 자동 로드 번들과 환경변수 삭제 (AutoCAD를 닫고 실행)
$bundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\GaRuleCheck.bundle"
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force; Write-Host "삭제: $bundle" }
[Environment]::SetEnvironmentVariable("GARC_ENGINE_DIR", $null, "User")
[Environment]::SetEnvironmentVariable("GARC_PYTHON", $null, "User")
Write-Host "GA-RuleCheck 자동 로드를 해제했습니다."
