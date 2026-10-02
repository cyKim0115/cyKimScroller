# Changelog

이 패키지의 변경 내역. 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/), 버전은 [SemVer](https://semver.org/lang/ko/)를 따른다.

## [Unreleased]

### Added
- 드래그 안전 좌표 이동 경로(내부 `ShiftScrollPosition`): 레이아웃 좌표가 밀려도 화면이 그대로 보이게 스크롤 위치를 옮기고, 드래그 기준점·직전 위치(놓을 때 관성 속도), 진행 중인 트윈의 시작점, 점프 정렬 위치를 함께 옮긴다.
  트윈 목표(스냅 포함)는 요청에서 매 프레임 다시 계산하므로 레이아웃을 따라간다
- Profiler 마커 `CyScroller.UpdateActiveRange`, `CyScroller.Relayout`
- 테스트: 드래그 중 좌표 이동·재배치 뒤 놓는 속도 비교, 트윈 중 좌표 이동(다음 프레임 위치로 시작점 보정 확인), 가장자리 너머로 당긴 드래그 중 재배치(위·왼쪽 가장자리, 스크롤 이벤트 안에서 옮겨진 아래쪽 가장자리), 루프 점프 직후 순환 보정 뒤 정렬 유지, 순환 보정을 지나는 루프 스크롤 GC 0, 레이아웃 모델 무작위 대조(고정 시드, 선형 탐색 기준 모델)
- `ScrollIntoView(dataIndex, align, margin, tweenType, tweenTime, onComplete, loopJumpDirection)`와 `ScrollAlign`(Start·Center·End·Nearest): 셀이 보이게만 이동한다.
  Nearest는 이미 완전히 보이면 움직이지 않고 바로 완료하며, 앞쪽에 걸리면 Start, 뒤쪽이면 End, 여백까지 합쳐 뷰포트보다 크면 Start로 맞춘다. 루프는 보이는 사본 기준이다.
  여백은 콘텐츠 끝 너머로는 셈하지 않고, 목표가 지금 위치라 더 움직일 수 없을 때(뷰포트보다 큰 셀이 이미 시작에 맞춰져 있을 때 등)도 움직이지 않는다.
  Nearest가 움직이지 않으면 사용자 드래그·관성은 그대로 둔다
- `IsDataIndexFullyVisible(dataIndex, margin)` (lookAhead를 뺀 실제 뷰포트 기준, 여백은 콘텐츠 끝까지만, 루프는 어느 사본이든), `GetCellStart(dataIndex)`, `GetCellSize(dataIndex)`
- 테스트: ScrollIntoView(Nearest 정지·Start/End 선택·여백·큰 셀·Start/Center/End 수치·가로·루프 사본(앞·뒤 사본 선택)·드래그 중·트윈,
  콘텐츠 끝 셀의 여백과 이미 시작에 맞춰진 큰 셀에서 드래그·관성 유지), 완전히 보임 판정 경계(맞닿음·여백·lookAhead), 셀 좌표(패딩·간격·루프 가운데 세트),
  트윈 중 재배치(Spacing·위치 유지 리로드의 시작점 보정·개수 감소(목표가 스크롤 끝과 갈리는 경우, 루프)·0개·뷰포트 크기·스냅·루프 진행 방향),
  재배치·뷰포트 크기 변화 다음 걸음 위치(선형·EaseInOutCubic·EaseOutBack, 같이 밀린 재배치는 원래 곡선 유지), 마지막 걸음 콜백이 요청한 재배치(정렬 유지, 루프 스냅의 셀 번호),
  범위 갱신 콜백에서 시작한 점프, 트윈 목표 재계산 GC 0, 트윈 곡선 단조 분류(EditMode, 표본 대조)
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
- 테스트: 바인딩 버전(바인딩·회수·재사용·리로드, 풀 밖에서 만든 뷰, 스크롤러 파괴 때 사용자 코드 없는 바인딩 해제), 표시 이벤트(lookAhead 구간 제외와 드나듦, 리로드·ClearActive의 표시 끝 순서,
  루프 순환 보정 무이벤트, 가로, 무작위 연산 짝 대조(일반·루프), 가상 메서드 호출 시점, 콜백 안 리로드 지연, 사용자 예외,
  범위 갱신 콜백 7곳(표시 끝·회수 이벤트·`OnRecycled`·회수/활성화 알림·델리게이트·표시 시작) 안 즉시 점프(루프 데이터 2000개의 활성 셀 수·활성화 수·뷰포트 밖 표시 시작 없음, 루프가 아닐 때 새 위치 범위)와
  즉시 스냅(`ScrollerSnapped`의 셀 번호·셀 뷰, 루프·일반, 같은 콜백에서 위치 유지 리로드를 요청할 때 한 번만 알림),
  멈추지 않고 옮기는 콜백의 다시 맞추기 상한과 같은 프레임 LateUpdate 재시도), 위치 훅(피벗·뷰포트 크기 반영 값, 가로, 활성화 즉시 값,
  꺼짐, 바뀐 것 없는 프레임 무호출, 스크롤러 LateUpdate 뒤 변화의 같은 프레임·다음 프레임 반영), 셀 훅을 켠 스크롤·루프 스윕 GC 0
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
- 테스트: 앵커 캡처(앞·뒤 기준, 경계, ID 없음·빈 목록), 복원 왕복(세로 패딩·간격 안·스크롤 끝, 가로, 루프 가운데 사본·창 밖 사본), ID를 못 찾을 때 인덱스 대체·지워진 맨 앞 항목,
  복원의 트윈·관성·정렬 정지와 드래그 기준점, 콜백 안 복원, 준비 전 보관(빈 목록·로드 전·델리게이트 없음·뷰포트 길이 0·같은 프레임 0 왕복)과 버리는 요청 9종(빈 목록 `Snap` 포함),
  재배치 안 즉시 적용(빈 목록의 위치 유지 리로드·축 전환, 프레임을 넘기지 않고 앵커 자리 셀만 표시), 보관 중 자동 스냅 대기와 적용할 때 관성·스냅 대기 정지,
  `ReloadAnchor`(Start·End·Factor·루프 End·FirstVisible 삽입·LastVisible 크기 변화·끝에 붙은 채팅·앞뒤 삽입·빈 목록·콜백 안 지연), ID 위치 유지 리로드(앞 삽입·삭제, 루프),
  ID 정렬 유지(앞 삽입·정렬 항목 삭제), ID 트윈 재매핑(같은 항목 도착·완료 1회), 셀 `ItemId` 바인딩·해제, ID 사전 갱신 시점, 중복 ID 경고 1회,
  데이터가 그대로일 때 뒤쪽 중복 ID 항목 제자리(복원 왕복·여백 재배치·위치 유지 리로드·FirstVisible·정렬 유지·루프 전환, 자리가 바뀌면 앞 인덱스),
  ID 제공자 스크롤 스윕·앵커 왕복 GC 0, 레이아웃 뒤 기준 슬롯 조회(EditMode, 기준 모델 대조)

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
