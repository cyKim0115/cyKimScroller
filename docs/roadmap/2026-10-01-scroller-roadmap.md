# CyKim Scroller 발전 로드맵 (2026-10-01)

> 워크플로 `scroller-roadmap-ideation` 결과. Android RecyclerView · iOS UICollectionView · 웹 가상 리스트(TanStack Virtual 등) · Unity 대안 에셋 4개 생태계를 조사하고(에이전트 4), 우리 구조에 맞는지 검토한 뒤(에이전트 4) 합쳤다(에이전트 1).
> 후보 74건 → 기능 35개. **사용자 선택 필요** 표시는 제품·UX 판단이라 에이전트가 정하지 않은 항목이다.
> v0.1.0에서 이미 반영한 것: 1위 회귀 가드 일부(GC 0 PlayMode 테스트, `Assets/Dev` 스트레스 씬), 2위 중 드래그 중 재배치 기준점 갱신·재배치 시 점프 콜백 유지(리뷰 반영 커밋 참고).

## 요약
- 후보 74건을 기능 35개와 거절 목록으로 병합했습니다. 중복이 가장 많았던 축은 콘텐츠 이동 프리미티브(4건), 앵커·상태 보존(5), 증분 변경(4), 크기 측정·보정(4), 채팅 모드(4), 게임패드 내비게이션(4), 스티키 헤더(4), 중첩 제스처(3)입니다.
- 루프 재중심, 앵커 복원, 증분 변경, 크기 보정, 채팅 기능은 모두 드래그 중에도 안전한 `ShiftScrollPosition` 하나에 의존합니다. 조사 때 짚은 v0.1의 잠재 버그 두 개는 v0.1.0에서 해결됨 상태이며 회귀 테스트가 있습니다.
  - 드래그 중에 `RelayoutKeepingPosition`이 실행되면 드래그 기준점이 갱신되지 않아 콘텐츠가 튀던 문제: v0.1.0에서 해결됨 (`Relayout_MidDrag_ContentDoesNotJump`). 가장자리 너머로 당긴(Elastic) 구간에서 위치가 범위로 잘리던 남은 경우는 ①에서 해결 (`Relayout_MidDragPastTopEdge_KeepsPullAndRubberBand` 등)
  - 재배치할 때 `CancelTween`이 실행 중인 `JumpToDataIndex`의 `onComplete`를 잃어버리던 문제: v0.1.0에서 해결됨. ②에서 트윈을 끊지 않고 새 배치의 목표로 이어 간 뒤 한 번 완료하도록 바꿈 (`Relayout_DuringJumpTween_RetargetsAndInvokesCallbackOnce`)
- v0.2의 주제는 "데이터가 바뀌어도 위치와 셀 상태를 지킨다"입니다. v0.1에서 범위 밖으로 둔 그리드, 중첩 제스처, 자동 크기 측정은 v0.3 이후로 미룹니다. v0.2 묶음에는 새 MonoBehaviour가 없고, partial 파일과 일반 C# 타입만 추가합니다.
- 소스 사이에서 충돌한 기술 판단은 다음과 같이 정했습니다.
  - ID는 `long ItemId`로 둡니다(Key가 아님). payload는 object 대신 int 비트마스크로 둡니다.
  - 배치 변경 의미론은 순차 적용(RecyclerView 방식)입니다. UIKit처럼 "삭제는 배치 전 인덱스"를 쓰지 않습니다.
  - 그리드는 별도 `CyGridScroller`가 아니라 CyScroller 내부 배치 계층으로 만듭니다.
  - 중첩 제스처는 형제 컴포넌트에서 축 플래그를 토글하는 방식을 1안으로, `CyScrollRect` 서브클래스를 2안으로 둡니다.
  - 드래그 속도 보정은 `ScrollRect.Rebuild(PostLayout)`를 1안으로, onValueChanged 큐잉을 2안으로 둡니다. 어느 쪽인지는 PlayMode 테스트로 확정합니다.
- **사용자 선택 필요** (각 항목 옆에도 표시했습니다):
  - v0.2를 한 번에 낼지 나눠서 낼지
  - 기본 스냅 감각
  - 채팅에서 프리펜드할 때의 기본 동작
  - 키 유지 리로드의 기본값
  - 타깃 플랫폼과 인벤토리 화면 수요(내비게이션·그리드 순위가 여기에 달려 있음)
  - 클릭 억제와 스크롤바 동작 기본값
  - 당겨서 새로고침 필요 여부
  - 애니메이션과 샘플 연출 스타일

