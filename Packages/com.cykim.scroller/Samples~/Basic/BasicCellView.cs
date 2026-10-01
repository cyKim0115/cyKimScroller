using UnityEngine;
using UnityEngine.UI;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>배경 이미지와 텍스트 하나를 가진 샘플 셀.</summary>
    public class BasicCellView : CyScrollerCellView
    {
        [SerializeField] private RectTransform _visual;
        [SerializeField] private Image _background;
        [SerializeField] private Text _label;

        /// <summary>시각 효과(스케일 등)를 줄 자식. 루트는 스크롤러가 배치하므로 건드리지 않는다.</summary>
        public RectTransform Visual => _visual;

        /// <summary>코드로 만든 템플릿에 참조를 넣는다. 프리팹을 쓰면 인스펙터에서 연결한다.</summary>
        public void Setup(RectTransform visual, Image background, Text label)
        {
            _visual = visual;
            _background = background;
            _label = label;
        }

        public void SetData(string text, Color color)
        {
            _label.text = text;
            _background.color = color;
        }

        public override void OnRecycled()
        {
            // 재사용될 때 이전 데이터의 시각 효과가 남지 않게 되돌린다.
            _visual.localScale = Vector3.one;
        }
    }
}
