namespace CyKim.Scroller
{
    /// <summary>
    /// 스크롤러에 데이터 개수·셀 크기·셀 뷰를 공급한다. 보통 리스트를 소유한 컨트롤러가 구현한다.
    /// </summary>
    public interface ICyScrollerDelegate
    {
        /// <summary>논리 데이터 개수. 루프 모드에서도 한 사이클 기준이다.</summary>
        int GetNumberOfCells(CyScroller scroller);

        /// <summary>
        /// 스크롤 축 방향 셀 크기 (세로면 높이, 가로면 너비). 셀마다 달라도 된다.
        /// <see cref="CyScroller.ReloadData"/> 때만 호출되므로 크기가 바뀌면 다시 로드한다.
        /// </summary>
        float GetCellViewSize(CyScroller scroller, int dataIndex);

        /// <summary>
        /// <see cref="CyScroller.GetCellView"/>로 뷰를 받아 데이터를 바인딩한 뒤 반환한다.
        /// <paramref name="cellIndex"/>는 스크롤 시퀀스 슬롯 번호로, 루프 모드에서는 <paramref name="dataIndex"/>와 다르다.
        /// </summary>
        CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex);
    }
}
