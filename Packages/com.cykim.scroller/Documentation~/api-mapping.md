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
| 풀 미리 채우기·상한 | `Prewarm(prefab, count)` / `PrewarmAsync(prefab, count)`, `SetMaxRecycled(cellIdentifier, max)` / `DefaultMaxRecycled` | — (`register(_:forCellReuseIdentifier:)`는 등록만 하고 미리 만들지 않는다) | `RecycledViewPool.putRecycledView()`로 미리 넣기, `RecycledViewPool.setMaxRecycledViews(viewType, max)` | — |
| 재활용 직전 훅 | `OnRecycled()`, `CellViewWillRecycle` | `prepareForReuse()` | `onViewRecycled()` | `unbindItem` |
| 셀이 보이는 데이터 인덱스 | `DataIndex` (루프 슬롯은 `CellIndex`) | `indexPath` | `getBindingAdapterPosition()` | `bindItem`의 index |
| 바뀐 부분만 다시 그리기 | `RefreshCellView(changeMask)` 재정의 (changeMask는 사용자 정의 비트 플래그) | — (`reconfigureRows(at:)`가 `tableView(_:cellForRowAt:)`에서 기존 셀을 다시 구성) | `onBindViewHolder(holder, position, payloads)` | `bindItem` |
| 항목 안정 ID | `ICyScrollerItemIdProvider.GetItemId` (델리게이트가 선택 구현), 셀 뷰 `ItemId` | diffable data source의 항목 식별자 (`NSDiffableDataSourceSnapshot`) | `Adapter.getItemId()` + `setHasStableIds(true)` | — |

## 갱신과 이동

