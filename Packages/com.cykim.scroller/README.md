# CyKim Scroller

uGUI `ScrollRect` 위에서 동작하는 가상화 스크롤러. 보이는 구간의 셀 뷰만 만들고 재사용하며,
나머지는 빈 스크롤 길이로 둔다.

- 세로·가로, 셀마다 다른 크기, 간격·패딩, 미리 만들기(lookAhead)
- `CellIdentifier` 단위 셀 뷰 풀링 (재부모화 없이 비활성으로 보관), 풀 미리 채우기(`Prewarm`·`PrewarmAsync`)와 식별자별 회수 상한
- 데이터 인덱스 점프 + 31종 트윈 + 커스텀 곡선, 점프 정렬은 뷰포트 크기가 바뀌어도 유지
- 셀이 보이게만 옮기는 `ScrollIntoView` (Nearest·여백), 트윈 중 재배치·뷰포트 크기 변화가 일어나도 끊기거나 튀지 않고 새 목표로 이어 가는 트윈
- 무한 루프 (짧은 목록도 뷰포트를 채우도록 세트 수 자동 결정, 재바인딩 없는 순환 보정, 드래그 중 기준점 재설정)
- 스냅 (드래그·휠 후, 누르고 있는 동안은 대기), 속도 상한, 스크롤바 표시 모드
- 셀 훅: 실제 뷰포트 기준 표시 이벤트(lookAhead 구간 제외, 스크롤러 자체가 파괴될 때 말고는 항상 짝), 늦은 비동기 결과를 버리는 `BindVersion`, 캐러셀·휠 피커 연출용 뷰포트 위치 훅
- 안정 항목 ID와 위치 앵커: 앞쪽에 항목이 삽입·삭제돼도 보던 항목을 지키는 리로드, 아래쪽 기준(채팅) 리로드, 항목 기준 위치 저장·복원(데이터·뷰포트가 준비되기 전 요청은 보관)
- 증분 구조 변경: `InsertCells`·`RemoveCells`·`MoveCell`(`BeginUpdates`/`EndUpdates`로 묶기)은 전체를 다시 읽지 않고 바뀐 자리만 반영한다.
  남은 셀은 다시 바인딩하지 않고, 보던 화면·진행 중인 트윈·드래그를 지킨다
- 부분 갱신: 내용만 바뀐 항목은 `RefreshCells`(사용자 정의 changeMask, 배치 안에서는 OR로 모아 한 번)로 그 항목의 활성 셀만 다시 그리고,
  크기·셀 종류가 바뀐 항목은 `ReloadCellView`로 그 항목만 다시 받는다
- 키 유지 리로드(`PreserveCellsById`, 옵트인): 바뀐 자리를 모를 때 위치를 지키며 다시 읽어도 항목 ID가 같은 셀은 다시 바인딩하지 않고 새 자리로 옮긴다
- 셀 크기 변경: `ResizeCellView`는 셀을 다시 바인딩하지 않고 크기만 바꾼다(바로 또는 애니메이션). 셀 위·아래 가장자리나 보던 화면을 지키고,
  애니메이션 중간 걸음은 접두합을 다시 더하지 않아 항목 수와 무관하게 활성 셀 수만큼 든다
- 정착·고속 스크롤 상태: 움직임이 모두 끝났을 때 한 번 오는 `ScrollerSettled`(셀마다 `OnScrollerSettled`)와 히스테리시스를 둔 `IsFastScrolling`으로 무거운 로드를 미룬다
- 끝 근접 이벤트: 콘텐츠 처음·끝까지 남은 거리가 `NearEdgeDistance` 이하가 되면 가장자리마다 한 번 알려 다음 페이지를 불러온다
- 스크롤 핫패스 GC 할당 0 (PlayMode 테스트로 검증, 셀 훅을 켠 상태 포함)

Unity 6000.0 이상, uGUI 2.0 이상 (6000.6.0f1 / uGUI 2.6.0에서 검증).

## 설치

`Packages/manifest.json`에 git URL을 넣는다. `?path=` 다음에 `#`를 두고, 태그나 **전체** 커밋 SHA로 고정한다.

