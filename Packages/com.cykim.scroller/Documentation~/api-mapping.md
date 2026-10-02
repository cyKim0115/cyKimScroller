# 다른 목록 UI와의 개념 대응

가상화 목록 UI는 플랫폼마다 이름만 다를 뿐 "개수·크기·셀을 묻는 데이터 소스 + 재사용 풀"이라는 같은 구조를 쓴다.
다른 환경에 익숙하다면 아래 표로 CyKim Scroller의 대응 멤버를 찾으면 된다. 개념 대응일 뿐이며 **소스 호환은 목표가 아니다.**

## 데이터 소스와 셀

| 개념 | CyKim Scroller | UIKit `UITableView` | Android `RecyclerView` | Unity UI Toolkit `ListView` |
|---|---|---|---|---|
| 항목 개수 | `ICyScrollerDelegate.GetNumberOfCells` | `tableView(_:numberOfRowsInSection:)` | `Adapter.getItemCount()` | `itemsSource.Count` |
| 항목 크기 | `GetCellViewSize` (스크롤 축 길이) | `tableView(_:heightForRowAt:)` | 레이아웃 측정 | `fixedItemHeight` / `virtualizationMethod` |
| 셀 만들기·바인딩 | `GetCellView` 안에서 `scroller.GetCellView(prefab)` 후 데이터 채우기 | `tableView(_:cellForRowAt:)` + `dequeueReusableCell(withIdentifier:for:)` | `onCreateViewHolder` / `onBindViewHolder` | `makeItem` / `bindItem` |
| 재사용 풀 키 | `CyScrollerCellView.CellIdentifier` | `reuseIdentifier` | `getItemViewType()` | 템플릿 하나 |
| 재활용 직전 훅 | `OnRecycled()`, `CellViewWillRecycle` | `prepareForReuse()` | `onViewRecycled()` | `unbindItem` |
| 셀이 보이는 데이터 인덱스 | `DataIndex` (루프 슬롯은 `CellIndex`) | `indexPath` | `getBindingAdapterPosition()` | `bindItem`의 index |

## 갱신과 이동

| 개념 | CyKim Scroller | UIKit | Android | UI Toolkit |
|---|---|---|---|---|
| 전체 다시 읽기 | `ReloadData(scrollPositionFactor)` | `reloadData()` | `notifyDataSetChanged()` | `RefreshItems()` / `Rebuild()` |
| 위치를 지키며 다시 읽기 | `ReloadDataKeepingPosition()` | — | — | — |
| 보이는 셀만 다시 그리기 | `RefreshActiveCellViews()` | `reconfigureRows(at:)` | `notifyItemRangeChanged()` | `RefreshItem(index)` |
| 항목으로 이동 | `JumpToDataIndex(...)` (정렬 비율·트윈·완료 콜백) | `scrollToRow(at:at:animated:)` | `scrollToPositionWithOffset()` / `smoothScrollToPosition()` | `ScrollToItem(index)` |
| 항목이 보이게만 이동 | `ScrollIntoView(dataIndex, ScrollAlign, margin, ...)` (`Nearest`: 이미 보이거나 더 움직일 수 없으면 그대로(드래그·관성 유지), 아니면 최소 이동. 여백은 콘텐츠 끝까지만) | `scrollToRow(at:at:animated:)`의 `.none` (`.top`·`.middle`·`.bottom` = Start·Center·End) | `scrollToPosition()` / `LinearSmoothScroller`의 `SNAP_TO_ANY` (`SNAP_TO_START`·`SNAP_TO_END`) | `ScrollToItem(index)` |
| 보이는 범위 | `StartDataIndex` / `EndDataIndex` | `indexPathsForVisibleRows` | `findFirstVisibleItemPosition()` / `findLastVisibleItemPosition()` | — |
| 완전히 보이는지 | `IsDataIndexFullyVisible(dataIndex, margin)` (lookAhead 제외, 여백은 콘텐츠 끝까지만) | `rectForRow(at:)`를 보이는 영역과 비교 | `findFirstCompletelyVisibleItemPosition()` / `findLastCompletelyVisibleItemPosition()` | — |
| 항목 위치·크기 | `GetCellStart` / `GetCellSize` (화면 밖 항목도) | `rectForRow(at:)` | 붙어 있는 뷰만 (`getDecoratedTop()` 등) | — |
| 인덱스로 셀 찾기 | `GetCellViewAtDataIndex` | `cellForRow(at:)` | `findViewHolderForAdapterPosition()` | `GetRootElementForIndex` |
| 위치로 인덱스 찾기 | `GetCellViewIndexAtPosition` | `indexPathForRow(at:)` | `findChildViewUnder()` | — |
| 스크롤 위치 | `ScrollPosition`, `NormalizedScrollPosition` | `contentOffset` | `computeVerticalScrollOffset()` | `scrollOffset` (ScrollView) |

## 이벤트·스냅·루프

