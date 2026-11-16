# Changelog

이 패키지의 변경 내역. 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/), 버전은 [SemVer](https://semver.org/lang/ko/)를 따른다.

## [Unreleased]

## [0.4.0] - 2026-11-16

첫 공개 릴리스.

### Added
- `CyScroller`: uGUI `ScrollRect` 위의 가상화 스크롤러. 보이는 구간과 overscan 구간의 셀만 만들고 나머지는 빈 스크롤 길이로 둔다.
  세로·가로, 셀마다 다른 크기, 간격·패딩, 뷰포트 앞뒤 overscan(`OverscanBefore`·`OverscanAfter`)
- 데이터 소스 `ICyScrollerDataSource`(`GetItemCount`·`GetItemSize`·`GetCell`)와 셀 기반 클래스 `CyScrollerCell`.
  `ReuseIdentifier` 단위 셀 풀(`DequeueCell`), 풀 미리 채우기(`Prewarm`·`PrewarmAsync`)와 식별자별 상한(`SetMaxPoolSize`·`DefaultMaxPoolSize`)
- 이동: `ScrollToIndex`(뷰포트 기준점·셀 기준점, `ScrollEase` 31종과 커스텀 곡선), 셀이 보이게만 옮기는 `ScrollIntoView`(`ScrollAlign.Nearest`·여백).
  이동 중 재배치·뷰포트 크기 변화가 일어나도 새 목표로 이어 가고, 이동 뒤에는 사용자가 움직이기 전까지 정렬을 유지한다
- 루프(짧은 목록도 뷰포트를 채우는 세트 수, 재바인딩 없는 순환 보정, 드래그 중 기준점 재설정), 스냅(`SnapPickAnchor`·`SnapViewportAnchor`·`SnapCellAnchor`·`SnapSpeedThreshold`),
  속도 상한(`MaxScrollSpeed`), 스크롤바 표시 모드
- 증분 변경: `InsertCells`·`RemoveCells`·`MoveCell`·`RefreshCells`·`ReloadCell`을 `BeginUpdates`/`EndUpdates`로 묶어 전체를 다시 읽지 않고 반영하며 보던 화면을 지킨다
- 안정 항목 ID(`ICyScrollerItemIdProvider`)와 위치 앵커: `ReloadDataKeepingPosition`, `ReloadData(ReloadAnchor)`, `CaptureAnchor`/`RestoreAnchor`, 키 유지 리로드(`PreserveCellsById`)
- 셀 크기 변경 `ResizeCell`(바로 또는 애니메이션, `ResizeAnchor`로 고정할 가장자리 선택)
- 셀 훅: 활성·표시 이벤트(`CellActiveChanged`, `CellWillDisplay`/`CellDidEndDisplay`), 늦은 비동기 결과를 버리는 `BindVersion`, 뷰포트 위치 훅(`NotifyCellPositions`·`OnViewportPositionChanged`)
- 스크롤 상태: 정착(`IsSettled`·`ScrollSettled`), 고속 스크롤(`IsFastScrolling`·`FastScrollingChanged`), 끝 근접 이벤트(`NearEdgeReached`·`NearEdgeDistance`)
- 에디터: `GameObject > UI (Canvas) > CyKim Scroller` 생성 메뉴(Unity 6.2 이하 `UI`), 플레이 중 상태·디버그 인스펙터
- 샘플 `Basic`: 가변 높이 목록과 편집 버튼, 루프 휠 피커, 루프 캐러셀. UI를 런타임에 만든다
- 테스트: EditMode(레이아웃·이징)와 PlayMode(가상화·이동·루프·스냅·증분 변경·셀 훅·스크롤 상태), 스크롤 핫패스 GC 할당 0 검사
