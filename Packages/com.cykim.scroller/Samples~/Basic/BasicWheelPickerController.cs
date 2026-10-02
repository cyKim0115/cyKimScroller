using System;
using UnityEngine;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 세로 루프 + 가운데 스냅 휠 피커 (0~59 같은 숫자 목록). 행 높이는 고정이고, 행 모양은
    /// <see cref="BasicWheelRowView"/>가 위치 훅으로 그린다. 스냅이 끝나면 가운데 값을 알린다.
    /// </summary>
    public class BasicWheelPickerController : MonoBehaviour, ICyScrollerDelegate
    {
        public const float ROW_HEIGHT = 56f;

        [SerializeField] private CyScroller _scroller;
        [SerializeField] private BasicWheelRowView _rowPrefab;
        [SerializeField, Min(1)] private int _valueCount = 60;
        [SerializeField, Min(0)] private int _initialValue = 30;

        private string[] _labels;
        private int _pendingValue = -1;
        private Action _onJumpComplete;

        /// <summary>마지막으로 가운데에 멈춘 값. 아직 없으면 -1.</summary>
        public int SelectedValue { get; private set; } = -1;

        /// <summary>스냅·점프가 끝나 가운데 값이 정해질 때.</summary>
        public event Action<int> ValueSelected;

        public void Setup(CyScroller scroller, BasicWheelRowView rowPrefab, int valueCount, int initialValue)
        {
            _scroller = scroller;
            _rowPrefab = rowPrefab;
            _valueCount = valueCount;
            _initialValue = initialValue;
        }

        private void Start()
        {
            // 바인딩 때는 미리 만든 문자열을 대입만 한다.
            _labels = new string[_valueCount];
            for (int i = 0; i < _valueCount; i++)
            {
                _labels[i] = i.ToString("00");
            }

            _onJumpComplete = OnJumpComplete;

            _scroller.Loop = true;
            _scroller.Snapping = true;
            _scroller.SnapWatchOffset = 0.5f;
            _scroller.SnapJumpToOffset = 0.5f;
            _scroller.SnapCellCenterOffset = 0.5f;
            _scroller.SnapUseCellSpacing = false;
            _scroller.SnapTweenType = TweenType.EaseOutCubic;
            _scroller.SnapTweenTime = 0.2f;
            _scroller.NotifyCellPositions = true;
            _scroller.CellPositionPivot = 0.5f;
            _scroller.ScrollerSnapped += OnSnapped;
            _scroller.Delegate = this;

            // 다음 프레임 자동 로드를 기다리지 않고 바로 로드해서 첫 값을 가운데에 둔다.
            _scroller.ReloadData();
            int initial = Mathf.Clamp(_initialValue, 0, _valueCount - 1);
            _scroller.JumpToDataIndex(initial, 0.5f, 0.5f, false);
            Select(initial);
        }

        private void OnDestroy()
        {
            if (_scroller != null)
            {
                _scroller.ScrollerSnapped -= OnSnapped;
            }
        }

        /// <summary>무작위 값으로 굴려서 가운데에 맞춘다.</summary>
        public void SpinToRandom()
        {
            _pendingValue = UnityEngine.Random.Range(0, _valueCount);
            _scroller.JumpToDataIndex(_pendingValue, 0.5f, 0.5f, false, TweenType.EaseOutCubic, 0.6f,
                _onJumpComplete, LoopJumpDirection.Forward);
        }

        public int GetNumberOfCells(CyScroller scroller) => _valueCount;

        public float GetCellViewSize(CyScroller scroller, int dataIndex) => ROW_HEIGHT;

        public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
        {
            var view = (BasicWheelRowView)scroller.GetCellView(_rowPrefab);
            view.SetText(_labels[dataIndex]);
            return view;
        }

        private void OnSnapped(CyScroller scroller, int cellIndex, int dataIndex, CyScrollerCellView cellView)
        {
            Select(dataIndex);
        }

        private void OnJumpComplete()
        {
            Select(_pendingValue);
        }

        private void Select(int value)
        {
            SelectedValue = value;
            ValueSelected?.Invoke(value);
        }
    }
}
