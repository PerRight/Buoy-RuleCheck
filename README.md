# **Buoy** RuleCheck

**고칠 곳에 떠오르는 부표 — GA 도면이 바뀌면 관련 규정만 다시 검사해, 고칠 자리에 점으로 된 구(부표)를 띄우는 AutoCAD 플러그인**
2026 K-조선 해커톤 · 홍대도면반 · 정식 이름 Buoy-RuleCheck(작업명 GA-RuleCheck에서 2026-10-26 변경)

> 규칙 엔진의 **후보 판정**입니다. 선급 승인이 아니며 최종 판단은 설계자 검토로 합니다.

---

## 무엇을 하나
1. 설계자가 AutoCAD에서 GA(일반배치도) 측면도를 고친다 — 예: 선교 앞 갑판에 컨테이너를 한 단 더 쌓는다.
2. 플러그인이 `GA_*` 레이어의 변경을 감지해 도면 스냅샷을 로컬 규칙 엔진(`127.0.0.1`)에 보낸다.
3. 엔진이 **바뀐 입력에 연결된 규칙만** 다시 판정한다(나머지는 캐시, Undo도 즉시 복귀).
4. **고칠 자리에 부표**가 떠서 천천히 돈다. 커서를 올리면 "상단을 1.55 m 낮추세요 · z ≤ 22.45 m 이면 기준 충족" 같은 **계산된 수치**와 근거 선(시선·허용선)이 보이고, 도면을 고치면 부표가 파랗게 멈췄다가 사라진다. 도구는 도면을 바꾸지 않는다(표시는 임시 그래픽).
5. 도면을 저장하면 버전(작성자·수정 사유·관련 규정)을 남기고, **이전 버전과 무엇이 달라졌고 어떤 위반이 해결됐는지**를 비교 창과 생산팀 전달용 PDF/PNG 한 장으로 보여 준다.
6. GA_ 레이어로 정리되지 않은 **실제 도면**도 레이어를 바꾸지 않고 연결한다(`BRCLINK`) — 도면 문자에서 치수·축척을 읽어 확인받고, 측면도 영역·기준점·객체 역할만 지정하면 검사된다.

### 주요 화면 (플러그인 v0.6 — 디자인: 잉크는 진하기로, 색은 판정에만)
- **도면 위 부표**: 수정 소요 빨강 · 확인 필요 주황(한쪽이 빈 채로 돎 = 입력이 빔) · 문제없음 파랑. 화면 크기 고정(줌과 무관), 명령 중에는 멈추고 흐려지며, 끝나면 빠르게 돌며 다시 검사(바뀐 입력에 걸린 규칙만)
- **팔레트 검토 탭**: '수정 소요 2 · 문제없음 0', 항목 카드(고칠 내용 · 조건 · 게이지 · 기여 객체 · 부표로 이동), 변경 알림, 준비 상태 n/10(빈 입력은 판정하지 않음), 하단 고지
- **떠 있는 로고**: 오른쪽 아래 오브의 부표 등 색이 전체 상태, 남은 개수 칩. 누르거나 `Ctrl+.` → 수정 소요 목록, 해결되면 토스트
- **실도면 연결**: 치수·축척 확인 창 → 측면도 영역 → AP·FP·기선 → 역할 지정(도면 사전에 저장, 레이어 그대로) → 조종위치, 도면에서 고르는 동안 단계 안내 띠
- **버전 비교**: 나란히 / 겹쳐 / 차이만(추가 ○ 빗금 · 삭제 □ 파선 · 변경 ◇ 이전 점선·이후 실선), 변경 내역(z 24.00 → 21.40 m · 해결), **생산팀 전달 A4**(흑백 인쇄에서도 구분, 확인란)
- **동작 줄이기**: `BRCMOTION` 또는 Windows '애니메이션 표시' 끔 → 회전·물결을 멈추고 상태 색은 그대로

AI가 설계하는 도구가 아니라 **변경 이후 검토를 보조**하는 도구입니다. 판정은 결정론적 규칙 엔진만 하고, 입력이 모자라거나 모호하면 추측하지 않고 **'확인 필요'로 멈춥니다**(문제없음으로 보이지 않음).

