# cyKimScroller

uGUI ScrollRect 기반 가상화·셀 재사용 스크롤러를 직접 구현하는 프로젝트.
Unity 6000.6.0f1, URP, uGUI 2.6.0.

> **미정**: 설계 방향과 목표 범위 (2026-10-01 기준, 별도 세션에서 결정 예정).
> 정해지면 이 절을 갱신한다. 정하기 전에는 스크롤러 본체 구현을 시작하지 않는다.

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
