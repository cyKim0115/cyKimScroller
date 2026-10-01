using CyKim.Scroller;
using UnityEngine;
using UnityEngine.UI;

namespace CyKim.Scroller.Dev
{
    /// <summary>스트레스 씬용 셀. 미리 만든 문자열만 대입해 바인딩 중 GC를 만들지 않는다.</summary>
    public class DevStressCellView : CyScrollerCellView
    {
        [SerializeField] private Image _background;
        [SerializeField] private Text _label;

        public void Setup(Image background, Text label)
        {
            _background = background;
            _label = label;
        }

        public void SetData(string label, Color color)
        {
            _label.text = label;
            _background.color = color;
        }
    }
}
