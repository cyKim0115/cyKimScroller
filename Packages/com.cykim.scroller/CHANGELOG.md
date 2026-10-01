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

### Fixed
- 드래그로 가장자리 너머로 당긴 상태(Elastic)에서 재배치(`ReloadDataKeepingPosition`·`Spacing` 등)가 일어나면 위치가 스크롤 범위로 잘려 콘텐츠가 손가락 아래에서 가장자리로 튀고,
  이어지는 드래그에 고무줄 감쇠가 한 번 더 걸려 당긴 거리가 줄던 문제 (0.1.0부터). 이제 맨 앞 셀 기준 위치를 지키되 당기던 거리까지는 가장자리를 넘게 두고,
  ScrollRect 드래그 기준점을 고무줄 감쇠 전 위치로 잡는다. 드래그 중이 아닐 때는 0.1.0처럼 스크롤 범위 안으로 맞춘다
- 트윈 중 `ReloadDataKeepingPosition`(델리게이트 재질의)이 트윈을 취소해 완료 콜백이 불리지 않던 문제 (0.1.0부터).
  이제 같은 데이터(개수가 줄었으면 잘린 인덱스)로 계속 가고, 데이터가 0개가 되면 그 자리에서 끝내고 완료 콜백을 부른다
- 셀 표시 이벤트 같은 범위 갱신 콜백 안에서 새 점프를 시작하면, 이전 트윈의 마지막 프레임이면 새 점프가 움직이기도 전에 완료 처리되던 문제 (0.1.0부터)

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
