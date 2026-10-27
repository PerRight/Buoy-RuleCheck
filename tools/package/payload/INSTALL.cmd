@echo off
setlocal
rem Buoy-RuleCheck 설치 - 더블클릭으로 실행. 관리자 권한 필요 없음.
if not exist "%~dp0app\python\python.exe" (
  echo [Buoy-RuleCheck] 압축을 먼저 모두 푼 뒤 INSTALL.cmd 를 실행하세요.
  echo [Buoy-RuleCheck] Extract the whole ZIP first, then run INSTALL.cmd.
  pause
  exit /b 1
)
"%~dp0app\python\python.exe" -I -X utf8 "%~dp0app\buoy_setup.py" install
set RC=%ERRORLEVEL%
echo.
pause
exit /b %RC%