## 우선순위 표

| 순위 | 기능 | 가치 | 비용 | 위험 | 선행 | 출처 생태계 |
|---|---|---|---|---|---|---|
| 1 | GC·레이아웃 회귀 가드: 할당 0 PlayMode 테스트, 무차별 대입 기준 모델과 비교하는 레이아웃 테스트, ProfilerMarker, `Assets/Dev` 벤치 씬 | 높음 | S | 낮음: 첫 호출이나 딥 프로파일에서 오탐 | — | 자체 |
| 2 | 드래그 안전 콘텐츠 이동 `ShiftScrollPosition` (루프 재중심·재배치가 같은 경로를 씀) | 높음 | S | 중: uGUI `ScrollRect.LateUpdate` 내부 순서에 의존하므로 회귀 테스트로 고정 | 1 | 자체 |
| 3 | 트윈 목표 실시간 재계산, `ScrollIntoView`(Nearest·margin), `GetCellStart/Size` | 높음 | S | 낮음: 매 프레임 호출되면 목표가 같을 때 트윈을 재시작하지 않아야 함 | 2 | AND·iOS·Web |
| 4 | 셀 수명 훅: `BindVersion`, 실제 뷰포트 기준 표시 이벤트·가상 메서드 | 높음 | S | 낮음 | — | AND·iOS |
| 5 | 셀 뷰포트 위치 훅 `OnViewportPositionChanged` (캐러셀·휠 피커 샘플) | 높음 | S | 낮음: 셀 루트가 아니라 자식만 변형해야 함 | — | U·FLT |
| 6 | 안정 아이템 ID `ICyScrollerItemIdProvider` (long) | 높음 | S | 낮음: 해시 충돌, 인덱스를 ID로 쓰는 오용 | — | AND·iOS·Web |
| 7 | 공개 앵커·상태 API, `ReloadData(ReloadAnchor)`, 준비 전 요청의 지연 복원 | 높음 | S | 중: 트윈 중 복원, 루프 가운데 세트 매핑 | 2·6 | AND·FLT·Web |
| 8 | 부분 갱신 `RefreshCells(changeMask)`·`ReloadCellView` (활성 셀만, 크기 불변) | 높음 | S | 낮음: 활성 셀에만 전달됨을 문서화 | — | AND·iOS |
| 9 | 증분 구조 변경 `Begin/EndUpdates`, `InsertCells/RemoveCells/MoveCell`, Reconcile | 높음 | M | 높음: 인덱스 장부·재진입 → 기준 모델 비교 테스트 | 2·7 | AND·iOS |
| 10 | 셀 크기 변경과 스크롤 보정 (`SetCellViewSize`, 애니메이션, `ResizeAnchor`) — **브랜치 구현(미병합)**: `feature/cell-resize`, API는 `ResizeCellView`(크기는 델리게이트 기준) | 높음 | M | 중: 루프 SetCount 변화, 탄성 구간 | 2·3 | iOS·Web·FLT |
| 11 | 손을 뗄 때 정하는 스냅 전략 (착지 예측·한 칸·페이지, 속도가 끊기지 않는 트윈) **사용자 선택 필요(기본 감각)** | 높음 | M | 중: 감각 튜닝, 끝단 오버슈트 이징 | 2 | AND·iOS·FLT |
| 12 | 끝 근접 이벤트 `ScrollerNearEdge`, 범위 로더 헬퍼 — **브랜치 구현(미병합)**: `feature/near-edge`, 범위 로더 헬퍼는 만들지 않고 README 예시로 대신 | 중 | S | 낮음: 래치, 콘텐츠가 뷰포트보다 짧은 경우 | 7·9 | Web·AND |
| 13 | 채팅 모드: 짧은 콘텐츠 끝 정렬, 끝 따라가기, `IsAtEnd`, 점프 없는 프리펜드(앞쪽 삽입) **사용자 선택 필요** | 높음 | M | 중: 드래그·관성 중 프리펜드 | 2·7·9·10 | Web·AND·FLT |
| 14 | 풀 프리웜(동기·`InstantiateAsync`), 식별자별 풀 상한 — **브랜치 구현(미병합)**: `feature/pool-prewarm` | 중 | S | 낮음: 비동기 생성 중 초과분은 완료 시 정리 | — | AND·U |
| 15 | 정착 상태 `IsSettled`/`ScrollerSettled`, `IsFastScrolling` (플레이스홀더 패턴) — **브랜치 구현(미병합)**: `feature/settled-state` | 중 | S | 낮음: 임계값에 히스테리시스 필요 | 4 | Web·FLT |
| 16 | 중첩 스크롤 제스처 중재 (직교 드래그를 부모로 넘기는 별도 컴포넌트) | 높음 | M | 중: 제스처 도중 행이 재활용되면 OnDisable에서 부모 OnEndDrag 호출 | 4 | AND·iOS·U |
| 17 | 스티키 섹션 헤더 (뷰포트 오버레이 레이어, 밀어내기) **사용자 선택 필요(중복 헤더 처리)** | 중~높음 | M | 중: 같은 인덱스에 뷰 두 개, 루프에서 비활성 | 3 | iOS·AND·FLT |
| 18 | 고정 머리·꼬리 콘텐츠 (재활용 안 하는 헤더·푸터를 패딩으로 편입) | 중 | S | 낮음: 알림 없는 리사이즈 | 2·10 | Web·FLT |
| 19 | 페이지 뷰 부품: `CurrentPage`/`PageChanged`, 인디케이터, 자동 넘김 **사용자 선택 필요(자동 넘김 조건)** | 중 | S | 낮음 | 11 | FLT·AND |
| 20 | 게임패드·키보드 내비게이션 (아직 생성 안 된 셀까지 이동, 재활용 시 선택 유지) **사용자 선택 필요(플랫폼 우선순위)** | 높음(콘솔·PC) / 낮음(모바일) | M | 중: 자동 내비게이션과 충돌, OnSelect 안에서 재선택하면 오류 | 3·4 | iOS·AND·Web |
| 21 | 타입 어댑터 `CyScrollerListAdapter<TData,TView>`, `CyConcatDelegate` | 중 | S | 낮음: API 표면 증가 | 9 | AND |
| 22 | KeepAlive·임시 상태 보호 (재활용 유예, 개수 상한·타임아웃) | 중 | M | 중: 누수, 컬링돼도 CPU 비용 지속 | 4 | FLT·AND·Web |
| 23 | 키 기반 diff 유틸 `CyListDiff` (LIS로 이동 검출), `ApplyUpdates` | 중 | M | 낮음: 중복 ID, 대량 이동 | 6·9 | AND·iOS |
| 24 | 모바일 감각 옵션: 멈춤 탭 클릭 억제, 누르는 동안 스냅 보류, 스크롤바 포인터 위치로 점프 **사용자 선택 필요(기본값)** | 중 | S | 중: uGUI·Input System 내부 동작에 의존 | — | iOS·AND |
| 25 | 추정 크기를 실측으로 교정 (`MeasureSize`, 지연 접두합, 키 기반 크기 캐시) | 높음 | L | 높음: 플링 중 측정 스파이크, 연쇄 보정 | 2·6·10 | iOS·Web·AND |
| 26 | 그리드 (줄당 K개, 내부 배치 계층, 아이템마다 셀 하나) **사용자 선택 필요(인벤토리 수요)** | 높음(수요 있을 때) | L | 높음: "슬롯 = 셀" 가정을 전반적으로 수정 | 9 | AND·iOS·Web |
| 27 | 아이템 애니메이션 (삽입·삭제·이동, `ICyScrollerItemAnimator`) **사용자 선택 필요(기본 길이·이징)** | 중 | L | 높음: 애니메이션 중 범위 이탈, 대량 이동 | 4·6·9·22 | AND·iOS |
| 28 | 프레임당 바인드 예산, 여유 시간에 채우는 소프트 준비 구간 | 중 | M | 중: 예산이 부족하면 미리보기 구간에 공백 | 1·14 | AND·iOS·FLT |
| 29 | 데이터 프리페치 콜백 (플링 착지 구간 기준, 취소 포함) | 중(원격 리소스일 때) | M | 낮음 | 11 | iOS·AND |
| 30 | 균일 크기 모드 (O(1) 리로드·탐색) | 중~낮음 | S | 낮음 | — | AND·FLT·Web |
| 31 | 키 기반 선택 추적 헬퍼 `CySelectionTracker` | 중 | S | 낮음 | 6 | AND |
| 32 | 트리 평탄화 헬퍼 (펼침·접힘, 자식 지연 로드) | 중 | M | 낮음 | 6·9 | iOS |
| 33 | 드래그 재정렬 (리스트 하나 안에서만) | 중 | L | 높음: 롱프레스와 스크롤 구분 | 9·10·22 | AND·iOS |
| 34 | 당겨서 새로고침 **사용자 선택 필요(필요 여부)** | 낮음~중 | M | 중: Elastic 이동 모드에서만 동작 | 2·9 | AND·FLT·iOS |
| 35 | 측정 크기 내보내기·가져오기 | 중 | S | 낮음 | 7·25 | Web |

