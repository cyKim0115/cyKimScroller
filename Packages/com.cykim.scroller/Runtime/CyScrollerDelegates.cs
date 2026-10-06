using UnityEngine;

namespace CyKim.Scroller
{
    /// <summary>
    /// 셀 뷰가 활성화되거나(<see cref="CyScrollerCellView.Active"/> true, lookAhead 미리보기 구간 포함) 재활용될 때(false).
    /// 실제 뷰포트 기준은 <see cref="CellViewDisplayChangedHandler"/>.
    /// </summary>
    public delegate void CellViewVisibilityChangedHandler(CyScrollerCellView cellView);

    /// <summary>셀 뷰가 실제 뷰포트(lookAhead 구간 제외)에 걸치기 시작하거나(WillDisplay) 벗어날 때(DidEndDisplay).</summary>
    public delegate void CellViewDisplayChangedHandler(CyScroller scroller, CyScrollerCellView cellView);

    /// <summary>
    /// 셀 뷰의 뷰포트 안 위치. <paramref name="normalizedOffset"/> 0 = 앞 가장자리, 0.5 = 가운데, 1 = 뒤 가장자리
    /// (<see cref="CyScrollerCellView.OnViewportPositionChanged"/>와 같은 값).
    /// </summary>
    public delegate void CellViewPositionChangedHandler(CyScroller scroller, CyScrollerCellView cellView, float normalizedOffset);

    /// <summary>풀에 없어서 프리팹을 새로 Instantiate했을 때.</summary>
    public delegate void CellViewInstantiatedHandler(CyScroller scroller, CyScrollerCellView cellView);

    /// <summary>풀에서 꺼내 다시 쓸 때.</summary>
    public delegate void CellViewReusedHandler(CyScroller scroller, CyScrollerCellView cellView);

    /// <summary>셀 뷰가 풀로 돌아가기 직전. 인덱스는 아직 유효하다.</summary>
    public delegate void CellViewWillRecycleHandler(CyScrollerCellView cellView);

    /// <summary>스크롤 위치가 바뀌었을 때. <paramref name="normalizedPosition"/>은 ScrollRect 값 그대로다.</summary>
    public delegate void ScrollerScrolledHandler(CyScroller scroller, Vector2 normalizedPosition, float scrollPosition);

    /// <summary>
    /// 스냅이 끝났을 때. <paramref name="cellIndex"/>는 루프 순환 보정 뒤 슬롯이고, 셀이 활성 범위 밖이면 <paramref name="cellView"/>는 null이다.
    /// 범위 갱신 콜백 안에서 끝난 스냅은 범위 갱신이 끝난 뒤 온다.
    /// </summary>
    public delegate void ScrollerSnappedHandler(CyScroller scroller, int cellIndex, int dataIndex, CyScrollerCellView cellView);

    /// <summary>드래그·관성 이동 상태가 바뀔 때. 트윈 이동은 포함하지 않는다.</summary>
    public delegate void ScrollerScrollingChangedHandler(CyScroller scroller, bool scrolling);

    /// <summary>점프·스냅 트윈 상태가 바뀔 때.</summary>
    public delegate void ScrollerTweeningChangedHandler(CyScroller scroller, bool tweening);

    /// <summary>콘텐츠 가장자리까지 남은 거리가 <see cref="CyScroller.NearEdgeDistance"/> 이하가 됐을 때. 다음 페이지를 불러오는 데 쓴다.</summary>
    public delegate void ScrollerNearEdgeHandler(CyScroller scroller, ScrollEdge edge);
}
