namespace CyKim.Scroller
{
    /// <summary>스크롤 축.</summary>
    public enum ScrollDirection
    {
        Vertical = 0,
        Horizontal = 1,
    }

    /// <summary>스크롤 축 스크롤바를 언제 보일지.</summary>
    public enum ScrollbarVisibility
    {
        /// <summary>콘텐츠가 뷰포트보다 클 때만 보인다.</summary>
        OnlyIfNeeded = 0,
        Always = 1,
        Never = 2,
    }

    /// <summary>루프 모드에서 같은 데이터가 여러 슬롯에 있을 때 점프할 사본을 고르는 기준.</summary>
    public enum LoopJumpDirection
    {
        /// <summary>현재 위치에서 가장 가까운 사본.</summary>
        Closest = 0,

        /// <summary>현재 위치보다 앞(위·왼쪽) 방향 사본.</summary>
        Backward = 1,

        /// <summary>현재 위치보다 뒤(아래·오른쪽) 방향 사본.</summary>
        Forward = 2,
    }

    /// <summary>
    /// 점프·스냅 트윈 곡선. 직렬화 값이 바뀌지 않도록 숫자를 고정한다.
    /// </summary>
    public enum TweenType
    {
        Immediate = 0,
        Linear = 1,

        EaseInSine = 10,
        EaseOutSine = 11,
        EaseInOutSine = 12,

        EaseInQuad = 20,
        EaseOutQuad = 21,
        EaseInOutQuad = 22,

        EaseInCubic = 30,
        EaseOutCubic = 31,
        EaseInOutCubic = 32,

        EaseInQuart = 40,
        EaseOutQuart = 41,
        EaseInOutQuart = 42,

        EaseInQuint = 50,
        EaseOutQuint = 51,
        EaseInOutQuint = 52,

        EaseInExpo = 60,
        EaseOutExpo = 61,
        EaseInOutExpo = 62,

        EaseInCirc = 70,
        EaseOutCirc = 71,
        EaseInOutCirc = 72,

        EaseInBack = 80,
        EaseOutBack = 81,
        EaseInOutBack = 82,

        EaseInElastic = 90,
        EaseOutElastic = 91,
        EaseInOutElastic = 92,

        EaseInBounce = 100,
        EaseOutBounce = 101,
        EaseInOutBounce = 102,

        /// <summary><see cref="CyScroller.CustomTweenCurve"/>를 쓴다.</summary>
        Custom = 1000,
    }
}