출처 표기: AND = Android RecyclerView 계열, iOS = UIKit UICollectionView, Web = TanStack Virtual·react-virtuoso·virtua·react-window, FLT = Flutter, U = Unity uGUI 스크롤러 에셋(FancyScrollView·LoopScrollRect·OSA 등), 자체 = 조사 목록에 없고 우리 구조에서 도출

## v0.2 추천 묶음
위 표의 1~9위를 다섯 덩어리로 묶었습니다. 구현 순서는 ① → ② → ⑤ → ③ → ④입니다.

> **v0.2.0 (2026-10-06) 반영:** ①~⑤ 다섯 묶음을 모두 v0.2.0 하나로 냈습니다(묶음마다 피처 브랜치). 키 유지 리로드는 옵트인(`PreserveCellsById`, 기본 꺼짐)으로 정했습니다. 10위(크기 변경)는 다음 버전 후보로 남깁니다. 실제 API와 동작은 패키지 README·CHANGELOG가 기준입니다.

**사용자 선택 필요:**
- 다섯 개를 v0.2 하나로 낼지, v0.2(①②⑤)와 v0.2.x(③④)로 나눌지
- ⑤ 대신 10위(아코디언 크기 변경)를 넣을지

### ① 안전 기반: 드래그 안전 이동 + 회귀 가드 (1·2위, S+S) — v0.2.0 완료
`RecenterLoopIfNeeded`와 `RelayoutKeepingPosition`을 이 경로 하나로 합칩니다.
```csharp
// CyScroller.Anchoring.cs (partial)
private bool _inValueChanged;            // OnScrollRectValueChanged 실행 중에만 true
internal void ShiftScrollPosition(float delta);
// 1) SetScrollPositionInternal(ScrollPosition + delta), 활성 셀 PositionCell (O(active))
// 2) _dragging이면 _scrollRect.OnBeginDrag(_dragEventData)로 드래그 기준 재설정
// 3) !_inValueChanged이면 _scrollRect.Rebuild(CanvasUpdate.PostLayout) → UpdatePrevData로 플링 속도 스파이크 제거
//    (2안: onValueChanged 안에서 적용하도록 큐잉)
// 4) 진행 중인 트윈의 시작·목표와 스냅 목표에 delta를 더함  5) UpdateActiveRange, 루프면 재중심

// Tests/Runtime
[UnityTest] public IEnumerator Shift_MidDrag_ReleaseVelocityMatchesUnshifted();
[UnityTest] public IEnumerator Relayout_MidDrag_ContentDoesNotJump();   // v0.1.0에서 해결됨 (이미 있는 회귀 테스트)
[Test] public void ScrollSweep_AfterWarmup_DoesNotAllocate()
    => Assert.That(() => _scroller.ScrollPosition += 37f, Is.Not.AllocatingGCMemory());
private static readonly ProfilerMarker s_UpdateActiveRangeMarker = new("CyScroller.UpdateActiveRange");
```

