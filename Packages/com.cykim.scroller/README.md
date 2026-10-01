# CyKim Scroller

uGUI `ScrollRect` 위에서 동작하는 가상화 스크롤러. 보이는 구간의 셀 뷰만 만들고 재사용하며,
나머지는 빈 스크롤 길이로 둔다.

- 세로·가로, 셀마다 다른 크기, 간격·패딩, 미리 만들기(lookAhead)
- `CellIdentifier` 단위 셀 뷰 풀링 (재부모화 없이 비활성으로 보관)
- 데이터 인덱스 점프 + 31종 트윈 + 커스텀 곡선, 점프 정렬은 뷰포트 크기가 바뀌어도 유지
- 셀이 보이게만 옮기는 `ScrollIntoView` (Nearest·여백), 트윈 중 재배치·뷰포트 크기 변화가 일어나도 끊기거나 튀지 않고 새 목표로 이어 가는 트윈
- 무한 루프 (짧은 목록도 뷰포트를 채우도록 세트 수 자동 결정, 재바인딩 없는 순환 보정, 드래그 중 기준점 재설정)
- 스냅 (드래그·휠 후, 누르고 있는 동안은 대기), 속도 상한, 스크롤바 표시 모드
- 스크롤 핫패스 GC 할당 0 (PlayMode 테스트로 검증)

Unity 6000.0 이상, uGUI 2.0 이상 (6000.6.0f1 / uGUI 2.6.0에서 검증).

## 설치

`Packages/manifest.json`에 git URL을 넣는다. `?path=` 다음에 `#`를 두고, 태그나 **전체** 커밋 SHA로 고정한다.

```json
{
  "dependencies": {
    "com.cykim.scroller": "https://github.com/cyKim0115/cyKimScroller.git?path=/Packages/com.cykim.scroller#v0.1.0"
  }
}
```

테스트까지 보려면 같은 파일에 `"testables": ["com.cykim.scroller"]`를 추가한다.

## 빠른 시작

1. Hierarchy 우클릭 → **UI (Canvas) → CyKim Scroller → Vertical Scroller** (Unity 6.2 이하는 **UI → CyKim Scroller**).
   Canvas·EventSystem은 없으면 함께 만들고, Viewport는 `RectMask2D`로 바꿔 둔다
2. 셀 프리팹 루트에 `CyScrollerCellView`를 상속한 컴포넌트를 붙이고 `Cell Identifier`를 정한다
3. 컨트롤러에서 `ICyScrollerDelegate`를 구현하고 `Delegate`에 넣는다

```csharp
using CyKim.Scroller;
using UnityEngine;

public class InventoryList : MonoBehaviour, ICyScrollerDelegate
{
    [SerializeField] private CyScroller _scroller;
    [SerializeField] private ItemCellView _cellPrefab;   // : CyScrollerCellView

    private ItemData[] _items;

    private void Start()
    {
        _items = LoadItems();
        _scroller.Delegate = this;   // 다음 LateUpdate에 자동 ReloadData
    }

    public int GetNumberOfCells(CyScroller scroller) => _items.Length;

    public float GetCellViewSize(CyScroller scroller, int dataIndex) => _items[dataIndex].IsHeader ? 48f : 120f;

    public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
    {
        var view = (ItemCellView)scroller.GetCellView(_cellPrefab);   // 풀에서 꺼내거나 생성
        view.SetData(_items[dataIndex]);                              // 이전 상태를 전부 덮어쓴다
        return view;
    }
}
```

데이터가 바뀌면 `ReloadData()` (처음으로) 또는 `ReloadDataKeepingPosition()` (맨 앞 셀 유지)을 호출한다.
크기는 그대로이고 보이는 셀 내용만 바뀌었으면 `RefreshActiveCellViews()`가 가장 싸다.
단 이 메서드는 활성 셀마다 `RefreshCellView()`를 부르기만 하므로, 셀 뷰가 이를 재정의해 자기 데이터로 다시 그려야 한다.

```csharp
public class ItemCellView : CyScrollerCellView
{
    private ItemData _data;

    public void SetData(ItemData data) { _data = data; Redraw(); }

    public override void RefreshCellView() => Redraw();   // 같은 _data 참조의 바뀐 값을 다시 그린다

    private void Redraw() { /* _data로 텍스트·아이콘 갱신 */ }
}
```

## 주요 API