```json
{
  "dependencies": {
    "com.cykim.scroller": "https://github.com/cyKim0115/cyKimScroller.git?path=/Packages/com.cykim.scroller#v0.2.1"
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
어느 자리가 삽입·삭제·이동됐는지 알면 `InsertCells`·`RemoveCells`·`MoveCell`이 그 자리만 반영한다 (아래 [증분 변경](#증분-변경)).
앞쪽에 항목이 삽입·삭제돼도 같은 항목을 지키려면 델리게이트에 `ICyScrollerItemIdProvider`도 구현한다 (아래 [항목 ID와 위치 앵커](#항목-id와-위치-앵커)).
여기에 `PreserveCellsById`를 켜면 위치를 지키는 리로드가 ID가 같은 셀을 다시 바인딩하지 않는다 (아래 [키 유지 리로드](#키-유지-리로드)).
크기는 그대로이고 보이는 셀 내용만 바뀌었으면 `RefreshActiveCellViews()`가 가장 싸다.
단 이 메서드는 활성 셀마다 `RefreshCellView()`를 부르기만 하므로, 셀 뷰가 이를 재정의해 자기 데이터로 다시 그려야 한다.
어느 항목이 바뀌었는지 알면 `RefreshCells(dataIndex, count, changeMask)`가 그 항목의 활성 셀에만 부른다 (아래 [부분 갱신](#부분-갱신)).
펼치기·접기처럼 크기만 바뀌었으면 `ResizeCellView(dataIndex)`가 셀을 그대로 두고 크기만 바꾼다 (아래 [셀 크기 변경](#셀-크기-변경)).

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
| 데이터 | `Delegate`, `ReloadData()`, `ReloadData(factor)`, `ReloadData(ReloadAnchor, factor)`, `ReloadData(in CyScrollerAnchor)`, `ReloadDataKeepingPosition()`, `RefreshActiveCellViews()`, `RefreshActiveCellViews(changeMask)`, `GetCellView(prefab)` |
| 항목 ID·앵커 | `ICyScrollerItemIdProvider.GetItemId`, `FindDataIndexForItemId(itemId)`, `CaptureAnchor(trailing)`, `RestoreAnchor(in anchor)`, `CyScrollerAnchor`, `ReloadAnchor`(Factor·Start·End·FirstVisible·LastVisible), `PreserveCellsById`(키 유지 리로드) |
| 증분 변경 | `InsertCells(dataIndex, count)`, `RemoveCells(dataIndex, count)`, `MoveCell(fromDataIndex, toDataIndex)`, `BeginUpdates()`, `EndUpdates()` |
| 부분 갱신 | `RefreshCells(dataIndex, count, changeMask)`, `ReloadCellView(dataIndex)`, `RefreshActiveCellViews(changeMask)` |
| 크기 변경 | `ResizeCellView(dataIndex, duration, tweenType, anchor)`, `ResizeAnchor`(Auto·Start·End), `IsResizing` |
| 이동 | `JumpToDataIndex(dataIndex, scrollerOffset, cellOffset, useSpacing, tweenType, tweenTime, onComplete, loopJumpDirection)`, `ScrollIntoView(dataIndex, align, margin, tweenType, tweenTime, onComplete, loopJumpDirection)`, `Snap()`, `InterruptTween()`, `GetJumpTargetPosition(...)` |
| 위치 | `ScrollPosition`, `NormalizedScrollPosition`, `ScrollSize`, `ScrollRectSize`, `ContentSize`, `Velocity`, `LinearVelocity` |
| 상태 | `IsScrolling`, `IsTweening`, `IsDragging`, `IsSettled`, `IsFastScrolling`, `SettleVelocityThreshold`, `FastScrollEnterThreshold`, `FastScrollExitThreshold` |
| 범위 | `NumberOfCells`, `NumberOfCellSlots`, `StartDataIndex`/`EndDataIndex`, `StartCellViewIndex`/`EndCellViewIndex`, `ActiveCellViews`, `GetCellViewAtDataIndex`, `GetCellViewAtCellIndex`, `IsDataIndexFullyVisible(dataIndex, margin)` |
| 좌표 | `GetCellStart`, `GetCellSize`, `GetScrollPositionForDataIndex`, `GetScrollPositionForCellViewIndex`, `GetCellViewIndexAtPosition`, `GetDataIndexForCellViewIndex` |
| 루프 | `Loop`, `LoopWhileDragging`, `ToggleLoop()`, `IgnoreLoopJump(bool)` |
| 끝 근접 | `NearEdgeDistance`, `ScrollerNearEdge`, `ScrollEdge`(Start·End) |
| 정리 | `ClearActive()`, `ClearRecycled()`, `ClearAll()`, `GetRecycledCellCount()` |
| 풀 | `Prewarm(prefab, count)`, `PrewarmAsync(prefab, count)`, `SetMaxRecycled(cellIdentifier, max)`, `GetMaxRecycled(cellIdentifier)`, `DefaultMaxRecycled` |
| 이벤트 | `CellViewVisibilityChanged`, `CellViewWillDisplay`, `CellViewDidEndDisplay`, `CellViewPositionChanged`, `CellViewInstantiated`, `CellViewReused`, `CellViewWillRecycle`, `ScrollerScrolled`, `ScrollerSnapped`, `ScrollerScrollingChanged`, `ScrollerTweeningChanged`, `ScrollerSettled`, `ScrollerFastScrollingChanged` |
| 위치 훅 | `NotifyCellPositions`, `CellPositionPivot` |
| 셀 뷰 | `DataIndex`, `CellIndex`, `ItemId`, `HasItemId`, `Active`, `IsBound`, `IsDisplayed`, `BindVersion`, `RefreshCellView()`, `RefreshCellView(changeMask)`, `RequestResize(duration, tweenType, anchor)`, `OnRecycled()`, `OnBecameVisible()`, `OnBecameHidden()`, `OnViewportPositionChanged(normalizedOffset)`, `OnDataIndexChanged(previousDataIndex)`, `OnScrollerSettled()` |

`scrollerOffset` / `cellOffset`은 0 = 앞(위·왼쪽), 0.5 = 가운데, 1 = 뒤. 가운데 정렬은 `JumpToDataIndex(i, 0.5f, 0.5f)`.

`ScrollIntoView`는 셀이 보이게만 옮긴다. 기본 `ScrollAlign.Nearest`는 이미 완전히 보이면 움직이지 않고 바로 완료 콜백을 부르며,
앞쪽에 걸리면 셀 시작을 뷰포트 시작에(Start), 뒤쪽이면 셀 끝을 뷰포트 끝에(End) 맞춘다. 셀이 여백까지 합쳐 뷰포트보다 크면 Start.
`margin`은 셀 앞뒤로 남길 거리다 (Center는 쓰지 않음). 콘텐츠 끝 너머로는 남길 수 없으므로 첫·마지막 셀은 콘텐츠 끝까지 보이면 된다.
선택 항목을 따라가는 목록이면 `ScrollIntoView(selected, margin: 8f)`.

### 풀 미리 채우기와 상한

첫 스크롤에 셀을 만드는 비용(Instantiate)은 화면을 열 때나 로딩 중으로 옮길 수 있다. 셀 종류(`CellIdentifier`)마다 회수 풀에 남길 수에 상한을 둘 수도 있다.

```csharp
private IEnumerator Start()
{
    // 화면을 채울 만큼(뷰포트 + 미리보기 구간) 미리 만든다. 비동기는 끝날 때까지 기다릴 수 있다.
    yield return _scroller.PrewarmAsync(_messagePrefab, 12);
    _scroller.Prewarm(_imageMessagePrefab, 4);   // 바로 만들기

    // 드물게 쓰는 큰 셀은 2개까지만 남긴다. 넘치면 가장 오래 회수된 셀부터 파괴한다.
    _scroller.SetMaxRecycled(_bannerPrefab.CellIdentifier, 2);
    _scroller.Delegate = this;
}
```

- `Prewarm`·`PrewarmAsync`는 그 식별자 풀의 회수 셀(과 진행 중인 비동기 요청)을 세어 count가 될 때까지만 만든다(상한이 있으면 상한까지). 만든 셀은 content 아래에서 끄고 `CellViewInstantiated`를 부른다.
  프리팹이 켜져 있으면 셀의 Awake·OnEnable은 끄기 전에 한 번 불린다. `PrewarmAsync`는 할 일이 없으면 null을 돌려주고, 끝나기 전에 스크롤러가 파괴되면 만든 셀을 모두 파괴한다.
  진행 중인 비동기 요청은 그사이 `ClearRecycled`를 불러도 끝나면 풀에 넣는다.
- 상한은 식별자별 `SetMaxRecycled`(0 = 제한 없음, 음수 = 기본값 따름)가 없으면 `DefaultMaxRecycled`(인스펙터 **Pool**, 기본 0 = 제한 없음)를 쓴다. 바꾸면 넘친 만큼 바로 줄인다.
  활성 셀 수는 제한하지 않으므로, 상한을 화면에 필요한 수보다 작게 두면 스크롤할 때마다 파괴와 생성이 반복된다.

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

### 정착과 고속 스크롤

빠르게 지나가는 셀의 무거운 로드(썸네일·동영상)는 미뤘다가 스크롤이 멈춘 뒤 시작할 수 있다.

| | 언제 |
|---|---|
| `ScrollerScrollingChanged` / `IsScrolling` | 드래그·관성 이동이 시작·끝날 때 (트윈 제외) |
| `ScrollerTweeningChanged` / `IsTweening` | 점프·스냅 트윈이 시작·끝날 때 |
| `ScrollerSettled` / `IsSettled` | 드래그·관성·트윈·스냅 대기·크기 애니메이션이 모두 끝나 정착했을 때 한 번 (관성이 `SettleVelocityThreshold` 이하로 느려지면 정착으로 본다) |
| `ScrollerFastScrollingChanged` / `IsFastScrolling` | 스크롤 속도가 뷰포트 길이 × `FastScrollEnterThreshold`(기본 3)/s 이상이 되거나 × `FastScrollExitThreshold`(기본 1.5)/s 미만으로 떨어질 때 |

```csharp
public class ThumbnailCellView : CyScrollerCellView
{
    private Item _item;
    private bool _loaded;

