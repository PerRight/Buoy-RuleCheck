# 통합 설치 패키지 (다른 PC용)

다른 PC에는 Python·.NET SDK가 없을 수 있으므로, **개발 PC에서 한 번** 패키지를 만들어 zip 하나로 나눠 준다.
받는 PC에서는 압축을 풀고 `INSTALL.cmd` 더블클릭이면 끝이다(인터넷·관리자 권한·Python·.NET SDK 불필요).

## 1. 패키지 만들기 (개발 PC — AutoCAD 2027 · .NET 10 SDK · Python 3.10+ · 인터넷)
.NET 8용(2025·2026) 빌드에는 .NET 8 참조 팩(`Microsoft.NETCore.App.Ref`·`Microsoft.WindowsDesktop.App.Ref`)도 필요하다.
- 인터넷이 있으면 빌드할 때 NuGet에서 자동으로 받는다(권장).
- 오프라인이면 .NET 10 SDK가 정한 **정확한 버전**의 팩이 미리 있어야 한다. .NET 8 SDK를 따로 깔아도 패치 버전이 다르면 NU1101로 실패한다.
저장소 루트에서:
```powershell
python tools\package\make_package.py
```
결과: `dist\Buoy-RuleCheck_<버전>_setup.zip`과 `.sha256.txt`. `dist\`는 저장소에 올리지 않는다(.gitignore).

하는 일:
1. 플러그인 빌드
   - AutoCAD 2027용(.NET 10)은 `C:\Program Files\Autodesk\AutoCAD 2027`의 DLL로 빌드한다(다른 곳이면 `--acad2027 <폴더>`).
   - AutoCAD 2025·2026용(.NET 8)은 아래 순서로 참조 DLL을 찾는다.
     1. `--acad25 <폴더>`
     2. `C:\Program Files\Autodesk\AutoCAD 2025`, `…\AutoCAD 2026`
     3. `tools\package\refs\R25\`
     4. Autodesk 공식 NuGet 참조(`AutoCAD.NET` 25.0.*, 인터넷 필요 — 패키지 구성에 따라 실패할 수 있음)

     모두 안 되면 실패로 끝낸다. 2027용만 만들려면 `--allow-2027-only`를 붙이고, 그러면 zip 이름에 `_2027only`가 붙는다.
   - 대상 프레임워크는 `-p:BrcTfm=net8.0-windows`로 바꾼다. `-p:TargetFramework=`는 NuGet 복원이 무시해 NETSDK1005로 실패한다.
   - 2025·2026용 DLL은 2025 기준으로 빌드한다. 2025와 2026은 같은 R25 계열이라 한 DLL로 둘 다 로드된다.
2. 내장 Python
   - python.org에서 최신 3.14.x Windows 내장(embeddable) zip을 받아 `tools\package\cache\`에 둔다.
   - `python.exe`의 서명이 Python Software Foundation인지 확인한다.
   - `python3xx._pth`에 `..\engine`을 넣어 엔진을 찾게 한다.
   - 미리 받은 파일을 쓰려면 `--python-zip <파일>`, 버전을 고르려면 `--python-version 3.14.2`.
3. 조립: 엔진(`brc`·`garc`, 표준 라이브러리만), 시연용 합성 샘플, 설치 프로그램(`buoy_setup.py`), 번들 정의를 넣는다.
   - 번들 정의(`PackageContents.xml`)에서 R25.0~R25.1은 `Contents\Win64\R25`, R26.0은 `Contents\Win64\R26`을 가리킨다.
4. 시험·압축: 내장 Python이 다른 폴더에서 엔진을 불러오는지 확인한 뒤 zip과 SHA-256을 만든다.

### 2025·2026 참조 DLL을 구하는 법
AutoCAD 2025나 2026이 깔린 PC의 설치 폴더(`C:\Program Files\Autodesk\AutoCAD 2026`)에서 아래 4개를 복사해 `tools\package\refs\R25\`에 넣는다.
- `acmgd.dll`
- `acdbmgd.dll`
- `accoremgd.dll`
- `AdWindows.dll`

Autodesk 파일이므로 저장소에 올리지 않는다(.gitignore). 빌드할 때 참조만 하고 패키지에는 넣지 않는다.

## 2. 받는 PC
`README.txt`(패키지 안)와 같다.
1. zip을 **모두 압축 풀기**한다.
2. AutoCAD를 닫는다.
3. `INSTALL.cmd`를 더블클릭한다. SmartScreen 창이 뜨면 [추가 정보] → [실행].
4. AutoCAD를 켜고 `BRC`를 입력한다.

설치 프로그램(`app\buoy_setup.py`, 내장 Python으로 실행)이 하는 일:
1. AutoCAD 2025·2026·2027을 찾아 이 패키지가 그 버전을 지원하는지 알린다. AutoCAD가 켜져 있으면 닫을 때까지 기다린다.
2. 켜져 있던 엔진(`python -m brc|garc serve`)을 끈다.
3. 엔진과 내장 Python을 `%LOCALAPPDATA%\BuoyRuleCheck\app`에 복사한다.
4. 번들을 `%APPDATA%\Autodesk\ApplicationPlugins\BuoyRuleCheck.bundle`에 복사하고(옛 `GaRuleCheck.bundle`은 지움), 인터넷에서 받은 파일 표시(Zone.Identifier)를 지운다. 이 표시가 남아 있으면 AutoCAD 로드 때 경고나 차단이 생길 수 있다.
   - 설치 폴더와 번들은 옆 폴더(`.new`)에 다 만든 뒤 바꿔 끼운다. 파일이 잠겨 옛 폴더를 옮기지 못하면 아무것도 바꾸지 않고 멈춘다(반쯤 바뀐 설치가 남지 않음).
5. 사용자 환경변수 `BRC_ENGINE_DIR`·`BRC_PYTHON`을 설정한다(옛 `GARC_*`는 지움).
6. 엔진을 잠깐 켜서 `127.0.0.1`에서 `/v1/health` 응답을 확인한다. 기록은 `%LOCALAPPDATA%\BuoyRuleCheck\install.log`에 남는다.

종료 코드는 다음과 같다.
- 0: 완료
- 1: 실패(잠김·깨진 패키지)
- 2: AutoCAD가 켜져 있어 멈춤
- 3: 엔진 자체 시험 실패
- 4: 이 PC의 AutoCAD 버전용 플러그인이 패키지에 없음

제거는 받은 폴더나 설치 폴더의 `UNINSTALL.cmd`로 한다. 화면 설정과 도면 옆 버전 기록(`<도면>.brc`)은 남긴다.

## 3. 시험
```bash
python -m unittest discover -s tools/package/tests -v
```
- 번들 정의, 내장 Python `._pth` 처리, 가짜 Windows 폴더에서 설치·재설치·제거 흐름, `.cmd` 인코딩을 시험한다(CI에서도 돈다).
- Windows 전용 부분은 실제 Windows에서 확인한다: 레지스트리, 프로세스, 차단 해제, 서명, dotnet 빌드, AutoCAD 로드.

## 개발용 설치와의 관계
- `autocad\install.ps1`은 저장소에서 빌드해 설치하는 개발자용이다(시스템 Python, 2027만).
- 이 패키지를 설치하면 환경변수가 패키지 쪽 엔진을 가리킨다. 둘 중 마지막에 설치한 쪽이 쓰인다.