| 분류 | 멤버 |
|---|---|
| 데이터 | `Delegate`, `ReloadData(factor)`, `ReloadDataKeepingPosition()`, `RefreshActiveCellViews()`, `GetCellView(prefab)` |
| 이동 | `JumpToDataIndex(dataIndex, scrollerOffset, cellOffset, useSpacing, tweenType, tweenTime, onComplete, loopJumpDirection)`, `ScrollIntoView(dataIndex, align, margin, tweenType, tweenTime, onComplete, loopJumpDirection)`, `Snap()`, `InterruptTween()`, `GetJumpTargetPosition(...)` |
| 위치 | `ScrollPosition`, `NormalizedScrollPosition`, `ScrollSize`, `ScrollRectSize`, `ContentSize`, `Velocity`, `LinearVelocity` |
| 범위 | `NumberOfCells`, `NumberOfCellSlots`, `StartDataIndex`/`EndDataIndex`, `StartCellViewIndex`/`EndCellViewIndex`, `ActiveCellViews`, `GetCellViewAtDataIndex`, `GetCellViewAtCellIndex`, `IsDataIndexFullyVisible(dataIndex, margin)` |
| 좌표 | `GetCellStart`, `GetCellSize`, `GetScrollPositionForDataIndex`, `GetScrollPositionForCellViewIndex`, `GetCellViewIndexAtPosition`, `GetDataIndexForCellViewIndex` |
| 루프 | `Loop`, `LoopWhileDragging`, `ToggleLoop()`, `IgnoreLoopJump(bool)` |
| 정리 | `ClearActive()`, `ClearRecycled()`, `ClearAll()`, `GetRecycledCellCount()` |
| 이벤트 | `CellViewVisibilityChanged`, `CellViewInstantiated`, `CellViewReused`, `CellViewWillRecycle`, `ScrollerScrolled`, `ScrollerSnapped`, `ScrollerScrollingChanged`, `ScrollerTweeningChanged` |

`scrollerOffset` / `cellOffset`은 0 = 앞(위·왼쪽), 0.5 = 가운데, 1 = 뒤. 가운데 정렬은 `JumpToDataIndex(i, 0.5f, 0.5f)`.

`ScrollIntoView`는 셀이 보이게만 옮긴다. 기본 `ScrollAlign.Nearest`는 이미 완전히 보이면 움직이지 않고 바로 완료 콜백을 부르며,
앞쪽에 걸리면 셀 시작을 뷰포트 시작에(Start), 뒤쪽이면 셀 끝을 뷰포트 끝에(End) 맞춘다. 셀이 여백까지 합쳐 뷰포트보다 크면 Start.
`margin`은 셀 앞뒤로 남길 거리다 (Center는 쓰지 않음). 콘텐츠 끝 너머로는 남길 수 없으므로 첫·마지막 셀은 콘텐츠 끝까지 보이면 된다.
선택 항목을 따라가는 목록이면 `ScrollIntoView(selected, margin: 8f)`.

## 동작 규칙

- **content를 스크롤러가 소유한다.** 앵커·피벗·크기를 실행 시 다시 설정하고, content의 `LayoutGroup`·`ContentSizeFitter`는 끈다.
- **셀 루트 RectTransform은 스크롤러가 배치한다.** 스케일·회전 같은 효과는 자식에 준다.
- 셀 크기는 `GetCellViewSize`가 정한다. 셀이 스스로 크기를 바꾸면 `ReloadDataKeepingPosition()`으로 다시 계산한다.
- 점프·스냅·`ScrollIntoView` 뒤 정렬은 사용자가 드래그·휠·스크롤바로 움직이거나 `ScrollPosition`을 직접 바꾸기 전까지 유지된다
  (첫 프레임 Canvas 크기 확정, 화면 회전에도 같은 셀이 같은 자리에 있다).
- 트윈은 목표 좌표를 저장하지 않고 요청(셀·정렬 위치·여백)으로 매 프레임 지금 배치에서 다시 계산한다.
  트윈 중 재배치(`Spacing`·`Padding`·`Loop`·방향 변경, `ReloadDataKeepingPosition`)나 뷰포트 크기 변화가 일어나도 트윈은 멈추지 않는다.
  재배치 순간 화면은 맨 앞 셀 기준으로 그대로 두고, 같은 데이터(개수가 줄었으면 잘린 인덱스)로 남은 시간 동안 이어 간 뒤
  완료 콜백(스냅이면 `ScrollerSnapped`도)을 한 번 부른다. 루프에서는 가던 방향의 사본을 지킨다. 데이터가 0개가 되면 그 자리에서 끝내고 완료 콜백을 부른다.
  - 다음 프레임에 화면이 튀지 않도록 트윈 시작점을 다시 잡는다. 지금 화면에서 새 목표까지 남은 거리를 곡선의 남은 진행에 싣고 같은 시각에 끝낸다.
    1을 넘거나 되돌아오는 곡선(Back·Elastic·Bounce), 끝에서 1로 뛰는 곡선(EaseOutExpo·EaseInOutExpo), Custom은
    지금 화면에서 남은 시간 동안 같은 곡선을 처음부터 다시 그린다 (화면과 목표가 같이 밀린 재배치면 원래 곡선을 그대로 잇는다).
  - 마지막 프레임에 셀 표시 이벤트 같은 범위 갱신 콜백이 요청한 재배치도 트윈을 끝내기 전에 처리하므로 새 배치의 목표에서 끝나고 정렬도 유지된다.
    같은 콜백에서 `ReloadData`를 요청하면 다른 프레임처럼 트윈을 멈추고 완료 콜백을 부르지 않는다.
