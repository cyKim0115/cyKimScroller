using System;
using UnityEngine;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 무한 순환 + 가운데 스냅 가로 캐러셀. 카드 크기·투명도는 카드 셀 뷰(<see cref="BasicCarouselCardView"/>)가
    /// 스크롤러의 위치 훅으로 직접 그리므로 컨트롤러는 스크롤 이벤트를 받지 않는다.
    /// </summary>
    public class BasicCarouselController : MonoBehaviour, ICyScrollerDelegate
    {
        [SerializeField] private CyScroller _scroller;
        [SerializeField] private BasicCarouselCardView _cardPrefab;
        [SerializeField, Min(1)] private int _cardCount = 12;
        [SerializeField, Min(1f)] private float _cardWidth = 260f;

        private string[] _labels;
        private Color[] _colors;
        private int _pendingCard = -1;
        private Action _onJumpComplete;

        /// <summary>스냅·점프가 끝나 가운데 카드가 정해질 때.</summary>
        public event Action<int> CardCentered;

        public void Setup(CyScroller scroller, BasicCarouselCardView cardPrefab, int cardCount, float cardWidth)
        {
            _scroller = scroller;
            _cardPrefab = cardPrefab;
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
                _colors[i] = Color.HSVToRGB((float)i / _cardCount, 0.45f, 0.95f);
            }

            _onJumpComplete = OnJumpComplete;

            _scroller.Loop = true;
            _scroller.Snapping = true;
            _scroller.SnapWatchOffset = 0.5f;
            _scroller.SnapJumpToOffset = 0.5f;
            _scroller.SnapCellCenterOffset = 0.5f;

            // 카드 가운데가 뷰포트 어디에 있는지 받는다 (0.5 = 가운데).
            _scroller.NotifyCellPositions = true;
            _scroller.CellPositionPivot = 0.5f;
            _scroller.ScrollerSnapped += OnSnapped;
            _scroller.Delegate = this;

            // 다음 프레임 자동 로드를 기다리지 않고 바로 로드해서 첫 카드를 가운데에 둔다.
            _scroller.ReloadData();
            _scroller.JumpToDataIndex(0, 0.5f, 0.5f, false);
            CardCentered?.Invoke(0);
        }

        private void OnDestroy()
        {
            if (_scroller != null)
            {
                _scroller.ScrollerSnapped -= OnSnapped;
            }
        }

        public void Next()
        {
            _pendingCard = (CenterDataIndex() + 1) % _cardCount;
            _scroller.JumpToDataIndex(_pendingCard, 0.5f, 0.5f, false, TweenType.EaseOutBack, 0.35f,
                _onJumpComplete, LoopJumpDirection.Forward);
        }

        public void Previous()
        {
            _pendingCard = (CenterDataIndex() - 1 + _cardCount) % _cardCount;
            _scroller.JumpToDataIndex(_pendingCard, 0.5f, 0.5f, false, TweenType.EaseOutBack, 0.35f,
                _onJumpComplete, LoopJumpDirection.Backward);
        }

        public int GetNumberOfCells(CyScroller scroller) => _cardCount;

        public float GetCellViewSize(CyScroller scroller, int dataIndex) => _cardWidth;

        public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
        {
            var view = (BasicCarouselCardView)scroller.GetCellView(_cardPrefab);
            view.SetData(_labels[dataIndex], _colors[dataIndex]);
            return view;
        }

        private int CenterDataIndex()
        {
            float center = _scroller.ScrollPosition + _scroller.ScrollRectSize * 0.5f;
            return _scroller.GetDataIndexForCellViewIndex(_scroller.GetCellViewIndexAtPosition(center));
        }

        private void OnSnapped(CyScroller scroller, int cellIndex, int dataIndex, CyScrollerCellView cellView)
        {
            CardCentered?.Invoke(dataIndex);
        }

        private void OnJumpComplete()
        {
            CardCentered?.Invoke(_pendingCard);
        }
    }
}