    public void SetData(Item item)
    {
        _item = item;
        _loaded = false;
        ShowPlaceholder();
        if (!Scroller.IsFastScrolling)
        {
            LoadThumbnail();   // 천천히 지나가면 바로 불러온다
        }
    }

    // 정착하면 활성 셀마다 한 번 불린다. 고속 스크롤 중에 미룬 로드를 시작한다.
    protected override void OnScrollerSettled()
    {
        if (!_loaded)
        {
            LoadThumbnail();
        }
    }
}
```

- 정착 이벤트는 정착하지 않은 상태에서 정착으로 바뀔 때 LateUpdate 끝에서 한 번 오고, 첫 로드 직후에는 오지 않는다. 이벤트 뒤에도 정착해 있으면 활성 셀(미리보기 구간 포함)마다 `OnScrollerSettled`를 부른다.
  핸들러가 다시 움직이게 했으면(트윈 시작 등) 셀에는 다음 정착 때 알린다.
- 속도는 트윈 중이면 트윈 이동 속도, 아니면 ScrollRect 관성 속도다. ScrollRect의 inertia를 끄면 드래그 중 속도는 0이고,
  휠·스크롤바 이동은 ScrollRect가 속도 없이 위치만 바꾸므로 정착으로 본다(스냅을 켜면 휠 뒤 스냅 대기 동안은 정착이 아니다).
- 가장자리 너머(Elastic)에서 되돌아오는 동안은 정착이 아니다. 되돌아오는 꼭짓점에서 속도가 0을 지나도 범위 안으로 돌아온 뒤 한 번만 정착한다.
- 크기 애니메이션(`ResizeCellView`에 시간을 준 요청)이 도는 동안도 정착이 아니다(화면 밖 항목 포함). 끝난 프레임에 정착 이벤트가 오므로, 펼친 셀의 무거운 로드도 정착 뒤로 미룰 수 있다.
  정착 핸들러에서 크기 애니메이션을 시작하면 셀에는 애니메이션이 끝난 뒤 알린다.
- `FastScrollExitThreshold`가 `FastScrollEnterThreshold`보다 크면 들어가는 값을 쓰고, 0이면 완전히 멈출 때 고속 스크롤이 끝난다. `FastScrollEnterThreshold`가 0이면 고속 스크롤을 끈다.
- 두 상태 모두 스크롤러가 꺼져 있는 동안에는 갱신하지 않고, 다시 켜진 뒤 LateUpdate에서 맞춘다.

### 끝 근접과 페이지 불러오기

`NearEdgeDistance`(px, 기본 0 = 끔)를 정하면 콘텐츠 처음·끝까지 남은 거리가 그 값 이하가 될 때 `ScrollerNearEdge`가 가장자리마다 한 번 온다.
LateUpdate 끝에서 알리므로 핸들러에서 바로 `InsertCells`를 불러도 된다.

```csharp
private void Awake()
{
    _scroller.NearEdgeDistance = 600f;   // 끝까지 600px 남으면
    _scroller.ScrollerNearEdge += OnNearEdge;
}

private void OnNearEdge(CyScroller scroller, ScrollEdge edge)
{
    if (edge != ScrollEdge.End || _loading || _lastPageLoaded)
    {
        return;
    }

    _loading = true;
    LoadNextPage(page =>
    {
        _loading = false;
        _lastPageLoaded = page.Count == 0;
        int at = _items.Count;
        _items.AddRange(page);
        _scroller.InsertCells(at, page.Count);   // 개수가 바뀌면 다시 알릴 수 있게 열린다
    });
}
```

- 알린 가장자리는 잠긴다. 남은 거리가 `NearEdgeDistance` × 1.5를 넘게 멀어지거나 데이터 개수가 바뀌면(리로드·삽입·삭제가 끝난 뒤) 다시 열린다.
  그래서 페이지를 붙였는데도 아직 끝 근처면 다시 알리고, 개수가 그대로인 리로드는 다시 알리지 않는다.
- 콘텐츠가 뷰포트보다 짧으면(빈 목록 포함) `End`만 알린다. 화면을 채울 때까지 페이지를 더 불러오는 용도다. 맨 위에서 시작하면 첫 판단에서 `Start`도 온다.
- 루프 모드와 뷰포트 길이가 0일 때는 알리지 않는다. 증분 변경 배치가 열려 있는 동안에는 닫힌 뒤 판단한다.

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

### 증분 변경

어느 자리가 바뀌었는지 알면 전체를 다시 읽지 않고 그 자리만 알린다. **데이터를 먼저 바꾼 뒤** 부른다.
삽입한 항목만 크기(와 항목 ID)를 묻고, 남은 셀은 다시 바인딩하지 않으며, 뷰포트 맨 앞 항목보다 앞에서 생긴 변화만큼 스크롤 위치를 옮겨 화면이 움직이지 않는다.

```csharp
// 한 연산은 바로 적용된다. 호출 시점의 GetNumberOfCells는 이 연산까지 반영돼 있어야 한다.
_messages.InsertRange(0, older);
_scroller.InsertCells(0, older.Count);   // 보던 메시지가 같은 자리에 남는다

