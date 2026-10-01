using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CyKim.Scroller
{
    // 점프·트윈·스냅·루프 순환 보정·정렬 유지.
    public partial class CyScroller
    {
        // 휠 스크롤이 멈춘 뒤 스냅까지 기다리는 시간. 휠 틱마다 스냅이 시작되지 않게 한다.
        private const float WHEEL_SNAP_DELAY = 0.15f;

        // 루프 점프 사본을 고를 때 부동소수 오차를 흡수하는 여유 (사이클 비율).
        private const float LOOP_SET_EPSILON = 0.0001f;

        // 이 거리보다 크게 위치가 달라지면 사용자가 직접 움직인 것으로 본다.
        private const float ALIGNMENT_TOLERANCE = 0.5f;

        private bool _tweening;
        private float _tweenFrom;
        private float _tweenTo;
        private float _tweenDuration;
        private float _tweenElapsed;
        private TweenType _tweenType;
        private Action _tweenComplete;

        private bool _snapArmed;
        private bool _snapPending;
        private int _snapSlot = -1;
        private float _wheelIdleTime = WHEEL_SNAP_DELAY;

        private bool _ignoreLoopJump;

        // 점프·스냅으로 맞춘 정렬. 사용자가 움직이기 전까지 뷰포트 크기가 바뀌어도 다시 맞춘다.
        private bool _alignmentActive;
        private int _alignSlot;
        private float _alignScrollerOffset;
        private float _alignCellOffset;
        private bool _alignUseSpacing;
        private float _alignedPosition;

        /// <summary>
        /// 데이터 인덱스로 이동한다. 이동 뒤에는 사용자가 직접 스크롤하기 전까지 뷰포트 크기가 바뀌어도 같은 정렬을 유지한다.
        /// 드래그 중에 부르면 그 드래그를 끝내고 이동한다 (코드로 요청한 이동이 우선).
        /// </summary>
        /// <param name="dataIndex">이동할 데이터. 범위 밖이면 잘린다.</param>
        /// <param name="scrollerOffset">맞출 뷰포트 지점. 0 = 앞, 0.5 = 가운데, 1 = 뒤.</param>
        /// <param name="cellOffset">뷰포트 지점에 맞출 셀 안의 지점. 0 = 앞, 0.5 = 가운데, 1 = 뒤.</param>
        /// <param name="useSpacing">셀 앞뒤 간격의 절반씩을 셀 영역에 포함해 계산한다.</param>
        /// <param name="tweenType">이동 곡선. <see cref="TweenType.Immediate"/>면 바로 이동한다.</param>
        /// <param name="tweenTime">이동 시간(초, unscaled).</param>
        /// <param name="jumpComplete">이동이 끝나면 호출된다. 사용자 입력·<see cref="InterruptTween"/>으로 중단되면 호출되지 않는다.</param>
        /// <param name="loopJumpDirection">루프 모드에서 어느 사본으로 갈지.</param>
        public void JumpToDataIndex(
            int dataIndex,
            float scrollerOffset = 0f,
            float cellOffset = 0f,
            bool useSpacing = true,
            TweenType tweenType = TweenType.Immediate,
            float tweenTime = 0f,
            Action jumpComplete = null,
            LoopJumpDirection loopJumpDirection = LoopJumpDirection.Closest)
        {
            if (!EnsureInitialized())
            {
                return;
            }

            if (!_hasLoaded && !_reloadPending)
            {
                ReloadData();
            }

            FlushPendingWork();

            if (_layout.DataCount == 0)
            {
                jumpComplete?.Invoke();
                return;
            }

            EndDragForProgrammaticMove();
            int slot = ResolveJumpSlot(dataIndex, scrollerOffset, cellOffset, useSpacing, loopJumpDirection);
            _snapArmed = false;
            StartAlignedTween(slot, scrollerOffset, cellOffset, useSpacing, tweenType, tweenTime, jumpComplete, false);
        }

        /// <summary>현재 위치에서 가장 가까운 셀로 스냅 설정에 따라 이동한다. 스냅이 꺼져 있어도 동작한다.</summary>
        public void Snap()
        {
            if (!_hasLoaded || _layout.SlotCount == 0)
            {
                return;
            }

            EndDragForProgrammaticMove();
            _snapArmed = false;
            float watchPosition = ScrollPosition + _snapWatchOffset * ScrollRectSize;
            int slot = _layout.GetNearestSlot(watchPosition);
            StartAlignedTween(slot, _snapJumpToOffset, _snapCellCenterOffset, _snapUseCellSpacing,
                _snapTweenType, _snapTweenTime, null, true);
        }

        /// <summary>진행 중인 트윈을 멈춘다. 완료 콜백과 스냅 이벤트는 호출되지 않는다.</summary>
        public void InterruptTween()
        {
            CancelTween();
            _alignmentActive = false;
        }

        /// <summary>루프를 켜고 끈다.</summary>
        public void ToggleLoop()
        {
            Loop = !_loop;
        }

        /// <summary>
        /// 루프 순환 보정(가운데 세트로 순간이동)을 잠시 막는다. 외부에서 위치를 직접 애니메이션할 때 쓴다.
        /// </summary>
        public void IgnoreLoopJump(bool ignore)
        {
            _ignoreLoopJump = ignore;
            if (!ignore && _hasLoaded)
            {
                RecenterLoopIfNeeded();
                UpdateActiveRange();
            }
        }

        /// <summary>해당 데이터로 점프했을 때의 스크롤 위치. 이동하지 않고 계산만 한다.</summary>
        public float GetJumpTargetPosition(
            int dataIndex,
            float scrollerOffset,
            float cellOffset,
            bool useSpacing,
            LoopJumpDirection loopJumpDirection = LoopJumpDirection.Closest)
        {
            if (_layout.DataCount == 0)
            {
                return 0f;
            }

            int slot = ResolveJumpSlot(dataIndex, scrollerOffset, cellOffset, useSpacing, loopJumpDirection);
            return Mathf.Clamp(GetJumpPositionForSlot(slot, scrollerOffset, cellOffset, useSpacing), 0f, ScrollSize);
        }

        /// <summary>점프할 슬롯. 루프면 방향 규칙에 맞고 콘텐츠 안에 들어가는 사본을 고른다.</summary>
        private int ResolveJumpSlot(int dataIndex, float scrollerOffset, float cellOffset, bool useSpacing,
            LoopJumpDirection loopJumpDirection)
        {
            int dataCount = _layout.DataCount;
            dataIndex = Mathf.Clamp(dataIndex, 0, dataCount - 1);
            if (!_layout.IsLoop)
            {
                return dataIndex;
            }

            // 세트 k 사본의 목표 위치 = firstSetTarget + k × cycle
            float cycle = _layout.CycleExtent;
            float firstSetTarget = GetJumpPositionForSlot(dataIndex, scrollerOffset, cellOffset, useSpacing);
            float maxPosition = ScrollSize;
            int minSet = Mathf.Max(0, Mathf.CeilToInt(-firstSetTarget / cycle - LOOP_SET_EPSILON));
            int maxSet = Mathf.Min(_layout.SetCount - 1, Mathf.FloorToInt((maxPosition - firstSetTarget) / cycle + LOOP_SET_EPSILON));

            float relative = (ScrollPosition - firstSetTarget) / cycle;
            int set;
            switch (loopJumpDirection)
            {
                case LoopJumpDirection.Forward:
                    set = Mathf.CeilToInt(relative - LOOP_SET_EPSILON);
                    break;
                case LoopJumpDirection.Backward:
                    set = Mathf.FloorToInt(relative + LOOP_SET_EPSILON);
                    break;
                default:
                    set = Mathf.RoundToInt(relative);
                    break;
            }

            // 세트 수에 한 사이클씩 여유가 있으므로 보통은 자르지 않는다. 셀 오프셋이 극단적일 때만 잘린다.
            set = minSet <= maxSet
                ? Mathf.Clamp(set, minSet, maxSet)
                : Mathf.Clamp(set, 0, _layout.SetCount - 1);

            return set * dataCount + dataIndex;
        }

        private float GetJumpPositionForSlot(int slot, float scrollerOffset, float cellOffset, bool useSpacing)
        {
            float start = _layout.GetSlotStart(slot);
            float size = _layout.GetSlotSize(slot);
            if (useSpacing)
            {
                float spacing = _layout.Spacing;
                start -= spacing * 0.5f;
                size += spacing;
            }

            return start + cellOffset * size - scrollerOffset * ScrollRectSize;
        }

        private void StartAlignedTween(int slot, float scrollerOffset, float cellOffset, bool useSpacing,
            TweenType tweenType, float tweenTime, Action onComplete, bool isSnap)
        {
            CancelTween();
            _scrollRect.StopMovement();

            _alignmentActive = true;
            _alignSlot = slot;
            _alignScrollerOffset = scrollerOffset;
            _alignCellOffset = cellOffset;
            _alignUseSpacing = useSpacing;

            float from = ScrollPosition;
            float target = Mathf.Clamp(GetJumpPositionForSlot(slot, scrollerOffset, cellOffset, useSpacing), 0f, ScrollSize);
            _alignedPosition = target;

            if (tweenType == TweenType.Immediate || tweenTime <= 0f || Mathf.Approximately(from, target))
            {
                MoveContentTo(target);
                UpdateActiveRange();
                CompleteTween(onComplete, isSnap, slot, false);
                return;
            }

            _tweenFrom = from;
            _tweenTo = target;
            _tweenDuration = tweenTime;
            _tweenElapsed = 0f;
            _tweenType = tweenType;
            _tweenComplete = onComplete;
            _snapPending = isSnap;
            _snapSlot = isSnap ? slot : -1;
            SetTweening(true);
        }

        private void UpdateTween(float deltaTime)
        {
            _tweenElapsed += deltaTime;
            float t = _tweenElapsed >= _tweenDuration ? 1f : _tweenElapsed / _tweenDuration;
            float eased = CyScrollerEasing.Evaluate(_tweenType, t, _customTweenCurve);

            MoveContentTo(Mathf.LerpUnclamped(_tweenFrom, _tweenTo, eased));
            _scrollRect.velocity = Vector2.zero;
            UpdateActiveRange();

            if (t >= 1f && _tweening)
            {
                Action complete = _tweenComplete;
                bool snapped = _snapPending;
                int snapSlot = _snapSlot;
                ClearTweenState();
                CompleteTween(complete, snapped, snapSlot, true);
            }
        }

        /// <summary>
        /// 트윈(또는 즉시 이동)을 마무리한다. 상태를 먼저 확정하고 스냅샷한 값으로 알린다.
        /// 이벤트 핸들러가 새 점프를 시작해도 이번 결과(스냅 대상·완료 콜백)는 덮이지 않는다.
        /// </summary>
        private void CompleteTween(Action complete, bool snapped, int snapSlot, bool tweenEnded)
        {
            _alignedPosition = ScrollPosition;
            int snapDataIndex = snapped ? _layout.SlotToDataIndex(snapSlot) : -1;

            int slotShift = RecenterLoopIfNeeded();
            UpdateActiveRange();

            if (tweenEnded)
            {
                ScrollerTweeningChanged?.Invoke(this, false);
            }

            if (snapped)
            {
                int cellIndex = snapSlot + slotShift;
                ScrollerSnapped?.Invoke(this, cellIndex, snapDataIndex, GetCellViewAtCellIndex(cellIndex));
            }

            complete?.Invoke();
        }

        /// <summary>트윈 상태만 지운다 (이벤트 없음).</summary>
        private void ClearTweenState()
        {
            _tweening = false;
            _tweenComplete = null;
            _snapPending = false;
            _snapSlot = -1;
        }

        /// <summary>트윈을 취소한다. 완료 콜백·스냅 이벤트 없이 끝나며, 트윈 중이었으면 끝났다고 알린다.</summary>
        private void CancelTween()
        {
            bool wasTweening = _tweening;
            ClearTweenState();
            if (wasTweening)
            {
                ScrollerTweeningChanged?.Invoke(this, false);
            }
        }

        private void SetTweening(bool tweening)
        {
            if (_tweening == tweening)
            {
                return;
            }

            _tweening = tweening;
            ScrollerTweeningChanged?.Invoke(this, tweening);
        }

        /// <summary>코드로 요청한 이동이 진행 중인 사용자 드래그와 싸우지 않도록 드래그를 끝낸다.</summary>
        private void EndDragForProgrammaticMove()
        {
            if (!_dragging)
            {
                return;
            }

            if (_dragEventData != null)
            {
                _scrollRect.OnEndDrag(_dragEventData);
            }

            _dragging = false;
            _dragEventData = null;
        }

        /// <summary>
        /// 뷰포트 크기가 바뀐 뒤 점프·스냅 정렬을 다시 맞춘다. 트윈 중이면 목표만 바꾼다.
        /// 남아 있는 탄성·관성 속도는 멈춘다 (정렬 위치에서 미끄러져 나가지 않게).
        /// </summary>
        private void ReapplyAlignment()
        {
            float target = GetJumpPositionForSlot(_alignSlot, _alignScrollerOffset, _alignCellOffset, _alignUseSpacing);
            if (_tweening)
            {
                _tweenTo = Mathf.Clamp(target, 0f, ScrollSize);
                return;
            }

            // 루프면 같은 정렬이 되는 사본 중 가운데 창 안의 것으로 옮긴다.
            int cycles = _layout.GetRecenterCycles(target);
            if (cycles != 0)
            {
                target -= cycles * _layout.CycleExtent;
                _alignSlot -= cycles * _layout.DataCount;
            }

            target = Mathf.Clamp(target, 0f, ScrollSize);
            MoveContentTo(target);
            _alignedPosition = target;
            if (!_dragging)
            {
                _scrollRect.StopMovement();
            }
        }

        /// <summary>점프·스냅이 맞춘 위치에서 벗어났으면(스크롤바·외부 이동 등) 정렬 유지를 끝낸다.</summary>
        private void ReleaseAlignmentIfMoved()
        {
            if (_alignmentActive && !_tweening && Mathf.Abs(ScrollPosition - _alignedPosition) > ALIGNMENT_TOLERANCE)
            {
                _alignmentActive = false;
            }
        }

        private void UpdateSnap(float deltaTime)
        {
            if (!_snapping || !_snapArmed || _dragging)
            {
                return;
            }

            // 스크롤바를 잡았으면 스냅하지 않는다 (스크롤바 이동은 스냅 대상이 아니다).
            if (IsScrollbarSelected())
            {
                _snapArmed = false;
                return;
            }

            // 누른 채 멈춘 손가락 아래에서 콘텐츠가 미끄러지지 않도록 손을 뗄 때까지 기다린다.
            if (IsPointerHeld())
            {
                return;
            }

            if (_wheelIdleTime < WHEEL_SNAP_DELAY)
            {
                _wheelIdleTime += deltaTime;
                return;
            }

            if (Mathf.Abs(LinearVelocity) > _snapVelocityThreshold)
            {
                return;
            }

            Snap();
        }

        private bool IsPointerHeld()
        {
            return _pressEventData != null && _pressEventData.eligibleForClick;
        }

        private bool IsScrollbarSelected()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null)
            {
                return false;
            }

            UnityEngine.UI.Scrollbar scrollbar = _appliedVertical ? _scrollRect.verticalScrollbar : _scrollRect.horizontalScrollbar;
            return scrollbar != null && selected == scrollbar.gameObject;
        }

        /// <summary>
        /// 루프 모드에서 스크롤 위치가 가운데 세트에서 반 사이클 넘게 벗어나면 사이클 단위로 되돌린다.
        /// 활성 셀은 데이터가 같으므로 다시 바인딩하지 않고 슬롯 번호와 위치만 옮긴다.
        /// </summary>
        /// <returns>슬롯 번호에 더해진 값 (보정하지 않았으면 0).</returns>
        private int RecenterLoopIfNeeded()
        {
            if (!_layout.IsLoop || _tweening || _ignoreLoopJump)
            {
                return 0;
            }

            if (_dragging && !_loopWhileDragging)
            {
                return 0;
            }

            float position = ScrollPosition;
            int cycles = _layout.GetRecenterCycles(position);
            if (cycles == 0)
            {
                return 0;
            }

            int slotShift = -cycles * _layout.DataCount;

            // 보던 셀을 cycles 사이클 앞 사본 슬롯으로 옮기는 좌표 이동이다.
            // 정렬 위치와 드래그 기준점·직전 위치는 ShiftScrollPosition이 같이 옮긴다 (손가락 아래 콘텐츠·관성 속도가 튀지 않게).
            ShiftScrollPosition(-cycles * _layout.CycleExtent);
            ShiftActiveSlots(slotShift);

            if (_alignmentActive)
            {
                _alignSlot += slotShift;
            }

            return slotShift;
        }
    }
}
