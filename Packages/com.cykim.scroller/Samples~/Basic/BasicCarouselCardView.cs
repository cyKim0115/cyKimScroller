using UnityEngine;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 캐러셀 카드. 스크롤러의 위치 훅(<see cref="CyScroller.NotifyCellPositions"/>)으로 뷰포트 가운데에서 멀수록
    /// Visual 자식을 작고 흐리게 그린다. 셀 루트는 스크롤러가 배치하므로 건드리지 않는다.
    /// </summary>
    public class BasicCarouselCardView : BasicCellView
    {
        private const float EDGE_SCALE = 0.72f;
        private const float EDGE_ALPHA = 0.4f;

        [SerializeField] private CanvasGroup _group;

        /// <summary>코드로 만든 템플릿에 참조를 넣는다. 프리팹을 쓰면 인스펙터에서 연결한다.</summary>
        public void SetupCard(CanvasGroup group)
        {
            _group = group;
        }

        protected override void OnViewportPositionChanged(float normalizedOffset)
        {
            // 0 = 가운데, 1 = 뷰포트 가장자리. 가운데 근처에서 빨리 줄어들게 해 가운데 카드가 도드라지게 한다.
            float distance = Mathf.Clamp01(Mathf.Abs(normalizedOffset - 0.5f) * 2f);
            float falloff = 1f - (1f - distance) * (1f - distance);

            float scale = Mathf.Lerp(1f, EDGE_SCALE, falloff);
            Visual.localScale = new Vector3(scale, scale, 1f);
            _group.alpha = Mathf.Lerp(1f, EDGE_ALPHA, falloff);
        }

        public override void OnRecycled()
        {
            base.OnRecycled();
            _group.alpha = 1f;
        }
    }
}
