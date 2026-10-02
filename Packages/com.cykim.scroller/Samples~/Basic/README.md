# Basic 샘플

빈 씬에 GameObject를 하나 만들고 `BasicSample` 컴포넌트를 붙인 뒤 Play한다.
씬·프리팹 없이 UI 전체를 코드로 만든다. 1920×1080 기준 Canvas에 비율 앵커로 배치하므로 16:9 해상도(1280×720 등)에서는 같은 배치로 보인다.

| 영역 | 보여 주는 것 |
|---|---|
| 왼쪽 세로 목록 | 높이가 다른 500개 셀, 간격·패딩, lookAhead, 미리 만든 문자열로 GC 없는 바인딩. 상태 줄은 `CellViewWillDisplay`·`CellViewDidEndDisplay`와 `IsDisplayed`로 실제 보이는 셀(미리보기 구간 제외)만 세고, 셀 표시·활성화·회수 이벤트가 온 프레임에만 다시 그린다 |
| 가운데 버튼 | `JumpToDataIndex` 트윈 점프 (목록 처음·가운데·끝·무작위, 캐러셀 이전·다음, 휠 무작위 회전) |
| 오른쪽 휠 피커 | 세로 `Loop` + 가운데 `Snapping`, 고정 행 높이 56, `NotifyCellPositions` 위치 훅으로 원통에 감긴 것처럼 기울고(X축 회전) 작아지고 흐려지는 행, 가운데 행을 가리키는 반투명 하이라이트 바(레이캐스트 통과), 스냅된 값 표시 |
| 아래 캐러셀 | 가로 `Loop` + `Snapping`, 루프 방향 점프(`LoopJumpDirection`), 위치 훅으로 가운데 카드가 가장 크고 선명하게(스케일·`CanvasGroup` 투명도) |

| 파일 | 역할 |
|---|---|
| `BasicSample` | 화면 배치와 버튼·상태 표시 |
| `BasicListController` / `BasicCellView` | 가변 높이 목록과 기본 셀 |
| `BasicCarouselController` / `BasicCarouselCardView` | 캐러셀과 위치 훅으로 크기·투명도를 바꾸는 카드 셀 |
| `BasicWheelPickerController` / `BasicWheelRowView` | 휠 피커와 위치 훅으로 원통 모양을 그리는 행 셀 |
| `SampleUiFactory` | 런타임 UI 생성 도우미와 색 |

실제 프로젝트에서는 셀을 프리팹으로 만들고 `CellIdentifier`를 프리팹마다 다르게 둔다.
셀 루트 RectTransform은 스크롤러가 배치하므로, 스케일·회전·투명도 같은 효과는 자식(`Visual`)에 준다.
위치 효과는 셀 뷰에서 `protected override void OnViewportPositionChanged(float normalizedOffset)`를 재정의해 그린다
(0 = 뷰포트 앞 가장자리, 0.5 = 가운데, 1 = 뒤 가장자리). 스크롤러의 `NotifyCellPositions`를 켜야 불린다.
