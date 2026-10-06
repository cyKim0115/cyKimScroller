namespace CyKim.Scroller
{
    /// <summary>
    /// 항목마다 안정 ID를 주는 선택 인터페이스. <see cref="ICyScrollerDelegate"/>를 구현한 객체가 함께 구현하면
    /// 스크롤러가 델리게이트를 다시 받을 때마다 모든 항목의 ID를 받아 둔다.
    /// </summary>
    /// <remarks>
    /// <para>데이터가 바뀌어도(앞쪽 삽입·삭제, 정렬) 같은 항목은 같은 ID를 돌려줘야 한다. 데이터 인덱스를 그대로 ID로 쓰면 의미가 없다.</para>
    /// <para>ID가 있으면 <see cref="CyScroller.ReloadDataKeepingPosition"/>·<see cref="CyScroller.RestoreAnchor"/>·<see cref="CyScroller.ReloadData(ReloadAnchor, float)"/>가
    /// 인덱스 대신 ID로 같은 항목을 찾는다. 셀 뷰는 <see cref="CyScrollerCellView.ItemId"/>로 받는다.</para>
    /// <para>같은 ID가 여럿이면 ID로 찾을 때 앞 인덱스가 이긴다 (에디터·개발 빌드에서는 다시 받을 때마다 경고 1회).
    /// 위치 유지(앵커 복원·정렬 유지)는 이전 인덱스 자리에 같은 ID가 남아 있으면 그 자리를 쓰므로, 데이터가 그대로면 뒤쪽 중복 항목에서도 제자리다.</para>
    /// </remarks>
    public interface ICyScrollerItemIdProvider
    {
        /// <summary>
        /// dataIndex 항목의 안정 ID. <see cref="CyScroller.ReloadData()"/>·<see cref="CyScroller.ReloadDataKeepingPosition"/>처럼
        /// 델리게이트를 다시 받을 때 항목 수만큼, <see cref="CyScroller.InsertCells"/> 때는 삽입한 항목에만 불린다 (스크롤 중에는 불리지 않는다).
        /// </summary>
        long GetItemId(CyScroller scroller, int dataIndex);
    }
}