- `ScrollPosition`·`NormalizedScrollPosition`을 대입하면 진행 중인 트윈·관성을 멈춘다. `ReloadData`도 관성을 멈춘다.
  `ReloadData`·`InterruptTween`·사용자 드래그·휠·포인터 다운(`InterruptTweenOnPointerDown`)으로 멈춘 트윈은 완료 콜백을 부르지 않는다.
- 드래그 중에 `JumpToDataIndex`·`Snap`·`ScrollIntoView`를 부르면 그 드래그를 끝내고 이동한다 (코드 요청 우선). `ScrollIntoView`의 Nearest는 실제로 움직여야 할 때만 끝낸다.
  반대로 사용자 드래그·휠은 진행 중인 트윈을 멈춘다.
- `ScrollIntoView`·`IsDataIndexFullyVisible`의 "보인다"는 lookAhead를 뺀 실제 뷰포트 기준이다. 여백은 콘텐츠 끝 너머로는 셈하지 않는다.
  루프에서는 어느 사본이든 완전히 보이면 보이는 것으로 본다.
  Nearest로 이미 보이는 셀이나 더 움직일 수 없는 셀(뷰포트보다 큰 셀이 이미 시작에 맞춰져 있을 때 등)을 요청하면 움직이지 않고 바로 완료 콜백을 부른다.
  이때 진행 중인 트윈은 그 자리에서 멈추고(새 요청이 대신하므로 이전 완료 콜백은 없음), 드래그·관성은 그대로 둔다. Start·Center·End는 점프처럼 항상 정렬을 맞춘다.
- 스크롤러가 코드로 콘텐츠를 옮기는 경로(루프 순환 보정, `ReloadDataKeepingPosition`·`Spacing` 등 재배치 뒤 위치 유지, `ScrollPosition` 대입)는 하나의 이동 루틴을 거친다.
  드래그 중이면 손가락 아래 기준점과 ScrollRect의 직전 위치를 같이 옮기므로, 드래그 도중 재배치·순환 보정이 일어나도 콘텐츠가 손가락에서 떨어지지 않고 놓을 때 관성 속도도 튀지 않는다.
  가장자리 너머로 당기는 중(Elastic)에도 같다. 다만 가장자리를 당기던 거리보다 더 넘기지는 않으며, 이어지는 드래그의 고무줄 저항도 끊기지 않는다.
  드래그 중이 아니면 재배치 뒤 위치는 스크롤 범위 안으로 맞춘다.
- 델리게이트·셀 이벤트 콜백 안에서 `ReloadData`·`ReloadDataKeepingPosition`·`Clear*`를 불러도 된다. 범위 갱신이 끝난 뒤 처리한다.
- 루프 모드에서는 스크롤 축 스크롤바를 ScrollRect에서 떼어 숨기고, 루프를 끄면 다시 붙인다.
  `ScrollPosition`은 내부 슬롯 좌표이므로 저장·복원에는 `NormalizedScrollPosition`을 쓴다.
- 루프는 뷰포트·미리보기 길이를 덮고도 양쪽에 한 사이클씩 남도록 세트 수(최소 5)를 정한다. 셀 크기가 모두 0이면 루프하지 않는다.
- ScrollRect를 끄면(스크롤 잠금) CyScroller도 입력을 무시한다. 코드로 시작한 점프는 계속 진행된다.
- `ActiveCellViews`는 `IReadOnlyList`다. GC를 피하려면 `foreach` 대신 `for` + 인덱서로 순회한다.
- Profiler에서 `CyScroller.UpdateActiveRange`(활성 범위 갱신, 셀 바인딩 포함)와 `CyScroller.Relayout`(위치 유지 재배치) 마커로 비용을 확인할 수 있다.

## 샘플

Package Manager → CyKim Scroller → Samples → **Basic** Import.
빈 씬의 GameObject에 `BasicSample`을 붙이고 Play하면 세로 목록·점프 버튼·루프 캐러셀이 만들어진다.

## 다른 목록 UI와 비교

UITableView·RecyclerView·UI Toolkit ListView와의 개념 대응표는 [`Documentation~/api-mapping.md`](Documentation~/api-mapping.md).

## 라이선스

[LICENSE.md](LICENSE.md)
