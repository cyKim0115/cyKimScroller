# CyKim Scroller

uGUI `ScrollRect` 위에서 동작하는 가상화 스크롤러. 보이는 구간의 셀 뷰만 만들고 재사용하며,
나머지는 빈 스크롤 길이로 둔다.

- 세로·가로, 셀마다 다른 크기, 간격·패딩, 미리 만들기(lookAhead)
- `CellIdentifier` 단위 셀 뷰 풀링 (재부모화 없이 비활성으로 보관)
- 데이터 인덱스 점프 + 31종 트윈 + 커스텀 곡선, 점프 정렬은 뷰포트 크기가 바뀌어도 유지
- 셀이 보이게만 옮기는 `ScrollIntoView` (Nearest·여백), 트윈 중 재배치·뷰포트 크기 변화가 일어나도 끊기거나 튀지 않고 새 목표로 이어 가는 트윈
- 무한 루프 (짧은 목록도 뷰포트를 채우도록 세트 수 자동 결정, 재바인딩 없는 순환 보정, 드래그 중 기준점 재설정)
- 스냅 (드래그·휠 후, 누르고 있는 동안은 대기), 속도 상한, 스크롤바 표시 모드
- 셀 훅: 실제 뷰포트 기준 표시 이벤트(lookAhead 구간 제외, 스크롤러 자체가 파괴될 때 말고는 항상 짝), 늦은 비동기 결과를 버리는 `BindVersion`, 캐러셀·휠 피커 연출용 뷰포트 위치 훅
- 안정 항목 ID와 위치 앵커: 앞쪽에 항목이 삽입·삭제돼도 보던 항목을 지키는 리로드, 아래쪽 기준(채팅) 리로드, 항목 기준 위치 저장·복원(데이터·뷰포트가 준비되기 전 요청은 보관)
- 스크롤 핫패스 GC 할당 0 (PlayMode 테스트로 검증, 셀 훅을 켠 상태 포함)

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
앞쪽에 항목이 삽입·삭제돼도 같은 항목을 지키려면 델리게이트에 `ICyScrollerItemIdProvider`도 구현한다 (아래 [항목 ID와 위치 앵커](#항목-id와-위치-앵커)).
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
| 데이터 | `Delegate`, `ReloadData()`, `ReloadData(factor)`, `ReloadData(ReloadAnchor, factor)`, `ReloadData(in CyScrollerAnchor)`, `ReloadDataKeepingPosition()`, `RefreshActiveCellViews()`, `GetCellView(prefab)` |
| 항목 ID·앵커 | `ICyScrollerItemIdProvider.GetItemId`, `FindDataIndexForItemId(itemId)`, `CaptureAnchor(trailing)`, `RestoreAnchor(in anchor)`, `CyScrollerAnchor`, `ReloadAnchor`(Factor·Start·End·FirstVisible·LastVisible) |
| 이동 | `JumpToDataIndex(dataIndex, scrollerOffset, cellOffset, useSpacing, tweenType, tweenTime, onComplete, loopJumpDirection)`, `ScrollIntoView(dataIndex, align, margin, tweenType, tweenTime, onComplete, loopJumpDirection)`, `Snap()`, `InterruptTween()`, `GetJumpTargetPosition(...)` |
| 위치 | `ScrollPosition`, `NormalizedScrollPosition`, `ScrollSize`, `ScrollRectSize`, `ContentSize`, `Velocity`, `LinearVelocity` |
| 범위 | `NumberOfCells`, `NumberOfCellSlots`, `StartDataIndex`/`EndDataIndex`, `StartCellViewIndex`/`EndCellViewIndex`, `ActiveCellViews`, `GetCellViewAtDataIndex`, `GetCellViewAtCellIndex`, `IsDataIndexFullyVisible(dataIndex, margin)` |
| 좌표 | `GetCellStart`, `GetCellSize`, `GetScrollPositionForDataIndex`, `GetScrollPositionForCellViewIndex`, `GetCellViewIndexAtPosition`, `GetDataIndexForCellViewIndex` |
| 루프 | `Loop`, `LoopWhileDragging`, `ToggleLoop()`, `IgnoreLoopJump(bool)` |
| 정리 | `ClearActive()`, `ClearRecycled()`, `ClearAll()`, `GetRecycledCellCount()` |
| 이벤트 | `CellViewVisibilityChanged`, `CellViewWillDisplay`, `CellViewDidEndDisplay`, `CellViewPositionChanged`, `CellViewInstantiated`, `CellViewReused`, `CellViewWillRecycle`, `ScrollerScrolled`, `ScrollerSnapped`, `ScrollerScrollingChanged`, `ScrollerTweeningChanged` |
| 위치 훅 | `NotifyCellPositions`, `CellPositionPivot` |
| 셀 뷰 | `DataIndex`, `CellIndex`, `ItemId`, `HasItemId`, `Active`, `IsBound`, `IsDisplayed`, `BindVersion`, `RefreshCellView()`, `OnRecycled()`, `OnBecameVisible()`, `OnBecameHidden()`, `OnViewportPositionChanged(normalizedOffset)` |

`scrollerOffset` / `cellOffset`은 0 = 앞(위·왼쪽), 0.5 = 가운데, 1 = 뒤. 가운데 정렬은 `JumpToDataIndex(i, 0.5f, 0.5f)`.

`ScrollIntoView`는 셀이 보이게만 옮긴다. 기본 `ScrollAlign.Nearest`는 이미 완전히 보이면 움직이지 않고 바로 완료 콜백을 부르며,
앞쪽에 걸리면 셀 시작을 뷰포트 시작에(Start), 뒤쪽이면 셀 끝을 뷰포트 끝에(End) 맞춘다. 셀이 여백까지 합쳐 뷰포트보다 크면 Start.
`margin`은 셀 앞뒤로 남길 거리다 (Center는 쓰지 않음). 콘텐츠 끝 너머로는 남길 수 없으므로 첫·마지막 셀은 콘텐츠 끝까지 보이면 된다.
선택 항목을 따라가는 목록이면 `ScrollIntoView(selected, margin: 8f)`.

### 셀 훅

셀 뷰는 "정말 보이는지"(`OnBecameVisible`/`OnBecameHidden`), "뷰포트 안 어디인지"(`OnViewportPositionChanged`),
"지금 몇 번째 바인딩인지"(`BindVersion`)를 받을 수 있다. 위치 훅은 스크롤러의 `NotifyCellPositions`를 켜야 불린다.

```csharp
public class CardCellView : CyScrollerCellView
{
    [SerializeField] private RectTransform _visual;   // 루트가 아닌 자식만 변형한다

    // 0 = 뷰포트 앞 가장자리, 0.5 = 가운데, 1 = 뒤 가장자리 (CellPositionPivot 지점 기준)
    protected override void OnViewportPositionChanged(float normalizedOffset)
    {
        float distance = Mathf.Clamp01(Mathf.Abs(normalizedOffset - 0.5f) * 2f);
        float scale = Mathf.Lerp(1f, 0.75f, distance);
        _visual.localScale = new Vector3(scale, scale, 1f);
    }

    protected override void OnBecameVisible() { /* 등장 애니메이션·노출 기록 */ }
}
```

비동기 로드(아이콘·썸네일)는 바인딩할 때 `BindVersion`을 기억해 두고, 끝났을 때 값이 다르면(그사이 재활용·재바인딩되거나 스크롤러와 함께 파괴됨) 결과를 버린다.

### 항목 ID와 위치 앵커

델리게이트가 `ICyScrollerItemIdProvider`도 구현하면 스크롤러는 다시 읽을 때마다 항목 ID를 받아 두고, 위치를 지킬 때 인덱스 대신 ID로 같은 항목을 찾는다.
ID는 데이터가 바뀌어도 같은 항목이면 같은 값이어야 한다 (서버 ID·DB 키 등. 데이터 인덱스를 그대로 쓰면 의미가 없다).

```csharp
public class ChatList : MonoBehaviour, ICyScrollerDelegate, ICyScrollerItemIdProvider
{
    [SerializeField] private CyScroller _scroller;
    private readonly List<Message> _messages = new List<Message>();

    public long GetItemId(CyScroller scroller, int dataIndex) => _messages[dataIndex].Id;
    // GetNumberOfCells · GetCellViewSize · GetCellView는 위 예와 같다

    private void OnOlderMessagesLoaded(List<Message> older)
    {
        _messages.InsertRange(0, older);
        _scroller.ReloadDataKeepingPosition();   // 보던 메시지가 같은 자리에 남는다
    }

    private void OnMessageReceived(Message message)
    {
        bool atEnd = _scroller.ScrollPosition >= _scroller.ScrollSize - 1f;
        _messages.Add(message);

        // 끝에 있었으면 새 메시지를 따라가고, 아니면 보던 아래쪽 메시지를 같은 자리에 둔다.
        _scroller.ReloadData(atEnd ? ReloadAnchor.End : ReloadAnchor.LastVisible);
    }
}
```

화면을 닫았다 다시 열 때는 앵커를 저장해 두고 복원한다. 데이터가 아직 없거나 뷰포트 크기가 정해지기 전이면 보관했다가 준비되면 적용한다.

```csharp
CyScrollerAnchor saved = _scroller.CaptureAnchor();   // [Serializable] 구조체

// 다시 열 때 (Delegate를 넣기 전이든 뒤든)
_scroller.RestoreAnchor(saved);
```

`CaptureAnchor()`는 뷰포트 맨 앞에 걸친 항목과 셀 시작 → 뷰포트 시작 거리를, `CaptureAnchor(true)`는 뷰포트 맨 뒤에 걸친 항목과 뷰포트 끝 → 셀 끝 거리를 적는다.
`ReloadDataKeepingPosition()`은 셀만 다시 배치하므로 진행 중인 트윈·점프 정렬을 이어 가고, `ReloadData(ReloadAnchor.FirstVisible)`·`LastVisible`은 전체 리로드(트윈·정렬 정지)에 앵커 복원을 더한 것이다.

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
- 스크롤러가 코드로 콘텐츠를 옮기는 경로(루프 순환 보정, `ReloadDataKeepingPosition`·`Spacing` 등 재배치 뒤 위치 유지, `ScrollPosition` 대입, `RestoreAnchor`)는 하나의 이동 루틴을 거친다.
  드래그 중이면 손가락 아래 기준점과 ScrollRect의 직전 위치를 같이 옮기므로, 드래그 도중 재배치·순환 보정이 일어나도 콘텐츠가 손가락에서 떨어지지 않고 놓을 때 관성 속도도 튀지 않는다.
  가장자리 너머로 당기는 중(Elastic)에도 같다. 다만 가장자리를 당기던 거리보다 더 넘기지는 않으며, 이어지는 드래그의 고무줄 저항도 끊기지 않는다.
  드래그 중이 아니면 재배치 뒤 위치는 스크롤 범위 안으로 맞춘다.
- 델리게이트·셀 이벤트 콜백 안에서 `ReloadData`(`ReloadAnchor` 포함)·`ReloadDataKeepingPosition`·`Clear*`를 불러도 된다. 범위 갱신이 끝난 뒤 처리한다.
  즉시 점프·`ScrollPosition` 대입·`RestoreAnchor`는 바로 옮기고, 범위는 옛 위치 기준 작업을 멈춘 뒤 새 위치로 다시 맞춘다 (아래 셀 훅 동작).
- 항목 ID(`ICyScrollerItemIdProvider`)는 `ReloadData`·`ReloadDataKeepingPosition`·`Delegate` 리로드처럼 델리게이트를 다시 받을 때만 항목마다 한 번씩 받는다(O(N)).
  스크롤 중에는 받지 않고 할당도 없다. 같은 ID가 여럿이면 ID로 찾을 때(`FindDataIndexForItemId`) 앞 인덱스가 이기고, 에디터·개발 빌드에서는 다시 받을 때마다 경고를 한 번 남긴다.
  위치를 지킬 때(앵커 복원·정렬 유지)는 이전 인덱스 자리에 같은 ID가 남아 있으면 그 자리를 쓰므로, 데이터가 그대로면 뒤쪽 중복 항목을 보고 있어도 제자리다.
  데이터를 바꾼 뒤 다시 읽기 전에는 `FindDataIndexForItemId`·셀 `ItemId`가 이전 데이터 기준이다.
- `ReloadDataKeepingPosition()`은 ID가 있으면 맨 앞 항목과 트윈 목표·점프 정렬 대상을 ID로 다시 찾는다 (지워졌으면 같은 인덱스, 정렬하던 항목이 지워지면 정렬을 푼다).
  ID가 없으면 데이터 인덱스를 지키고, 델리게이트를 다시 받으면 점프 정렬을 푼다 (같은 인덱스가 다른 항목일 수 있으므로).
- 앵커 복원(`RestoreAnchor`·`ReloadData(in anchor)`·FirstVisible·LastVisible)은 ID → `DataIndex`(개수를 넘으면 마지막) 순으로 항목을 찾는다
  (ID는 `DataIndex` 자리의 항목이 같은 ID면 그 자리, 아니면 `FindDataIndexForItemId`). 빈 앵커(빈 목록에서 캡처)는 앞 기준이면 처음, 뒤 기준이면 끝이다.
  결과는 스크롤 범위로 자르고, 루프면 가운데 사이클 사본에 맞춘다. `RestoreAnchor`는 `ScrollPosition` 대입처럼 트윈·관성·점프 정렬을 멈추고, 드래그 중이면 손가락 아래 기준점만 옮긴다.
- 로드 전·데이터 0개·뷰포트 길이 0일 때 받은 `RestoreAnchor`는 보관했다가 다음 `ReloadData()`(`Delegate` 리로드·`ReloadDataKeepingPosition` 포함, 데이터가 있을 때)나 뷰포트 길이가 생길 때 적용한다.
  그 사이 위치를 정하는 요청(새 `RestoreAnchor`, `ReloadData(factor)`·Start·End, 점프·`ScrollIntoView`·`Snap`, `ScrollPosition` 대입)이 오면 갈 셀이 없어도 버리고,
  FirstVisible·LastVisible 리로드는 보관한 앵커를 쓴다. 드래그·휠은 버리지 않고, 그 뒤 자동 스냅은 보관하는 동안 기다린다.
  보관한 앵커를 나중에 적용할 때도 `RestoreAnchor`처럼 남은 관성과 스냅 대기를 멈춘다(빈 목록을 튕긴 관성이 복원한 자리를 옮기지 않게).
  그래서 `ReloadData()`는 보관한 앵커를 적용하고 `ReloadData(0f)`는 버린다. 보관하는 동안 `CaptureAnchor`는 보관한 앵커를 그대로 돌려준다.
- LastVisible은 뷰포트 끝에 걸친 항목의 끝을 같은 자리에 둔다. 끝에 붙어 있을 때 마지막 항목이 커져도 끝에 남지만, 뒤에 항목이 붙으면 보던 항목을 지키고 새 항목은 그 아래에 붙는다.
  새 항목을 따라가려면 다시 읽기 전에 끝에 있었는지 보고 `ReloadData(ReloadAnchor.End)`를 쓴다.
- 루프 모드에서는 스크롤 축 스크롤바를 ScrollRect에서 떼어 숨기고, 루프를 끄면 다시 붙인다.
  `ScrollPosition`은 내부 슬롯 좌표이므로 저장·복원에는 `NormalizedScrollPosition`이나 `CaptureAnchor`/`RestoreAnchor`를 쓴다.
- 루프는 뷰포트·미리보기 길이를 덮고도 양쪽에 한 사이클씩 남도록 세트 수(최소 5)를 정한다. 셀 크기가 모두 0이면 루프하지 않는다.
- ScrollRect를 끄면(스크롤 잠금) CyScroller도 입력을 무시한다. 코드로 시작한 점프는 계속 진행된다.
- `ActiveCellViews`는 `IReadOnlyList`다. GC를 피하려면 `foreach` 대신 `for` + 인덱서로 순회한다.
- Profiler에서 `CyScroller.UpdateActiveRange`(활성 범위 갱신, 셀 바인딩 포함)와 `CyScroller.Relayout`(위치 유지 재배치) 마커로 비용을 확인할 수 있다.

### 셀 훅 동작

`CellViewVisibilityChanged`는 0.1.0과 같은 활성화 기준이고, `CellViewWillDisplay`/`CellViewDidEndDisplay`는 실제 뷰포트 기준이다.

| | `CellViewVisibilityChanged` | `CellViewWillDisplay` / `CellViewDidEndDisplay` |
|---|---|---|
| 기준 범위 | 활성 범위 = 뷰포트 + lookAhead 미리보기 구간 | 실제 뷰포트 (lookAhead 제외). 경계에 맞닿기만 한 셀은 넣지 않는다 |
| 시점 | 셀을 활성화(바인딩)한 직후(`Active` true) / 회수 직전(`Active` false) | 뷰포트에 조금이라도 걸치기 시작할 때 / 완전히 벗어나거나 보이던 채로 회수되거나 `ClearActive`로 파괴될 때 |
| 미리보기 구간 셀 | 받는다 | 받지 않는다 (뷰포트로 들어올 때 받는다) |
| 셀 뷰 가상 메서드 | 없음 (회수는 `OnRecycled`) | `OnBecameVisible` / `OnBecameHidden` (각 이벤트 바로 뒤) |
| 루프 순환 보정 | 오지 않는다 | 오지 않는다 |
| 쓰임 | 바인딩 부가 작업, 풀·리소스 관리 | 등장 애니메이션, 노출 기록, 동영상 재생·정지 |

- 한 번의 범위 갱신은 표시 끝(`CellViewDidEndDisplay` → `OnBecameHidden`) → 회수(`CellViewWillRecycle` → `OnRecycled` → `CellViewVisibilityChanged`)
  → 활성화(델리게이트 `GetCellView` → `CellViewVisibilityChanged` → 위치 훅) → 표시 시작(`CellViewWillDisplay` → `OnBecameVisible`) 순서다.
  활성 범위는 그대로이고 미리보기 구간 셀만 뷰포트에 드나들면 표시 이벤트만 온다.
- 표시 시작과 끝은 셀마다 항상 짝이 맞는다. 보이던 셀이 `ReloadData`·`ClearActive`·재배치로 회수·파괴될 때도 표시 끝을 먼저 받는다.
  이벤트 핸들러가 예외를 던져도 가상 메서드는 불리고 활성 목록·표시 범위 장부는 어긋나지 않는다. 콜백 안 `ReloadData` 등은 다른 콜백과 같이 범위 갱신이 끝난 뒤 처리한다.
- 예외는 스크롤러 자체가 파괴될 때(팝업 닫기·씬 언로드)다. 파괴 순서가 정해져 있지 않아 사용자 코드를 부르지 않고 활성 셀의 바인딩만 푼다(`BindVersion` 증가, `IsBound` false).
  표시 끝은 오지 않고 `IsDisplayed`가 마지막 값으로 남으므로, 표시 중 시작한 일(노출 타이머 등)은 셀의 `OnDestroy`에서 `IsDisplayed`를 보고 정리한다.
- 범위 갱신 콜백(델리게이트 `GetCellView`, 셀 이벤트, `OnRecycled`·`OnBecameVisible` 같은 셀 뷰 가상 메서드) 안에서 즉시 점프·`ScrollPosition` 대입으로 콘텐츠를 옮기면
  옛 위치 기준으로 남은 회수·활성화·표시 이벤트를 멈추고 새 위치로 범위를 다시 맞춘다. 표시 시작은 그 순간 뷰포트에 걸친 셀에만 온다.
  루프 순환 보정은 콜백 안에서는 미뤘다가 다시 맞추기 직전에 하므로, 한 번에 활성화하는 셀은 뷰포트 + 미리보기 구간 분량을 넘지 않는다.
  콜백이 매번 다시 옮기면 4번까지만 다시 맞추고 나머지는 다음 LateUpdate에 이어 간다.
  콜백 안에서 바로 끝난 스냅(즉시 `Snap()` 등)의 `ScrollerSnapped`는 범위를 다시 맞춘 뒤에 오므로 셀 번호와 셀 뷰가 순환 보정 뒤 새 위치 기준이다.
  점프 완료 콜백은 콜백 안에서 바로 불린다.
- `BindVersion`은 셀이 데이터에 바인딩될 때(활성화할 슬롯용으로 `GetCellView(prefab)`가 내줄 때)와 바인딩이 풀릴 때(회수, `ClearActive`로 파괴, 활성인 채로 스크롤러와 함께 파괴) 1씩 는다.
  델리게이트 `GetCellView` 안에서 읽은 값이 그 바인딩의 값이다. `RefreshCellView`·루프 순환 보정처럼 같은 데이터로 남으면 늘지 않는다. `IsBound`는 `DataIndex >= 0`이다.
- 위치 훅(`NotifyCellPositions`)은 위치·활성 범위·레이아웃·뷰포트 크기가 바뀐 프레임에 한 번, 스크롤러 LateUpdate 끝(실행 순서 100)에서 활성 셀마다
  `CellViewPositionChanged` → `OnViewportPositionChanged`를 부른다.
  normalizedOffset = (셀 시작 + 셀 크기 × `CellPositionPivot` − 뷰포트 시작) / 뷰포트 길이. 0 = 앞(위·왼쪽) 가장자리, 0.5 = 가운데, 1 = 뒤 가장자리이고, 미리보기 구간 셀은 0 미만·1 초과가 될 수 있다.
  - 새로 활성화된 셀(리로드 직후 포함)은 활성화 즉시 한 번 받으므로 재사용된 뷰가 이전 모습으로 한 프레임 보이지 않는다.
  - 스크롤러 LateUpdate 뒤(더 늦은 실행 순서의 LateUpdate·코루틴 등)에서 위치가 바뀌면 다음 프레임 LateUpdate에 반영된다.
  - 아무것도 바뀌지 않은 프레임에는 부르지 않고, 꺼져 있으면 계산 자체를 건너뛴다. 뷰포트 길이가 0이면 부르지 않는다. 레이아웃 캐시로만 계산해 할당이 없다.
  - 셀 루트 RectTransform은 스크롤러가 배치하므로 위치 훅에서는 자식(스케일·회전·투명도)만 바꾼다.
- 셀 뷰 훅(`OnBecameVisible`·`OnBecameHidden`·`OnViewportPositionChanged`)은 `protected internal`이다. 다른 어셈블리에서는 `protected override`로 재정의한다.

## 샘플

Package Manager → CyKim Scroller → Samples → **Basic** Import.
빈 씬의 GameObject에 `BasicSample`을 붙이고 Play하면 세로 목록·점프 버튼·휠 피커·루프 캐러셀이 만들어진다.
휠 피커와 캐러셀은 위치 훅으로 행·카드 모양을 그린다.

## 다른 목록 UI와 비교

UITableView·RecyclerView·UI Toolkit ListView와의 개념 대응표는 [`Documentation~/api-mapping.md`](Documentation~/api-mapping.md).

## 라이선스

[LICENSE.md](LICENSE.md)
