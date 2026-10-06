using UnityEngine;

namespace CyKim.Scroller.Tests
{
    /// <summary>테스트용 셀 뷰. 바인딩된 데이터와 호출 횟수만 기록한다.</summary>
    public class TestCellView : CyScrollerCellView
    {
        public int BoundData = -1;

        /// <summary>델리게이트가 바인딩하면서 읽은 <see cref="CyScrollerCellView.BindVersion"/> (비동기 로드가 캡처하는 값).</summary>
        public int BoundVersion = -1;

        /// <summary>ID를 주는 델리게이트가 바인딩하면서 읽은 <see cref="CyScrollerCellView.ItemId"/> (ID가 없으면 -1).</summary>
        public long BoundItemId = -1;

        public int RefreshCount;
        public int RecycledCount;
        public int BecameVisibleCount;
        public int BecameHiddenCount;
        public int PositionCallCount;
        public float LastNormalizedOffset = float.NaN;
        public int LastPositionFrame = -1;

        /// <summary>증분 변경으로 인덱스만 바뀐 횟수와 마지막으로 받은 이전 인덱스.</summary>
        public int DataIndexChangedCount;
        public int LastPreviousDataIndex = -1;

        /// <summary>바뀌기 직전 셀 위치(RectTransform offsetMax.y). OnDataIndexChanged 뒤에 재배치되는지 보는 데 쓴다.</summary>
        public float OffsetMaxYAtIndexChange = float.NaN;

        /// <summary>OnRecycled 안에서 불린다. 직렬화되지 않으므로 Instantiate한 뷰에는 따로 넣는다.</summary>
        public System.Action<TestCellView> RecycledHook;

        public override void RefreshCellView()
        {
            RefreshCount++;
        }

        public override void OnRecycled()
        {
            RecycledCount++;
            RecycledHook?.Invoke(this);
        }

        // 테스트 어셈블리는 InternalsVisibleTo로 internal 접근이 있어 protected internal로 재정의한다 (일반 사용자 어셈블리는 protected).
        protected internal override void OnBecameVisible()
        {
            BecameVisibleCount++;
        }

        protected internal override void OnBecameHidden()
        {
            BecameHiddenCount++;
        }

        protected internal override void OnDataIndexChanged(int previousDataIndex)
        {
            DataIndexChangedCount++;
            LastPreviousDataIndex = previousDataIndex;
            OffsetMaxYAtIndexChange = RectTransform.offsetMax.y;
        }

        protected internal override void OnViewportPositionChanged(float normalizedOffset)
        {
            PositionCallCount++;
            LastNormalizedOffset = normalizedOffset;
            LastPositionFrame = Time.frameCount;
        }
    }
}
