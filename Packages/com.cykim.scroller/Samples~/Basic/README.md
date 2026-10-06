# Basic 샘플

빈 씬에 GameObject를 하나 만들고 `BasicSample` 컴포넌트를 붙인 뒤 Play한다.
씬·프리팹 없이 UI 전체를 코드로 만든다. 1920×1080 기준 Canvas에 비율 앵커로 배치하므로 16:9 해상도(1280×720 등)에서는 같은 배치로 보인다.

| 영역 | 보여 주는 것 |
|---|---|
| 왼쪽 세로 목록 | 높이가 다른 500개 셀, 간격·패딩, lookAhead, 미리 만든 문자열로 GC 없는 바인딩. 항목은 `List`로 두고 셀에 항목 번호를 보여 주며, 번호를 `ICyScrollerItemIdProvider`의 안정 ID로 준다. 상태 줄은 `CellViewWillDisplay`·`CellViewDidEndDisplay`와 `IsDisplayed`로 실제 보이는 셀(미리보기 구간 제외)만 세어 그 데이터 인덱스 범위를 보여 주고, 셀 표시·활성화·회수 이벤트가 온 프레임과 편집한 프레임에만 다시 그린다 |
| 가운데 버튼 | `JumpToDataIndex` 트윈 점프 (목록 처음·#250 항목(번호로 찾는다)·끝·무작위, 캐러셀 이전·다음, 휠 무작위 회전) |
| 가운데 편집 버튼 | 증분 변경으로 보던 화면을 지킨다. `List: Insert 3 at top`은 맨 위에 3개를 넣고 `InsertCells(0, 3)`으로 알린다(보던 항목은 그 자리에 남고 인덱스만 3씩 밀린다). `List: Remove first visible`은 뷰포트 안에서 시작하는 첫 항목을 `RemoveCells`로 지우고(아래 항목이 빈자리를 채운다), `List: Move visible to top`은 보이는 항목 가운데 하나를 `MoveCell`로 맨 위로 옮긴다(화면은 움직이지 않고, 위로 스크롤하면 맨 위에 있다). 남은 셀은 다시 바인딩하지 않는다 |
| 오른쪽 휠 피커 | 세로 `Loop` + 가운데 `Snapping`, 고정 행 높이 56, `NotifyCellPositions` 위치 훅으로 원통에 감긴 것처럼 기울고(X축 회전) 작아지고 흐려지는 행, 가운데 행 뒤에 깔린 캡슐 하이라이트(레이캐스트 통과), 스냅된 값 표시 |
| 아래 캐러셀 | 가로 `Loop` + `Snapping`, 루프 방향 점프(`LoopJumpDirection`), 위치 훅으로 가운데 카드가 가장 크고 선명하게(스케일·`CanvasGroup` 투명도) |

| 파일 | 역할 |
|---|---|
| `BasicSample` | 화면 배치와 버튼·상태 표시 |
| `BasicListController` / `BasicCellView` | 가변 높이 목록(List 데이터, 항목 번호 ID, 삽입·삭제·이동 편집)과 기본 셀 |
| `BasicCarouselController` / `BasicCarouselCardView` | 캐러셀과 위치 훅으로 크기·투명도를 바꾸는 카드 셀 |
| `BasicWheelPickerController` / `BasicWheelRowView` | 휠 피커와 위치 훅으로 원통 모양을 그리는 행 셀 |
| `SampleUiFactory` | 런타임 UI 생성 도우미, 테마 색, 스프라이트 로드 |
| `Resources/CyKimScrollerBasic` | 임시 9-slice 스프라이트 4장(`Panel`·`Card`·`Button`·`Pill`)과 임포트 설정 `.meta` |

실제 프로젝트에서는 셀을 프리팹으로 만들고 `CellIdentifier`를 프리팹마다 다르게 둔다.
셀 루트 RectTransform은 스크롤러가 배치하므로, 스케일·회전·투명도 같은 효과는 자식(`Visual`)에 준다.
위치 효과는 셀 뷰에서 `protected override void OnViewportPositionChanged(float normalizedOffset)`를 재정의해 그린다
(0 = 뷰포트 앞 가장자리, 0.5 = 가운데, 1 = 뒤 가장자리). 스크롤러의 `NotifyCellPositions`를 켜야 불린다.

## 테마와 임시 텍스처

색은 세이지·차콜 라이트 테마다. 배경 `#FCFDFC`, 패널·보조 버튼 `#F0F3F0`, 글자 `#2C3333`·`#515B56`, 강조 `#567E65`이고 값은 `SampleUiFactory` 위쪽에 모여 있다.
목록 셀은 흰색에 세이지(일부는 강조·슬레이트도 옅게)를 섞은 밝은 톤 6가지를 밝은 톤과 조금 진한 톤이 번갈아 오게 돌려 쓴다.
캐러셀 카드는 목록보다 조금 진한, 패널보다 확실히 어두운 세이지 톤 4가지를 차례로 돌려 쓴다. 기본 12장은 4로 나누어떨어져 순환 이음매에서도 이웃 카드 톤이 다르다. 카드가 패널에 묻히지 않고, 흐려진 양옆 카드는 패널 쪽으로 밝아진다.
색은 항목을 만들 때 미리 정한다. 셀·카드 면 위 글자 대비는 어느 톤에서도 4.5 이상이다(가장 진한 톤에서 약 7.7).
위치 훅으로 흐려지는 캐러셀 양옆 카드와 휠 가운데 ±2행 숫자는 굵은 큰 글자 기준 3 이상을 남기고, 그보다 먼 휠 행은 원통 끝처럼 더 흐리게 그린다.

면은 `Resources/CyKimScrollerBasic`의 흰색 9-slice 스프라이트에 `Image.color`를 곱해 그린다.
샘플용 **임시 텍스처**라 실제 프로젝트의 그림으로 바꿔 쓰는 것을 전제로 한다.

| 스프라이트 | 쓰는 곳 |
|---|---|
| `Panel` | 세 스크롤러의 배경 (둥근 면 + 안쪽 테두리) |
| `Card` | 목록 셀, 캐러셀 카드 (둥근 면 + 테두리 + 아래쪽 립) |
| `Button` | 가운데 버튼 열 (편집 버튼은 강조색, 나머지는 보조 면 색) |
| `Pill` | 휠 피커 가운데 하이라이트 (반투명 캡슐) |

- Sprite 타입·9-slice 보더 같은 임포트 설정은 함께 든 `.meta`에 있다. 같은 이름의 Sprite로 바꾸면 그 그림으로 그린다. 바꿀 그림도 색을 곱하므로 흰색·회색으로 그린다.
- 스프라이트를 찾지 못하면 같은 색 단색으로 그리고(휠 하이라이트는 옅은 띠와 위·아래 선) 경고를 한 번 남긴다.
- 텍스처는 cyKimScroller 저장소의 `tools/generate_sample_textures.py`(Python 3.8+, Pillow 9.1+)로 다시 만든다. 이 스크립트는 패키지에 들어 있지 않다.
  보더가 바뀌면 샘플을 Import한 사본에서 임포트 설정(`TextureImporter.spriteBorder`)을 고치고, Unity가 만든 `.png.meta`만 `Samples~`로 복사한다(절차는 스크립트 머리말).