## 검사 규칙 (MVP)
| ID | 근거 | 기준 | 길이 L |
|---|---|---|---|
| `SOLAS-V-22.1.1` | SOLAS V/22.1.1 선교 가시거리 | 선수 전방 수면 사각 거리 ≤ min(2L, 500 m), 입력된 모든 흘수 조건 중 최악 | 전장 LOA |
| `SOLAS-II1-12` | SOLAS II-1/12 충돌격벽 위치 | min(0.05L, 10 m) ≤ 기준점~격벽 ≤ max(0.08L, 0.05L+3 m), 구상선수 기준점·계단형 | 만재흘수선협약 L |

정의·원문·다루지 않는 범위: [docs/rule-definitions.md](docs/rule-definitions.md) · 판본 대조는 진행 중

## 구조
```
AutoCAD 2027 ── autocad/BuoyRuleCheck (C# .NET 10, 판단하지 않는 어댑터)
                  변경 감지 → GA_ 레이어 스냅샷 추출 → 엔진 호출 → 부표·팔레트·로고·리본 (도면은 바꾸지 않음)
                     │ POST http://127.0.0.1:8765/v1/check
                     ▼
engine/brc (Python 3.10+, 표준 라이브러리만 — 옛 이름 garc도 같은 패키지로 넘김)
  contract(입력 계약) → rules(규칙 2개) → engine(판정) → session(변경 diff·캐시) → server / report(HTML)
  DXF 경로(adapters/dxf.py, ezdxf)로 AutoCAD 없이도 같은 흐름을 재현
```

## 빠른 시작 1 — 엔진만 (AutoCAD 없이)
```bash
cd engine
pip install ezdxf                       # DXF 경로에만 필요
python -m unittest discover -s tests    # 24건 기대값 포함 161개 테스트 (ezdxf 없으면 DXF 테스트는 skip)
python -m brc revs ../samples/S140_rev*.dxf
```
출력 예:
```
=== rev 2: S140_rev1_deck_containers.dxf
    변경: 추가 ['9A'] …
    영향 규칙: ['SOLAS-V-22.1.1']  재계산: ['SOLAS-V-22.1.1']  캐시: ['SOLAS-II1-12']
    ⚠ SOLAS-V-22.1.1: 문제없음 → 수정 소요 (new_violation)
    [FAIL  ] SOLAS-V-22.1.1   사각 거리 433.84 m > 허용 280 m (최악: 밸러스트 도착)
=== rev 4: S140_rev3_undo.dxf  (rev 2와 동일 — Undo 상태)
    영향 규칙: ['SOLAS-II1-12']  재계산: []  캐시: ['SOLAS-V-22.1.1', 'SOLAS-II1-12']
```
그 밖에: `python -m brc check <도면.dxf>` (한 도면 판정), `python -m brc report <도면…> -o 리포트.html` (HTML 검토 리포트), `python -m brc serve` (플러그인용 로컬 서버). 옛 명령 `python -m garc …`도 그대로 됩니다.

## 다른 PC에 설치 — 통합 패키지 (Python·.NET SDK 없어도 됨)
개발 PC에서 `python tools\package\make_package.py` → `dist\Buoy-RuleCheck_<버전>_setup.zip`.
받는 PC에서는 **압축을 모두 풀고 `INSTALL.cmd` 더블클릭** → AutoCAD를 켜고 `BRC`. 인터넷·관리자 권한·Python·.NET SDK 불필요(내장 Python 포함), AutoCAD 2027(.NET 10)과 2025·2026(.NET 8)용 DLL을 함께 넣어 AutoCAD가 버전에 맞는 것을 로드한다. 자세히: [tools/package/README.md](tools/package/README.md)

## 빠른 시작 2 — AutoCAD 2027 플러그인 (개발자: 저장소에서 빌드)
준비물: AutoCAD 2027(LT 제외), .NET 10 SDK, Python 3.10+
```powershell
# AutoCAD를 모두 닫고
cd autocad
powershell -ExecutionPolicy Bypass -File .\install.ps1   # 빌드 → 자동 로드 번들 설치 → 엔진 자동 실행 설정
```
AutoCAD에서 `samples\S140_rev1_deck_containers.dxf`를 열고 `BRC` → 고칠 자리에 부표, 오른쪽 팔레트에 항목 카드. 명령·화면·첫 실행 확인표: [autocad/README.md](autocad/README.md)
AutoCAD 없이 화면만 보려면: `cd autocad\BuoyPreview` → `dotnet run` (오브·부표 상태·부표 카드·팔레트·로고·마법사·버전 비교·A4를 합성 샘플로)

