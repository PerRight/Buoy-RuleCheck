# CLAUDE.md — Buoy-RuleCheck (2026 K-조선 해커톤 · 홍대도면반)

> Claude Code / Cowork가 이 폴더에서 작업할 때 가장 먼저 읽는 문서.
> 결정이 바뀌면 코드보다 이 문서를 먼저 고친다.
> 정식 이름 **Buoy-RuleCheck**(2026-10-26 확정, 워드마크 **Buoy** RuleCheck). 옛 작업명 `GA-RuleCheck` / `garc` / `GARC_`는 호환용 별칭으로만 남는다(§9).
> GitHub 저장소는 **PerRight/Buoy-RuleCheck**(10-27 이름 변경 — 옛 주소 PerRight/GA-RuleCheck는 GitHub가 새 주소로 연결). `docs/review`·`docs/mentoring`의 지난 기록은 당시 이름 그대로 둔다.

---

## 0. 현재 상태 (2026-10-30)

- **방향 전환**: FreeCAD 3D 복원성 검사(상위 폴더의 이전 저장소) → **AutoCAD GA 도면 변경 검토 플러그인**. 계획서(③ 2026 K-조선 해커톤 계획서_홍대도면반) 기준.
- **멘토링 1차 준비 피드백 반영**: 엔진과 24건 기대값 테스트를 먼저 고정, 플러그인은 어댑터. 정리: `docs/mentoring/2026-10-12_1차-준비-피드백.md`
- **완료 (테스트 84개 통과 — Python 3.10~3.14, 24건 기대값 포함)**: Python 규칙 엔진(규칙 2개), 입력 계약, revision diff·캐시, 로컬 HTTP 서버, DXF 경로, 합성 GA 샘플 5개 revision.
- **AutoCAD 플러그인**: v0.3.1 **10-12 18:34 사용자 PC에서 빌드 성공**(AutoCAD 2027 참조, `VertexPosition`·`UseProxy` 등 확인). 이후 v0.3.2 변경(layer_counts 전송·유사 레이어 감시 신호·엔진 주소 허용 목록·리다이렉트 금지, 10-13 '발표 모드' → **'라벨 확대'**·명령 `GARCLARGE` 이름 변경)은 10-18 Claude 작업 환경에서 AutoCAD 2027 참조로 **컴파일 확인**(v0.4.0과 함께). **실기 동작(`autocad/README.md` 첫 실행 확인표)은 아직 미확인.**
- **오류 수정 (10-12, 리뷰 후)**: '모르면 멈춘다'가 깨져 초록으로 새던 경로 차단 — 장애물·선체·격벽 레이어의 블록(INSERT), NaN·무한대·0 이하 선박 값, 좌표 없는 객체 → 전부 `review`. 수치 제안은 모든 흘수 조건 기준. 폴리선 호(bulge)를 외접 꺾은선으로(안전 쪽). 엔진 서버 Host·Origin 검사, 플러그인 엔진 주소 루프백 전용, 문서 ID 충돌 수정. Sonnet 1차 검증 지적(LOA 오타 → 적용 제외 초록, 눈높이·L 자릿수 오타, GA_EYE 선, 무한대 제안 순서, 제안값 반올림, 얕은 호) 반영 후 2차 검증. 이어서 2차 지적 F1~F12(조종위치·흘수·기선·용골일·숫자 형식 오타, 유사 레이어 이름 등)를 사용자 PC 연결 폴더에서 고치고 Sonnet 3·4차 검증. 회귀 테스트 `engine/tests/test_robustness.py`. 기록: `docs/review/2026-10-12_오류수정_검증기록.md`.
- **GitHub 공개 준비 (10-13)**: 공개(Public) 저장소로 준비 → 10-14 업로드(PerRight/GA-RuleCheck). `.gitignore`가 실도면(`*.dwg`, `*.dxf` — 합성 샘플 `samples/S140_*.dxf`만 예외)과 플러그인 검토 리포트(`*_GA검토_*.html`)를 막는다. `.gitattributes`(ps1은 CRLF, DXF는 바이트 그대로), GitHub Actions 엔진 테스트(`.github/workflows/engine-tests.yml`, Python 3.10~3.14 + 24건 표 일치 확인), `CONTRIBUTING.md`(브랜치·커밋·보안 규칙), PR 체크리스트. **라이선스는 미정** — 정하기 전까지 공개돼도 재사용 권한은 없다.
- **기능 3종 (10-18, 플러그인 v0.4.0 / 엔진 0.3.0)**: ① 버전 기록(저장 시 작성자·사유·관련 규정) + 비교 창(나란히·겹쳐·차이만, 번호 표식, 변경 내역) + 생산팀 전달 PDF/PNG ② 떠 있는 로고 패널(Ctrl+., 배지, 접근성) ③ 마우스 오버 하이라이트·툴팁·점검 항목 연동·'미충족만 보기'. 판정 색 변경: **충족 파랑** · 미충족 빨강 · 확인 필요 주황(버전 비교 추가 초록과 구분). 엔진: 요소별 판정(elements)·`/v1/compare`. 데모 샘플 S140_v1_review → S140_v2_fixed(+ .garc 버전 기록). 테스트 91개. Sonnet 검증 3차까지(비교의 '해결'은 이후 충족·삭제일 때만, 판정 못 함은 '확인 필요') — `docs/review/2026-10-18_기능3종_검증기록.md`. **C# 컴파일 확인**: Claude 작업 환경에서 사용자 PC의 AutoCAD 2027 DLL·.NET 10 WindowsDesktop 참조로 빌드(오류·경고 0, 커밋마다) — 방법은 §6 '실행'. AutoCAD 안 실행은 미확인(확인표 15~25).
- **실도면 연결 (10-18, 엔진 0.4.0 / 플러그인 0.5.0)**: GA_ 레이어가 없는 실제 GA를 레이어를 바꾸지 않고 검사. 엔진: `GARC_SCALE`(1/N)·`GARC_REGION`(측면도 영역)·객체별 `role`·`GARC_LAYER_ROLES`·`GARC_LBP` 대조·기선 대조(입력값도)·ARC/CIRCLE/ELLIPSE/SPLINE·준비 상태 `readiness`·치수표 읽기 `/v1/particulars`·블록 하위 객체 묶음(제안 문구·버전 비교). DXF 어댑터: 블록 펼침·역할 XRecord. 플러그인: `GARCLINK` 마법사·`GARCROLE`·`GARCROLESHOW`·준비 상태 카드·`RoleMap`(도면 사전 XRecord `GARC_ROLES`). 테스트 140개. Sonnet 검증 7차까지(블록 안 형상이 조용히 사라지는 경로 — 솔리드·해치·360° 호·프록시·OLE·모르는 타입·중첩 평탄화·중첩 XREF/MINSERT, 못 펼친 블록은 영역 밖이어도 멈춤, NaN 좌표) — `docs/review/2026-10-18_실도면연결_검증기록.md`. 실도면 5건(저장소 밖)에서 치수표·축척 5/5 추출, 탱커 GA는 역할 지정 후 판정까지(장애물 7천~1만여 객체, 계약+판정 0.3~1.1 초). **쟁점**: 마스트·크레인 지브 같은 좁은 구조물을 22.1.1 장애물로 넣으면 '위반', 빼면 '충족' → `docs/rule-definitions.md`에 '확인 필요'로 기록. 계약 §11.
- **UI/UX v2 + 이름 변경 (10-26, 플러그인 0.6.0 / 엔진 0.5.0)**: 디자인 번들(서용언 시안)과 `Bouy-RuleCheck_UI_prompt.md` 기준. 원칙 "고칠 곳에 떠오르는 부표" — 위반 후보 위치에 점으로 된 구(부표·오브)가 떠서 천천히 돌고, 고치면 사라진다(Transient, 화면 크기 고정). 잉크는 진하기로·색은 판정에만(문제없음 파랑 #5A9CFF · 수정 소요 빨강 #FF5F57 · 확인 필요 주황 #FFB340, 비교 전용 추가 초록·변경 노랑). 화면: 테마 v2(`Theme.cs`), 오브(`OrbRenderer.cs`, 동작 줄이기 `BRCMOTION`), 도면 부표·카드(`BuoyLayer`·`RuleCard`), 떠 있는 로고·목록·토스트(`FloatingUi`·`FloatingParts`), 팔레트(`ResultPanel`·`PaletteParts`), 리본(4패널+슬라이드아웃, 그린 아이콘), 명령줄 출력, 연결 마법사 5단계(`LinkWizard`·`LinkProgress`·`ParticularsDialog`), 버전 비교(`CompareForm`·`CompareParts`·`CompareCanvas`), A4 전달 시트(`ExportSheet`). 문구는 `UiText.cs` 한곳에서 — "적합"·"승인" 쓰지 않음(고지 문구 안의 "선급 승인이 아니며"만 예외), 통과는 "문제없음". 이름: 명령 `BRC*`(옛 `GARC*` 별칭), 특성 `BRC_*`(옛 `GARC_*`도 읽음, 둘 다 있고 값이 다르면 `PROP_DUPLICATE`로 전 규칙 검토 불가), 역할 XRecord `BRC_ROLES`(옛 `GARC_ROLES` 읽음), 버전 폴더 `<도면>.brc\`(옛 `.garc\` 자동 이동), 환경변수 `BRC_*`, 엔진 `engine/brc`(`python -m garc`도 동작), 응답 스키마 `brc.*`(옛 `garc.raw/1` 읽음). 판정은 바뀌지 않음 — 이름 변경 전후 응답을 정규화해 비교(합성 샘플·실도면 모두 동일). 미리보기 `autocad/BuoyPreview`(AutoCAD 없이 WinForms 화면 확인). 테스트 161개. 검증 기록 `docs/review/2026-10-26_BuoyUI_검증기록.md`. **AutoCAD 안 실행은 미확인**(확인표).
- **통합 설치 패키지 (10-27)**: 다른 PC(Python·.NET SDK 없음)용. 개발 PC에서 `python tools/package/make_package.py` → `dist/Buoy-RuleCheck_<버전>_setup.zip`, 받는 PC는 압축 풀고 `INSTALL.cmd`. 내장 Python(embeddable, `._pth`에 `..\engine`) + 미리 빌드한 DLL(2027용 .NET 10 · 2025·2026용 .NET 8 — 참조 DLL이 있을 때) + 번들 정의(R25/R26 버전별 로드). 설치 프로그램 `tools/package/buoy_setup.py`(내장 Python으로 실행, 사용자 폴더만, 관리자·인터넷 불필요, Zone.Identifier 해제, 엔진 자체 시험 127.0.0.1). 시험 `tools/package/tests`(CI 포함). **실제 Windows·AutoCAD 2025/2026에서는 미확인**(확인표 62~66).
- **AI 설명 (10-30, 플러그인 0.7.0 / 엔진 0.6.0)**: 사용자 결정 **A안 — 외부 LLM(Claude API)에 숫자 대신 빈칸만 보낸다**, 범위는 항목 카드 [설명] + 생산팀 전달 A4 '변경 설명'. 기본 꺼짐(`BRCAI`로 켜고 키 저장, DPAPI). 엔진 `explain.py`가 판정·비교 결과를 빈칸 값(PC 안)·보낼 사실(정해진 어휘)·기본 설명으로 나누고, `ai.py`(외부 호출 유일, api.anthropic.com 고정·리다이렉트 거부)가 보낸다. AI 문장의 `{빈칸}`은 PC 안에서 엔진 값으로 채우고, 숫자(조항 번호 말고)·모르는 빈칸·'승인' 같은 말·판정과 반대되는 말이 있으면 버리고 기본 설명. `POST /v1/explain`·`/v1/explain/change`(mode template/preview/ai), CLI `python -m brc explain`. 테스트 183개(유출 시험: 24건·샘플·무작위 도면 300장에서 보낼 본문에 숫자 없음, 이름만 바꾼 도면은 보낼 본문이 같음). 설계·보낼 것 표: `docs/ai-explain.md`. **AutoCAD 안 실행·실제 키 호출은 미확인**(확인표 67~72).
- **본선**: 2026-11-09(월) 양재 엘타워 — 본선에서 실제 동작 시연.

## 1. 팀 (계획서 기준)
| 이름 | 역할 | 이 저장소에서 |
|---|---|---|
| 표인범 (팀장) | 기획·설계 — 조선 규정 검토, GA 도면 분석, AutoCAD | `docs/rule-definitions.md` 판본 대조, 샘플 GA |
| 손종민 | 개발·데이터 — 플러그인 개발, 도면 데이터 처리 | `engine/`, `autocad/` |
| 복준용 | 설계·검증 — 선박설계 지식 기반 기능 검증 | 24건 수기 검산(`verified_by`) |
| 서용언 | 디자인 — UI/UX, Figma | 팔레트 화면(`ResultPanel.cs`), 흐름도 |

## 2. 제품 한 줄
**GA가 바뀌면 → 바뀐 객체를 감지하고 → 관련 규칙만 다시 돌려 → 도면 위에 경고와 수치 제안을 띄운다.**
AI가 설계하는 게 아니라 변경 이후 검토를 보조한다. 판정은 결정론적 규칙 엔진, 생성형 AI는 설명만(10-30 구현 — 숫자는 엔진 값으로 채우는 빈칸 방식, `docs/ai-explain.md`).

## 3. 아키텍처
```
AutoCAD 2027 ─ autocad/BuoyRuleCheck (C#, 어댑터: 판단 안 함)
  DocWatcher : DB 이벤트 → dirty → 300ms 디바운스 → Idle에서 검사
  Extractor  : 모델 공간 GA_ 레이어 → raw JSON (도면 단위 그대로, handle 포함)
  Overlay    : 위반 객체·시선·허용선을 Transient로 표시 (DB 무수정 → Undo 안전)
  BuoyLayer  : 위반 후보 위치의 부표(오브, 화면 픽셀 고정) + 호버/고정 카드(RuleCard)  ← OrbRenderer
  ResultPanel: 팔레트 (규칙 카드·게이지·점검 항목, 선박 정보 탭, 이력 탭, 고지 문구)  ← Theme.cs·PaletteParts·UiText(문구)
  FloatingUi : 떠 있는 로고(오브) + 부표 목록·토스트, Ctrl+.  / UiState: ui.json
  VersionStore·VersionDialog: 저장 시 버전 기록(<도면>.brc\)  CompareForm·CompareParts·CompareCanvas·ExportSheet: 비교 창·A4 내보내기
  LinkWizard(BRCLINK·BRCROLE·BRCROLESHOW)·LinkProgress·ParticularsDialog·RoleMap: 실도면 연결 5단계(축척·영역·기준점·역할, 레이어 안 바꿈)
  Ribbon     : 리본 'Buoy-RuleCheck' 탭 / DrawingProps: BRC_* 특성(옛 GARC_* 읽음)·레이어·라벨 스타일
  LegacyCommands: 옛 GARC* 명령 별칭
  AiExplain  : 항목 카드 [설명] 창·A4 변경 설명 창·BRCAI(켜기·키 DPAPI·미리보기·연결 시험) — 엔진 문장을 보여 주기만
        │ POST http://127.0.0.1:8765/v1/check {doc_id, raw}   /v1/compare {a, b}
        ▼
engine/brc (Python 표준 라이브러리만, engine/garc = 옛 이름 별칭)          DXF 경로: adapters/dxf.py (ezdxf)
  contract : raw → model (단위·좌표·레이어·조건, 계약 위반은 issues)  ← 같은 raw 계약
  rules/   : visibility (SOLAS V/22.1.1), collision_bulkhead (SOLAS II-1/12)
  engine   : 상태 pass/fail/na/review/skip, overall, 요소별 판정(elements: pass/fail/unknown)
  compare  : 두 스냅샷 → 변경 항목·해결/새 위반·표식 위치 (/v1/compare)
  readiness: 판정에 필요한 입력의 준비 상태(/v1/check 응답)   particulars: 치수표·축척 후보(/v1/particulars)
  session  : revision diff, 영향 규칙, 입력 해시 캐시(Undo = 캐시 적중)
  server   : 127.0.0.1 전용 HTTP (/v1/check, /v1/compare, /v1/report, /v1/reset, /v1/explain, /v1/explain/change)
  explain  : 판정·비교 → 빈칸 값(PC 안)·보낼 사실(정해진 어휘)·기본 설명, AI 답 검사·채우기
  ai       : 외부 호출 유일(AI 설명, 기본 꺼짐) — api.anthropic.com 고정, 숫자·이름 없는 빈칸 본문만
  report   : HTML 검토 리포트(측면도 SVG·판정식·중간값·이력)
```

## 4. 폴더
```
CLAUDE.md                      ← 이 문서
README.md                      GitHub 첫 화면 (소개·빠른 시작·데모·상태)
CONTRIBUTING.md                브랜치·커밋·PR·도면 보안 규칙
.github/                       Actions 엔진 테스트, PR 체크리스트
docs/
  mentoring/2026-10-12_1차-준비-피드백.md
  review/2026-10-12_오류수정_검증기록.md   오류 수정 + Sonnet 검증(1~7차) 결과, 남은 한계
  review/2026-10-18_기능3종_검증기록.md    기능 3종(v0.4.0) Sonnet 검증 1~3차, AutoCAD 실기에서 볼 남은 한계
  review/2026-10-18_실도면연결_검증기록.md  실도면 연결(v0.5.0) Sonnet 검증 1~7차
  review/2026-10-26_BuoyUI_검증기록.md     Buoy UI·이름 변경(v0.6.0) Sonnet 검증 1~4차, 완료 기준 확인표
  ai-explain.md                AI 설명 결정(10-30)·보내는 것/안 보내는 것·검사·화면·한계
  input-contract.md            단위·좌표·레이어·도면 특성 명세
  rule-definitions.md          L·관측점·수선·흘수 정의, 원문, 판본, 미검토 범위
  test-cases.md                24건 기대 결과표 (자동 생성 — 직접 고치지 말 것)
  metrics/                     정량지표 측정 계획(봉인 입력·판단 시간·ROI)과 자동 측정 결과
engine/
  brc/                         엔진 패키지
  garc/                        옛 이름 별칭(같은 모듈을 가리킴)
  tests/                       unittest (24건 + 계약·revision·서버·DXF + 오류 회귀 + 요소 판정·버전 비교·데모 샘플)
  tests/cases/expected_24.json 24건 원본 (hand_calc, verified_by)
  scripts/make_samples.py      합성 GA revision DXF 생성
  scripts/render_cases.py      expected_24.json → docs/test-cases.md
  scripts/metrics.py           정량지표 자동 측정(24건 일치·어림 검사 대비·처리 시간) → docs/metrics/
samples/                       S140_rev0~4 *.dxf (mm, AP·기선 원점), S140_v1_review → S140_v2_fixed (+ S140_v2_fixed.brc 버전 기록)
tools/package/                 다른 PC용 통합 설치 패키지: make_package.py(개발 PC) · buoy_setup.py(받는 PC 설치·제거) · payload/(INSTALL.cmd 등) · tests/
dist/                          패키지 결과물(저장소에 올리지 않음)
autocad/
  README.md                    빌드·NETLOAD·첫 실행 확인표
  BuoyRuleCheck/               C# 플러그인 (.NET 10)
  BuoyPreview/                 AutoCAD 없이 화면 미리보기(WinForms, 같은 소스를 링크)
```

## 5. 규칙 (MVP 2개)
| ID | 근거 | 기준 | L |
|---|---|---|---|
| SOLAS-V-22.1.1 | SOLAS V/22.1.1 | 선수 전방 수면 사각 거리 ≤ min(2L, 500 m), 모든 흘수 조건 중 최악 | **LOA** (V/2.4) |
| SOLAS-II1-12 | SOLAS II-1/12.1·12.3·12.4 | min(0.05L,10) ≤ 기준점~격벽 ≤ max(0.08L, 0.05L+3), 구상선수 기준점, 계단형 | **ICLL L** (II-1/2) |

정의·원문·미검토 범위: `docs/rule-definitions.md`. 경계값 포함, 비교 보정 1 µm.

## 6. 개발 규칙 (Claude가 코드를 쓸 때 지킬 것)

**불변 원칙**
- **판정은 엔진만.** C# 플러그인은 추출·호출·표시만 한다. 판정·단위 변환·diff 로직을 C#에 넣지 않는다.
- **모르면 멈춘다.** 단위 불명·모순·모호 입력은 추측하지 말고 `review`, 입력 없음은 `na`. `na`/`review`가 하나라도 있으면 전체 `incomplete` — **절대 충족(파랑)으로 보이지 않게.** 요소 판정도 같다: 판정 못 한 객체는 `unknown`(주황). 엔진 연결 실패도 검토 불가.
- **스냅샷이 진실.** 플러그인은 매번 GA_ 레이어 전체를 보낸다. 증분 갱신(+1/−1) 금지. 캐시 키 = (규칙 ID, 그 규칙 입력의 해시).
- **표시는 Transient만.** 위반 표시를 위해 도면 객체 색·레이어를 바꾸지 않는다(Undo·저장 오염).
- **수치 제안은 계산으로.** "얼마나 낮추면/옮기면 통과"는 엔진이 계산. LLM은 인용만 — AI 설명은 숫자를 `{빈칸}`으로만 쓰고 엔진 값으로 채운다.
- **밖으로 나가는 요청은 `engine/brc/ai.py` 하나.** AI 설명(기본 꺼짐)만 api.anthropic.com으로 보낸다. 보내는 본문은 `explain.request_body()` — 규칙 조항·판정 상태·고칠 방향·빈칸 이름 같은 정해진 어휘만, 숫자·handle·흘수 조건·레이어·블록·도면/버전 이름은 넣지 않는다. 바꾸면 `test_explain.py` 유출 시험(숫자 없음·이름만 바꾼 도면은 본문 동일)이 통과해야 한다.
- **버전 비교도 엔진.** 무엇이 바뀌었고 어떤 위반이 해결·발생했는지는 `/v1/compare`. 비교 창·내보내기는 그리기만.
- **판정 색**: 문제없음 파랑 · 수정 소요 빨강 · 확인 필요 주황(입력 없음·판정 멈춤·엔진 연결 안 됨). **색은 판정에만** — UI 크롬은 흑백 잉크 단계로. 버전 비교는 추가 초록 · 삭제 빨강(파선) · 변경 노랑 + 표식 모양(○ □ ◇) — 흑백 인쇄에서도 구분.
- **승인처럼 보이지 않게.** 화면 문구에 "적합"·"승인"·"통과 확정"을 쓰지 않는다. 통과는 "문제없음". 판정이 나오는 화면에는 고지 문구를 항상 둔다: "규칙 엔진의 후보 판정입니다. 선급 승인이 아니며 최종 판단은 설계자 검토로 합니다." 문구는 `UiText.cs`(플러그인)·`engine.py`의 `STATUS_KO`·`DISCLAIMER_KO`(엔진)에서만 고친다.
- **규칙 추가 = `rules/` 모듈 1개 + 기대값 케이스(정상·경계·위반·누락).** 케이스 없이 규칙을 넣지 않는다.

**모델 사용 규칙 (사용자 지정, 2026-10-12)**
- **기본 작업 = Opus 5.5.** 분석·구현·문서 작성은 메인 세션(Opus)이 한다.
- **막히면 Fable 5.1 사용 허가.** 같은 문제를 두 번 이상 고쳐도 안 풀리거나, 설계 판단·수학 유도처럼 더 깊은 추론이 필요할 때 Agent 도구 `model: "fable"` 서브에이전트에 맡길 수 있다. 썼으면 왜 썼는지와 결과를 보고·기록한다.
- **기본 검증 = Sonnet 5.5.** 엔진·규칙·기대값·C#·입력 계약을 바꾸면 Agent 도구 `model: "sonnet"` 서브에이전트로 **독립 검증**한다.
  - 검증자는 저장소를 고치지 않고 별도 폴더의 프로브로 직접 재현만 한다. 고친 쪽의 설명을 그대로 믿지 않는다.
  - 지적을 반영하면 새 Sonnet 검증자로 다시 검증한다(중요 변경은 2차까지).
  - 결과는 `docs/review/<날짜>_*.md`에 남긴다(확인됨/부분/미해결, 재현 근거, 남은 항목).
- 모델 이름과 실제 실행: Agent 도구는 `sonnet`·`opus`·`fable` 같은 계열 이름으로 지정한다. 세부 버전은 실행 환경이 정하므로, 보고할 때는 "sonnet 옵션으로 검증"처럼 사실대로 적는다.

**코드 스타일**
- 엔진: Python 3.10+, 표준 라이브러리만(ezdxf는 `adapters/dxf.py`에서만). 테스트는 `unittest`.
- 플러그인: C# / .NET 10 / AutoCAD 2027 API (팀 표준 버전, 2026-10-12 결정). 2025·2026은 net8.0-windows로 별도 빌드. AutoCAD DLL은 `AcadDir` 참조, Private=false.
- 엔진 내부 단위 m, 좌표 X=선수+, Y(z)=위+. 변환은 `contract.py`에서만.
- 주석·문서·커밋 메시지는 한국어, 식별자는 영어.
- 24건 표는 `expected_24.json`만 고치고 `render_cases.py`로 재생성.

**실행**
```bash
cd engine
python -m unittest discover -s tests      # 전체 테스트
python scripts/make_samples.py            # 샘플 DXF 재생성 (pip install ezdxf)
python -m brc revs ../samples/S140_rev*.dxf    # 변경 감지 데모 (CLI, 옛 python -m garc 도 동작)
python -m brc serve                       # 플러그인용 서버
python -m brc compare ../samples/S140_v1_review.dxf ../samples/S140_v2_fixed.dxf    # 버전 비교 (CLI)
python scripts/make_samples.py --compare-demo   # 버전 비교 데모 샘플 재생성
```

**C# 컴파일 확인 (Claude 작업 환경, 10-18부터)**: 리눅스에 `dotnet-sdk-10.0`(Ubuntu 패키지), 사용자 PC의 `C:\Program Files\Autodesk\AutoCAD 2027`의 `acmgd/acdbmgd/accoremgd/AdWindows.dll`과 `C:\Program Files\dotnet\packs\Microsoft.WindowsDesktop.App.Ref\10.0.x`(→ `/usr/lib/dotnet/packs/Microsoft.WindowsDesktop.App.Ref/10.0.x`)를 복사해
`dotnet build -c Release -p:AcadDir=<DLL 폴더> -p:EnableWindowsTargeting=true --source <빈 폴더>` — NuGet 없이 빌드된다. 실행 확인은 여전히 사용자 PC(AutoCAD) 몫.

## 7. 일정 (본선 11-09까지)
| 주 | 기간 | 목표 |
|---|---|---|
| 1 | 10/12–10/18 | AutoCAD 빌드·실기 확인, 24건 수기 검증, 판본 대조, 멘토 제출 자료 5종 |
| 2 | 10/19–10/25 | 실제형 GA 샘플(표인범), 팔레트 UX(서용언 시안 반영), 실패 케이스(객체 수백 개, 블록·폴리라인 변형) |
| 3 | 10/26–11/01 | 생성형 AI 설명(이전 저장소 `server/explain.py` 재사용, 판정 불변), 정량지표 측정 — 탐지 정확도·판정일치율(24건+α)·검토 시간 |
| 4 | 11/02–11/08 | 데모 리허설, 영상, 발표자료, 경계 문구 점검 |

**본선 데모(계획서: Before → Change → Warning)**: rev0 충족(파랑) → 갑판 컨테이너 추가 → 밸러스트 조건 가시거리 위반 + "x=112 m 장애물 상단을 z=22.45 m 이하로"(모든 흘수 조건 기준 최솟값) → Undo로 즉시 복귀(캐시) → 충돌격벽 이동 → 위반 → 흘수 조건 삭제 → 검토 불가(주황). (v0.4) 이어서 S140_v2_fixed로 버전 비교 v1 → v2(미충족 3 → 0) → 생산팀 전달 PDF.

## 8. 미결 사항
- [ ] C# 플러그인 v0.4.0 다시 빌드(`install.ps1`) + 첫 실행 확인표 1~25 (손종민) — 15~25가 10-18 기능 3종
- [x] 로고 — 10-26 코드로 그린 오브(점으로 된 구)로 대체. 그림 파일 불필요
- [x] 실도면 축척(1/N) 입력·측면도 영역 지정 — 10-18 `GARCLINK`(현 `BRCLINK`, 엔진 0.4.0·플러그인 0.5.0). AutoCAD 안 실행은 확인표 26~35 (손종민)
- [ ] **마스트·크레인 지브 같은 좁은 구조물을 V/22.1.1 장애물로 볼지**(아니면 V/22.1.2 사각 섹터로 따로) — 선급·MSC/Circ.982·현업 관행 확인. 실도면 시험에서 판정이 갈림 (표인범)
- [ ] 실도면 역할 지정 보조(선체 외곽선·충돌격벽 후보 자동 제시) — 지금은 사람이 고름. AI 사용 시 실행 위치(로컬만) 결정 후 (손종민·표인범)
- [x] GitHub 공개 저장소 업로드 (PerRight/GA-RuleCheck → 10-27 PerRight/Buoy-RuleCheck로 이름 변경) — Actions 결과 확인, README 배지는 남음 (전원)
- [ ] 라이선스 결정 (공개 저장소 — 정하기 전까지 '열람만 가능'. 대회 규정의 지식재산 조항과 함께 확인) (표인범)
- [x] 블록(INSERT) 장애물 지원 — 10-18 추출기(C#·DXF)가 펼쳐서 보냄(handle '<블록>.<번호>'). 펼칠 수 없는 블록만 '검토 불가'
- [x] 생성형 AI 설명 — 10-30 결정(A안: 외부 LLM에 빈칸만, 기본 꺼짐·키 옵트인·기본 설명 폴백, 범위 카드 [설명] + A4 변경 설명) 후 구현(플러그인 0.7.0·엔진 0.6.0). 남은 것: AutoCAD 실기·실제 키 호출(확인표 67~72), 시연용 API 키 준비(누가·어느 계정) (손종민·전원)
- [ ] 오타 감지의 한계 인지: 범위 안 오타(조종위치 x 18→118, 선미 흘수 등)는 못 잡음 — 발표에서 "모든 입력 실수를 막는다"고 말하지 않기. `GA_OBSTRUCTION`처럼 GA_ 이름으로 시작하는 보조 레이어는 '검토 불가'로 멈추므로 보조선은 `GA_REF` 같은 다른 이름을 쓸 것 (전원)
- [ ] 24건 수기 검산, `verified_by` 기입 (복준용)
- [ ] 정량지표(10-29 계획 `docs/metrics/2026-10-29_정량지표_측정계획.md`): 자동 측정 완료(24/24, 어림 검사 대비). 남은 것: A 봉인 확인용 입력 6~8건(복준용·표인범), B 판단 완료 시간 Before/After(10-30 과제 도면 6·정답표·기록표 준비 — 저장소 밖 `정량지표_측정\`, 팀원 4명, 10-31~11-01 주말 예정), D ROI 가정값 (전원)
- [ ] SOLAS 판본 확정, MSC/Circ.982 조종위치 정의 원문 확인 (표인범)
- [ ] 멘토 질문 3개 확정 (전원) — 초안은 멘토링 문서 4장
- [ ] 실제 GA에 가까운 샘플(DWG) 1건 — 저장소가 공개이므로 공개해도 되는 도면만. `*.dwg`는 기본 차단이라 `.gitignore`에 그 파일 예외 줄 추가 (CONTRIBUTING §5)
- [ ] 평면도 ±10° 검토를 범위에 넣을지 결정 (멘토 답 후)
- [x] 정식 이름 — Buoy-RuleCheck (10-26)
- [x] Buoy UI(플러그인 0.6.0) AutoCAD 실기 확인 — 10-29 확인표 37·39·41~48·50·51·54·56~61 통과. 남은 것: 36 리본 아이콘 테마(보류, 시연 무관), 38·40 보고 없음, 49 실도면 연결, 52·53·55 옛 버전 호환(건너뜀). 기록 `docs/review/2026-10-26_BuoyUI_검증기록.md`
- [x] 통합 설치 패키지: 10-29 개발 PC에서 2025·2026+2027 패키지 생성(NuGet 참조 자동 선택), 시연 PC 설치·동작 확인(확인표 62~64). 남은 것: AutoCAD 2025/2026 PC 확인(65), 제거(66) (손종민)

## 9. 이름 변경 (2026-10-26 완료)
| 무엇 | 새 이름 | 옛 이름 처리 |
|---|---|---|
| 화면·리포트·A4·README | Buoy-RuleCheck (워드마크 **Buoy** RuleCheck) | 남기지 않음 |
| 엔진 패키지 | `engine/brc`, `python -m brc` | `engine/garc` 별칭(같은 모듈), `python -m garc` 동작 |
| 도면 특성 | `BRC_*` | `GARC_*`도 읽음. 플러그인이 쓸 때 `BRC_`로 옮기고 옛 키 지움. 둘 다 있고 값이 다르면 `PROP_DUPLICATE` |
| 역할 XRecord·레이어 역할 | `BRC_ROLES`·`BRC_LAYER_ROLES` | `GARC_ROLES`·`GARC_LAYER_ROLES` 읽음 |
| 명령 | `BRC*` | `GARC*` 별칭(처음 한 번 안내) |
| 버전 기록 폴더 | `<도면>.brc\` | `.garc\`가 있으면 자동 이동 |
| 환경변수 | `BRC_ENGINE_URL`·`BRC_ENGINE_DIR`·`BRC_PYTHON` | `GARC_*` 읽음 |
| 스키마 | `brc.raw/1`·`brc.versions/1` | `garc.raw/1` 읽음 |
| C# | `BuoyRuleCheck` 네임스페이스·어셈블리, `BuoyRuleCheck.bundle` | 설치 스크립트가 옛 `GaRuleCheck.bundle` 제거 |
| 레이어 접두어 | `GA_` 유지 (GA = General Arrangement) | — |
| GitHub 저장소·PC 폴더 (10-27) | `PerRight/Buoy-RuleCheck`, 저장소 폴더 `Buoy-RuleCheck` | GitHub가 옛 주소를 연결. 로컬은 `git remote set-url origin https://github.com/PerRight/Buoy-RuleCheck.git`. 폴더를 옮기면 `autocad\install.ps1`을 다시 실행(엔진 경로 환경변수) |

남은 것: 발표자료.

## 10. 참고
- 이전 저장소(FreeCAD, 상위 폴더 `core/ server/ freecad/`): IS Code 복원성·KG 역산·설명 서버. 설명 서버(`server/explain.py`)와 Guider식 UX는 3주차에 재사용 후보.
- 대회: https://event-us.kr/koshiphackathon/event/132963
- SOLAS II-1/12: https://www.imorules.com/GUID-8F05CA12-925C-4AE7-8115-CA66EBA51112.html
- SOLAS V/22 개정(MSC.142(77)): https://wwwcdn.imo.org/localresources/en/KnowledgeCentre/IndexofIMOResolutions/MSCResolutions/MSC.142(77).pdf