### ② 스크롤 대상과 트윈 목표 실시간 재계산 (3위, S) — v0.2.0 완료
트윈은 절대 좌표 대신 "요청"을 저장하고, `UpdateTween`마다 O(1)로 목표를 다시 계산합니다. 재배치가 일어나도 트윈을 취소하지 않으므로 `onComplete`가 유지됩니다.
```csharp
public enum ScrollAlign { Start = 0, Center = 1, End = 2, Nearest = 3 }

public void ScrollIntoView(int dataIndex, ScrollAlign align = ScrollAlign.Nearest, float margin = 0f,
    TweenType tweenType = TweenType.Immediate, float tweenTime = 0f, Action onComplete = null,
    LoopJumpDirection loopJumpDirection = LoopJumpDirection.Closest);
public bool IsDataIndexFullyVisible(int dataIndex, float margin = 0f);   // lookAhead를 뺀 실제 뷰포트 기준
public float GetCellStart(int dataIndex);                               // 루프면 가운데 세트 기준
public float GetCellSize(int dataIndex);

private struct JumpRequest { public int Slot; public float ScrollerOffset; public float CellOffset; public bool UseSpacing; }
private JumpRequest _tweenRequest;
private bool _hasTweenRequest;   // 루프 재중심 때 Slot도 함께 이동
```

