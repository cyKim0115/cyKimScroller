# Changelog

이 패키지의 변경 내역. 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/), 버전은 [SemVer](https://semver.org/lang/ko/)를 따른다.

## [Unreleased]

### Added
- 풀 미리 채우기 `Prewarm(prefab, count)`와 `PrewarmAsync(prefab, count)`(`Object.InstantiateAsync`): 그 식별자 풀의 회수 셀이 count가 될 때까지 만들어 끈 채 넣고 `CellViewInstantiated`를 부른다.
  비동기는 진행 중인 요청까지 세어 더 만들지 않고, 할 일이 없으면 null을 돌려주며, 끝나기 전에 스크롤러가 파괴되면 만든 셀을 모두 파괴한다
- 식별자별 회수 상한 `SetMaxRecycled(cellIdentifier, max)`·`GetMaxRecycled(cellIdentifier)`와 기본 상한 `DefaultMaxRecycled`(인스펙터 Pool, 기본 0 = 제한 없음): 넘치면 가장 오래 회수된 셀부터 파괴한다.
  상한은 풀 객체에 두어 회수할 때 사전을 다시 찾지 않는다

## [0.2.1] - 2026-10-06

### Changed
- 라이선스를 MIT로 바꾼다 (`LICENSE.md`, `package.json`의 `license`). 저장소 루트에도 같은 `LICENSE`를 둔다
- 샘플 `Basic`은 어두운 단색 테마 대신 세이지·차콜 라이트 테마로 그린다. 스크롤러 패널·셀·카드·버튼·휠 하이라이트는 `Resources/CyKimScrollerBasic`의
  임시 흰색 9-slice 스프라이트(`Panel`·`Card`·`Button`·`Pill`)에 색을 곱해 그리고, 스프라이트가 없으면 단색으로 그린다(경고 한 번).
  목록 셀·캐러셀 카드는 무지개 색 대신 밝은 세이지 톤을 돌아가며 쓰고(항목을 만들 때 미리 정해 바인딩 할당 없음), 편집 버튼 3개는 강조색 주 버튼으로 구분한다.
  밝은 바탕에서도 글자가 읽히게 캐러셀 양옆 카드·휠 행은 전보다 덜 흐리게 그린다. 배치·동작·버튼 라벨은 그대로다

### Fixed
- 키 유지 리로드·재배치(`PreserveCellsById`)가 다시 읽는 도중 델리게이트 예외로 멈추면(개수가 늘어 크기 버퍼를 다시 만든 뒤 `GetCellViewSize`가 던지는 경우 등)
  다음 갱신의 복구 리로드가 반쯤 읽은 배치에서 화면을 읽다 `IndexOutOfRangeException`을 내던 문제 (0.2.0부터).
  이제 다시 읽기 전 화면 앵커를 보관한 앵커로 넘겨 복구 리로드가 그 자리로 간다 (복구 전까지 `CaptureAnchor`도 이 앵커를 돌려준다).
  복구 전에 코드로 옮기면(`ScrollPosition`·`NormalizedScrollPosition` 대입, 점프·`ScrollIntoView`·`Snap`) 복구 리로드를 먼저 처리한 뒤 다시 읽은 배치에서 위치를 정하고,
  배치 안이라 먼저 처리할 수 없으면 복구 리로드가 그 요청이 옮긴 위치를 지킨다. 복구 전까지는 반쯤 읽은 배치로 범위를 맞추지 않는다
- 키 유지 리로드·재배치가 다시 읽는 도중 델리게이트가 닫은 배치(`BeginUpdates`/`EndUpdates`)에 미룬 `Clear*`·리로드 요청·범위 갱신이 있으면
  배치 끝에서 반쯤 읽은 배치로 범위를 맞춰 셀을 모두 다시 바인딩하거나 예외를 내던 문제 (0.2.0부터). 이제 셀을 새 인덱스로 맞춘 뒤 처리한다

## [0.2.0] - 2026-10-06

### Added
- 드래그 안전 좌표 이동 경로(내부 `ShiftScrollPosition`): 레이아웃 좌표가 밀려도 화면이 그대로 보이게 스크롤 위치를 옮기고, 드래그 기준점·직전 위치(놓을 때 관성 속도), 진행 중인 트윈의 시작점, 점프 정렬 위치를 함께 옮긴다.
  트윈 목표(스냅 포함)는 요청에서 매 프레임 다시 계산하므로 레이아웃을 따라간다
- Profiler 마커 `CyScroller.UpdateActiveRange`, `CyScroller.Relayout`
- `ScrollIntoView(dataIndex, align, margin, tweenType, tweenTime, onComplete, loopJumpDirection)`와 `ScrollAlign`(Start·Center·End·Nearest): 셀이 보이게만 이동한다.
  Nearest는 이미 완전히 보이면 움직이지 않고 바로 완료하며, 앞쪽에 걸리면 Start, 뒤쪽이면 End, 여백까지 합쳐 뷰포트보다 크면 Start로 맞춘다. 루프는 보이는 사본 기준이다.
  여백은 콘텐츠 끝 너머로는 셈하지 않고, 목표가 지금 위치라 더 움직일 수 없을 때(뷰포트보다 큰 셀이 이미 시작에 맞춰져 있을 때 등)도 움직이지 않는다.
  Nearest가 움직이지 않으면 사용자 드래그·관성은 그대로 둔다
- `IsDataIndexFullyVisible(dataIndex, margin)` (lookAhead를 뺀 실제 뷰포트 기준, 여백은 콘텐츠 끝까지만, 루프는 어느 사본이든), `GetCellStart(dataIndex)`, `GetCellSize(dataIndex)`
- 셀 훅 `CyScrollerCellView.BindVersion`(바인딩할 때와 바인딩이 풀릴 때(회수, `ClearActive`로 파괴, 활성인 채로 스크롤러와 함께 파괴) 1씩 증가,
  늦게 끝난 비동기 결과를 버리는 데 쓴다), `IsBound`, `IsDisplayed`,
  가상 메서드 `OnBecameVisible`·`OnBecameHidden`(실제 뷰포트 기준 표시 시작·끝)과 `OnViewportPositionChanged(normalizedOffset)`(뷰포트 안 위치)
- 표시 이벤트 `CellViewWillDisplay`·`CellViewDidEndDisplay`(`CellViewDisplayChangedHandler`): lookAhead 구간을 뺀 실제 뷰포트 기준이다.
  표시 범위를 활성 범위와 따로 추적해 범위 갱신마다 바뀐 슬롯만큼만 알리고(O(변경 수)), 셀마다 항상 짝을 맞춘다. 보이던 셀은 회수·`ClearActive` 파괴 전에 표시 끝을 받고, 루프 순환 보정에는 오지 않는다.
  스크롤러 자체가 파괴될 때는 파괴 순서가 정해져 있지 않아 사용자 코드를 부르지 않고 활성 셀의 바인딩만 푼다 (표시 끝 없음, `IsDisplayed`는 마지막 값으로 남아 셀의 `OnDestroy`에서 정리할 수 있다)
- 위치 훅 `NotifyCellPositions`·`CellPositionPivot`과 `CellViewPositionChanged`(`CellViewPositionChangedHandler`): 위치·활성 범위·레이아웃·뷰포트 크기가 바뀐 프레임에
  LateUpdate 끝에서 활성 셀마다 한 번, 새로 활성화된 셀은 활성화 즉시 알린다. 꺼져 있으면 계산을 건너뛰고, 켜도 할당이 없다
- 인스펙터 플레이 중 상태에 표시 범위(Displayed Slots)
- 샘플 `Basic`: 휠 피커(`BasicWheelPickerController`·`BasicWheelRowView`: 세로 루프 + 가운데 스냅, 위치 훅으로 원통처럼 기울고 작아지는 행, 가운데 하이라이트 바, 스냅된 값 표시),
  위치 훅으로 크기·투명도를 바꾸는 캐러셀 카드(`BasicCarouselCardView`), 표시 이벤트로 실제 보이는 셀을 세는 목록 상태 줄(셀 표시·활성화·회수가 있었던 프레임에만 다시 그린다),
  1920×1080·1280×720에서 겹치지 않는 비율 배치와 어두운 테마
- 안정 항목 ID `ICyScrollerItemIdProvider`(선택 구현, `GetItemId(scroller, dataIndex)`): 델리게이트가 함께 구현하면 델리게이트를 다시 받을 때마다 모든 항목의 ID를 받아
  인덱스별 배열과 ID → 인덱스 사전에 둔다(O(N), 사전은 비운 뒤 다시 써서 용량 유지). 스크롤 중에는 ID를 다시 받지 않고 할당도 없다.
  `FindDataIndexForItemId(itemId)`로 지금 인덱스를 찾고(없으면 −1), 셀 뷰는 `ItemId`·`HasItemId`로 받는다(바인딩할 때 채우고 바인딩이 풀리면 지움).
  같은 ID가 여럿이면 ID 조회는 앞 인덱스가 이기고(앵커 복원·정렬 유지는 이전 인덱스 자리에 같은 ID가 남아 있으면 그 자리라서 데이터가 그대로면 제자리),
  에디터·개발 빌드에서는 다시 받을 때마다 경고를 한 번 남긴다
- 위치 앵커 `CyScrollerAnchor`(직렬화 가능: `DataIndex`·`ItemId`·`HasItemId`·`Offset`·`Trailing`, `IsValid`)와 `CaptureAnchor(trailing)`·`RestoreAnchor(in anchor)`:
  뷰포트 앞(또는 뒤) 가장자리에 걸친 항목과 그 가장자리까지 거리로 위치를 적고 되돌린다. 복원은 ID → `DataIndex`(개수를 넘으면 마지막) 순으로 항목을 찾고,
  빈 앵커는 앞 기준이면 처음·뒤 기준이면 끝, 루프면 가운데 사이클 사본, 결과는 스크롤 범위로 자른다. `ScrollPosition` 대입처럼 트윈·관성·점프 정렬을 멈추고 드래그 기준점을 옮긴다
- 준비 전 복원 보관: 로드 전·데이터 0개·뷰포트 길이 0일 때 받은 `RestoreAnchor`는 보관했다가 다음 `ReloadData()`(`Delegate` 리로드 포함, 데이터가 있을 때),
  `ReloadDataKeepingPosition`·축 전환 같은 재배치(그 재배치 안에서 바로), 뷰포트 길이가 생길 때 적용한다. 그 사이 새 위치 요청(새 `RestoreAnchor`, `ReloadData(factor)`·Start·End, 점프·`ScrollIntoView`·`Snap`, `ScrollPosition` 대입)이 오면 갈 셀이 없어도 버린다.
  드래그·휠은 버리지 않고 그 뒤 자동 스냅은 보관하는 동안 기다리며, 나중에 적용할 때도 `RestoreAnchor`처럼 남은 관성과 스냅 대기를 멈춘다
- `ReloadData(ReloadAnchor, scrollPositionFactor)`와 `ReloadAnchor`(Factor·Start·End·FirstVisible·LastVisible), `ReloadData(in CyScrollerAnchor)`, 매개변수 없는 `ReloadData()`.
  FirstVisible·LastVisible은 다시 읽기 전 화면의 앞·뒤 기준 앵커를 다시 읽은 뒤 복원한다. LastVisible은 뷰포트 끝에 걸친 항목의 끝을 같은 자리에 두므로
  끝에 붙어 있는 채팅에서 마지막 메시지가 커져도 끝에 남고, 뒤에 항목이 붙으면 보던 메시지를 그대로 둔다. 루프의 End는 마지막 항목 끝을 뷰포트 끝에 맞춘다.
  콜백 안에서 부르면 위치 기준까지 보관했다가 범위 갱신이 끝난 뒤 처리한다
- 증분 변경: 데이터 일부만 바뀌었을 때 전체를 다시 읽지 않고 바뀐 것만 반영한다. 남은 셀은 다시 바인딩하지 않고 보던 화면·진행 중인 트윈·드래그를 지킨다.
  공통 계약(순차 의미론, 데이터를 먼저 바꾼 뒤 알림, 개수 불일치·루프 모드·콜백 안 호출은 앵커 보존 리로드로 대체, 트윈 대상이 지워지면 완료 콜백 없이 멈춤,
  키 유지 리로드는 내용 변경을 감지하지 않음)은 README "증분 변경 동작"에 모았다
  - 구조 변경 `InsertCells(dataIndex, count)`·`RemoveCells(dataIndex, count)`·`MoveCell(fromDataIndex, toDataIndex)`와 배치 `BeginUpdates()`·`EndUpdates()`(중첩 가능).
    연산은 호출 순서대로 적용한다(각 인덱스는 앞 연산까지 반영한 기준. RecyclerView `notifyItem*`과 같고, 삭제를 배치 전·삽입을 배치 후 인덱스로 읽는 UIKit 배치와는 다르다).
    삽입한 항목만 크기(와 항목 ID)를 묻고, 뷰포트 맨 앞 항목보다 앞에서 생긴 크기 변화만큼 스크롤 위치를 옮겨 화면을 지킨다(드래그 중이면 손가락 기준점까지).
    맨 앞 항목이 지워지거나 `MoveCell`로 옮겨지면 그 자리에 온 다음 항목이 같은 거리에 오고(같은 배치에서 그 빈자리에 삽입한 항목은 자리를 채운다),
    진행 중인 트윈·점프 정렬·보관한 앵커는 같은 항목을 따라간다(트윈·앵커는 옮겨진 항목 포함, 끝난 점프의 정렬은 대상이 옮겨지면 풀려 화면을 지킨다.
    대상이 지워지면 트윈을 멈추고 완료 콜백·`ScrollerSnapped`는 부르지 않는다).
    `EndUpdates`에서 개수가 `GetNumberOfCells`와 맞지 않으면(연산이 없는 배치 포함) 경고 후 `ReloadData(ReloadAnchor.FirstVisible)`로 다시 읽고, 루프 모드와 델리게이트·셀 이벤트 콜백 안 호출도 같은 리로드로 바꾼다(콜백 안이면 범위 갱신 뒤).
    배치 중에는 배치 전 상태를 유지하고 범위 갱신·델리게이트 호출·리로드·재배치·정리를 `EndUpdates`로 미룬다. 짝이 깨져 배치가 프레임을 넘겨 열려 있으면 배치마다 한 번 경고하고
    (예외에 대비해 `EndUpdates`는 `finally`에서 부른다), `ScrollerSnapped` 핸들러가 `EndUpdates` 안에서 예외를 던져도 배치는 닫는다. 범위 밖 인자는 자른다
  - 부분 갱신 `RefreshCells(dataIndex, count, changeMask = ~0)`·`RefreshActiveCellViews(changeMask)`와 `CyScrollerCellView.RefreshCellView(changeMask)`(사용자 정의 비트 플래그, 기본 구현은 `RefreshCellView()`):
    크기·개수는 그대로이고 내용만 바뀐 항목의 활성 셀만 다시 그린다(다시 바인딩·크기 질의 없음, `BindVersion` 그대로, 루프 모드는 사본 셀마다).
    배치 안에서는 항목마다 changeMask를 OR로 모아 `EndUpdates`에서 남은 셀마다 한 번 부르고, 뒤따른 삽입·삭제·이동을 따라가며 지워진 항목과 새로 바인딩되는 셀에는 부르지 않는다.
    갱신만 있는 배치는 루프 모드나 콜백 안에서 끝나도 리로드하지 않는다. 무인자 `RefreshActiveCellViews()`는 그대로다
  - 다시 받기 `ReloadCellView(dataIndex)`: 크기·셀 종류가 바뀐 항목 하나만 크기(와 항목 ID)를 다시 묻고 활성 셀을 다시 받는다(셀 종류가 바뀌어도 된다, `BindVersion` 증가,
    보이던 셀은 `CellViewDidEndDisplay` → `CellViewWillDisplay` 짝 유지). 활성 셀이 없으면 크기(와 ID)만 갱신하고, 크기가 바뀌면 삽입처럼 보던 화면을 지킨다
  - 키 유지 리로드 `PreserveCellsById`(옵트인, 기본 꺼짐, 인스펙터 Reload): 델리게이트가 항목 ID를 주면 위치를 지키며 다시 읽는 리로드
    (`ReloadData(ReloadAnchor.FirstVisible / LastVisible)`·`ReloadData(in anchor)`·`ReloadDataKeepingPosition()`)가 ID가 같은 활성 셀을 회수·재바인딩하지 않고 새 인덱스로 옮긴다
    (`OnDataIndexChanged`, `BindVersion` 그대로, 계속 보이면 표시 이벤트 없음, 새로 받은 크기로 배치, 루프 모드는 화면 자리가 가장 가까운 사본). 지워진 ID의 셀만 회수하고 새 ID만 바인딩하며, 위치 결과는 옵션을 끈 리로드와 같다.
    내용 변경은 감지하지 않으므로 바뀐 항목은 리로드한 뒤 `RefreshCells`·`ReloadCellView`로 알린다. 같은 ID는 같은 셀 종류로 본다.
    셀을 새 인덱스로 맞추기 전에 사용자 코드(트윈 멈춤 알림, 다시 읽는 중의 델리게이트)가 부른 `RefreshCells`(그 안에서 닫은 갱신만 있는 배치 포함)는 맞춘 뒤 남은 셀에 부른다.
    처음부터 다시 그리는 리로드(`ReloadData()`·`ReloadData(factor)`·Start·End)·다시 읽지 않는 재배치·축 전환과 증분 변경의 대체 리로드, 델리게이트를 바꾼 뒤의 첫 리로드,
    기다리는 리로드가 그사이 알린 내용 갱신·다시 받기를 대신 처리해야 할 때는 옵션과 무관하게 모든 셀을 다시 바인딩한다
  - `CyScrollerCellView.OnDataIndexChanged(previousDataIndex)`: 다시 바인딩하지 않고 인덱스만 바뀐 셀(증분 변경·키 유지 리로드)이 새 위치로 옮겨지기 직전에 받는다(`BindVersion` 그대로)
  - Profiler 마커 `CyScroller.ApplyUpdates`. 워밍업 뒤에는 증분 변경·부분 갱신·키 유지 리로드 모두 할당하지 않는다
  - 샘플 `Basic`: 세로 목록 데이터를 List로 두고 셀에 항목 번호(ID)를 보여 주며, 편집 버튼 `List: Insert 3 at top`(맨 위 삽입, 보던 화면 유지)·`List: Remove first visible`·`List: Move visible to top`으로
    증분 변경과 위치 보존을 보여 준다. 상태 줄은 표시 중인 데이터 인덱스 범위·항목 수·마지막 편집을 보여 준다
- 테스트: 위 기능마다 PlayMode 회귀 테스트와 List 기준 모델 무작위 대조(고정 시드), 레이아웃 증분 계산의 처음부터 계산 비트 단위 대조(EditMode),
  스크롤·루프·셀 훅·앵커·증분 변경·부분 갱신·키 유지 리로드 경로의 GC 0 테스트. EditMode 126개, PlayMode 257개

### Changed
- 루프 순환 보정은 `ShiftScrollPosition`을, 재배치 뒤 위치 복원(앵커·정렬)·`ScrollPosition` 대입·`ReloadData`·점프·트윈 이동은 같은 내부 이동 루틴을 거친다.
  아래 트윈 목표 재계산 항목과 Fixed 항목 말고는 동작이 0.1.0과 같다
- 트윈(점프·스냅·ScrollIntoView)은 목표 좌표 대신 요청(슬롯·정렬 위치·여백)을 저장하고 매 프레임 지금 배치에서 목표를 다시 계산한다.
  트윈 중 재배치(`Spacing`·`Padding`·`Loop`·방향 변경)가 일어나면 0.1.0은 새 목표로 바로 옮기고 완료 처리했지만, 이제 맨 앞 셀 기준 화면을 그대로 두고
  같은 데이터로 남은 시간 동안 이어 가며 끝에서 완료 콜백(스냅이면 `ScrollerSnapped`)을 한 번 부른다.
  재배치·뷰포트 크기 변화 다음 프레임에 화면이 튀지 않게 트윈 시작점을 다시 잡아 지금 화면에서 새 목표로 같은 시각에 끝낸다
  (Back·Elastic·Bounce·EaseOutExpo·EaseInOutExpo·Custom은 지금 화면에서 남은 시간 동안 곡선을 다시 그린다).
  마지막 프레임의 범위 갱신 콜백이 요청한 재배치도 트윈을 끝내기 전에 처리하고, 같은 때 요청한 `ReloadData`는 다른 프레임처럼 트윈을 멈춘다
  (0.1.0은 완료 처리 뒤에 리로드해 완료 콜백을 불렀다)
- `ClearActive`가 파괴할 셀의 바인딩을 푼다(`DataIndex`·`CellIndex` -1, `BindVersion` 증가). 0.1.0은 `Active`만 껐다. 보이던 셀은 파괴 전에 `CellViewDidEndDisplay`를 받는다.
  `CellViewVisibilityChanged`는 0.1.0과 같은 활성화(lookAhead 포함) 기준이다 (문서만 보강)
- 범위 갱신 콜백(셀 표시·회수 이벤트, 델리게이트 등) 안에서 바로 끝난 스냅(즉시 `Snap()` 등)의 `ScrollerSnapped`를 범위 갱신이 끝난 뒤
  (콜백 안에서 미룬 순환 보정과 새 위치의 셀 활성화 뒤) 보낸다. 셀 번호는 순환 보정 뒤 슬롯이고 셀 뷰는 그 슬롯의 활성 셀이다.
  0.1.0은 콜백 안에서 바로 보내 새 위치의 셀이 아직 활성화되지 않았으면 셀 뷰가 null이었다. 점프 완료 콜백은 0.1.0처럼 콜백 안에서 바로 부른다
- 샘플 캐러셀이 스크롤 이벤트에서 카드 크기를 직접 계산하던 방식(`OnScrolled`·`ApplyScale`)을 카드 셀 뷰의 위치 훅으로 바꾸고, 가운데에서 멀수록 투명도도 낮춘다.
  스냅 로그 대신 화면에 가운데 카드를 표시한다
- `ReloadDataKeepingPosition()`: 델리게이트가 항목 ID를 주면 맨 앞 항목과 진행 중인 트윈 목표·점프 정렬 대상을 ID로 다시 찾는다(앞쪽 삽입·삭제에도 같은 항목, 지워졌으면 같은 인덱스).
  ID가 있으면 델리게이트를 다시 받아도 점프 정렬을 유지한다(0.1.0은 데이터를 다시 받으면 정렬을 풀었다). ID가 없으면 0.1.0과 같다
- `ReloadData(float scrollPositionFactor = 0f)`를 `ReloadData()`와 `ReloadData(float)`로 나눴다. 기존 호출은 그대로 컴파일되고(정수 `0`도 비율 오버로드) 동작도 같다.
  다른 점은 보관한 앵커뿐이다: `ReloadData()`는 적용하고, 비율을 넘기면 버린다
- 레이아웃 접두합은 마지막 계산 뒤 크기가 바뀐 가장 앞 자리부터 다시 더한다(처음부터 더한 것과 같은 값). 리로드처럼 모든 크기를 다시 받으면 지금처럼 처음부터 더한다
- `ReloadData`(모든 오버로드)·`ReloadDataKeepingPosition`·재배치(`Spacing`·`Padding`·`Loop`·방향 등)·`ClearActive`·`ClearRecycled`를 증분 변경 배치 중에 부르면 `EndUpdates`에서 처리한다.
  배치를 쓰지 않으면 동작은 그대로다. `PreserveCellsById`를 켜지 않으면 리로드는 0.1.0처럼 모든 셀을 다시 바인딩한다
- 샘플 `Basic` 가운데 버튼 열: 편집 버튼 3개가 더해져 버튼 높이를 52 → 40으로 줄였다(1920×1080·1280×720에서 겹치지 않는다. 기존 버튼 라벨은 그대로).
  `List: Item #250 (center)`는 항목 번호(ID) 250을 찾아간다(편집하기 전에는 0.1.0과 같은 항목). 목록 상태 줄의 `#` 범위는 데이터 인덱스(`index`)로 바꿔 셀 라벨의 항목 번호와 구분한다

### Fixed
- 드래그로 가장자리 너머로 당긴 상태(Elastic)에서 재배치(`ReloadDataKeepingPosition`·`Spacing` 등)가 일어나면 위치가 스크롤 범위로 잘려 콘텐츠가 손가락 아래에서 가장자리로 튀고,
  이어지는 드래그에 고무줄 감쇠가 한 번 더 걸려 당긴 거리가 줄던 문제 (0.1.0부터). 이제 맨 앞 셀 기준 위치를 지키되 당기던 거리까지는 가장자리를 넘게 두고,
  ScrollRect 드래그 기준점을 고무줄 감쇠 전 위치로 잡는다. 드래그 중이 아닐 때는 0.1.0처럼 스크롤 범위 안으로 맞춘다
- 트윈 중 `ReloadDataKeepingPosition`(델리게이트 재질의)이 트윈을 취소해 완료 콜백이 불리지 않던 문제 (0.1.0부터).
  이제 같은 데이터(개수가 줄었으면 잘린 인덱스)로 계속 가고, 데이터가 0개가 되면 그 자리에서 끝내고 완료 콜백을 부른다
- 셀 표시 이벤트 같은 범위 갱신 콜백 안에서 새 점프를 시작하면, 이전 트윈의 마지막 프레임이면 새 점프가 움직이기도 전에 완료 처리되던 문제 (0.1.0부터)
- 범위 갱신 콜백(셀 표시·회수 이벤트, `OnRecycled`, 델리게이트 `GetCellView` 등) 안에서 즉시 점프·`ScrollPosition` 대입으로 콘텐츠를 옮기면
  범위 갱신이 옛 위치 기준으로 끝까지 이어지던 문제 (0.1.0부터). 루프 즉시 점프가 순환 보정으로 슬롯 번호를 옮기면 회수가 빈 활성 목록에서 셀을 꺼내
  `ArgumentOutOfRangeException`이 나거나, 활성화 상한(2048)을 지나쳐 한 사이클 분량(데이터 개수만큼)의 셀을 한 번에 활성화했다.
  루프가 아니어도 활성 범위가 옛 위치에 남아, ScrollRect 스크롤 이벤트 안이었으면 다음 스크롤 전까지 빈 뷰포트가 보였다.
  이제 콜백이 콘텐츠를 옮기면 옛 위치 기준의 남은 회수·활성화·표시 이벤트를 멈추고 새 위치로 다시 맞춘다. 콜백 안의 순환 보정은 다시 맞추기 직전으로 미루고,
  매번 옮기는 콜백은 4번까지만 다시 맞춘 뒤 다음 LateUpdate에 이어 간다

## [0.1.0] - 2026-10-01

### Added
- `CyScroller`: ScrollRect 기반 가상화 스크롤러 (세로·가로, 가변 셀 크기, 간격·패딩, lookAhead)
- `ICyScrollerDelegate`, `CyScrollerCellView`: 델리게이트 패턴과 `CellIdentifier` 단위 셀 뷰 풀링
- `JumpToDataIndex` + 31종 `TweenType`과 커스텀 곡선, 점프·스냅 정렬의 뷰포트 리사이즈 유지
- 루프 모드: 뷰포트·미리보기 길이에 맞춘 동적 세트 수(최소 5, 양쪽 한 사이클 여유), 재바인딩 없는 순환 보정, 드래그 중 기준점 재설정
- 스냅(누르고 있는 동안 대기, 탭으로 끊기면 재스냅), 속도 상한, 스크롤바 표시 모드(Never·루프는 ScrollRect에서 분리), 이벤트 8종
- `ReloadDataKeepingPosition`: 맨 앞 데이터 인덱스·오프셋 유지 리로드
- 콜백 안 `ReloadData`·`ReloadDataKeepingPosition`·`Clear*` 지연 처리, 델리게이트 교체 시 옛 인덱스 호출 방지, 사용자 코드 예외에도 활성 목록 일관성 유지
- 에디터: `GameObject > UI (Canvas) > CyKim Scroller` 생성 메뉴(6.2 이하 `UI`, RectMask2D 뷰포트, 우클릭한 부모 유지), 플레이 중 상태·디버그 인스펙터
- 테스트: 레이아웃 모델·이징 EditMode, 가상화·점프·루프·스냅·GC 할당·리뷰 회귀 PlayMode
- 샘플 `Basic`: 런타임 생성 UI (가변 높이 목록, 점프 버튼, 루프·스냅 캐러셀)