| 개념 | CyKim Scroller | UIKit | Android | UI Toolkit |
|---|---|---|---|---|
| 전체 다시 읽기 | `ReloadData()` / `ReloadData(scrollPositionFactor)` / `ReloadData(ReloadAnchor.Start / End)` | `reloadData()` | `notifyDataSetChanged()` | `RefreshItems()` / `Rebuild()` |
| 위치를 지키며 다시 읽기 | `ReloadDataKeepingPosition()`, `ReloadData(ReloadAnchor.FirstVisible / LastVisible)` (항목 ID가 있으면 ID로 같은 항목, LastVisible은 아래쪽 기준) | — | — | — |
| 스크롤 위치 저장·복원 | `CaptureAnchor(trailing)` / `RestoreAnchor(anchor)` (`CyScrollerAnchor`: 항목 + 뷰포트 가장자리까지 거리, 데이터·뷰포트가 준비되기 전 요청은 보관) | — (`contentOffset` 좌표를 저장) | `LayoutManager.onSaveInstanceState()` / `onRestoreInstanceState()` (맨 앞 항목 위치·오프셋, 항목이 생길 때까지 보관), `scrollToPositionWithOffset()` | — |
| ID로 인덱스 찾기 | `FindDataIndexForItemId(itemId)` | `UITableViewDiffableDataSource.indexPath(for:)` | — (`findViewHolderForItemId()`는 붙어 있는 뷰만) | — |
| 항목 삽입·삭제·이동 알림 | `InsertCells(dataIndex, count)` / `RemoveCells(dataIndex, count)` / `MoveCell(from, to)` (삽입분만 크기를 묻고 남은 셀은 다시 바인딩하지 않으며 보던 화면을 지킨다) | `insertRows(at:with:)` / `deleteRows(at:with:)` / `moveRow(at:to:)` | `notifyItemRangeInserted()` / `notifyItemRangeRemoved()` / `notifyItemMoved()` | — (`itemsSource`를 바꾼 뒤 `RefreshItems()` / `Rebuild()`) |
| 여러 변경을 묶어 한 번에 적용 | `BeginUpdates()` / `EndUpdates()` (중첩 가능. 연산은 호출 순서대로 적용하고 각 인덱스는 앞 연산까지 반영한 기준. 예외에 대비해 `EndUpdates`는 `finally`에서, 닫히지 않은 배치는 프레임을 넘기면 경고) | `performBatchUpdates(_:completion:)` (또는 `beginUpdates()` / `endUpdates()`). **인덱스 해석이 다르다**: 호출 순서와 상관없이 삭제·이동 출발점은 배치 전, 삽입·이동 도착점은 배치 후 인덱스다. 옮길 때는 삭제를 큰 인덱스부터 먼저, 그다음 삽입을 작은 인덱스부터 부른다 | — (알림은 다음 레이아웃 패스에서 모아 처리하고, 각 위치는 앞 알림을 반영한 기준이라 CyScroller와 같다) | — |
| 인덱스만 바뀐 셀 | 셀 뷰 `OnDataIndexChanged(previousDataIndex)` | — (셀은 그대로이고 `indexPath(for:)`가 바뀐다) | — (뷰 홀더는 그대로이고 `getBindingAdapterPosition()`이 바뀐다) | — |
| 보이는 셀만 다시 그리기 | `RefreshActiveCellViews()` / `RefreshActiveCellViews(changeMask)` | `reconfigureRows(at:)`에 `indexPathsForVisibleRows` | `notifyItemRangeChanged(0, itemCount, payload)` | `RefreshItems()` |
| 항목 내용만 바뀜 (크기·셀 종류 그대로) | `RefreshCells(dataIndex, count, changeMask)` (그 항목의 활성 셀만 `RefreshCellView(changeMask)`를 받고 다시 바인딩하지 않는다. 배치 안에서는 셀마다 OR로 모아 한 번) | `reconfigureRows(at:)` | `notifyItemRangeChanged(start, count, payload)` (페이로드는 다음 바인딩까지 모였다가 `onBindViewHolder(holder, position, payloads)`로) | `RefreshItem(index)` |
| 항목 다시 받기 (크기·셀 종류가 바뀔 수 있음) | `ReloadCellView(dataIndex)` (그 항목만 크기를 다시 묻고 활성 셀은 델리게이트로 다시 받는다) | `reloadRows(at:with:)` | `notifyItemChanged(position)` (페이로드 없이) | `RefreshItem(index)` |
| 항목 크기만 바뀜 (셀 그대로, 애니메이션 가능) | `ResizeCellView(dataIndex, duration, tweenType, anchor)`, 셀 뷰 `RequestResize(...)` (크기만 다시 묻고 다시 바인딩하지 않는다. `ResizeAnchor`로 셀의 위·아래 가장자리나 보던 화면을 지킨다) | 높이를 바꾼 뒤 `performBatchUpdates(nil)` (또는 빈 `beginUpdates()` / `endUpdates()`) | `notifyItemChanged(position, payload)` (같은 뷰 홀더를 페이로드로 부분 바인딩하고 다시 측정, `ItemAnimator`가 바뀐 크기를 애니메이션) | — |
| 목록을 통째로 바꿔도 같은 항목의 셀 유지 | `PreserveCellsById` + `ReloadData(ReloadAnchor.FirstVisible / LastVisible)`·`ReloadData(in anchor)`·`ReloadDataKeepingPosition()` (항목 ID가 같은 활성 셀은 다시 바인딩하지 않고 새 자리로 옮긴다. 내용은 비교하지 않으므로 바뀐 항목은 `RefreshCells`로 알린다) | diffable data source의 `apply(_:animatingDifferences:)` (같은 식별자의 셀을 유지하고, 내용이 바뀐 항목은 스냅샷의 `reconfigureItems` / `reloadItems`로 알린다) | `ListAdapter.submitList()` (`DiffUtil`: `areItemsTheSame`으로 같은 항목을 찾아 삽입·삭제·이동으로 알리고, `areContentsTheSame`이 false인 항목만 다시 바인딩한다) | — |
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
| 스크롤이 멈춤 (관성·애니메이션 포함) | `ScrollerSettled` / `IsSettled`, 셀 뷰 `OnScrollerSettled()` | `scrollViewDidEndDecelerating(_:)` / `scrollViewDidEndScrollingAnimation(_:)` (감속 없이 놓으면 `scrollViewDidEndDragging(_:willDecelerate:)`) | `onScrollStateChanged()`의 `SCROLL_STATE_IDLE` | — |
| 빠르게 스크롤하는 중 | `IsFastScrolling` / `ScrollerFastScrollingChanged` (뷰포트 비율 임계값, 히스테리시스) | — (`scrollViewDidScroll(_:)`에서 속도를 계산) | — (`onScrolled()`의 이동량으로 계산) | — |
| 끝 근처 도달 (다음 페이지 불러오기) | `NearEdgeDistance` + `ScrollerNearEdge(scroller, ScrollEdge)` (가장자리마다 한 번, 멀어지거나 개수가 바뀌면 다시 열림) | `UITableViewDataSourcePrefetching`의 `tableView(_:prefetchRowsAt:)`, 또는 `scrollViewDidScroll(_:)`에서 남은 거리 비교 | `OnScrollListener.onScrolled()`에서 `findLastVisibleItemPosition()`과 개수 비교 (Paging 라이브러리는 `prefetchDistance`) | — |
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
| 항목 ID·앵커 | 델리게이트를 다시 받을 때만 항목 ID를 받아 인덱스별 배열과 ID → 인덱스 사전을 채운다 (O(N), 스크롤 중에는 배열만 읽어 할당 없음). 앵커는 항목(ID·인덱스)과 뷰포트 앞 또는 뒤 가장자리까지 거리이고, 복원은 ID → 인덱스 순으로 찾는다. ID는 앵커 인덱스 자리에 같은 ID가 남아 있으면 그 자리를 먼저 써서(O(1)) 중복 ID라도 데이터가 그대로면 제자리다. 로드 전·데이터 0개·뷰포트 길이 0일 때 받은 복원은 보관했다가 준비되면(재배치면 그 재배치 안에서) 적용하고, 코드로 부른 새 위치 요청이 오면 갈 셀이 없어도 버린다. 드래그·휠 뒤 자동 스냅은 보관 중에는 기다리고, 적용할 때 남은 관성과 스냅 대기를 멈춘다 |
| 증분 변경 | 연산을 목록에 적어 두었다가 배치 끝에 크기·항목 ID 배열을 `Array.Copy`로 옮기고 삽입분만 델리게이트에 묻는다. 접두합과 ID 사전은 바뀐 가장 앞 자리부터 다시 맞춘다(버퍼를 늘릴 때는 옛 버퍼를 통째로 옮겨 이미 맞은 접두합을 지킨다). 활성 셀은 연산을 거친 새 인덱스로 옮기고(다시 바인딩 없음) 지워졌거나 범위를 벗어난 셀만 회수, 빈 자리만 바인딩한다. 위치는 맨 앞 항목 기준으로 좌표 이동 경로를 거쳐 보정해 드래그·트윈을 끊지 않는다. 맨 앞 항목이 지워지거나 옮겨지면 그 항목 대신 비운 자리를 따라가(그 자리에 삽입한 항목은 자리를 채운다) 화면이 옮겨진 항목으로 튀지 않고, 트윈 대상·보관한 앵커·활성 셀은 항목을 따라간다(끝난 점프의 정렬은 대상이 옮겨지면 푼다). 루프 모드·개수 불일치(연산이 없는 배치 포함)·콜백 안 호출은 앵커 보존 리로드로 바꾼다(내용 갱신만 있는 배치는 제외, 아래 부분 갱신. 이 리로드는 키 유지 없이 모든 셀을 다시 바인딩한다) |
| 부분 갱신 | 내용 갱신은 레이아웃을 건드리지 않고 활성 셀(루프 사본 포함)에만 부른다. 배치 안에서는 연산 목록에 적었다가 배치 끝에 옛 활성 셀마다 연산을 따라가며 changeMask를 OR로 모아, 남은 셀에 한 번 부른다(항목별 마스크 배열 없이 연산 수 × 활성 셀 수, 작업 목록은 재사용). 내용 갱신만 있는 배치는 루프 모드·콜백 안에서도 리로드하지 않는다. 콜백 안에서 끝났으면 셀별 마스크를 먼저 모으고 연산 목록을 비운 뒤 부르고(그 안에서 다시 연 배치와 섞이지 않는다), 증분 변경을 적용하거나 키 유지 리로드·재배치로 다시 읽는 중이면 셀을 새 인덱스로 맞춘 뒤로 미룬다. 다시 받기는 그 자리 크기를 비워 배치 끝에 삽입분과 함께 묻고, 그 항목의 활성 셀만 회수·재바인딩한다. 항목 ID는 바뀌었을 때만 그 자리부터 사전을 다시 맞춘다 |
| 크기 변경 | 증분 변경과 같은 연산 목록에 적어 배치 끝에 그 자리 크기만 묻고 셀은 회수하지 않는다. 애니메이션은 매 프레임 크기 한 걸음을 같은 경로로 반영하되, 중간 걸음은 접두합을 다시 더하지 않고 그 뒤 항목 위치에 변화량을 더해 계산한다(항목 수와 무관). 마지막 걸음이나 다른 크기·개수 변경이 바뀐 자리부터 한 번 다시 더한다. 루프 모드는 바로 바꾸고 위치를 지키는 재배치로 맞춘다 |
| 키 유지 리로드 | 셀을 먼저 회수하지 않고 개수·크기·ID를 모두 다시 읽어 위치를 정한 뒤, 옛 활성 셀마다 같은 ID 항목의 새 인덱스(이전 인덱스 자리에 같은 ID가 남아 있으면 그 자리, 아니면 ID → 인덱스 사전)를 찾아 증분 변경과 같은 순서(표시 끝 → 회수 → 인덱스 변경 알림·재배치 → 새 자리 바인딩 → 표시 시작)로 맞춘다. 내용은 비교하지 않는다. 루프는 셀이 화면에 있던 자리에 가장 가까운 사본으로 옮기고, 한 자리에는 셀 하나만 옮긴다. 작업 목록은 재사용해 할당하지 않는다. 셀을 맞추기 전에 사용자 코드(트윈 멈춤 알림, 다시 읽는 중의 델리게이트)가 부른 내용 갱신은 미뤘다가 맞춘 뒤 남은 셀에 부른다(`ReloadDataKeepingPosition` 재배치도 같다). 기다리는 리로드가 그사이 알린 내용 갱신·다시 받기를 대신 처리해야 하거나 델리게이트가 바뀌었으면 모두 다시 바인딩한다 |
| 코드로 콘텐츠 이동 | 루프 순환 보정·위치 유지 재배치가 한 이동 루틴을 쓴다. 드래그 중이면 손가락 기준점과 직전 위치를 같이 옮겨 놓을 때 관성 속도가 튀지 않는다. 가장자리 너머로 당기는 중이면 당긴 거리와 고무줄 저항을 이어 간다 |