### ③ 안정 ID와 위치 보존 리로드 (6·7위, S+S) — v0.2.0 완료
- 데이터가 0개이거나 뷰포트 크기가 0일 때 들어온 복원·점프 요청은 `_pendingAnchor`에 보관합니다. 다음 `ReloadData`(N > 0)나 `CheckViewportResize`에서 적용합니다. 리로드 직후 점프하면 빈 화면이 나오는 문제가 이것으로 해결됩니다.
- `ReloadDataKeepingPosition()`은 `ReloadData(ReloadAnchor.FirstVisible)`의 별칭으로 남깁니다.
- 구현 반영(③): 보관하는 것은 복원 요청(`RestoreAnchor`·`ReloadData(in anchor)`)뿐이고, 점프·`ScrollIntoView`·`Snap`은 갈 셀이 없어도 보관한 앵커를 버립니다(빈 목록 점프는 지금처럼 바로 완료).
  드래그·휠 뒤 자동 스냅은 보관 중에는 기다리고, 앵커를 적용할 때 남은 관성과 스냅 대기를 멈춥니다.
  `ReloadDataKeepingPosition()`은 별칭이 아니라 트윈·정렬을 이어 가는 기존 재배치에 ID 찾기를 더했습니다(ID가 있으면 정렬 대상도 ID로 찾아 유지).
  같은 ID가 여럿이면 ID 조회는 앞 인덱스지만, 앵커 복원·정렬 유지는 이전 인덱스 자리에 같은 ID가 남아 있으면 그 자리를 씁니다(데이터가 그대로면 제자리).
  매개변수 없는 `ReloadData()`는 보관한 앵커를 적용하고 `ReloadData(factor)`는 버립니다. LastVisible은 보던 아래쪽 항목을 지키고, 끝 따라가기는 13위(채팅 모드)에 남깁니다.
```csharp
public interface ICyScrollerItemIdProvider          // 선택 구현. Delegate setter에서 as 캐스트 1회
{
    long GetItemId(CyScroller scroller, int dataIndex);
}

[Serializable]
public struct CyScrollerAnchor
{
    public int DataIndex; public long ItemId; public bool HasItemId; public float Offset;
}
public enum ReloadAnchor { Factor = 0, Start = 1, End = 2, FirstVisible = 3, LastVisible = 4 }

public CyScrollerAnchor CaptureAnchor(bool trailing = false);   // 기존 private CaptureAnchor는 이름 변경
public void RestoreAnchor(in CyScrollerAnchor anchor);         // 준비 전이면 보관
public void ReloadData(ReloadAnchor anchor, float scrollPositionFactor = 0f);
public void ReloadData(in CyScrollerAnchor anchor);
public int FindDataIndexForItemId(long itemId);                // 리로드 때 채운 Dictionary<long,int>, 없으면 -1

// CyScrollerCellView
public long ItemId { get; internal set; }
public bool HasItemId { get; internal set; }
```

