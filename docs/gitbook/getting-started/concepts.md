---
description: "데이터 소스, 셀 식별자와 풀, 데이터 인덱스와 슬롯, 활성 범위와 overscan"
icon: book-open
---

# 기본 개념

## 데이터 소스

스크롤러는 데이터를 들고 있지 않다. 필요할 때마다 데이터 소스(`ICyScrollerDataSource`)에게 묻는다.

| 메서드 | 묻는 것 | 언제 |
|---|---|---|
| `GetItemCount` | 항목 수 (루프여도 한 사이클 기준) | 리로드할 때 |
| `GetItemSize` | 항목의 스크롤 축 크기 (세로면 높이, 가로면 너비) | 리로드할 때 모든 항목, 삽입·`ReloadCell` 때 그 항목만 |
| `GetCell` | 그 항목을 그릴 셀 | 항목이 활성 범위에 들어올 때 |

크기는 데이터 소스가 정한다. 셀이 스스로 크기를 바꾸지 않고, 크기가 바뀌면 다시 묻게 한다 (`ReloadDataKeepingPosition()`, 한 항목이면 `ReloadCell(dataIndex)`, 크기만 바뀐 한 항목이면 [`ResizeCell`](../guides/cell-resize.md)).

## 셀 식별자와 풀

셀은 `CyScrollerCell`을 상속한 컴포넌트이고, **Cell Identifier**로 종류를 나눈다.
`DequeueCell` 안에서 `scroller.DequeueCell(prefab)`을 부르면 같은 식별자로 회수해 둔 셀이 있으면 꺼내고, 없으면 프리팹을 새로 만든다.

* 활성 범위를 벗어난 셀은 부모를 바꾸지 않고 비활성으로 풀에 들어간다 (`OnRecycled()`가 불린다).
* 한 목록에 셀 종류가 여럿이면 프리팹마다 다른 식별자를 준다 (헤더·본문·광고 등).
* 재사용된 셀에는 이전 항목의 상태가 남아 있다. 바인딩할 때 **모든 표시 상태를 덮어쓴다.**

{% hint style="info" %}
`CellCreated`(새로 만듦)·`CellReused`(풀에서 꺼냄)·`CellRecycling`(풀로 돌아감) 이벤트로 풀 동작을 볼 수 있다.
첫 스크롤의 생성 비용을 미리 치르거나 풀에 남길 수를 제한하려면 [풀 미리 채우기와 회수 상한](../guides/pool-prewarm.md)을 본다.
{% endhint %}

## 데이터 인덱스와 슬롯

| 이름 | 뜻 |
|---|---|
| 데이터 인덱스 (`dataIndex`) | 항목 번호. 0 ~ `ItemCount` − 1 |
| 셀 인덱스·슬롯 (`slotIndex`) | 스크롤 축에 실제로 늘어선 자리 번호. 루프가 아니면 데이터 인덱스와 같다 |

루프 모드에서는 같은 데이터를 여러 세트 이어 붙이므로 슬롯 수(`SlotCount`)가 데이터 수 × 세트 수가 되고, 한 항목이 여러 슬롯에 있을 수 있다.
그래서 `ScrollPosition`도 슬롯 좌표다. 위치를 저장·복원할 때는 `NormalizedPosition`이나 `CaptureAnchor`/`RestoreAnchor`를 쓴다.

## 활성 범위와 overscan

스크롤러는 **뷰포트 + 미리 만들기 구간**에 걸친 항목만 셀을 둔다. 이 구간이 활성 범위다.

* `OverscanBefore` / `OverscanAfter` — 뷰포트 앞·뒤로 미리 만들어 둘 거리(px). 빠르게 스크롤할 때 가장자리에서 셀이 늦게 나타나는 것을 막는다.
* `FirstActiveDataIndex`~`LastActiveDataIndex` — 지금 활성 범위의 항목. `ActiveCells`로 활성 셀을 순회한다 (GC를 피하려면 `for` + 인덱서).
* "실제로 보이는가"는 overscan를 뺀 뷰포트 기준이다. 표시 이벤트·`ScrollIntoView`·`IsDataIndexFullyVisible`이 이 기준을 쓴다 ([셀 훅](../guides/cell-hooks.md)).

## 좌표

| 프로퍼티 | 뜻 |
|---|---|
| `ScrollPosition` | 콘텐츠 시작(위·왼쪽)에서 뷰포트 시작까지 거리. 대입하면 트윈·관성을 멈추고 옮긴다 |
| `NormalizedPosition` | 0 = 처음, 1 = 끝 |
| `MaxScrollPosition` | 스크롤할 수 있는 길이 (콘텐츠 − 뷰포트) |
| `ViewportSize` / `ContentSize` | 뷰포트 길이 / 콘텐츠 길이 |
| `Velocity` / `AxisVelocity` | 관성 속도 |

`GetSlotAtPosition`·`GetDataIndexForSlot`로 항목과 좌표를 오간다.

## 이벤트 한눈에 보기

| 이벤트 | 언제 |
|---|---|
| `ScrollPositionChanged` | 스크롤 위치가 바뀔 때 |
| `ScrollingChanged` / `AnimatingChanged` | 스크롤(드래그·관성) / 트윈이 시작·끝날 때 |
| `SnapCompleted` | 스냅이 끝났을 때 |
| `CellWillDisplay` / `CellDidEndDisplay` | 셀이 실제 뷰포트에 걸치기 시작 / 완전히 벗어날 때 |
| `CellActiveChanged` | 셀이 활성화(바인딩)될 때 / 회수될 때 |
| `CellPositionChanged` | 셀의 뷰포트 안 위치가 바뀔 때 (`NotifyCellPositions`를 켰을 때) |

전체 목록과 순서 규칙은 [패키지 설명서](../../../Packages/com.cykim.scroller/README.md)의 **주요 API**·**셀 훅 동작** 절에 있다.