| 개념 | CyKim Scroller | UIKit | Android | UI Toolkit |
|---|---|---|---|---|
| 셀 활성화·회수 (미리보기 구간 포함) | `CellViewVisibilityChanged` | — | `onViewAttachedToWindow` / `onViewDetachedFromWindow` | `bindItem` / `unbindItem` |
| 실제 표시 시작·끝 (미리보기 구간 제외) | `CellViewWillDisplay` / `CellViewDidEndDisplay`, 셀 뷰 `OnBecameVisible()` / `OnBecameHidden()` | `tableView(_:willDisplay:forRowAt:)` / `tableView(_:didEndDisplaying:forRowAt:)` | — (`OnScrollListener`에서 `findFirstVisibleItemPosition()` 등으로 계산) | — |
| 늦은 비동기 결과 버리기 | `BindVersion` (바인딩 때 기억, 끝날 때 비교), `IsBound` | `prepareForReuse()`에서 작업 취소, 또는 완료 때 `indexPath(for:)` 재확인 | `onViewRecycled()`에서 작업 취소, 또는 `getBindingAdapterPosition()` 재확인 | `unbindItem`에서 작업 취소 |
| 뷰포트 안 셀 위치 (캐러셀·휠 연출) | `NotifyCellPositions` + 셀 뷰 `OnViewportPositionChanged(normalizedOffset)`, `CellViewPositionChanged` | `scrollViewDidScroll(_:)`에서 셀 frame 변환, 또는 `UICollectionViewLayout` 레이아웃 속성 | `OnScrollListener`에서 자식 뷰 위치 계산, 또는 커스텀 `LayoutManager` | — |
| 스크롤 중 | `ScrollerScrolled`, `ScrollerScrollingChanged` | `scrollViewDidScroll(_:)` | `OnScrollListener` | 스크롤바 `valueChanged` |
| 스냅 | `Snapping` + `ScrollerSnapped` | `isPagingEnabled`, `scrollViewWillEndDragging(_:withVelocity:targetContentOffset:)` | `LinearSnapHelper` / `PagerSnapHelper` | — |
| 무한 루프 | `Loop`, `LoopJumpDirection` | — | — | — |

## 구현 메모

| 항목 | CyKim Scroller의 방식 |
|---|---|
| 배치 | LayoutGroup 없이 셀 RectTransform을 직접 배치 |
| 재활용 셀 보관 | content 아래에 비활성으로 둔다 (재부모화 없음) |
| 루프 세트 | 뷰포트·미리보기를 덮고 양쪽에 한 사이클씩 남는 최소 홀수 세트 (최소 5) |
| 루프 스크롤바 | ScrollRect에서 떼어 숨기고, 루프를 끄면 다시 붙인다 |
| 점프 후 리사이즈 | 점프·스냅·ScrollIntoView 정렬을 유지한다 |
| 트윈 목표 | 좌표 대신 요청(셀·정렬 위치·여백)을 저장하고 매 프레임 지금 배치에서 다시 계산한다. 트윈 중 재배치·뷰포트 크기 변화가 일어나도 맨 앞 셀 기준 화면을 그대로 두고 끊지 않고 이어 가며, 다음 프레임에 화면이 튀지 않게 시작점을 다시 잡아 같은 시각에 끝낸다. 마지막 프레임 콜백이 요청한 재배치도 끝내기 전에 처리하고, 완료 콜백은 끝에서 한 번 |
| 콜백 안 리로드 | 범위 갱신이 끝난 뒤 처리한다 (재질의 포함) |
| 콜백 안 이동 | 즉시 점프·위치 대입은 바로 옮기고, 범위 갱신은 옛 위치 기준의 남은 작업을 멈춘 뒤 새 위치로 다시 맞춘다. 루프 순환 보정(슬롯 번호 이동)은 범위 계산 도중에 하지 않고 다시 맞추기 직전에 한다. 다시 맞추기는 한 번에 최대 4번, 나머지는 다음 LateUpdate |
| 표시 이벤트 | 실제 뷰포트에 걸친 슬롯 범위를 활성 범위와 따로 추적하고, 범위 갱신마다 이전 범위와의 차이만큼만 알린다 (O(변경 수)). 셀마다 표시 플래그로 짝을 지키고, 보이던 셀은 회수·`ClearActive` 파괴 전에 표시 끝을 받는다. 스크롤러 자체가 파괴될 때는 사용자 코드 없이 활성 셀의 바인딩만 푼다 |
| 위치 훅 | 레이아웃 캐시(슬롯 시작·크기)로만 계산한다. 위치·범위·레이아웃·뷰포트 크기가 바뀐 프레임에만 LateUpdate 끝에서 한 번 돌고, 새 셀은 활성화 즉시 받는다. 꺼져 있으면 건너뛴다 |
| 코드로 콘텐츠 이동 | 루프 순환 보정·위치 유지 재배치가 한 이동 루틴을 쓴다. 드래그 중이면 손가락 기준점과 직전 위치를 같이 옮겨 놓을 때 관성 속도가 튀지 않는다. 가장자리 너머로 당기는 중이면 당긴 거리와 고무줄 저항을 이어 간다 |
