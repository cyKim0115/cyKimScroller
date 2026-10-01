# Changelog

이 패키지의 변경 내역. 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/), 버전은 [SemVer](https://semver.org/lang/ko/)를 따른다.

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