// 여러 연산은 묶어서 한 번에 적용한다. 각 연산의 인덱스는 앞 연산까지 반영한 기준이다.
// BeginUpdates는 반드시 EndUpdates와 짝을 맞춘다. 사이 코드가 예외를 던져도 배치가 닫히도록 finally에서 부른다.
_scroller.BeginUpdates();
try
{
    _items.RemoveAt(3);
    _scroller.RemoveCells(3, 1);
    _items.Insert(0, pinned);
    _scroller.InsertCells(0, 1);
    Item moved = _items[10];             // 10번을 빼서 2번 자리에 넣는다
    _items.RemoveAt(10);
    _items.Insert(2, moved);
    _scroller.MoveCell(10, 2);
}
finally
{
    _scroller.EndUpdates();              // 여기서 GetNumberOfCells가 최종 개수와 맞으면 된다
}
```

배치가 닫히지 않으면 범위 갱신·리로드·재배치가 계속 미뤄져 스크롤해도 셀이 갱신되지 않는다(프레임을 넘겨 열려 있으면 경고를 한 번 남긴다).
예외로 연산을 다 알리지 못한 채 닫혀도 `EndUpdates`가 개수 불일치를 경고하고 `ReloadData(ReloadAnchor.FirstVisible)`로 데이터와 다시 맞춘다(개수가 그대로인 변경은 알아채지 못한다).

**UIKit에서 옮길 때 인덱스 해석이 다르다.** `performBatchUpdates`(`beginUpdates`/`endUpdates`)는 호출 순서와 상관없이 삭제·이동 출발점을 배치 전 인덱스로,
삽입·이동 도착점을 배치 후 인덱스로 해석한다. CyScroller는 RecyclerView의 `notifyItem*`처럼 호출 순서대로, 앞 연산까지 반영한 인덱스를 쓴다.
같은 결과를 내려면 삭제를 큰 인덱스부터 먼저 부르고, 그다음 삽입을 (배치 후 기준) 작은 인덱스부터 부른다. 이동이 섞이면 `MoveCell`의 두 인덱스를 그 시점 기준으로 바꿔 넘긴다.
예를 들어 UIKit의 `insertRows([0])` + `deleteRows([3])`(배치 전 3번 삭제)는 `RemoveCells(3, 1)` → `InsertCells(0, 1)` 순서다.
`InsertCells(0, 1)` → `RemoveCells(3, 1)`로 부르면 개수는 맞아 경고 없이 배치 전 2번이 지워진다.

인덱스만 바뀐 셀은 `OnDataIndexChanged(previousDataIndex)`를 받는다(`BindVersion`은 그대로). 셀이 인덱스(순번 등)를 그린다면 여기서 다시 그린다.
삭제된 항목의 셀은 회수하고(보이던 셀은 `CellViewDidEndDisplay`를 먼저), 새로 보이는 자리만 델리게이트로 받는다.
뷰포트 맨 앞 항목을 지우거나 `MoveCell`로 다른 자리로 옮기면 화면은 그 항목을 따라가지 않고 그 자리에 온 다음 항목이 같은 거리에 온다(아래 [증분 변경 동작](#증분-변경-동작)).

### 부분 갱신

개수는 그대로이고 일부 항목의 내용만 바뀌었으면 그 항목만 알린다. 증분 변경처럼 **데이터를 먼저 바꾼 뒤** 부르고, 같은 배치에 섞을 수 있다.

```csharp
// 7번부터 3개 항목의 텍스트만 바뀌었다. 그 항목의 활성 셀만 RefreshCellView(changeMask)를 받는다 (다시 바인딩·크기 질의 없음).
_scroller.RefreshCells(7, 3, MessageCellView.TEXT);

// 12번 메시지가 이미지 메시지가 되어 셀 종류와 크기가 바뀌었다. 그 항목만 크기를 다시 묻고 셀을 다시 받는다.
_scroller.ReloadCellView(12);
```

셀 뷰는 `RefreshCellView(int changeMask)`를 재정의해 바뀐 부분만 다시 그린다. `changeMask`는 스크롤러가 해석하지 않는 사용자 정의 비트 플래그이고,
기본 구현은 `RefreshCellView()`를 부른다.

```csharp
public class MessageCellView : CyScrollerCellView
{
    public const int TEXT = 1;
    public const int ICON = 2;

    // 무인자 RefreshActiveCellViews()도 같은 경로로 받는다.
    public override void RefreshCellView() => RefreshCellView(~0);

