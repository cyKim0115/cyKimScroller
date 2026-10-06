# cyKimScroller

uGUI ScrollRect 기반 가상화·셀 재사용 스크롤러를 직접 구현하는 프로젝트.
Unity 6000.6.0f1, URP, uGUI 2.6.0.

## 설계 방향 (2026-10-01 결정)

UITableView·RecyclerView·UI Toolkit ListView 등 여러 가상화 목록 UI가 공통으로 쓰는 델리게이트·셀 재사용 패턴을
uGUI ScrollRect에 맞게 새로 설계한다. 조사 근거: `docs/research/2026-10-01-scroller-design-research.md`

- 공개 타입은 `CyScroller` / `ICyScrollerDelegate` / `CyScrollerCellView`
- 특정 라이브러리와의 소스 호환은 목표가 아니다. 다른 목록 UI와의 개념 대응표는 패키지 `Documentation~/api-mapping.md`
- v0.1.0 범위: 세로·가로 가상화, 가변 크기, cellIdentifier 풀링, 점프·트윈, 루프, 스냅, 이벤트
- 밖: 그리드 전용 API, 중첩 스크롤 제스처 분배, 셀 자동 크기 측정

## 보고

작업 보고 웹훅 키는 `DISCORD_REPORT_WEBHOOK_URL` (전역 스킬 `webhook-report`).

## 구조 (2026-10-01 결정)

스크롤러는 **임베디드 UPM 패키지**로 만든다. 다른 프로젝트에 git URL로 가져다 쓰기 위해서다.
`Assets/`에는 이 저장소에서만 쓰는 개발·검증용 씬만 둔다.

```
Packages/com.cykim.scroller/        패키지 본체 (git 추적)
  package.json
  Runtime/   CyKim.Scroller.asmdef          런타임 코드
  Editor/    CyKim.Scroller.Editor.asmdef   인스펙터·에디터 도구
  Tests/Runtime, Tests/Editor               테스트 어셈블리
  Samples~/                                 배포용 샘플 (Unity가 임포트하지 않음)
Assets/Dev/                         개발·검증 씬 (패키지에 포함 안 됨)
```

- 루트 네임스페이스 `CyKim.Scroller`. 런타임 asmdef는 `UnityEngine.UI`만 참조한다
- 패키지 코드는 `Assets/`의 코드를 참조하지 않는다 (의존 방향은 Assets → 패키지 한쪽)
- 테스트가 Test Runner에 안 보이면 `Packages/manifest.json`의 `testables`에 패키지 이름을 넣는다
- 로드맵: `docs/roadmap/2026-10-01-scroller-roadmap.md` (v0.2 이후 후보·사용자 선택 필요 항목)

## 검증 (2026-10-01)

- 테스트: MCP `run_tests` — EditMode `CyKim.Scroller.Editor.Tests`, PlayMode `CyKim.Scroller.Tests` (`init_timeout` 120000)
- 스크롤 핫패스 할당 0은 PlayMode 테스트 `Scrolling_DoesNotAllocateAfterWarmup`이 지킨다. 핫패스를 고치면 반드시 돌린다
- 샘플 확인: `Sample.FindByPackage(...).Import()`로 `Assets/Samples/`에 가져와 확인한 뒤 **지운다** (커밋하지 않는다)
- 스트레스: `Assets/Dev/Scenes/DevStress.unity` — 10만 셀 자동 스크롤 + 프레임 GC HUD
- 에디터가 백그라운드면 플레이 프레임이 멈춘다. 런타임에서 `Application.runInBackground = true` (프로젝트 설정은 바꾸지 않는다)
- PlayMode 테스트를 돌리면 저장 안 한 열린 씬이 교체된다. 임시 씬 작업은 테스트 전에 끝낸다
- PlayMode를 연달아 돌리면 0개로 끝날 수 있다 (Enter Play Mode Options와 Test Framework 정적 캐시). 매 실행 전 `refresh_unity(compile="request", mode="force")`
- README 이미지 재캡처: Basic 샘플 Import → 빈 씬에 `BasicSample` → Play → `execute_code`로 `ReadmeCaptureHarness.Begin(캔버스 "CyScroller Sample Canvas", 출력 폴더, "버튼 라벨|초" 배열, 1920, 1080, 1280, 720, 20f, 1.5f)`.
  `still.png` → `docs/images/overview.png`(1280 폭), `frames/` → ffmpeg 팔레트 GIF(960 폭, 128색) `docs/images/demo.gif`. 끝나면 샘플·임시 씬 정리

## 상시 규칙

- `.meta` 파일을 직접 생성·수정하지 않는다 (Unity가 만든다)
- 씬·프리팹·`.asset` 원본을 통째로 Read하지 않는다. MCP 조회나 path-scoped read를 쓴다
- 다른 스크롤러 에셋·라이브러리의 소스·리소스를 이 저장소로 복사하지 않는다. 일반적인 동작·API 패턴만 참고한다
- 에디터 전용 도구는 전역 룰 `unity-agent-editor-tools`를 따른다 —
  `public static` 진입점 + validate MenuItem이 `false` 반환

## 패키지 고정

`Packages/manifest.json`의 git 참조는 **커밋 SHA로 고정한다.** 브랜치(`#main`)나 ref 생략을 쓰지 않는다.

| 패키지 | 고정 SHA | 해당 태그 |
|---|---|---|
| `com.coplaydev.unity-mcp` | `30d2207509...` | v10.2.0 |

MCP 패키지 버전은 전역 `unityMCP` 서버(`mcpforunityserver==10.2.0`)와 맞춘다.
올릴 때는 둘을 같이 올리고 이 표도 갱신한다.

## Unity MCP

`unityMCP` 서버는 Claude 데스크톱 전역 설정에 stdio로 등록돼 있다 (프로젝트 `.mcp.json` 불필요).
**Unity Editor가 이 프로젝트를 열고 있어야 동작한다.** 처음 쓸 때 `ToolSearch`로 `mcp__unityMCP__*`를 로드한다.

- 다른 Unity 프로젝트가 같이 열려 있으면 `set_active_instance`로 `cyKimScroller@...`를 먼저 고른다
- CLI `-batchmode` / `-executeMethod`는 쓰지 않는다
- Unity API 문서는 `unity-docs` MCP

## C# 컨벤션

private `_camelCase` / const `UPPER_SNAKE_CASE` / public `PascalCase`.
`[SerializeField] private` 사용. MonoBehaviour는 파일당 1개.
`GetComponent`❌→`TryGetComponent`. `FindObjectOfType`❌. 리플렉션❌.
`UnityEngine.Input`❌→Input System.
로그는 `[ClassName]` 접두어로 `Debug.LogWarning`/`Debug.LogError`.

스크롤러 핫패스(스크롤 콜백·레이아웃 계산)에서는 GC 할당을 만들지 않는다 — LINQ·박싱·클로저 캡처 금지.

## 커밋

전역 룰 `korean-git-commit`을 따른다 — `{영역} - {변경 내용}` 한국어 한 줄, 명사/동사구 종결.
영역 라벨: `스크롤러`·`에디터`·`샘플`·`테스트`·`문서`·`패키지`·`프로젝트`