### ④ 증분 구조 변경과 부분 갱신 (8·9위, S+M) — v0.2.0 완료
- **계약:** 각 연산은 호출한 순서대로 적용됩니다. 호출하는 시점의 `GetNumberOfCells`는 이미 변경을 반영하고 있어야 합니다.
- **개수 불일치:** `EndUpdates`에서 개수가 안 맞으면 `[CyScroller]` LogWarning을 남기고 `ReloadData(ReloadAnchor.FirstVisible)`로 대체합니다.
- **위치 보존:** 뷰포트 위쪽에 삽입하면 앵커 인덱스를 재계산하고 `ShiftScrollPosition`으로 보정하므로 화면이 움직이지 않습니다.
- **루프 모드:** 앵커를 보존하는 전체 재배치로 대체하고 문서화합니다.
- **재진입:** `_inRangeUpdate` 중에 들어온 호출은 큐에 넣습니다.
- **테스트:** 무작위 연산을 `List<int>` 기준 모델에 똑같이 적용해 결과를 비교합니다.
- **사용자 선택 필요:** 키 유지 리로드(ID가 같은 활성 셀은 재활용하지 않음)를 기본값으로 할지, 옵트인으로 둘지. 기본값으로 하면 v0.1보다 `CellViewWillRecycle`/`CellViewReused`가 덜 발생합니다.
```csharp
// CyScroller.Updates.cs (partial)
public void BeginUpdates();                          // int 깊이 카운터(중첩 허용, 할당 없음)
public void EndUpdates();                            // 접두합을 최소 변경 인덱스부터 재구성, Reconcile, 앵커 복원
public void InsertCells(int dataIndex, int count);   // GetCellViewSize는 삽입분만 조회 (Array.Copy)
public void RemoveCells(int dataIndex, int count);
public void MoveCell(int fromDataIndex, int toDataIndex);
public void RefreshCells(int dataIndex, int count, int changeMask = ~0);   // 크기 불변, 활성 셀만, 배치 안에서 OR 누적
public void ReloadCellView(int dataIndex);           // 델리게이트로 다시 바인딩(식별자가 바뀌어도 됨). 비활성이면 no-op
public void RefreshActiveCellViews(int changeMask);

// CyScrollerCellView
public virtual void RefreshCellView(int changeMask) => RefreshCellView();
protected internal virtual void OnDataIndexChanged(int previousDataIndex) { }
```

### ⑤ 셀 훅: 바인딩 버전, 표시 이벤트, 뷰포트 위치 (4·5위, S+S) — v0.2.0 완료
- 기존 `CellViewVisibilityChanged`는 lookAhead 구간을 포함한 활성화 기준을 그대로 유지합니다(v0.1 동작 호환).
- 위치 훅은 플래그가 꺼져 있으면 루프 자체를 건너뜁니다. 계산은 레이아웃 캐시에서 하고, `LateUpdate` 끝에서 한 번만 실행합니다.
- **사용자 선택 필요:** 캐러셀·휠 피커 샘플의 연출 스타일.
```csharp
// CyScrollerDelegates.cs
public delegate void CellViewDisplayChangedHandler(CyScroller scroller, CyScrollerCellView cellView);

// CyScroller
public event CellViewDisplayChangedHandler CellViewWillDisplay;     // 실제 뷰포트 범위의 구간 차이로 계산, O(changed)
public event CellViewDisplayChangedHandler CellViewDidEndDisplay;
[SerializeField] private bool _notifyCellPositions;
[SerializeField, Range(0f, 1f)] private float _cellPositionPivot = 0.5f;

// CyScrollerCellView
public int BindVersion { get; internal set; }     // 바인딩마다 +1. 비동기 로드가 캡처해 두고 바뀌었으면 결과를 버림
public bool IsBound => DataIndex >= 0;
protected internal virtual void OnBecameVisible() { }
protected internal virtual void OnBecameHidden() { }
protected internal virtual void OnViewportPositionChanged(float normalizedOffset) { }   // 루트 말고 자식만 변형
```