    // base를 부르지 않는다 (기본 구현은 RefreshCellView()를 불러 서로 부르며 끝나지 않는다).
    public override void RefreshCellView(int changeMask)
    {
        if ((changeMask & TEXT) != 0) { /* 텍스트 */ }
        if ((changeMask & ICON) != 0) { /* 아이콘 */ }
    }
}
```

- `RefreshCells`는 활성 셀(루프 모드면 같은 데이터의 사본 셀마다)에만 부른다. 활성 범위 밖 항목은 나중에 활성화될 때 최신 데이터로 바인딩된다.
- 배치 안에서는 항목마다 changeMask를 OR로 모아 `EndUpdates`에서 남은 셀마다 한 번 부르고, 뒤따른 삽입·삭제·이동으로 인덱스가 옮겨지면 같은 항목을 따라간다.
  지워진 항목과 배치 끝에 새로 바인딩되는 셀은 이미 최신 데이터이므로 부르지 않는다. 갱신만 있는 배치는 루프 모드나 셀 이벤트 콜백 안에서도 리로드하지 않는다.
- `ReloadCellView`는 그 항목만 크기(와 항목 ID)를 다시 묻고 활성 셀을 회수한 뒤 델리게이트로 다시 받는다(`BindVersion` 증가, 보이던 셀은 표시 끝 → 표시 시작).
  크기가 바뀌면 삽입처럼 보던 화면을 지킨다. 활성 셀이 없으면 크기(와 ID)만 갱신한다.
- `RefreshActiveCellViews(changeMask)`는 모든 항목에 `RefreshCells`를 부른 것과 같다. 무인자 `RefreshActiveCellViews()`는 지금처럼 활성 셀마다 `RefreshCellView()`를 바로 부른다.

### 셀 크기 변경

펼치기·접기처럼 셀 종류와 내용은 그대로이고 크기만 바뀌면, 델리게이트가 돌려줄 크기를 먼저 바꾼 뒤 `ResizeCellView`를 부른다.
그 항목의 크기만 다시 묻고 셀은 다시 바인딩하지 않는다(`BindVersion` 그대로). `duration`을 주면 그 시간 동안 크기를 바꾼다.

```csharp
public void Toggle(int dataIndex)
{
    _items[dataIndex].Expanded = !_items[dataIndex].Expanded;   // GetCellViewSize가 새 크기를 돌려준다

    // 0.25초 동안 펼친다. 셀의 위쪽 가장자리를 화면에 고정해 아래로 펼쳐진다.
    _scroller.ResizeCellView(dataIndex, 0.25f, TweenType.EaseOutCubic, ResizeAnchor.Start);
}
```

셀 안 버튼에서 부르면 `RequestResize(...)`가 그 셀의 `DataIndex`로 같은 일을 한다.

| `ResizeAnchor` | 화면에서 지키는 것 |
|---|---|
| `Auto` (기본) | 다른 증분 변경과 같다. 뷰포트 맨 앞 항목보다 앞에서 생긴 변화만 보정하고, 맨 앞 항목 자신이거나 그 뒤면 옮기지 않는다 |
| `Start` | 그 셀의 시작(위·왼쪽) 가장자리. 셀은 아래·오른쪽으로 늘어나거나 줄어든다 |
| `End` | 그 셀의 끝(아래·오른쪽) 가장자리. 셀은 위·왼쪽으로 늘어나거나 줄어든다 |

- 결과는 스크롤 범위로 자른다. 점프·스냅·`ScrollIntoView` 정렬이 유지되는 중이면 정렬이 먼저이고, 진행 중인 트윈은 새 배치의 목표로 이어 가 완료 콜백을 한 번 부른다.
- 애니메이션은 LateUpdate에서 매 프레임 크기를 바꾸고 기준에 맞춰 위치를 옮긴다(드래그 중이면 손가락 기준점도). `IsResizing`이 진행 여부이고, 진행 중에는 정착이 아니다([정착과 고속 스크롤](#정착과-고속-스크롤)).
  같은 항목에 새 요청이 오면 지금 크기에서 이어 가고, 그 항목이 지워지거나 `ReloadCellView`·리로드로 크기를 다시 읽으면 애니메이션을 버리고 그 크기를 따른다.
- 크기의 기준은 계속 델리게이트다. 크기 값을 직접 받는 메서드는 없다(다음 리로드 때 델리게이트 값으로 돌아가지 않게).
- 루프 모드에서는 애니메이션 없이 바로 바꾸고 위치를 지키는 재배치로 맞춘다(활성 셀을 다시 바인딩하고 기준은 `Auto`).
  델리게이트·셀 이벤트 콜백 안에서 부르면 다른 증분 변경처럼 범위 갱신 뒤 앵커 보존 리로드로 바뀐다. 자세한 규칙은 아래 [증분 변경 동작](#증분-변경-동작).

### 키 유지 리로드

바뀐 자리를 모르고 목록을 통째로 다시 받는 경우(서버 응답으로 목록 교체 등)에도, 델리게이트가 항목 ID를 주고 `PreserveCellsById`를 켜면(기본 꺼짐, 인스펙터 **Reload → Preserve Cells By Id**)
위치를 지키는 리로드가 ID가 같은 셀을 다시 바인딩하지 않고 새 자리로 옮긴다. 이미 불러 둔 이미지·진행 중인 연출이 남고, 지워진 항목의 셀만 회수하고 새 항목만 바인딩한다.

```csharp
private void Awake()
{
    _scroller.PreserveCellsById = true;   // 인스펙터에서 켜도 된다
}

