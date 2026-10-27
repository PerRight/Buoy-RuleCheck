@echo off
setlocal
rem Buoy-RuleCheck 제거 - 설치된 폴더에서 실행. 끝나면 이 폴더도 잠시 뒤 지워진다.
"%~dp0python\python.exe" -I -X utf8 "%~dp0buoy_setup.py" uninstall
if errorlevel 1 pause
exit /b
