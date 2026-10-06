# Basic 샘플

빈 씬에 GameObject를 하나 만들고 `BasicSample` 컴포넌트를 붙인 뒤 Play한다.
씬·프리팹 없이 UI 전체를 코드로 만든다. 1920×1080 기준 Canvas에 비율 앵커로 배치하므로 16:9 해상도(1280×720 등)에서는 같은 배치로 보인다.

| 영역 | 보여 주는 것 |
|---|---|
| 왼쪽 세로 목록 | 높이가 다른 500개 셀, 간격·패딩, lookAhead, 미리 만든 문자열로 GC 없는 바인딩. 항목은 `List`로 두고 셀에 항목 번호를 보여 주며, 번호를 `ICyScrollerItemIdProvider`의 안정 ID로 준다. 상태 줄은 `CellViewWillDisplay`·`CellViewDidEndDisplay`와 `IsDisplayed`로 실제 보이는 셀(미리보기 구간 제외)만 세어 그 데이터 인덱스 범위를 보여 주고, 셀 표시·활성화·회수 이벤트가 온 프레임과 편집한 프레임에만 다시 그린다 |
| 가운데 버튼 | `JumpToDataIndex` 트윈 점프 (목록 처음·#250 항목(번호로 찾는다)·끝·무작위, 캐러셀 이전·다음, 휠 무작위 회전) |
| 가운데 편집 버튼 | 증분 변경으로 보던 화면을 지킨다. `List: Insert 3 at top`은 맨 위에 3개를 넣고 `InsertCells(0, 3)`으로 알린다(보던 항목은 그 자리에 남고 인덱스만 3씩 밀린다). `List: Remove first visible`은 뷰포트 안에서 시작하는 첫 항목을 `RemoveCells`로 지우고(아래 항목이 빈자리를 채운다), `List: Move visible to top`은 보이는 항목 가운데 하나를 `MoveCell`로 맨 위로 옮긴다(화면은 움직이지 않고, 위로 스크롤하면 맨 위에 있다). 남은 셀은 다시 바인딩하지 않는다 |
| 오른쪽 휠 피커 | 세로 `Loop` + 가운데 `Snapping`, 고정 행 높이 56, `NotifyCellPositions` 위치 훅으로 원통에 감긴 것처럼 기울고(X축 회전) 작아지고 흐려지는 행, 가운데 행을 가리키는 반투명 하이라이트 바(레이캐스트 통과), 스냅된 값 표시 |
| 아래 캐러셀 | 가로 `Loop` + `Snapping`, 루프 방향 점프(`LoopJumpDirection`), 위치 훅으로 가운데 카드가 가장 크고 선명하게(스케일·`CanvasGroup` 투명도) |

| 파일 | 역할 |
|---|---|
| `BasicSample` | 화면 배치와 버튼·상태 표시 |
| `BasicListController` / `BasicCellView` | 가변 높이 목록(List 데이터, 항목 번호 ID, 삽입·삭제·이동 편집)과 기본 셀 |
| `BasicCarouselController` / `BasicCarouselCardView` | 캐러셀과 위치 훅으로 크기·투명도를 바꾸는 카드 셀 |
| `BasicWheelPickerController` / `BasicWheelRowView` | 휠 피커와 위치 훅으로 원통 모양을 그리는 행 셀 |
| `SampleUiFactory` | 런타임 UI 생성 도우미와 색 |

실제 프로젝트에서는 셀을 프리팹으로 만들고 `CellIdentifier`를 프리팹마다 다르게 둔다.
셀 루트 RectTransform은 스크롤러가 배치하므로, 스케일·회전·투명도 같은 효과는 자식(`Visual`)에 준다.
위치 효과는 셀 뷰에서 `protected override void OnViewportPositionChanged(float normalizedOffset)`를 재정의해 그린다
(0 = 뷰포트 앞 가장자리, 0.5 = 가운데, 1 = 뒤 가장자리). 스크롤러의 `NotifyCellPositions`를 켜야 불린다.