## 이후 후보
- 10 셀 크기 변경과 보정 (v0.3 1순위): `SetCellViewSize(dataIndex, size, duration, tweenType, ResizeAnchor)`, `RequestResize()`. 진행 중인 크기 변화는 접두합 재구성 없이 임시 항 하나로 처리합니다.
- 11 스냅 전략 (v0.3): `SnapMode { VelocityThreshold, PredictedLanding, OneCellPerSwipe, Page }`와 커스텀 `ICyScrollerSnapStrategy`. 기본값은 v0.1 동작(속도 임계값 스냅)을 권장합니다. **사용자 선택 필요:** 플릭 임계값과 곡선.
- 12 끝 근접 이벤트 + `CyScrollerRangeLoader` (v0.3): 위아래 양 끝에 래치를 걸고, 루프 모드에서는 비활성입니다.
- 13 채팅 모드 (v0.3): `ShortContentAlignment`, `FollowOutput`, `AtEndChanged`, TMP `GetPreferredValues` 기반 샘플. **사용자 선택 필요:** 위치 0에서 프리펜드할 때 같은 메시지를 유지할지 맨 위를 유지할지, 오버스크롤 중 끝 따라가기 처리.
- 14 풀 프리웜과 식별자별 상한 (v0.3): `Prewarm`, `PrewarmAsync`, `SetMaxRecycled`. 상한을 넘으면 가장 오래된 항목부터 Destroy합니다.
- 15 정착 상태와 고속 스크롤 플래그 (v0.3): 정착할 때 셀마다 `OnScrollerSettled` 가상 메서드를 호출합니다.
- 16 중첩 제스처 중재 컴포넌트 (v0.3, v0.1 범위 밖이던 항목): 같은 축 경계에서 넘겨주기는 보류합니다.
- 17 스티키 섹션 헤더 (v0.4): `ICyScrollerSectionDelegate`와 오버레이 레이어. 점프할 때 `scrollPaddingStart`를 반영합니다.
- 18 고정 머리·꼬리 콘텐츠 (v0.4): `_leadingContent/_trailingContent`를 패딩으로 편입합니다.
- 19 페이지 뷰 부품 (v0.4): 인디케이터와 자동 넘김을 각각 파일 하나짜리 컴포넌트로 만듭니다.
- 20 게임패드·키보드 내비게이션 (v0.4, **사용자 선택 필요:** 플랫폼에 따라 순위 조정): `SelectedDataIndex`, IMoveHandler 컴포넌트, 끝에서 빠져나갈 Selectable 지정.
- 21 타입 어댑터와 Concat 델리게이트 (v0.4): MonoBehaviour가 아닌 일반 C# 클래스, 추상 메서드 기반(클로저 없음).
- 22 KeepAlive·임시 상태 보호 (v0.4): `KeepAlive`, `HasTransientState`, `_maxKeptAlive`, 강제 재활용 타임아웃.
- 23 키 기반 diff (v0.4): 연산 수가 N/2를 넘으면 리로드로 대체합니다. 개발 빌드에서만 중복 ID를 검증합니다.
- 24 모바일 감각 옵션 (v0.4, **사용자 선택 필요:** 클릭 억제 기본값, 스크롤바 클릭을 페이지 단위로 할지 포인터 위치 점프로 할지).
- 25 추정 크기 실측 교정 (v0.5, v0.1 범위 밖이던 항목): 셀 루트에 ContentSizeFitter가 있으면 경고하고, 보정 패스는 프레임당 3회로 제한합니다.
- 26 그리드 (v0.5, **사용자 선택 필요:** 인벤토리형 화면 수요): `ICyScrollerGridDelegate`, N%K≠0이면 루프를 금지합니다.
- 27 아이템 애니메이션 (v0.5): 화면 밖에서 들어오는 셀의 이전 위치는 이중 버퍼 접두합으로 구합니다. 대량 이동은 애니메이션 개수를 제한합니다.
- 28 프레임당 바인드 예산과 소프트 준비 구간 (프로파일 근거가 생긴 뒤): 시간이 아니라 개수로 상한을 둡니다.
- 29 데이터 프리페치 콜백 (원격 리소스가 많은 게임일 때): ReadOnlySpan과 미리 할당한 int[]를 씁니다.
- 30 균일 크기 모드: 10만 개 이상 리스트에서 리로드 비용을 O(1)로 줄입니다.
- 31 키 기반 선택 추적 헬퍼: HashSet<long>을 재사용하며, 샘플로 낼지 런타임에 넣을지는 미정입니다.
- 32 트리 평탄화 헬퍼: 퀘스트 로그·설정 트리 샘플.
- 33 드래그 재정렬 (리스트 하나 안에서만): 핸들 방식을 먼저 만들고 롱프레스는 나중에 합니다.
- 34 당겨서 새로고침 (**사용자 선택 필요:** 필요 여부, 게임 UI는 새로고침 버튼이 흔함): load-more와 분리해서 판단합니다.
- 35 측정 크기 내보내기·가져오기: 25번 이후, 호출자가 소유한 배열로 할당 없이 처리합니다.

