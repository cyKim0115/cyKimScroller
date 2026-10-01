namespace CyKim.Scroller.Tests
{
    /// <summary>테스트용 셀 뷰. 바인딩된 데이터와 호출 횟수만 기록한다.</summary>
    public class TestCellView : CyScrollerCellView
    {
        public int BoundData = -1;
        public int RefreshCount;
        public int RecycledCount;

        public override void RefreshCellView()
        {
            RefreshCount++;
        }

        public override void OnRecycled()
        {
            RecycledCount++;
        }
    }
}