private void OnListArrived(List<Message> latest)
{
    _messages.Clear();
    _messages.AddRange(latest);                        // 같은 메시지는 같은 Id (GetItemId)
    _scroller.ReloadData(ReloadAnchor.FirstVisible);   // 남은 메시지의 셀은 그대로 옮기고 새 메시지만 바인딩한다

    // 키 유지 리로드는 내용 변경을 감지하지 않는다. 내용이 바뀐 항목은 리로드한 뒤 새 인덱스로 알린다.
    for (int i = 0; i < _messages.Count; i++)
    {
        if (_messages[i].IsEdited)
        {
            _scroller.RefreshCells(i, 1, MessageCellView.TEXT);
        }
    }
}
```

- 키 유지를 따르는 리로드는 위치를 지키며 다시 읽는 `ReloadData(ReloadAnchor.FirstVisible / LastVisible)`·`ReloadData(in anchor)`·`ReloadDataKeepingPosition()`뿐이다.
  `ReloadData()`·`ReloadData(factor)`·Start·End처럼 처음부터 다시 그리는 리로드는 옵션과 무관하게 모든 셀을 다시 바인딩한다.
- 옮긴 셀은 인덱스가 바뀌면 `OnDataIndexChanged`를 받고 `BindVersion`은 그대로이며, 계속 보이면 표시 이벤트가 없다. 크기는 새로 받은 값으로 배치한다.
- 같은 ID는 같은 셀 종류로 보고 그 셀을 그대로 쓴다. 셀 종류가 바뀐 항목은 리로드한 뒤 `ReloadCellView`를 부른다.
- 바뀐 자리를 알면 `InsertCells`·`RemoveCells`·`MoveCell`이 더 싸다(크기·ID도 바뀐 자리만 묻는다). 자세한 규칙은 아래 [증분 변경 동작](#증분-변경-동작).

## 동작 규칙

- **content를 스크롤러가 소유한다.** 앵커·피벗·크기를 실행 시 다시 설정하고, content의 `LayoutGroup`·`ContentSizeFitter`는 끈다.
- **셀 루트 RectTransform은 스크롤러가 배치한다.** 스케일·회전 같은 효과는 자식에 준다.
- 셀 크기는 `GetCellViewSize`가 정한다. 셀이 스스로 크기를 바꾸면 `ReloadDataKeepingPosition()`(한 항목이면 `ResizeCellView(dataIndex)`, 셀 종류도 바뀌면 `ReloadCellView(dataIndex)`)으로 다시 계산한다.
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
- 증분 변경(`InsertCells`·`RemoveCells`·`MoveCell`)·부분 갱신(`RefreshCells`·`ReloadCellView`)·키 유지 리로드(`PreserveCellsById`)의 계약과 동작은 아래 [증분 변경 동작](#증분-변경-동작)에 모았다.
- LastVisible은 뷰포트 끝에 걸친 항목의 끝을 같은 자리에 둔다. 끝에 붙어 있을 때 마지막 항목이 커져도 끝에 남지만, 뒤에 항목이 붙으면 보던 항목을 지키고 새 항목은 그 아래에 붙는다.
  새 항목을 따라가려면 다시 읽기 전에 끝에 있었는지 보고 `ReloadData(ReloadAnchor.End)`를 쓴다.
- 루프 모드에서는 스크롤 축 스크롤바를 ScrollRect에서 떼어 숨기고, 루프를 끄면 다시 붙인다.
  `ScrollPosition`은 내부 슬롯 좌표이므로 저장·복원에는 `NormalizedScrollPosition`이나 `CaptureAnchor`/`RestoreAnchor`를 쓴다.
- 루프는 뷰포트·미리보기 길이를 덮고도 양쪽에 한 사이클씩 남도록 세트 수(최소 5)를 정한다. 셀 크기가 모두 0이면 루프하지 않는다.
- ScrollRect를 끄면(스크롤 잠금) CyScroller도 입력을 무시한다. 코드로 시작한 점프는 계속 진행된다.
- `ActiveCellViews`는 `IReadOnlyList`다. GC를 피하려면 `foreach` 대신 `for` + 인덱서로 순회한다.
- Profiler에서 `CyScroller.UpdateActiveRange`(활성 범위 갱신, 셀 바인딩 포함), `CyScroller.Relayout`(위치 유지 재배치), `CyScroller.ApplyUpdates`(증분 변경 적용),
  `CyScroller.Resize`(셀 크기 애니메이션 한 걸음) 마커로 비용을 확인할 수 있다.

### 증분 변경 동작

구조 변경(`InsertCells`·`RemoveCells`·`MoveCell`), 부분 갱신(`RefreshCells`·`RefreshActiveCellViews(changeMask)`·`ReloadCellView`), 키 유지 리로드(`PreserveCellsById`)의 계약:

1. **순차 의미론.** 연산은 호출한 순서대로 적용하고, 각 인덱스는 앞 연산까지 반영한 기준이다. RecyclerView `notifyItem*`과 같고 UIKit 배치와는 다르다(위 [증분 변경](#증분-변경)).
2. **데이터를 먼저 바꾼 뒤 알린다.** 배치 밖에서 부른 연산은 그 자리에서 한 연산짜리 배치로 적용하므로 호출 시점의 `GetNumberOfCells`가 그 연산까지 반영해야 한다.
   배치(`BeginUpdates`~`EndUpdates`) 안에서는 `EndUpdates` 때 최종 개수와 맞으면 된다.
3. **개수가 맞지 않으면 다시 읽는다.** `EndUpdates`에서 연산을 반영한 개수가 `GetNumberOfCells`와 다르면(연산이 없는 배치 포함) 경고를 남기고 `ReloadData(ReloadAnchor.FirstVisible)`로 다시 읽는다.
4. **루프 모드는 리로드로 바꾼다.** 같은 데이터가 여러 슬롯에 있으므로 삽입·삭제·이동·다시 받기는 증분 대신 같은 앵커 보존 리로드로 처리한다. 내용 갱신만 있는 배치는 리로드하지 않고 사본 셀마다 부른다.
5. **재진입은 미룬다.** 델리게이트·셀 이벤트 콜백 안에서 부르거나 끝난 삽입·삭제·이동·다시 받기는 범위 갱신이 끝난 뒤 같은 앵커 보존 리로드로 처리한다(내용 갱신만 있으면 바로 부른다).
   배치 중 들어온 `ReloadData`·`ReloadDataKeepingPosition`·재배치(`Spacing` 등)·`Clear*`는 `EndUpdates`에서 처리하고, 다시 읽는 요청이 있었으면 기록한 연산 대신 그 요청을 처리한다(위치 기준은 배치 전 화면).
6. **트윈 대상이 지워지면 멈춘다.** 진행 중인 트윈·점프 정렬의 대상이 지워지면 트윈은 그 자리에서 멈추고 완료 콜백·`ScrollerSnapped`는 부르지 않는다(`ScrollerTweeningChanged(false)`는 온다). 옮겨진 대상은 진행 중인 트윈만 따라가고, 끝난 점프의 정렬은 풀려 화면을 지킨다.
7. **키 유지 리로드는 내용 변경을 감지하지 않는다.** 항목 ID가 같은 셀은 다시 바인딩하지 않으므로 내용이 바뀐 항목은 리로드한 뒤 새 인덱스로 `RefreshCells`를, 셀 종류가 바뀐 항목은 `ReloadCellView`를 부른다.

3·4의 리로드와 5에서 콜백 안 삽입·삭제·이동·다시 받기를 대신하는 리로드는 `PreserveCellsById`와 무관하게 모든 셀을 다시 바인딩한다(알리지 못한 변경과 기록한 내용 갱신까지 데이터와 맞춘다).
5에서 배치·콜백 중 사용자가 직접 부른 위치 유지 리로드(FirstVisible·LastVisible·`ReloadData(in anchor)`·`ReloadDataKeepingPosition`)는 미뤄져도 키 유지를 따른다(배치에 구조 연산만 있으면 그 연산 대신 다시 읽으며 셀을 옮긴다).
배치에 `RefreshCells`·`ReloadCellView`를 기록했거나 콜백 안에서 리로드를 기다리는 동안 알렸으면 그 리로드가 내용 갱신까지 대신 처리하므로 모두 다시 바인딩한다(위 대체 리로드와 겹쳐도 그렇다).

- **위치 보존.** 뷰포트 맨 앞 항목보다 앞에서 생긴 크기 변화만큼 스크롤 위치를 옮긴다(드래그 중이면 손가락 기준점과 직전 위치도 같이 옮겨 드래그를 끊지 않는다).
  맨 앞 항목 자리의 삽입은 그 항목 앞(뷰포트 위)에 들어가 화면은 그 항목을 지킨다.
  맨 앞 항목이 지워지거나 `MoveCell`로 다른 자리로 옮겨지면 화면은 그 항목을 따라가지 않고 그 자리에 온 다음 항목이 같은 거리에 온다.
  이동은 그 자리에서 지우고 새 자리에 삽입한 것과 같아서, 맨 앞 항목을 0번으로 올리면 그 크기만큼 보정되고 목록 맨 위로 튀지 않는다.
  같은 배치에서 그 빈자리에 삽입한 항목은 자리를 채운다(맨 앞 항목을 지우고 같은 자리에 새 항목을 넣으면 새 항목이 그 자리에 보인다).
  결과는 스크롤 범위로 자른다(드래그로 가장자리 너머로 당긴 거리는 남긴다). 진행 중인 트윈·점프 정렬과 보관한 앵커는 같은 항목을 향하게 인덱스를 옮긴다
  (진행 중인 트윈은 `MoveCell`로 옮겨진 항목도 따라가 끊기지 않고 그 항목에 도착해 완료 콜백을 한 번 부른다). 대상이 지워지면 정렬은 풀린다.
  끝난 점프의 정렬 대상이 `MoveCell`로 옮겨지면 정렬을 풀고 화면을 지킨다(따라가면 화면이 그 항목으로 튄다).
- **셀.** 남은 셀은 다시 바인딩하지 않는다(`BindVersion` 그대로). 인덱스가 바뀐 셀은 `OnDataIndexChanged` 뒤 새 위치로 옮기고, 계속 보이는 셀에는 표시 이벤트가 없다.
  삭제된 항목의 셀은 회수하고(보이던 셀은 `CellViewDidEndDisplay`를 먼저), 새로 활성 범위에 들어온 자리만 델리게이트로 받는다.
  한 번의 적용은 표시 끝 → 회수 → 인덱스 변경 알림·재배치 → 내용 갱신(배치에서 `RefreshCells`를 모은 셀) → 새 자리 바인딩 → 표시 시작 순서다.
- **배치.** 배치(중첩 가능) 중에는 개수·좌표·활성 셀·항목 ID가 배치 전 상태로 남고, 범위 갱신과 델리게이트 호출을 배치 끝으로 미룬다(그 사이 스크롤해도 된다).
  배치 중 점프·`ScrollIntoView`처럼 인덱스를 받는 이동은 배치 전 인덱스로 해석하고, 배치 끝에 다른 진행 중 이동처럼 같은 항목으로 옮긴다.
  `BeginUpdates`는 반드시 `EndUpdates`와 짝을 맞춘다(사이 코드가 예외를 던질 수 있으면 `finally`에서). 닫히지 않은 배치는 위 작업을 계속 미루므로 스크롤해도 셀이 갱신되지 않고 `ReloadData`도 처리되지 않는다.
  배치가 프레임을 넘겨 열려 있으면 LateUpdate에서 경고를 한 번 남긴다(배치마다 한 번). 늦게라도 `EndUpdates`를 부르면 그때 적용한다.
- **범위 밖 인자는 자른다.** 삽입 위치는 [0, 개수], 삭제·갱신 구간은 [0, 개수) 안쪽만, 이동은 두 인덱스를 [0, 개수 − 1]로. `count`가 0 이하이거나 changeMask가 0이면 아무것도 하지 않고,
  `ReloadCellView`는 [0, 개수) 밖 인덱스를 무시한다. `EndUpdates`를 `BeginUpdates`보다 많이 부르면 경고 후 무시한다. 로드 전에 부른 연산은 무시한다(첫 리로드가 전부 읽는다).
- **부분 갱신.** `RefreshCells`는 레이아웃을 바꾸지 않고 활성 셀(루프 모드면 같은 데이터의 사본 셀마다)에만 `RefreshCellView(changeMask)`를 부른다(다시 바인딩하지 않으므로 `BindVersion` 그대로).
  배치 밖에서는 바로 부르고, 델리게이트·셀 이벤트 콜백 안에서도 바로 부른다(증분 변경을 적용하거나 키 유지 리로드·재배치로 다시 읽는 중이면 활성 셀을 새 인덱스로 맞춘 뒤).
  리로드·재배치가 기다리고 있으면 그쪽이 셀을 모두 다시 바인딩하므로 부르지 않는다.
  배치 안에서는 항목마다 changeMask를 OR로 모아 `EndUpdates`에서 남은 셀마다 한 번 부르고(인덱스 변경 알림·재배치 뒤, 새 자리 바인딩·표시 시작 전), 새로 바인딩되는 셀(새로 활성 범위에 들어온 항목, 삽입·다시 받은 항목)과
  지워진 항목, 리로드로 바뀐 배치에는 부르지 않는다. 갱신만 있는 배치는 콜백 안에서 끝나도 리로드하지 않고 셀마다 모은 changeMask로 한 번 부르므로, 표시 시작 핸들러에서 갱신을 배치로 묶어도 비용은 활성 셀 수만큼이다.
- **다시 받기.** `ReloadCellView`는 그 항목만 크기(와 항목 ID)를 다시 묻고 활성 셀을 회수한 뒤 다시 받는다(셀 종류가 바뀌어도 된다, `BindVersion` 증가, 보이던 셀은 표시 끝 → 표시 시작).
  활성 셀이 없으면 크기(와 ID)만 갱신하고 `GetCellView`는 부르지 않는다. 크기가 바뀌면 삽입과 같은 위치 보존 규칙을 따르고(맨 앞 항목 자신이면 그 안의 거리를 지킨다),
  ID가 바뀌었으면 ID → 인덱스 사전을 그 자리부터 다시 맞춘다. 배치 안에서는 `EndUpdates`까지 미뤄 인덱스 이동을 따라가고, 같은 배치에서 지워지면 아무것도 하지 않는다.
- **키 유지 리로드.** `PreserveCellsById`를 켜고 델리게이트가 항목 ID를 주면 `ReloadData(ReloadAnchor.FirstVisible / LastVisible)`·`ReloadData(in anchor)`·`ReloadDataKeepingPosition()`은
  셀을 먼저 회수하지 않고 다시 읽은 뒤, 옛 활성 셀마다 같은 ID 항목의 새 인덱스를 찾아(이전 인덱스 자리에 같은 ID가 남아 있으면 그 자리, 아니면 앞 인덱스) 위 증분 변경과 같은 순서로 맞춘다.
  새 활성 범위 밖이 되거나 ID가 사라진 셀은 회수하고, 한 자리에는 셀 하나만 옮긴다. 옮긴 셀은 새로 받은 크기로 배치한다. 위치 결과는 옵션을 끈 리로드와 같다.
  - 같은 ID는 같은 셀 종류로 보고 그 셀을 그대로 쓴다(델리게이트에 셀 종류만 따로 물을 방법이 없다). 셀 프리팹을 통째로 바꿨으면 `ClearActive`를 먼저 부르거나 `ReloadData()`를 쓴다.
  - 루프 모드에서는 같은 항목의 사본 중 셀이 화면에 있던 자리에 가장 가까운 사본으로 옮긴다(같은 화면이면 표시 이벤트가 없다).
  - 처음부터 다시 그리거나 위치를 정하는 리로드(`ReloadData()`·`ReloadData(factor)`·Start·End·Factor), 다시 읽지 않는 재배치(`Spacing`·`Padding`·`Loop` 등)와 축 전환,
    위 3·4의 리로드와 5에서 콜백 안 삽입·삭제·이동·다시 받기를 대신하는 리로드는 옵션과 무관하게 모든 셀을 다시 바인딩한다. 델리게이트를 바꾼 뒤 처음 다시 읽을 때도 그렇다.
  - 배치·콜백 중 사용자가 직접 부른 위치 유지 리로드는 미뤄져도 키 유지를 따른다. 다만 배치에 기록했거나 리로드를 기다리는 동안(콜백 안) 알린 `RefreshCells`·`ReloadCellView`는
    그 리로드가 대신 처리하므로, 이때는 키 유지 없이 모든 셀을 다시 바인딩한다.
    리로드를 한 직후 부른 `RefreshCells`는 남은 셀에 그대로 전달된다. 키 유지 리로드·재배치가 셀을 맞추기 전에 사용자 코드(트윈 멈춤 알림, 다시 읽는 중의 델리게이트 등)가
    부른 `RefreshCells`(그 안에서 닫은 갱신만 있는 배치 포함)는 새 인덱스로 보고 맞춘 뒤 부른다.
  - 사용자 코드 예외로 셀 맞추기가 멈추면 다음 갱신에 모두 다시 바인딩하는 앵커 보존 리로드로 데이터와 맞춘다.
- **크기 변경.** `ResizeCellView`는 배치에 기록되는 연산이다(순차 의미론). 바로 바꾸면 다시 받기처럼 그 자리 크기(와 항목 ID)를 배치 끝에 묻되 셀은 회수하지 않고 크기·위치만 바꾼다.
  애니메이션이면 배치 끝에 최종 인덱스로 목표 크기만 묻고 지금 크기에서 시작한다. 같은 배치에서 그 항목을 지우거나 다시 받거나 크기를 다시 요청하면 앞 요청은 버린다.
  위치는 `Auto`면 위 위치 보존 규칙을 따르고, `Start`·`End`면 배치 전 그 셀의 가장자리가 같은 화면 자리에 오게 한다(배치에 여럿이면 마지막 요청, 그 셀이 지워지면 `Auto`).
  진행 중인 애니메이션은 증분 변경을 따라 같은 항목으로 옮겨지고, 배치가 열려 있는 동안 멈춘다. 매 걸음은 같은 경로로 크기를 바꾸고 활성 셀을 맞춘다(새로 범위에 들어온 자리만 바인딩).
  중간 걸음은 레이아웃 접두합을 다시 더하지 않고 그 뒤 항목 위치에 변화량을 더해 계산하므로 항목 수와 무관하고, 마지막 걸음이나 다른 크기·개수 변경이 바뀐 자리부터 한 번 다시 더한다.
- **비용.** 증분 변경은 크기·항목 ID 배열을 `Array.Copy`로 옮기고(용량이 모자랄 때만 늘린다) 삽입분만 델리게이트에 묻는다. 접두합과 ID 사전은 바뀐 가장 앞 자리부터 다시 맞춘다.
  키 유지 리로드는 모든 항목의 크기·ID를 다시 받되(O(N)) 셀은 남은 만큼 바인딩하지 않는다. 워밍업 뒤에는 어느 쪽도 할당하지 않는다.

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
- 표시 시작과 끝은 셀마다 항상 짝이 맞는다. 보이던 셀이 `ReloadData`·`ClearActive`·재배치·증분 삭제·`ReloadCellView`로 회수·파괴될 때도 표시 끝을 먼저 받는다.
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
  델리게이트 `GetCellView` 안에서 읽은 값이 그 바인딩의 값이다. `RefreshCellView`(`RefreshCells`)·루프 순환 보정·증분 변경으로 인덱스만 바뀔 때처럼 같은 데이터로 남으면 늘지 않는다.
  `ReloadCellView`는 회수한 뒤 다시 바인딩하므로 는다. `IsBound`는 `DataIndex >= 0`이다.
- 위치 훅(`NotifyCellPositions`)은 위치·활성 범위·레이아웃·뷰포트 크기가 바뀐 프레임에 한 번, 스크롤러 LateUpdate 끝(실행 순서 100)에서 활성 셀마다
  `CellViewPositionChanged` → `OnViewportPositionChanged`를 부른다.
  normalizedOffset = (셀 시작 + 셀 크기 × `CellPositionPivot` − 뷰포트 시작) / 뷰포트 길이. 0 = 앞(위·왼쪽) 가장자리, 0.5 = 가운데, 1 = 뒤 가장자리이고, 미리보기 구간 셀은 0 미만·1 초과가 될 수 있다.
  - 새로 활성화된 셀(리로드 직후 포함)은 활성화 즉시 한 번 받으므로 재사용된 뷰가 이전 모습으로 한 프레임 보이지 않는다.
  - 스크롤러 LateUpdate 뒤(더 늦은 실행 순서의 LateUpdate·코루틴 등)에서 위치가 바뀌면 다음 프레임 LateUpdate에 반영된다.
  - 아무것도 바뀌지 않은 프레임에는 부르지 않고, 꺼져 있으면 계산 자체를 건너뛴다. 뷰포트 길이가 0이면 부르지 않는다. 레이아웃 캐시로만 계산해 할당이 없다.
  - 셀 루트 RectTransform은 스크롤러가 배치하므로 위치 훅에서는 자식(스케일·회전·투명도)만 바꾼다.
- 셀 뷰 훅(`OnBecameVisible`·`OnBecameHidden`·`OnViewportPositionChanged`·`OnDataIndexChanged`·`OnScrollerSettled`)은 `protected internal`이다. 다른 어셈블리에서는 `protected override`로 재정의한다.

## 샘플

Package Manager → CyKim Scroller → Samples → **Basic** Import.
빈 씬의 GameObject에 `BasicSample`을 붙이고 Play하면 세로 목록·점프와 편집 버튼·휠 피커·루프 캐러셀이 만들어진다.
편집 버튼(맨 위에 3개 삽입, 보이는 항목 삭제, 보이는 항목을 맨 위로 이동)은 증분 변경으로 보던 화면을 지키고, 휠 피커와 캐러셀은 위치 훅으로 행·카드 모양을 그린다.

## 다른 목록 UI와 비교

UITableView·RecyclerView·UI Toolkit ListView와의 개념 대응표는 [`Documentation~/api-mapping.md`](Documentation~/api-mapping.md).

## 라이선스

MIT — [LICENSE.md](LICENSE.md)
