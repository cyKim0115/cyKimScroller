using System.Collections.Generic;
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

        /// <summary>RefreshCellView(int) 호출 수와 마지막으로 받은 changeMask. 기본 구현을 거쳐 RefreshCount도 함께 는다.</summary>
        public int MaskedRefreshCount;
        public int LastChangeMask;

        /// <summary>RefreshCellView(int) 호출 하나: 받을 때 바인딩돼 있던 항목(<see cref="BoundData"/>)과 changeMask.</summary>
        public struct RefreshRecord
        {
            public int Item;
            public int Mask;
        }

        /// <summary>
        /// RefreshCellView(int)를 받을 때마다 쌓인다. 바인딩·회수 때 지우지 않으므로, 회수된 셀이 곧바로 다른 항목에 다시 쓰여
        /// <see cref="MaskedRefreshCount"/>가 0으로 돌아가도 그 전에 어느 항목으로 받았는지 확인할 수 있다 (직렬화하지 않는다).
        /// 용량은 부분 갱신 GC 0 테스트가 측정하는 동안 늘지 않도록 넉넉히 잡는다.
        /// </summary>
        public readonly List<RefreshRecord> RefreshHistory = new List<RefreshRecord>(64);

        /// <summary>RefreshCellView(int) 안에서 불린다 (셀 뷰 안의 사용자 코드를 흉내 낸다). 직렬화되지 않으므로 Instantiate한 뷰에는 따로 넣는다.</summary>
        public System.Action<TestCellView, int> RefreshHook;

        public int RecycledCount;
        public int SettledCount;
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

        public override void RefreshCellView(int changeMask)
        {
            MaskedRefreshCount++;
            LastChangeMask = changeMask;
            RefreshHistory.Add(new RefreshRecord { Item = BoundData, Mask = changeMask });
            RefreshHook?.Invoke(this, changeMask);

            // 기본 구현은 무인자 RefreshCellView를 부른다 (그 연결도 확인한다).
            base.RefreshCellView(changeMask);
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

        protected internal override void OnScrollerSettled()
        {
            SettledCount++;
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
