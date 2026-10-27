@echo off
setlocal
rem Buoy-RuleCheck 제거 - 받은 폴더에서 실행.
if not exist "%~dp0app\python\python.exe" (
  echo [Buoy-RuleCheck] 압축을 먼저 모두 푼 뒤 실행하세요. Extract the whole ZIP first.
  pause
  exit /b 1
)
"%~dp0app\python\python.exe" -I -X utf8 "%~dp0app\buoy_setup.py" uninstall
set RC=%ERRORLEVEL%
echo.
pause
exit /b %RC%