## 데모 시나리오 (합성 GA, S140 일반화물선 LOA 140 m)
| 파일 | 변경 | 결과 |
|---|---|---|
| `S140_rev0_base.dxf` | 기본 | 두 항목 모두 문제없음 |
| `S140_rev1_deck_containers.dxf` | 선교 앞 컨테이너 4단(상단 z=24 m) | 가시거리 수정 소요 433.84 m > 280 m — 부표 카드 "상단을 1.55 m 낮추세요 · z ≤ 22.45 m 이면 기준 충족" |
| `S140_rev2_bulkhead_moved.dxf` | 충돌격벽 2 m 선수 쪽 이동 | 충돌격벽 수정 소요 "격벽을 선수 쪽으로 0.44 m 옮기세요" |
| `S140_rev3_undo.dxf` | 격벽 되돌림 | rev1과 같은 상태(가시거리 수정 소요 유지) → 두 규칙 모두 캐시 재사용, 재계산 0 |
| `S140_rev4_three_tiers.dxf` | 컨테이너 3단(상단 z=21.4 m) | 모두 기준 충족(부표가 파랗게 멈췄다가 사라짐) |

실제 조선소 도면은 포함하지 않습니다. 샘플은 모두 합성 도면입니다(`engine/scripts/make_samples.py`로 재생성).

## 설계 원칙
- **판정은 엔진만** — C# 플러그인은 추출·호출·표시만 한다. 생성형 AI가 pass/fail을 정하지 않는다. 판정·숫자 문장은 엔진이 쓰고, AI는 그 뒤에 숫자·판정 없는 설명 문단만 덧붙인다([docs/ai-explain.md](docs/ai-explain.md)).
- **모르면 멈춘다** — 빈 입력·단위 불명·모순·오타 의심은 '확인 필요'(주황). 하나라도 있으면 전체가 문제없음(파랑)이 되지 않는다.
- **스냅샷이 진실** — 매번 GA_ 레이어 전체를 보내고, 무엇이 바뀌었는지는 엔진이 계산한다(Undo·Redo 어떤 경로든 동기화).
- **도구는 도면을 바꾸지 않는다** — 부표·근거 선은 임시 그래픽. 도면 객체의 색·레이어를 바꾸지 않는다.
- **잉크는 진하기로, 색은 판정에만** — 화면 바탕·버튼은 흑백 잉크 단계, 빨강·주황·파랑은 판정에만. '적합'·'승인'이라는 말을 쓰지 않고 통과는 '문제없음'.
- **도면은 PC 밖으로 나가지 않는다** — 엔진은 `127.0.0.1` 전용. AI 설명(기본 꺼짐)을 켜도 규칙 조항·판정 상태·고칠 방향 같은 정해진 어휘만 보내고 숫자·도면 이름은 보내지 않는다.

