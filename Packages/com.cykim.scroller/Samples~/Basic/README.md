# Basic 샘플

빈 씬에 GameObject를 하나 만들고 `BasicSample` 컴포넌트를 붙인 뒤 Play한다.
씬·프리팹 없이 UI 전체를 코드로 만든다.

| 영역 | 보여 주는 것 |
|---|---|
| 왼쪽 세로 목록 | 높이가 다른 500개 셀, 간격·패딩, lookAhead, 미리 만든 문자열로 GC 없는 바인딩 |
| 오른쪽 버튼 | `JumpToDataIndex` 트윈 점프 (처음·가운데·끝·무작위) |
| 아래 캐러셀 | `Loop` + `Snapping`, 루프 방향 점프(`LoopJumpDirection`), 위치에 따른 카드 스케일 |

실제 프로젝트에서는 셀을 프리팹으로 만들고 `CellIdentifier`를 프리팹마다 다르게 둔다.
셀 루트 RectTransform은 스크롤러가 배치하므로, 스케일 같은 효과는 자식(`Visual`)에 준다.
