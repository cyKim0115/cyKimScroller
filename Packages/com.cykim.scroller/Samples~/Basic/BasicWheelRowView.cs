using UnityEngine;
using UnityEngine.UI;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 휠 피커 한 행. 위치 훅으로 원통에 감긴 것처럼 그린다: 가운데에서 멀수록 X축으로 기울고(높이가 줄어듦)
    /// 가운데 쪽으로 모이며 작고 흐려진다. 셀 루트는 스크롤러가 배치하므로 Visual 자식만 바꾼다.
    /// </summary>
    public class BasicWheelRowView : CyScrollerCellView
    {
        // 뷰포트 위·아래 가장자리에서의 기울기. 미리보기 구간 행도 90도를 넘어 뒤집히지 않게 거리를 자른다.
        private const float EDGE_ANGLE = 64f;
        private const float MAX_DISTANCE = 1.2f;
        private const float EDGE_SCALE = 0.72f;
        private const float EDGE_ALPHA = 0.18f;

        [SerializeField] private RectTransform _visual;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Text _label;

        /// <summary>코드로 만든 템플릿에 참조를 넣는다. 프리팹을 쓰면 인스펙터에서 연결한다.</summary>
        public void Setup(RectTransform visual, CanvasGroup group, Text label)
        {
            _visual = visual;
            _group = group;
            _label = label;
        }

        public void SetText(string text)
        {
            _label.text = text;
        }

        protected override void OnViewportPositionChanged(float normalizedOffset)
        {
            // -1 = 뷰포트 위 가장자리, 0 = 가운데, 1 = 아래 가장자리.
            float signed = Mathf.Clamp((normalizedOffset - 0.5f) * 2f, -MAX_DISTANCE, MAX_DISTANCE);
            float distance = Mathf.Min(Mathf.Abs(signed), 1f);

            // 원통 투영: 가운데에서 잰 호 길이를 각도로 보고, 화면 위치는 반지름 × sin(각도)로 모은다.
            // 행 높이는 X축 회전(cos)으로 줄어들어 이웃 행과 겹치거나 벌어지지 않는다.
            float edgeRadians = EDGE_ANGLE * Mathf.Deg2Rad;
            float halfViewport = Scroller != null ? Scroller.ScrollRectSize * 0.5f : 0f;
            float pullToCenter = halfViewport * (signed - Mathf.Sin(signed * edgeRadians) / edgeRadians);

            _visual.anchoredPosition = new Vector2(0f, pullToCenter);
            _visual.localRotation = Quaternion.Euler(signed * EDGE_ANGLE, 0f, 0f);

            float scale = Mathf.Lerp(1f, EDGE_SCALE, distance);
            _visual.localScale = new Vector3(scale, scale, 1f);
            _group.alpha = Mathf.Lerp(1f, EDGE_ALPHA, distance);
        }

        public override void OnRecycled()
        {
            // 재사용될 때 이전 자리의 효과가 남지 않게 되돌린다. 다시 활성화되면 바로 새 위치를 받는다.
            _visual.anchoredPosition = Vector2.zero;
            _visual.localRotation = Quaternion.identity;
            _visual.localScale = Vector3.one;
            _group.alpha = 1f;
        }
    }
}
