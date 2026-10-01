# 가상화 스크롤러 설계 조사 (2026-10-01)

uGUI용 가상화 스크롤러를 새로 만들기 전에, 여러 플랫폼의 가상화 목록 UI와 Unity 커뮤니티 스크롤러들이
공통으로 쓰는 구조와 알려진 한계를 정리했다. 특정 구현을 옮기는 것이 아니라 **공통 패턴을 uGUI에 맞게 다시 설계**하는 것이 목적이다.

## 1. 살펴본 구현

| 구분 | 대상 |
|---|---|
| 플랫폼 기본 목록 | UIKit `UITableView`·`UICollectionView`, Android `RecyclerView`, Unity UI Toolkit `ListView` |
| Unity uGUI 에셋 | LoopScrollRect, Optimized ScrollView Adapter(OSA), FancyScrollView, Recyclable Scroll Rect, EnhancedScroller |
| 우리 방침 | 공개 문서·포럼에서 확인되는 *동작과 API 형태*만 참고한다. 소스·데모·프리팹·문서 문구는 가져오지 않는다 |

소스 호환은 목표가 아니다. 사용 흐름이 익숙한 독자 API를 만든다.

## 2. 공통 구조

### 데이터 소스 패턴
거의 모든 구현이 "개수·크기·셀"을 묻는 데이터 소스를 둔다 (`numberOfRows`/`heightForRow`/`cellForRow`, `getItemCount`/`onBindViewHolder` 등).

| 멤버 | 역할 |
|---|---|
| `GetNumberOfCells(scroller)` | 논리 데이터 개수 N |
| `GetCellViewSize(scroller, dataIndex)` | 스크롤 축 방향 셀 크기 (셀마다 달라도 됨) |
| `GetCellView(scroller, dataIndex, cellIndex)` | 스크롤러의 `GetCellView(prefab)`로 뷰를 받아 바인딩 후 반환 |

### 셀 뷰
- 재사용 풀 키 — 프리팹 종류별로 고유해야 한다 (`reuseIdentifier`, view type에 해당)
- `dataIndex` — 논리 데이터 인덱스 / `cellIndex` — 스크롤 시퀀스 슬롯 인덱스 (루프에서는 다름)
- 보이는 셀만 다시 그리는 갱신 훅
- 재사용된 뷰는 이전 상태(리스너·코루틴·선택 상태)를 리셋해야 한다

### 가상화
- 흔한 uGUI 구현은 콘텐츠에 **앞뒤 padder(LayoutElement) + Horizontal/VerticalLayoutGroup**을 두고 보이는 셀만 배치한다
- 미리 만들기 구간(lookAhead)은 **개수가 아니라 축 거리**로 두는 편이 가변 크기에 안전하다
- 크기 누적합 + 이진 탐색이면 리로드 O(N), 범위 계산 O(log N)

### 루프
- 슬롯을 여러 세트로 늘리고(`dataIndex = cellIndex % N`), 가운데 세트를 벗어나면 한 사이클만큼 위치를 순간이동하는 방식이 일반적이다
- 세트 수를 3으로 고정하면 한 사이클이 뷰포트보다 짧을 때 가운데 정렬 점프가 틀어진다는 보고가 있다
- 드래그 중 순간이동은 ScrollRect의 드래그 기준점과 충돌한다 → 드래그 중 루프 허용 여부를 옵션으로 둔다

### 스냅
- 관성 속도가 임계값 아래로 떨어지면 뷰포트의 기준 지점에 있는 셀을 정렬 위치로 트윈 이동하고 완료 이벤트를 낸다
- 스크롤바 드래그는 스냅 대상이 아니다
- 누르는 동안 트윈을 멈출지, 손을 뗄 때 강제로 스냅할지를 옵션으로 둔다 (UIKit `scrollViewWillEndDragging`, Android `SnapHelper`도 같은 시점을 쓴다)

