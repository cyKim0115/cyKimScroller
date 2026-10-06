---
description: "스크롤러 하나, 셀 프리팹 하나, 델리게이트 하나로 첫 목록을 띄운다"
icon: rocket
---

# 빠른 시작

높이가 다른 항목 목록을 세로로 띄우는 데 필요한 것은 세 가지다.

* `CyScroller`가 붙은 스크롤 뷰
* `CyScrollerCellView`를 상속한 셀 프리팹
* 항목 수·크기·셀을 알려 주는 `ICyScrollerDelegate`

{% stepper %}
{% step %}
### 스크롤러 만들기

Hierarchy 우클릭 → **UI (Canvas) → CyKim Scroller → Vertical Scroller**를 고른다 (Unity 6.2 이하는 **UI → CyKim Scroller**).
Canvas·EventSystem이 없으면 함께 만들고, Viewport 마스크는 `RectMask2D`로 바꿔 둔다. 가로 목록이면 **Horizontal Scroller**를 고른다.

{% hint style="info" %}
content는 스크롤러가 소유한다. 실행할 때 앵커·피벗·크기를 다시 설정하므로 content에 `LayoutGroup`·`ContentSizeFitter`를 붙이지 않는다.
{% endhint %}
{% endstep %}

{% step %}
### 셀 프리팹 만들기

셀 프리팹 루트에 `CyScrollerCellView`를 상속한 컴포넌트를 붙이고, 인스펙터에서 **Cell Identifier**를 정한다. 같은 식별자끼리 같은 풀을 쓴다.

{% code title="ItemCellView.cs" lineNumbers="true" %}
```csharp
using CyKim.Scroller;
using UnityEngine;
using UnityEngine.UI;

public class ItemCellView : CyScrollerCellView
{
    [SerializeField] private Text _label;

    private ItemData _data;

    // 재사용된 셀이 들어오므로 이전 상태를 전부 덮어쓴다.
    public void SetData(ItemData data)
    {
        _data = data;
        Redraw();
    }

    // RefreshActiveCellViews()가 부른다. 같은 _data의 바뀐 값을 다시 그린다.
    public override void RefreshCellView() => Redraw();

    private void Redraw()
    {
        _label.text = _data.Name;
    }
}
```
{% endcode %}

{% hint style="warning" %}
셀 루트 RectTransform은 스크롤러가 배치한다. 스케일·회전 같은 연출은 루트가 아닌 **자식**에 준다.
{% endhint %}
{% endstep %}

{% step %}
### 델리게이트 구현하기

컨트롤러에서 `ICyScrollerDelegate`의 세 메서드를 구현한다.

{% code title="InventoryList.cs" lineNumbers="true" %}
```csharp
using CyKim.Scroller;
using UnityEngine;

public class InventoryList : MonoBehaviour, ICyScrollerDelegate
{
    [SerializeField] private CyScroller _scroller;
    [SerializeField] private ItemCellView _cellPrefab;

    private ItemData[] _items;

    // 항목 수
    public int GetNumberOfCells(CyScroller scroller) => _items.Length;

    // 항목의 스크롤 축 크기 (세로면 높이)
    public float GetCellViewSize(CyScroller scroller, int dataIndex) => _items[dataIndex].IsHeader ? 48f : 120f;

    // 항목을 그릴 셀. 풀에서 꺼내거나 새로 만든다.
    public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
    {
        var view = (ItemCellView)scroller.GetCellView(_cellPrefab);
        view.SetData(_items[dataIndex]);
        return view;
    }
}
```
{% endcode %}
{% endstep %}

{% step %}
### 연결하고 불러오기

데이터를 준비한 뒤 `Delegate`에 넣는다. 다음 `LateUpdate`에 스크롤러가 알아서 `ReloadData()`를 부른다.

```csharp
private void Start()
{
    _items = LoadItems();
    _scroller.Delegate = this;   // 다음 LateUpdate에 자동 ReloadData
}
```

데이터가 바뀌면 처음부터 다시 그리는 `ReloadData()`나, 맨 앞 셀을 지키는 `ReloadDataKeepingPosition()`을 부른다.
{% endstep %}
{% endstepper %}

## 데이터가 바뀔 때 무엇을 부르나

| 바뀐 것 | 부를 것 |
|---|---|
| 목록을 통째로 새로 받음 | `ReloadData()` (처음으로) / `ReloadDataKeepingPosition()` (보던 자리 유지) |
| 어느 자리가 삽입·삭제·이동됐는지 앎 | `InsertCells`·`RemoveCells`·`MoveCell` — [증분 변경과 부분 갱신](../guides/incremental-updates.md) |
| 보이는 셀 내용만 바뀜 (크기 그대로) | `RefreshActiveCellViews()` 또는 `RefreshCells(dataIndex, count, changeMask)` |
| 한 항목의 크기·셀 종류가 바뀜 | `ReloadCellView(dataIndex)` |
| 앞쪽에 항목이 끼어들어도 보던 항목을 지키고 싶음 | `ICyScrollerItemIdProvider` 구현 — [항목 ID와 위치 앵커](../guides/item-id-and-anchor.md) |

다음은 [기본 개념](concepts.md)이다.
