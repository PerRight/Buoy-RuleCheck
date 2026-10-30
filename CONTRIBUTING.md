# 기여 안내 (팀 작업 규칙)

Buoy-RuleCheck(GitHub `PerRight/Buoy-RuleCheck`, 옛 이름 GA-RuleCheck)는 2026 K-조선 해커톤 홍대도면반의 저장소입니다. 팀원이 같은 방식으로 고치고 확인할 수 있게 최소한의 규칙만 적습니다.
구조·원칙의 원본은 [CLAUDE.md](CLAUDE.md)입니다. 이 문서와 어긋나면 CLAUDE.md가 우선입니다.

## 1. 처음 받았을 때
```bash
git clone <저장소 주소>
cd Buoy-RuleCheck/engine
pip install "ezdxf==1.4.4"               # CI와 같은 버전
python -m unittest discover -s tests      # 전부 통과해야 정상 (ezdxf 없으면 DXF 3개 skip)
```
AutoCAD 플러그인은 Windows에서 `autocad/install.ps1`로 빌드·설치합니다 — [autocad/README.md](autocad/README.md)

## 2. 브랜치와 PR
- `main`은 **항상 테스트가 통과하는 상태**로 둡니다. 직접 push하지 말고 브랜치 → PR → 팀원 1명 확인 후 병합.
- 브랜치 이름: `feat/<내용>`, `fix/<내용>`, `docs/<내용>` (예: `fix/bulkhead-ceil`, `docs/mentor-q3`)
- PR을 열면 체크리스트가 자동으로 붙습니다. GitHub Actions(엔진 테스트, Python 3.10~3.14)가 초록이어야 병합합니다.
- 커밋 메시지는 한국어로 `영역: 무엇을` — 예) `엔진: 충돌격벽 제안값을 cm 단위 올림`, `플러그인: 라벨 확대 버튼 문구`, `문서: 멘토 질문 3개 확정`

## 3. 무엇을 어디서 고치나
| 바꾸려는 것 | 고칠 곳 | 같이 할 일 |
|---|---|---|
| 규칙 판정·수치 제안 | `engine/brc/rules/` | 기대값 케이스(정상·경계·위반·누락) 추가 — 케이스 없이 규칙을 넣지 않는다 |
| 24건 기대값 | `engine/tests/cases/expected_24.json`만 | `python scripts/render_cases.py`로 `docs/test-cases.md` 재생성 (표를 직접 고치지 않는다 — CI가 어긋남을 잡는다) |
| 단위·좌표·레이어·도면 특성 | `engine/brc/contract.py` | `docs/input-contract.md` 같이 수정 |
| 규정 정의·원문 근거 | `docs/rule-definitions.md` | 근거가 불확실하면 '확인 필요'로 남긴다 (기억으로 단정하지 않기) |
| 팔레트·리본·도면 위 표시 | `autocad/BuoyRuleCheck/*.cs` | 판정 로직은 넣지 않는다. 빌드 후 첫 실행 확인표의 관련 번호를 PR에 적는다 |
| 샘플 도면 | `engine/scripts/make_samples.py` | `python scripts/make_samples.py`로 `samples/S140_*.dxf` 재생성 |

## 4. 꼭 지킬 원칙 (요약 — 전체는 CLAUDE.md §6)
- **판정은 엔진만.** C# 플러그인은 추출·호출·표시만 합니다.
- **모르면 멈춘다.** 단위 불명·모순·오타 의심은 `review`, 입력 없음은 `na`. 어느 경우든 전체가 초록으로 보이면 안 됩니다.
- **표시는 임시 그래픽(Transient)만.** 도면 객체의 색·레이어를 바꾸지 않습니다.
- **승인처럼 말하지 않기.** "적합·승인·통과 확정" 대신 "규칙 후보 기준 충족 — 최종 판단은 설계자 검토".

## 5. 도면·데이터 보안
- **실제 조선소 도면(DWG·DXF)과 그 검토 리포트는 커밋하지 않습니다.** `.gitignore`가 DWG·DWT·DWF·DXF(대소문자 무관, 합성 샘플 `samples/S140_*.dxf` 제외)와 HTML 전부(예시 `samples/S140_report_example.html` 제외 — 플러그인·CLI 리포트가 HTML)를 막아 두었습니다. `git add -f`로 우회하지 마세요.
- 실도면을 출력한 PDF·이미지, 실도면 화면 캡처도 올리지 않습니다(README·발표용 캡처는 합성 샘플 S140으로).
- 실도면은 저장소 밖 폴더에서 엽니다(플러그인은 리포트를 도면 옆에 저장합니다).
- 새 샘플 도면(합성 또는 공개해도 되는 도면)이나 HTML을 넣을 때는 `samples/`에 두고, `.gitignore` **맨 끝**에 그 파일만 푸는 예외 줄(`!samples/파일이름`)을 같은 PR에서 추가해 팀장 확인을 받습니다.
- 도면 내용을 외부 서비스로 보내는 코드를 만들지 않습니다. 엔진은 `127.0.0.1` 전용입니다. 유일한 외부 호출은 AI 설명(`engine/brc/ai.py`, 기본 꺼짐) — 규칙 조항·판정 상태·고칠 방향과 빈칸 이름만 보내고 숫자·handle·조건·레이어·블록·도면 이름은 보내지 않습니다(2026-10-30 결정, [docs/ai-explain.md](docs/ai-explain.md)). 보내는 본문을 바꾸면 `engine/tests/test_explain.py`의 유출 시험이 통과해야 합니다.
- 비밀번호·API 키·개인 연락처를 커밋하지 않습니다. 실수로 올렸다면 지우는 커밋만으로는 기록에 남으므로 바로 팀장에게 알리고 키를 폐기합니다.

## 6. Claude로 작업할 때
- 첫 메시지에 [docs/claude-onboarding-prompt.md](docs/claude-onboarding-prompt.md)를 붙입니다.
- 모델 사용 규칙: 기본 작업은 Opus, 막히거나 더 깊은 추론이 필요하면 Fable 허용, 엔진·규칙·기대값·C#·입력 계약을 바꾸면 Sonnet으로 독립 검증 (CLAUDE.md §6).
- 검증 결과는 `docs/review/<날짜>_*.md`에 남깁니다 (확인됨/부분/미해결, 재현 근거, 남은 항목).

## 7. 라이선스
아직 정하지 않았습니다(README 참고). 외부 코드를 가져올 때는 출처와 라이선스를 PR에 적어 주세요.
