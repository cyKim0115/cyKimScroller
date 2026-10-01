using UnityEngine;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 높이가 제각각인 세로 목록. 라벨·색·높이는 한 번만 만들어 두고 바인딩 때는 대입만 한다 (스크롤 중 GC 없음).
    /// </summary>
    public class BasicListController : MonoBehaviour, ICyScrollerDelegate
    {
        [SerializeField] private CyScroller _scroller;
        [SerializeField] private BasicCellView _cellPrefab;
        [SerializeField, Min(0)] private int _itemCount = 500;

        private float[] _heights;
        private string[] _labels;
        private Color[] _colors;

        public CyScroller Scroller => _scroller;
        public int ItemCount => _itemCount;

        public void Setup(CyScroller scroller, BasicCellView cellPrefab, int itemCount)
        {
            _scroller = scroller;
            _cellPrefab = cellPrefab;
            _itemCount = itemCount;
        }

        private void Start()
        {
            BuildData();
            _scroller.Delegate = this;
        }

        private void BuildData()
        {
            _heights = new float[_itemCount];
            _labels = new string[_itemCount];
            _colors = new Color[_itemCount];

            for (int i = 0; i < _itemCount; i++)
            {
                // 60~180 사이 높이를 결정적으로 섞는다.
                _heights[i] = 60f + (i * 37 % 5) * 30f;
                _labels[i] = $"Item #{i}  (height {_heights[i]:0})";
                _colors[i] = Color.HSVToRGB(i * 0.013f % 1f, 0.35f, 0.95f);
            }
        }

        public void JumpTo(int dataIndex)
        {
            _scroller.JumpToDataIndex(dataIndex, 0.5f, 0.5f, true, TweenType.EaseInOutCubic, 0.4f);
        }

        public int GetNumberOfCells(CyScroller scroller) => _itemCount;

        public float GetCellViewSize(CyScroller scroller, int dataIndex) => _heights[dataIndex];

        public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
        {
            var view = (BasicCellView)scroller.GetCellView(_cellPrefab);
            view.SetData(_labels[dataIndex], _colors[dataIndex]);
            return view;
        }
    }
}
