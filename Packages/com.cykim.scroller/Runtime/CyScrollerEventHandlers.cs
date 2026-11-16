using UnityEngine;

namespace CyKim.Scroller
{
    /// <summary>
    /// 셀이 활성화되거나(<see cref="CyScrollerCell.IsActive"/> true, overscan 미리보기 구간 포함) 재활용될 때(false).
    /// 실제 뷰포트 기준은 <see cref="CellDisplayChangedHandler"/>.
    /// </summary>
    public delegate void CellActiveChangedHandler(CyScrollerCell cell);

    /// <summary>셀이 실제 뷰포트(overscan 구간 제외)에 걸치기 시작하거나(WillDisplay) 벗어날 때(DidEndDisplay).</summary>
    public delegate void CellDisplayChangedHandler(CyScroller scroller, CyScrollerCell cell);

    /// <summary>
    /// 셀의 뷰포트 안 위치. <paramref name="normalizedOffset"/> 0 = 앞 가장자리, 0.5 = 가운데, 1 = 뒤 가장자리
    /// (<see cref="CyScrollerCell.OnViewportPositionChanged"/>와 같은 값).
    /// </summary>
    public delegate void CellPositionChangedHandler(CyScroller scroller, CyScrollerCell cell, float normalizedOffset);

    /// <summary>풀에 없어서 프리팹을 새로 Instantiate했을 때.</summary>
    public delegate void CellCreatedHandler(CyScroller scroller, CyScrollerCell cell);

    /// <summary>풀에서 꺼내 다시 쓸 때.</summary>
    public delegate void CellReusedHandler(CyScroller scroller, CyScrollerCell cell);

    /// <summary>셀이 풀로 돌아가기 직전. 인덱스는 아직 유효하다.</summary>
    public delegate void CellRecyclingHandler(CyScrollerCell cell);

    /// <summary>스크롤 위치가 바뀌었을 때. <paramref name="normalizedPosition"/>은 ScrollRect 값 그대로다.</summary>
    public delegate void ScrollPositionChangedHandler(CyScroller scroller, Vector2 normalizedPosition, float scrollPosition);

    /// <summary>
    /// 스냅이 끝났을 때. <paramref name="slotIndex"/>는 루프 순환 보정 뒤 슬롯이고, 셀이 활성 범위 밖이면 <paramref name="cell"/>은 null이다.
    /// 범위 갱신 콜백 안에서 끝난 스냅은 범위 갱신이 끝난 뒤 온다.
    /// </summary>
    public delegate void SnapCompletedHandler(CyScroller scroller, int slotIndex, int dataIndex, CyScrollerCell cell);

    /// <summary>드래그·관성 이동 상태가 바뀔 때. 트윈 이동은 포함하지 않는다.</summary>
    public delegate void ScrollingChangedHandler(CyScroller scroller, bool scrolling);

    /// <summary>ScrollToIndex·ScrollIntoView·스냅 애니메이션 상태가 바뀔 때.</summary>
    public delegate void AnimatingChangedHandler(CyScroller scroller, bool animating);

    /// <summary>움직이던 스크롤러가 정착했을 때 (드래그·관성·트윈·스냅 대기·크기 애니메이션이 모두 끝남). <see cref="CyScroller.IsSettled"/>.</summary>
    public delegate void ScrollSettledHandler(CyScroller scroller);

    /// <summary>고속 스크롤 상태가 바뀔 때. <see cref="CyScroller.IsFastScrolling"/>.</summary>
    public delegate void FastScrollingChangedHandler(CyScroller scroller, bool fastScrolling);

    /// <summary>콘텐츠 가장자리까지 남은 거리가 <see cref="CyScroller.NearEdgeDistance"/> 이하가 됐을 때. 다음 페이지를 불러오는 데 쓴다.</summary>
    public delegate void NearEdgeReachedHandler(CyScroller scroller, ScrollEdge edge);
}
