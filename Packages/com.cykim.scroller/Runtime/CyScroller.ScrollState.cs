using UnityEngine;

namespace CyKim.Scroller
{
    // 정착·고속 스크롤 상태: LateUpdate 끝에서 스크롤 속도로 판단하고, 바뀌었을 때만 알린다. 할당 없음.
    public partial class CyScroller
    {
        // 탄성으로 되돌아오는 중인지 볼 때 허용할 범위 밖 거리(px).
        private const float SETTLE_OUT_OF_BOUNDS_TOLERANCE = 0.5f;

        // 마지막으로 알린 상태. 처음은 정착한 상태로 둬 첫 로드 직후에는 정착 이벤트가 오지 않는다.
        private bool _settled = true;
        private bool _fastScrolling;

        // 이번 트윈 걸음의 이동 속도(px/s). 트윈은 ScrollRect 속도를 쓰지 않으므로 걸음마다 잰다 (UpdateTween). 트윈을 시작·끝낼 때 0으로 돌린다.
        private float _tweenStepSpeed;

        /// <summary>
        /// 움직이던 스크롤러가 정착했을 때 한 번 (드래그 중이 아니고, 트윈·스냅 대기가 없고, 스크롤 속도가 <see cref="SettleVelocityThreshold"/> 이하).
        /// LateUpdate 끝에서 바뀐 것을 알린다. 첫 로드 직후에는 오지 않는다. 이 이벤트 뒤에도 정착해 있으면 활성 셀마다
        /// <see cref="CyScrollerCellView.OnScrollerSettled"/>를 부른다 (핸들러가 다시 움직이게 했으면 부르지 않는다).
        /// </summary>
        public event ScrollerSettledHandler ScrollerSettled;

        /// <summary>
        /// 고속 스크롤 상태(<see cref="IsFastScrolling"/>)가 바뀔 때. LateUpdate 끝에서 바뀐 것을 알린다.
        /// </summary>
        public event ScrollerFastScrollingChangedHandler ScrollerFastScrollingChanged;

        /// <summary>
        /// 지금 정착해 있는지: 로드 전이거나, 드래그 중이 아니고 트윈·스냅 대기가 없고 관성 속도가 <see cref="SettleVelocityThreshold"/> 이하이며
        /// 가장자리 너머에서 탄성으로 되돌아오는 중이 아니다. 바로 계산한 값이다 (<see cref="ScrollerSettled"/>는 LateUpdate에서 바뀐 것을 알린다).
        /// </summary>
        /// <remarks>
        /// <see cref="IsScrolling"/>은 드래그·관성(트윈 제외), <see cref="IsTweening"/>은 점프·스냅 트윈만 본다.
        /// 이 값은 둘 다 끝나고 스냅 대기도 없을 때 true이고, 관성이 아주 느려진 끝(임계값 이하)도 정착으로 본다.
        /// 휠·스크롤바 이동은 ScrollRect가 속도 없이 위치만 바꾸므로 정착으로 본다(스냅을 켜면 휠 뒤 스냅 대기 동안은 정착이 아니다).
        /// </remarks>
        public bool IsSettled => !_hasLoaded || IsSettledAt(Mathf.Abs(LinearVelocity));

        /// <summary>
        /// 빠르게 스크롤하는 중인지. 스크롤 속도(트윈이면 트윈 이동 속도, 아니면 관성 속도)가 뷰포트 길이 × <see cref="FastScrollEnterThreshold"/> 이상이 되면 켜지고,
        /// 뷰포트 길이 × <see cref="FastScrollExitThreshold"/> 미만으로 떨어지면 꺼진다 (경계에서 깜빡이지 않게 두 값을 따로 둔다).
        /// LateUpdate 끝에서 갱신하고, 스크롤러가 꺼져 있는 동안에는 갱신하지 않는다.
        /// </summary>
        /// <remarks>
        /// 빠르게 지나가는 셀의 무거운 로드(이미지·동영상)를 미루는 데 쓴다. 관성 속도는 ScrollRect가 재므로, ScrollRect의 inertia를 끄면 드래그 중 속도는 0이다.
        /// </remarks>
        public bool IsFastScrolling => _fastScrolling;

        /// <summary>정착으로 볼 스크롤 속도 상한(px/s). 0이면 완전히 멈춰야 정착이다.</summary>
        public float SettleVelocityThreshold
        {
            get => _settleVelocityThreshold;
            set => _settleVelocityThreshold = Mathf.Max(0f, value);
        }

        /// <summary>고속 스크롤로 들어가는 속도. 뷰포트 길이의 배수(/s)라 해상도에 덜 민감하다. 0이면 고속 스크롤을 끈다.</summary>
        public float FastScrollEnterThreshold
        {
            get => _fastScrollEnterThreshold;
            set => _fastScrollEnterThreshold = Mathf.Max(0f, value);
        }

