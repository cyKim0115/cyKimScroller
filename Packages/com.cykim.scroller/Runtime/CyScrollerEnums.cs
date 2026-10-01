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

    /// <summary><see cref="CyScroller.ScrollIntoView"/>에서 셀을 뷰포트 어디에 맞출지.</summary>
    public enum ScrollAlign
    {
        /// <summary>셀 시작을 뷰포트 시작 + 여백에 맞춘다.</summary>
        Start = 0,

        /// <summary>셀 가운데를 뷰포트 가운데에 맞춘다. 여백은 쓰지 않는다.</summary>
        Center = 1,

        /// <summary>셀 끝을 뷰포트 끝 − 여백에 맞춘다.</summary>
        End = 2,

        /// <summary>
        /// 이미 완전히 보이면(여백은 콘텐츠 끝까지만) 움직이지 않는다. 아니면 덜 움직이는 쪽으로 맞춘다 (앞쪽에 걸리면 Start, 뒤쪽이면 End).
        /// 셀이 여백까지 합쳐 뷰포트보다 크면 Start. 목표가 지금 위치라 더 움직일 수 없을 때도 움직이지 않는다.
        /// </summary>
        Nearest = 3,
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