## 현재 상태 (2026-10-30)
- **AI 설명(플러그인 0.7 · 엔진 0.6)**: 항목 카드 [설명]과 생산팀 전달 A4의 변경 설명. 판정·숫자 문장은 엔진이 바로 쓰고, `BRCAI`로 켜면 Claude API가 그 뒤에 '규칙이 왜 있는지·다루지 않는 범위·검토할 방향' 문단을 덧붙인다 — 보내는 본문은 정해진 어휘뿐이고 숫자·이름은 없다. AI 문단에 숫자·판정 표현·'승인' 같은 말이 있으면 버리고 엔진 문장만. CLI: `python -m brc explain samples/S140_rev1_deck_containers.dxf --mode preview`
- **Buoy UI(플러그인 0.6 · 엔진 0.5)**: 디자인 명세대로 화면을 바꾸고 이름을 Buoy-RuleCheck로 — 명령 `BRC*`(옛 `GARC*` 별칭), 도면 특성 `BRC_*`(옛 `GARC_*`도 읽음), 엔진 패키지 `brc`(옛 `garc` 별칭), 버전 기록 폴더 `<도면>.brc`(옛 `.garc`는 옮겨 씀). 엔진에는 표시용 필드(부표 자리 anchor · 카드 숫자 fix · 비교 제목)만 더했고 판정은 그대로(샘플·실도면 판정이 바이트 단위로 같음). AutoCAD 2027 참조로 **컴파일 확인**, AutoCAD 안 실행 확인은 진행 중
- 엔진 0.4.0: 실도면 연결(축척 1/N·측면도 영역·객체/레이어 역할·블록 펼침·LBP 대조·준비 상태·치수표 읽기 `/v1/particulars`). 엔진 0.3.0: 요소(객체)별 판정, 버전 비교(`/v1/compare`, `python -m brc compare`). 테스트 161개 통과(Python 3.10~3.14 — GitHub Actions가 push·PR마다 자동 실행), 24건 기대값은 독립 재계산(정확 산술)과 일치 — 팀원 수기 검산은 진행 중. 오류 수정과 공개 준비는 Sonnet 독립 검증 1~7차를 거쳤다 — [검증 기록](docs/review/2026-10-12_오류수정_검증기록.md)
- 플러그인: v0.3.1 AutoCAD 2027 빌드·실행 확인. v0.4.0(버전 비교·로고 패널·마우스 오버)·v0.5.0(실도면 연결)은 AutoCAD 2027 참조로 **컴파일 확인**, AutoCAD 안 실행 확인은 진행 중 — [autocad/README.md](autocad/README.md) 확인표 15~35
- 실도면: 팀이 가진 실제 GA 5건(저장소에는 넣지 않음)에서 치수표·축척 5/5 자동 추출, 탱커 GA 1건은 역할 지정 후 엔진 판정까지 확인(장애물 7천~1만여 객체, 계약+판정 0.3~1.1 초). 마스트·크레인 같은 좁은 구조물을 수면 가시거리 장애물로 볼지는 확인 필요 — [규칙 정의](docs/rule-definitions.md)
- 데모: `samples/S140_v1_review.dxf`(수정 소요 3건) → `S140_v2_fixed.dxf`(수정 완료) + 버전 기록 `S140_v2_fixed.brc/` — AutoCAD에서 `S140_v2_fixed.dxf`를 열고 `BRC` → `BRCCOMPARE`, 또는 `python -m brc compare ../samples/S140_v1_review.dxf ../samples/S140_v2_fixed.dxf`
- 한계: 측면도(중심선)만 검토(좌우 10°·평면 미검토), 범위 안 입력 오타는 감지 못 함, 실도면의 역할 지정은 사람이 한다(자동 분류 없음) — [docs/input-contract.md](docs/input-contract.md)

## 문서
| 문서 | 내용 |
|---|---|
| [CLAUDE.md](CLAUDE.md) | 구조·원칙·일정·미결 사항 (Claude로 작업할 때 먼저 읽는 문서, 모델 사용 규칙 포함) |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 브랜치·커밋·PR·테스트 규칙 |
| [docs/input-contract.md](docs/input-contract.md) | 단위·좌표·레이어(`GA_*`)·도면 특성(`BRC_*`, 옛 `GARC_*`) 계약 |
| [docs/rule-definitions.md](docs/rule-definitions.md) | L·관측점·수선·흘수 정의, 원문 근거 |
| [docs/test-cases.md](docs/test-cases.md) | 24건 기대 결과표 (자동 생성) |
| [docs/mentoring/](docs/mentoring/) | 멘토링 피드백 정리 |
| [docs/review/](docs/review/) | 오류 수정·검증 기록 |
| [docs/claude-onboarding-prompt.md](docs/claude-onboarding-prompt.md) | 팀원이 Claude에 작업을 맡길 때 붙이는 첫 메시지 |

## 팀 (홍대도면반)
| 이름 | 역할 |
|---|---|
| 표인범 (팀장) | 기획·설계 — 조선 규정 검토, GA 도면 분석 |
| 손종민 | 개발·데이터 — 플러그인 개발, 도면 데이터 처리 |
| 복준용 | 설계·검증 — 선박설계 지식 기반 기능 검증 |
| 서용언 | 디자인 — UI/UX, Figma |

## 라이선스
아직 정하지 않았습니다. 라이선스가 정해지기 전까지 이 저장소의 코드·문서 저작권은 팀에 있으며, 열람은 자유지만 재사용·배포는 팀에 먼저 문의해 주세요.