        /// <summary>
        /// 고속 스크롤에서 나오는 속도. 뷰포트 길이의 배수(/s). <see cref="FastScrollEnterThreshold"/>보다 크면 그 값을 쓰고, 0이면 완전히 멈출 때 나온다.
        /// </summary>
        public float FastScrollExitThreshold
        {
            get => _fastScrollExitThreshold;
            set => _fastScrollExitThreshold = Mathf.Max(0f, value);
        }

        /// <summary>LateUpdate가 정착·고속 스크롤 판단에 쓰는 스크롤 속도(px/s). 트윈 중이면 이번 걸음의 이동 속도, 아니면 관성 속도.</summary>
        internal float ScrollSpeed => _tweening ? _tweenStepSpeed : Mathf.Abs(LinearVelocity);

        /// <summary>주어진 스크롤 속도에서 정착해 있는지 (로드 여부는 보지 않는다). 할당 없음.</summary>
        private bool IsSettledAt(float speed)
        {
            bool snapPending = _snapping && _snapArmed && !_hasPendingAnchor;
            return !_dragging && !_tweening && !snapPending && speed <= _settleVelocityThreshold && !IsReturningFromOverscroll();
        }

        /// <summary>
        /// 가장자리 너머(Elastic)에서 되돌아오는 중인지. 되돌아오는 꼭짓점에서 속도가 0을 지나도 정착으로 보지 않게 한다 (그 뒤 범위 안에서 한 번만 정착).
        /// 루프이거나 Elastic이 아니면 false다 (Unrestricted는 범위 밖에 머물 수 있다).
        /// </summary>
        private bool IsReturningFromOverscroll()
        {
            if (_layout.IsLoop || _scrollRect == null || _scrollRect.movementType != UnityEngine.UI.ScrollRect.MovementType.Elastic)
            {
                return false;
            }

            float position = ReadPosition(_appliedVertical);
            return position < -SETTLE_OUT_OF_BOUNDS_TOLERANCE || position > ScrollSize + SETTLE_OUT_OF_BOUNDS_TOLERANCE;
        }

        /// <summary>
        /// 고속 스크롤·정착 상태를 speed(px/s)로 판단하고 바뀌었으면 알린다. LateUpdate 끝에서 부른다 (테스트가 직접 진행시킬 수 있게 internal). 할당 없음.
        /// 정착하면 <see cref="ScrollerSettled"/> 뒤에 아직 정착해 있을 때만 활성 셀마다 <see cref="CyScrollerCellView.OnScrollerSettled"/>를 부른다.
        /// </summary>
        internal void UpdateScrollState(float speed)
        {
            if (!_hasLoaded)
            {
                return;
            }

            float viewport = ScrollRectSize;
            bool fast;
            if (viewport <= 0f || _fastScrollEnterThreshold <= 0f)
            {
                fast = false;
            }
            else if (_fastScrolling)
            {
                // 나오는 값이 0이면 완전히 멈출 때 나온다.
                fast = speed > 0f && speed >= Mathf.Min(_fastScrollExitThreshold, _fastScrollEnterThreshold) * viewport;
            }
            else
            {
                fast = speed >= _fastScrollEnterThreshold * viewport;
            }

            if (fast != _fastScrolling)
            {
                _fastScrolling = fast;
                ScrollerFastScrollingChanged?.Invoke(this, fast);
            }

            bool settled = IsSettledAt(speed);
            if (settled == _settled)
            {
                return;
            }

            _settled = settled;
            if (!settled)
            {
                return;
            }

            ScrollerSettled?.Invoke(this);

            // 핸들러가 다시 움직이게 했으면(트윈 시작 등) 셀에는 알리지 않는다. 다음 정착 때 알린다.
            if (_settled && IsSettledAt(Mathf.Abs(LinearVelocity)))
            {
                NotifyCellsSettled();
            }
        }

        /// <summary>
        /// 활성 셀마다 <see cref="CyScrollerCellView.OnScrollerSettled"/>를 부른다. 사용자 코드는 범위 갱신 중으로 돌려 활성 목록이 바뀌지 않게 하고
        /// (그 사이 리로드·정리·범위 갱신은 미룬다), 끝난 뒤 콘텐츠가 옮겨졌거나 미룬 작업이 있으면 범위를 맞춘다.
        /// </summary>
        private void NotifyCellsSettled()
        {
            bool nested = _inRangeUpdate;
            float position = ScrollPosition;
            _inRangeUpdate = true;
            try
            {
                for (int i = 0; i < _activeCells.Count; i++)
                {
                    CyScrollerCellView cell = _activeCells[i];
                    if (cell != null)
                    {
                        cell.OnScrollerSettled();
                    }
                }
            }
            finally
            {
                _inRangeUpdate = nested;
            }

            if (!nested && (HasMovedFrom(position) || HasPendingWork || _recenterDeferred || _snapEventPending))
            {
                UpdateActiveRange();
            }
        }
    }
}