### API 표면 (요약)
- 설정: 스크롤 방향, 간격, 패딩, 루프, 드래그 중 루프, 스크롤바 표시(OnlyIfNeeded/Always/Never), lookAhead, 최대 속도, 스냅 옵션
- 위치: ScrollPosition, NormalizedScrollPosition, ScrollSize, ScrollRectSize, NumberOfCells
- 범위·상태: Start/EndDataIndex, Start/EndCellViewIndex, IsScrolling(트윈 제외), IsTweening, Velocity, LinearVelocity
- 수명: ReloadData(scrollPositionFactor), RefreshActiveCellViews, ClearAll/ClearActive/ClearRecycled, GetCellView(prefab)
- 이동: JumpToDataIndex(정렬 비율·트윈·완료 콜백·루프 방향), GetCellViewAtDataIndex, GetScrollPositionFor*, GetCellViewIndexAtPosition, ToggleLoop, IgnoreLoopJump
- 이벤트: 셀 표시 변경·재활용·생성·재사용, 스크롤·스냅·스크롤 상태·트윈 상태 변경
- 트윈 곡선은 표준 이징 함수(Sine·Quad·Cubic·Quart·Quint·Expo·Circ·Back·Elastic·Bounce의 In/Out/InOut)로 독자 정의한다

## 3. 알려진 한계·불만 (여러 구현 공통)

- 셀이 스스로 크기를 바꾸면(ContentSizeFitter, 펼침) 캐시된 크기와 어긋난다 → 명시적 크기 무효화 + 앵커 유지 리로드 필요
- 셀 재활용·재부모화가 레이아웃을 더럽힌다 (LayoutGroup 비용)
- 중첩 스크롤러 제스처 분배는 대부분 기본 기능이 아니다
- 그리드는 "행 단위 셀 + 행 안의 서브셀" 패턴으로 처리하는 경우가 많다
- 삭제 후 리로드 시 위치 드리프트, 리로드 직후 점프 시 빈 공간 보고

## 4. 대안 비교

| 대안 | 강점 | 약점 |
|---|---|---|
| LoopScrollRect (MIT, GitHub) | ScrollRect에 가까운 동작, 그리드·역방향 | 무한 모드 스크롤바 미동작, 가변 크기 불안정 |
| OSA (유료) | 가변 크기·중첩·커스텀 레이아웃 | 설정이 많고 학습 비용 큼 |
| FancyScrollView (MIT) | 위치 기반 셀 애니메이션(캐러셀) | 아키텍처가 다르고 Rect/Grid 변형은 루프·스냅 미지원 |
| Recyclable Scroll Rect (MIT) | 단순한 데이터소스 모델, 그리드 | 점프·스냅·루프 계약이 작음 |
| EnhancedScroller (유료) | 델리게이트 모델, 점프·루프·스냅 | LayoutGroup·padder 기반 배치, 고정 루프 세트 |
| UI Toolkit ListView | 내장 가상화 | uGUI가 아님, 가로 가상화 미지원 |

## 5. Unity 6 성능 원칙

1. 핫패스 할당 0 — LINQ·클로저·박싱·임시 컬렉션 금지, 버퍼는 최고 수위에서만 증가
2. 레이아웃 전략을 의도적으로 고른다 — 뜨거운 리스트는 LayoutGroup 대신 **RectTransform 직접 배치**
3. 갱신 빈도별 Canvas 분리, 장식 그래픽 Raycast Target 끄기
4. 풀링 순서: 재활용은 비활성화 → 이동, 재사용은 이동·바인딩 → 활성화 (이중 dirty 방지)
5. 불변식 테스트: N=0, 1개, 뷰포트보다 짧은 목록, 간격·패딩 0, 가변 크기, 큰 점프, 드래그 중 루프 랩, 트윈 중 리사이즈

## 6. UPM 패키지 구성 결론

