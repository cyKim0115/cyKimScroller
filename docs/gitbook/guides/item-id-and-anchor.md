---
description: "항목 ID로 보던 항목을 지키는 리로드와 위치 저장·복원"
icon: link
---

# 항목 ID와 위치 앵커

인덱스만으로는 "같은 항목"을 알 수 없다. 앞쪽에 메시지가 3개 끼어들면 보던 항목의 인덱스가 3 밀리기 때문이다.
델리게이트가 항목 ID를 주면 스크롤러는 위치를 지킬 때 인덱스 대신 ID로 같은 항목을 찾는다.

## 항목 ID 주기

델리게이트에 `ICyScrollerItemIdProvider`도 구현한다.

```csharp
public class ChatList : MonoBehaviour, ICyScrollerDelegate, ICyScrollerItemIdProvider
{
    public long GetItemId(CyScroller scroller, int dataIndex) => _messages[dataIndex].Id;
    // GetNumberOfCells · GetCellViewSize · GetCellView는 그대로
}
```

{% hint style="warning" %}
ID는 데이터가 바뀌어도 같은 항목이면 같은 값이어야 한다 (서버 ID·DB 키 등). 데이터 인덱스를 그대로 돌려주면 의미가 없다.
같은 ID가 여럿이면 ID로 찾을 때 앞 인덱스가 이기고, 에디터·개발 빌드에서 경고를 남긴다.
{% endhint %}

ID는 리로드할 때만 항목마다 한 번 받는다. 스크롤 중에는 받지 않으므로 비용이 없다.

## 위치를 지키는 리로드

| 호출 | 결과 |
|---|---|
| `ReloadData()` | 처음으로 |
| `ReloadData(ReloadAnchor.End)` | 끝으로 (채팅에서 새 메시지 따라가기) |
| `ReloadData(ReloadAnchor.FirstVisible)` | 뷰포트 맨 앞에 걸친 항목을 같은 자리에 |
| `ReloadData(ReloadAnchor.LastVisible)` | 뷰포트 맨 뒤에 걸친 항목을 같은 자리에 (아래쪽 기준) |
| `ReloadDataKeepingPosition()` | 맨 앞 항목 유지. 진행 중인 트윈·점프 정렬도 이어 간다 |

```csharp
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
```

{% hint style="info" %}
어느 자리가 바뀌었는지 알면 리로드보다 `InsertCells`·`RemoveCells`·`MoveCell`이 싸다 (바뀐 자리만 크기·ID를 묻는다). [증분 변경과 부분 갱신](incremental-updates.md)을 본다.
목록을 통째로 다시 받으면서 같은 ID 셀을 다시 바인딩하지 않으려면 [키 유지 리로드](incremental-updates.md#키-유지-리로드)를 켠다.
{% endhint %}

## 위치 저장과 복원

화면을 닫았다 다시 열 때는 앵커를 저장해 두고 복원한다. `CyScrollerAnchor`는 `[Serializable]` 구조체라 그대로 저장할 수 있다.

```csharp
CyScrollerAnchor saved = _scroller.CaptureAnchor();   // 뷰포트 맨 앞 항목 기준
// CyScrollerAnchor saved = _scroller.CaptureAnchor(true);   // 뷰포트 맨 뒤 항목 기준

// 다시 열 때 (Delegate를 넣기 전이든 뒤든)
_scroller.RestoreAnchor(saved);
```

* 앵커에는 항목(ID·인덱스)과 그 항목에서 뷰포트 가장자리까지 거리가 들어 있다. 복원할 때는 ID → 인덱스 순으로 항목을 찾는다.
* 데이터가 아직 없거나 뷰포트 크기가 정해지기 전이면 보관했다가 준비되면 적용한다. 그 사이 점프·`ScrollPosition` 대입 같은 새 위치 요청이 오면 보관한 앵커는 버린다.
* `RestoreAnchor`는 `ScrollPosition` 대입처럼 트윈·관성을 멈춘다.

## 더 보기

* [패키지 설명서 — 항목 ID와 위치 앵커](../../../Packages/com.cykim.scroller/README.md#항목-id와-위치-앵커)
* [패키지 설명서 — 동작 규칙](../../../Packages/com.cykim.scroller/README.md#동작-규칙): 중복 ID, 보관한 앵커, LastVisible 세부 규칙