## 하지 않을 것
- **Staggered/Masonry 레이아웃:** 인덱스 순서대로 시작 위치가 늘어나지 않아 단일 이진 탐색 모델이 깨집니다. 줄마다 따로 배열을 두는 별도 모델이 필요합니다.
- **2D 윈도잉·고정 행/열(MultiGrid):** ScrollRect에 대각선 축 잠금이 없고 활성 셀 수가 면적만큼 늘어납니다. 그리드(26)로 수요를 확인한 뒤 재검토합니다.
- **NSCollectionLayout 같은 item/group/section 트리:** uGUI에는 과한 API입니다.
- **공개 `ICyLayout`/`CyScrollerLayoutBase`를 지금 노출:** 두 번째 레이아웃이 나오기 전에 일반화하면 API가 굳어 버립니다.
- **reverseLayout이나 콘텐츠 피벗 뒤집기:** 모든 인덱스·위치 경로가 두 벌이 되고 `ScrollPosition`의 의미도 바뀝니다. 델리게이트에서 N-1-i로 매핑하거나 채팅 모드(13)로 대체합니다.
- **범용 rangeExtractor 콜백:** 프레임마다 배열을 반환하므로 할당이 생기고, 활성 셀이 연속이라는 불변식이 깨집니다. 스티키 헤더(17)로 구체화합니다.
- **범용 onDraw/onDrawOver 데코레이션, 내장 구분선·섹션 배경:** uGUI에는 그리기 콜백이 없습니다. 셀 프리팹이 담당합니다.
- **아이템별 인셋 프로바이더:** `GetCellViewSize`와 프리팹 내부 여백으로 대체할 수 있어 API만 중복됩니다.
- **AsyncListDiffer식 워커 스레드 diff, Myers diff:** 사용자 비교자가 메인 스레드 전용 객체를 건드립니다. 게임 데이터에는 ID가 있으므로 키 기반 LIS diff로 충분합니다.
- **스크롤러 간 공유 풀:** 콘텐츠 사이 SetParent와 캔버스 재구축 비용이 들고, "SetParent 없음" 원칙에 어긋납니다.
- **바인딩 유지 뷰 캐시(재진입 시 재바인딩 생략):** "보일 때마다 바인딩"이라는 계약을 깨서 오래된 데이터가 보일 수 있습니다. 프로파일 근거가 생기면 재검토합니다.
- **타입별 Instantiate 비용 학습, 시간 기반 프레임 예산:** Unity는 프레임 마감 시점을 알려 주지 않습니다. 개수 상한(28)으로 대체합니다.
- **방향 가중 오버스캔:** 비용이 큰 바인딩은 진행 방향에서 일어나므로 이득이 거의 없고, 방향이 바뀔 때 셀이 반복 생성·재활용됩니다.
- **스크롤바 스무딩(스크롤바 직접 구동):** ScrollRect가 매 프레임 덮어써서 충돌합니다.
- **scaled time 토글:** ScrollRect 관성은 unscaled라서 우리 트윈과 어긋납니다. 문서로만 안내합니다.
- **재활용 파킹 모드(SetActive 생략):** 프로파일 근거가 없으면 오히려 손해일 수 있습니다(Update·Animator가 계속 돌고 RectMask2D에서만 컬링됨). 벤치(1)에서 SetActive 비용이 지배적일 때만 재검토합니다.
- **ScrollRect 하나 안에 가상화 섹션 여러 개(sliver 시스템):** CyScroller와 ScrollRect의 1:1 설계와 충돌하고 비용이 XL입니다. 고정 머리·꼬리(18)로 대체합니다.
- **리스트 간 드래그 이동, 중첩 스크롤의 같은 축 경계 넘겨주기:** 제스처·탄성 상호작용 위험에 비해 수요가 작습니다. 단일 재정렬(33)과 중재(16)가 안정된 뒤 재검토합니다.
- **Fenwick 트리 접두합:** 5만 개 이상에서 프로파일로 확인되기 전에는 O(N-i) 재구성으로 충분합니다.