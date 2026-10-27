Buoy-RuleCheck 설치 패키지  (플러그인 {plugin_version} · 엔진 {engine_version} · 내장 Python {python_version})
================================================================================

고칠 곳에 떠오르는 부표 — AutoCAD GA 도면이 바뀌면 관련 규칙(SOLAS V/22.1.1 선교 가시거리,
II-1/12 충돌격벽)만 다시 검사해 도면 위에 후보 판정을 띄우는 해커톤 시제품입니다.
규칙 엔진의 후보 판정이며 선급 승인이 아닙니다. 최종 판단은 설계자 검토로 합니다.

■ 필요한 것
  - Windows 10/11 (64비트)
  - AutoCAD {acad_years}
  그 밖에 Python·.NET SDK·인터넷·관리자 권한은 필요 없습니다(Python은 이 패키지 안에 들어 있음).

■ 설치
  1. 이 zip을 아무 폴더에나 "모두 압축 풀기" 합니다(zip 안에서 바로 실행하지 마세요).
  2. AutoCAD를 모두 닫습니다.
  3. INSTALL.cmd 를 더블클릭합니다.
     - "Windows의 PC 보호" 창이 뜨면 [추가 정보] → [실행].
     - 끝에 "[OK] 설치 완료." 가 나오면 됩니다.
  4. AutoCAD를 켭니다. 리본에 'Buoy-RuleCheck' 탭이 생깁니다.
     처음 한 번 보안 경고가 뜨면 [항상 로드]를 고릅니다.
  5. 도면에서 BRC 를 입력합니다. 연결 확인은 BRCHEALTH.
     시연용 샘플 도면: %LOCALAPPDATA%\BuoyRuleCheck\app\samples

■ 설치되는 곳 (이 사용자 계정에만)
  - 엔진·내장 Python : %LOCALAPPDATA%\BuoyRuleCheck\app
  - AutoCAD 플러그인 : %APPDATA%\Autodesk\ApplicationPlugins\BuoyRuleCheck.bundle
  - 사용자 환경변수   : BRC_ENGINE_DIR, BRC_PYTHON (플러그인이 엔진을 자동으로 켬)
  - 설치 기록         : %LOCALAPPDATA%\BuoyRuleCheck\install.log

■ 보안
  - 검사 엔진은 이 PC 안(127.0.0.1)에서만 동작하고, 도면 내용을 외부로 보내지 않습니다.
  - 도면을 고치지 않습니다. 화면 표시는 임시 그래픽이라 저장 파일에 남지 않습니다.

■ 업데이트
  새 패키지를 받아 같은 방법으로 INSTALL.cmd 를 실행하면 덮어씁니다.

■ 제거
  이 폴더의 UNINSTALL.cmd 또는 %LOCALAPPDATA%\BuoyRuleCheck\app\UNINSTALL.cmd.
  화면 설정(%APPDATA%\BuoyRuleCheck)과 도면 옆 버전 기록(<도면>.brc)은 남겨 둡니다.

■ 문제가 생기면
  - AutoCAD 명령줄에서 BRCHEALTH → 나오는 줄들과
  - %LOCALAPPDATA%\BuoyRuleCheck\install.log,
  - %APPDATA%\BuoyRuleCheck\error.log (있으면)
  를 홍대도면반에 보내 주세요.