- `Packages/com.cykim.scroller/` 임베디드, 소비 측은 `...git?path=/Packages/com.cykim.scroller#<태그 또는 전체 SHA>`
  (순서는 `?path=` 다음 `#`. 축약 SHA 불가)
- `package.json`: name·version 필수, `unity: "6000.6"`, displayName·description·samples·author 권장.
  패키지 `dependencies`에는 git URL 불가
- `license` 필드를 생략하면 `LICENSE.md` 필수
- `Samples~/`·`Documentation~/`는 임포트되지 않는다. 샘플은 Package Manager **Import**로 `Assets/Samples/`에 복사된다
- 테스트 asmdef: `optionalUnityReferences: ["TestAssemblies"]` + `defineConstraints: ["UNITY_INCLUDE_TESTS"]`.
  `overrideReferences`는 함부로 켜지 않는다. Editor 테스트만 `includePlatforms: ["Editor"]`
- 임베디드 상태에선 testables 불필요, git 소비 측에선 `testables`에 추가
- `.meta`는 커밋 (`~` 폴더 제외). 릴리스 때 version·CHANGELOG 갱신 → 커밋 → 태그
- 선택: `git subtree split --prefix=Packages/com.cykim.scroller -b upm`으로 루트형 브랜치 배포

## 7. 이번 구현의 설계 결정

| 결정 | 이유 |
|---|---|
| 타입 이름은 `CyScroller` / `ICyScrollerDelegate` / `CyScrollerCellView` | 다른 라이브러리와 이름 충돌 회피. `CyKim.Scroller` 네임스페이스 안에 `Scroller` 클래스를 두면 이름 모호성 발생, `UnityEngine.UIElements.Scroller`와도 겹침 |
| 데이터 소스 메서드는 "개수·크기·셀" 3개 | 여러 플랫폼에서 익숙한 흐름이라 학습 비용이 낮다 (`GetNumberOfCells`, `ReloadData`, `JumpToDataIndex` …) |
| 공개 멤버는 PascalCase 프로퍼티·이벤트 | 프로젝트 C# 컨벤션 |
| padder+LayoutGroup 대신 **RectTransform 직접 배치** | 레이아웃 리빌드 비용 제거 |
| 재활용 셀은 content 아래에 비활성으로 둔다 | SetParent 비용·이중 dirty 회피 |
| 루프 세트 수 동적 결정 (최소 3, 홀수) | "짧은 루프" 정렬 문제 회피 |
| 드래그 중 루프 순간이동은 `ScrollRect.OnBeginDrag(eventData)` 재호출로 기준점 재설정 | 리플렉션·ScrollRect 서브클래스 없이 드래그 연속성 유지 |
| 레이아웃 계산은 순수 C# 클래스로 분리 | EditMode 테스트 가능 |

## 출처

1. Apple Developer — UITableView / UITableViewDataSource / UICollectionView: https://developer.apple.com/documentation/uikit/uitableview
2. Android Developers — Create dynamic lists with RecyclerView: https://developer.android.com/develop/ui/views/layout/recyclerview
3. Unity Manual — ListView (UI Toolkit): https://docs.unity3d.com/Manual/UIE-uxml-element-ListView.html
4. LoopScrollRect: https://github.com/qiankanglai/LoopScrollRect
5. Optimized ScrollView Adapter: https://forbiddenbyte.com/optimized-scrollview-adapter
6. FancyScrollView: https://github.com/setchi/FancyScrollView
7. Recyclable Scroll Rect: https://github.com/MdIqubal/Recyclable-Scroll-Rect
8. EnhancedScroller — Unity Asset Store: https://assetstore.unity.com/packages/tools/gui/enhancedscroller-36378
9. Unity UI performance optimization tips: https://unity.com/how-to/unity-ui-optimization-tips
10. Unity Manual — Create custom packages / Package layout / Git dependencies / Package manifest / Add tests to your package (6000.x)
