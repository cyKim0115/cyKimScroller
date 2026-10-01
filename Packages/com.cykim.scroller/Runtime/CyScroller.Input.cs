using UnityEngine.EventSystems;

namespace CyKim.Scroller
{
    // ScrollRect와 같은 GameObject에 있으므로 같은 포인터 이벤트를 함께 받는다.
    // 입력 처리는 ScrollRect에 맡기고, 여기서는 드래그·누름 상태, 트윈 중단, 스냅 준비만 기록한다.
    // ScrollRect가 꺼져 있으면(스크롤 잠금) 아무것도 하지 않는다.
    public partial class CyScroller :
        IInitializePotentialDragHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IScrollHandler
    {
        private bool _dragging;
        private PointerEventData _dragEventData;

        // 스크롤 영역을 누른 포인터. 손을 떼면 eligibleForClick이 false가 된다.
        private PointerEventData _pressEventData;

        private bool AcceptsInput => _initialized && _scrollRect.IsActive();

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !AcceptsInput)
            {
                return;
            }

            _pressEventData = eventData;

            if (_interruptTweenOnPointerDown && _tweening)
            {
                CancelTween();
                _alignmentActive = false;

                // 탭으로 스냅을 멈췄으면 손을 뗀 뒤 다시 스냅한다 (셀 사이에 멈춰 있지 않게).
                _snapArmed = _snapping;
                _wheelIdleTime = WHEEL_SNAP_DELAY;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !AcceptsInput)
            {
                return;
            }

            // 드래그는 항상 트윈보다 우선한다. 사용자가 움직이므로 점프·스냅 정렬 유지도 끝낸다.
            CancelTween();
            _alignmentActive = false;
            _dragging = true;
            _dragEventData = eventData;
            _pressEventData = eventData;
            _wheelIdleTime = WHEEL_SNAP_DELAY;
            _snapArmed = _snapping;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_dragging)
            {
                _dragEventData = eventData;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !_dragging)
            {
                return;
            }

            _dragging = false;
            _dragEventData = null;

            // 드래그 중 순환 보정을 꺼 두었으면 관성이 없어도 놓는 순간 가운데 세트로 되돌린다.
            if (_hasLoaded && !_loopWhileDragging && _layout.IsLoop)
            {
                RecenterLoopIfNeeded();
                UpdateActiveRange();
            }
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!AcceptsInput)
            {
                return;
            }

            // 휠은 ScrollRect가 위치를 직접 바꾸므로 트윈과 싸우지 않게 멈춘다.
            CancelTween();
            _alignmentActive = false;

            if (_snapping)
            {
                _snapArmed = true;
                _wheelIdleTime = 0f;
            }
        }
    }
}
