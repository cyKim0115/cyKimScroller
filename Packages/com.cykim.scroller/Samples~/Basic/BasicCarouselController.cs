using UnityEngine;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 무한 순환 + 가운데 스냅 가로 캐러셀. 뷰포트 가운데에서 멀수록 카드를 작게 그린다.
    /// </summary>
    public class BasicCarouselController : MonoBehaviour, ICyScrollerDelegate
    {
        private const float MIN_SCALE = 0.75f;

        [SerializeField] private CyScroller _scroller;
        [SerializeField] private BasicCellView _cellPrefab;
        [SerializeField, Min(1)] private int _cardCount = 12;
        [SerializeField, Min(1f)] private float _cardWidth = 260f;

        private string[] _labels;
        private Color[] _colors;

        public void Setup(CyScroller scroller, BasicCellView cellPrefab, int cardCount, float cardWidth)
        {
            _scroller = scroller;
            _cellPrefab = cellPrefab;
            _cardCount = cardCount;
            _cardWidth = cardWidth;
        }

        private void Start()
        {
            _labels = new string[_cardCount];
            _colors = new Color[_cardCount];
            for (int i = 0; i < _cardCount; i++)
            {
                _labels[i] = $"Card {i}";
                _colors[i] = Color.HSVToRGB((float)i / _cardCount, 0.55f, 0.9f);
            }

            _scroller.Loop = true;
            _scroller.Snapping = true;
            _scroller.SnapWatchOffset = 0.5f;
            _scroller.SnapJumpToOffset = 0.5f;
            _scroller.SnapCellCenterOffset = 0.5f;
            _scroller.ScrollerScrolled += OnScrolled;
            _scroller.CellViewVisibilityChanged += OnCellVisibilityChanged;
            _scroller.ScrollerSnapped += OnSnapped;
            _scroller.Delegate = this;

            // 다음 프레임 자동 로드를 기다리지 않고 바로 로드해서 첫 카드를 가운데에 둔다.
            _scroller.ReloadData();
            _scroller.JumpToDataIndex(0, 0.5f, 0.5f, false);
        }

        private void OnDestroy()
        {
            if (_scroller != null)
            {
                _scroller.ScrollerScrolled -= OnScrolled;
                _scroller.CellViewVisibilityChanged -= OnCellVisibilityChanged;
                _scroller.ScrollerSnapped -= OnSnapped;
            }
        }

        public void Next()
        {
            int center = CenterDataIndex();
            _scroller.JumpToDataIndex((center + 1) % _cardCount, 0.5f, 0.5f, false, TweenType.EaseOutBack, 0.35f,
                null, LoopJumpDirection.Forward);
        }

        public void Previous()
        {
            int center = CenterDataIndex();
            _scroller.JumpToDataIndex((center - 1 + _cardCount) % _cardCount, 0.5f, 0.5f, false, TweenType.EaseOutBack, 0.35f,
                null, LoopJumpDirection.Backward);
        }

        public int GetNumberOfCells(CyScroller scroller) => _cardCount;

        public float GetCellViewSize(CyScroller scroller, int dataIndex) => _cardWidth;

        public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
        {
            var view = (BasicCellView)scroller.GetCellView(_cellPrefab);
            view.SetData(_labels[dataIndex], _colors[dataIndex]);
            return view;
        }

        private int CenterDataIndex()
        {
            float center = _scroller.ScrollPosition + _scroller.ScrollRectSize * 0.5f;
            return _scroller.GetDataIndexForCellViewIndex(_scroller.GetCellViewIndexAtPosition(center));
        }

        private void OnScrolled(CyScroller scroller, Vector2 normalizedPosition, float scrollPosition)
        {
            var cells = scroller.ActiveCellViews;
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] != null)
                {
                    ApplyScale((BasicCellView)cells[i]);
                }
            }
        }

        private void OnCellVisibilityChanged(CyScrollerCellView cellView)
        {
            if (cellView.Active)
            {
                ApplyScale((BasicCellView)cellView);
            }
        }

        private void OnSnapped(CyScroller scroller, int cellIndex, int dataIndex, CyScrollerCellView cellView)
        {
            Debug.Log($"[BasicCarouselController] Snapped to card {dataIndex}");
        }

        private void ApplyScale(BasicCellView view)
        {
            float halfViewport = _scroller.ScrollRectSize * 0.5f;
            float viewportCenter = _scroller.ScrollPosition + halfViewport;
            float cellCenter = _scroller.GetScrollPositionForCellViewIndex(view.CellIndex) + _cardWidth * 0.5f;
            float t = halfViewport > 0f ? Mathf.Clamp01(Mathf.Abs(cellCenter - viewportCenter) / halfViewport) : 0f;
            float scale = Mathf.Lerp(1f, MIN_SCALE, t);
            view.Visual.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
