using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CyKim.Scroller
{
    // 점프·ScrollIntoView·트윈·스냅·루프 순환 보정·정렬 유지.
    public partial class CyScroller
    {
        // 휠 스크롤이 멈춘 뒤 스냅까지 기다리는 시간. 휠 틱마다 스냅이 시작되지 않게 한다.
        private const float WHEEL_SNAP_DELAY = 0.15f;

        // 루프 점프 사본을 고를 때 부동소수 오차를 흡수하는 여유 (사이클 비율).
        private const float LOOP_SET_EPSILON = 0.0001f;

        // 이 거리보다 크게 위치가 달라지면 사용자가 직접 움직인 것으로 본다.
        private const float ALIGNMENT_TOLERANCE = 0.5f;

        // 완전히 보이는지 판정할 때 흡수하는 RectTransform 왕복 오차. 위치가 클수록 float 오차가 커져 비례분을 더한다.
        private const float VISIBILITY_TOLERANCE = 0.01f;
        private const float VISIBILITY_RELATIVE_TOLERANCE = 0.000001f;

        // 트윈 시작점을 다시 잡을 때 곡선의 남은 진행이 이보다 적으면 남은 거리를 곡선에 싣지 않는다 (100배 넘게 부풀리지 않게).
        private const float TWEEN_REBASE_MIN_REMAINING = 0.01f;

        // 트윈 곡선에서 이 거리 이하로 벗어난 화면은 곡선 위로 본다 (곡선을 새로 시작하지 않는다).
        private const float TWEEN_REBASE_TOLERANCE = 0.5f;

        // 트윈 마지막 걸음에서 범위 갱신 콜백이 미룬 작업을 트윈 중에 처리하는 최대 횟수. 콜백이 매번 다시 미루는 경우를 끊는다.
        private const int MAX_FINAL_STEP_FLUSHES = 4;

        /// <summary>
        /// 점프·스냅·ScrollIntoView가 맞출 정렬. 목표 위치를 저장하지 않고 현재 배치·뷰포트에서 매번 계산한다
        /// (<see cref="GetRequestPosition"/>). 트윈 목표와 정렬 유지가 같은 요청을 쓴다.
        /// </summary>
        private struct AlignRequest
        {
            /// <summary>맞출 슬롯. 루프면 어느 사본인지까지 정한다.</summary>
            public int Slot;

            /// <summary>맞출 뷰포트 지점. 0 = 앞, 1 = 뒤.</summary>
            public float ScrollerOffset;

            /// <summary>뷰포트 지점에 맞출 셀 안의 지점. 0 = 앞, 1 = 뒤.</summary>
            public float CellOffset;

            /// <summary>셀 앞뒤 간격의 절반씩을 셀 영역에 포함한다.</summary>
            public bool UseSpacing;

            /// <summary>목표 위치에 더하는 거리 (ScrollIntoView 여백).</summary>
            public float PixelOffset;
        }

        private bool _tweening;
        private float _tweenFrom;
        private float _tweenDuration;
        private float _tweenElapsed;
        private TweenType _tweenType;
        private Action _tweenComplete;

        // 트윈을 시작할 때마다 바뀐다. 범위 갱신 콜백 안에서 새 이동이 시작됐는지 알아보는 데 쓴다.
        private int _tweenSerial;

        private bool _snapArmed;
        private bool _snapPending;
        private float _wheelIdleTime = WHEEL_SNAP_DELAY;

        private bool _ignoreLoopJump;

        // 범위 갱신 콜백 안에서 순환 보정이 필요했는지. 그 안에서는 슬롯 번호를 옮기지 않고, 범위를 다시 맞추기 직전에 보정한다.
        private bool _recenterDeferred;

        // 범위 갱신 콜백 안에서 끝난 스냅의 ScrollerSnapped. 순환 보정과 새 위치의 셀 활성화가 콜백 뒤로 미뤄지므로 범위 갱신이 끝난 뒤 보낸다.
        // 슬롯은 그 사이 순환 보정만큼 같이 옮긴다. 같은 범위 갱신에서 여러 번 끝나면 마지막 스냅만 알린다.
        private bool _snapEventPending;
        private int _snapEventSlot;
        private int _snapEventDataIndex;

        // 점프·스냅·ScrollIntoView로 맞춘 정렬. 트윈 중에는 트윈 목표이고(트윈 중이면 항상 활성),
        // 끝난 뒤에는 사용자가 움직이기 전까지 뷰포트 크기가 바뀌어도 다시 맞춘다.
        private bool _alignmentActive;
        private AlignRequest _align;
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
        /// <param name="jumpComplete">
        /// 이동이 끝나면 호출된다. 트윈 중 재배치가 일어나도 새 배치의 목표까지 간 뒤 한 번 호출된다.
        /// 사용자 입력·<see cref="InterruptTween"/>·<see cref="ReloadData"/>로 중단되면 호출되지 않는다.
        /// </param>
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
            if (!PrepareProgrammaticMove(jumpComplete))
            {
                return;
            }

            EndDragForProgrammaticMove();
            var request = new AlignRequest
            {
                ScrollerOffset = scrollerOffset,
                CellOffset = cellOffset,
                UseSpacing = useSpacing,
            };
            request.Slot = ResolveJumpSlot(dataIndex, in request, loopJumpDirection);
            _snapArmed = false;
            StartAlignedTween(in request, tweenType, tweenTime, jumpComplete, false);
        }

        /// <summary>
        /// 셀이 보이게 이동한다. <see cref="ScrollAlign.Nearest"/>(기본)는 이미 완전히 보이면 움직이지 않고 바로 완료 콜백을 부른다.
        /// 이동 규칙은 <see cref="JumpToDataIndex"/>와 같다 (사용자가 움직이기 전까지 정렬 유지, 드래그 중이면 드래그를 끝내고 이동).
        /// </summary>
        /// <param name="dataIndex">보이게 할 데이터. 범위 밖이면 잘린다.</param>
        /// <param name="align">맞출 위치. Nearest는 앞쪽에 걸리면 Start, 뒤쪽이면 End로 맞춘다.</param>
        /// <param name="margin">
        /// 셀 앞뒤로 남길 거리. Start·End·Nearest에 쓰고 Center는 무시한다. 콘텐츠 끝 너머로는 남길 수 없으므로 그쪽은 콘텐츠 끝까지만 본다.
        /// </param>
        /// <param name="tweenType">이동 곡선. <see cref="TweenType.Immediate"/>면 바로 이동한다.</param>
        /// <param name="tweenTime">이동 시간(초, unscaled).</param>
        /// <param name="onComplete">
        /// 이동이 끝나면(움직일 필요가 없으면 바로) 호출된다. 사용자 입력·<see cref="InterruptTween"/>·<see cref="ReloadData"/>로 중단되면 호출되지 않는다.
        /// </param>
        /// <param name="loopJumpDirection">
        /// 루프 모드에서 이동할 사본. Nearest의 Closest는 앞쪽 사본 Start와 뒤쪽 사본 End 중 덜 움직이는 쪽,
        /// Backward는 앞쪽 사본 Start, Forward는 뒤쪽 사본 End.
        /// </param>
        /// <remarks>
        /// Nearest는 셀이 이미 완전히 보이거나(<see cref="IsDataIndexFullyVisible"/>, 루프면 보이는 사본 중 하나라도)
        /// 목표가 지금 위치라 더 움직일 수 없으면(뷰포트보다 큰 셀이 이미 시작에 맞춰져 있을 때 등) 움직이지 않는다.
        /// 이때 진행 중인 트윈은 그 자리에서 멈추고(새 요청이 대신하므로 이전 완료 콜백은 없음), 사용자 드래그·관성은 그대로 둔다.
        /// Start·Center·End는 목표가 지금 위치여도 점프처럼 정렬을 맞추고 드래그를 끝낸다.
        /// 뷰포트 크기가 정해지기 전(0)에 부르면 셀이 뷰포트보다 큰 것으로 보고 Start로 맞춘다.
        /// </remarks>
        public void ScrollIntoView(
            int dataIndex,
            ScrollAlign align = ScrollAlign.Nearest,
            float margin = 0f,
            TweenType tweenType = TweenType.Immediate,
            float tweenTime = 0f,
            Action onComplete = null,
            LoopJumpDirection loopJumpDirection = LoopJumpDirection.Closest)
        {
            if (!PrepareProgrammaticMove(onComplete))
            {
                return;
            }

            dataIndex = Mathf.Clamp(dataIndex, 0, _layout.DataCount - 1);
            AlignRequest request;
            if (align == ScrollAlign.Nearest)
            {
                if (FindFullyVisibleSlot(dataIndex, margin) >= 0)
                {
                    CompleteWithoutMoving(onComplete);
                    return;
                }

                request = ResolveNearestRequest(dataIndex, margin, loopJumpDirection);

                // 뷰포트보다 큰 셀이 이미 시작에 맞춰져 있는 것처럼 목표가 지금 위치로 잘리면 더 움직일 수 없다.
                // 이미 보일 때처럼 끝내고 드래그·관성은 건드리지 않는다.
                if (IsAtRequestTarget(in request))
                {
                    CompleteWithoutMoving(onComplete);
                    return;
                }
            }
            else
            {
                request = CreateIntoViewRequest(align, margin);
                request.Slot = ResolveJumpSlot(dataIndex, in request, loopJumpDirection);
            }

            EndDragForProgrammaticMove();
            _snapArmed = false;
            StartAlignedTween(in request, tweenType, tweenTime, onComplete, false);
        }

        /// <summary>
        /// 셀(앞뒤 margin 포함)이 실제 뷰포트 안에 완전히 들어 있는지. lookAhead로 미리 만든 구간은 뷰포트에 넣지 않는다.
        /// margin은 콘텐츠 끝 너머로는 셈하지 않는다 (앞 패딩이 margin보다 작은 첫 셀은 콘텐츠 시작까지 보이면 true, 마지막 셀도 같다).
        /// 루프면 어느 사본이든 완전히 보이면 true. 범위 밖 인덱스·로드 전이면 false.
        /// 부동소수 오차(0.01px + 위치의 100만분의 1)만큼 벗어난 것은 들어 있는 것으로 본다.
        /// </summary>
        public bool IsDataIndexFullyVisible(int dataIndex, float margin = 0f)
        {
            if (!_hasLoaded || dataIndex < 0 || dataIndex >= _layout.DataCount)
            {
                return false;
            }

            return FindFullyVisibleSlot(dataIndex, margin) >= 0;
        }

        /// <summary>셀 시작 위치 (콘텐츠 좌표, 앞 여백 포함). 루프면 가운데 세트 사본 기준. 범위 밖이면 잘리고, 데이터가 없으면 0.</summary>
        public float GetCellStart(int dataIndex)
        {
            if (_layout.DataCount == 0)
            {
                return 0f;
            }

            int clamped = Mathf.Clamp(dataIndex, 0, _layout.DataCount - 1);
            return _layout.GetSlotStart((_layout.IsLoop ? _layout.MiddleSetFirstSlot : 0) + clamped);
        }

        /// <summary>셀의 스크롤 축 크기 (간격 제외). 범위 밖이면 잘리고, 데이터가 없으면 0.</summary>
        public float GetCellSize(int dataIndex)
        {
            if (_layout.DataCount == 0)
            {
                return 0f;
            }

            return _layout.GetSize(Mathf.Clamp(dataIndex, 0, _layout.DataCount - 1));
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
            var request = new AlignRequest
            {
                Slot = _layout.GetNearestSlot(watchPosition),
                ScrollerOffset = _snapJumpToOffset,
                CellOffset = _snapCellCenterOffset,
                UseSpacing = _snapUseCellSpacing,
            };
            StartAlignedTween(in request, _snapTweenType, _snapTweenTime, null, true);
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

            var request = new AlignRequest
            {
                ScrollerOffset = scrollerOffset,
                CellOffset = cellOffset,
                UseSpacing = useSpacing,
            };
            request.Slot = ResolveJumpSlot(dataIndex, in request, loopJumpDirection);
            return Mathf.Clamp(GetRequestPosition(in request), 0f, ScrollSize);
        }

        /// <summary>
        /// 점프·ScrollIntoView 공통 준비. 로드 전이면 로드하고 미뤄 둔 작업을 처리한다.
        /// 데이터가 없으면 완료 콜백을 바로 부르고 false를 돌려준다.
        /// </summary>
        private bool PrepareProgrammaticMove(Action onComplete)
        {
            if (!EnsureInitialized())
            {
                return false;
            }

            if (!_hasLoaded && !_reloadPending)
            {
                ReloadData();
            }

            FlushPendingWork();

            if (_layout.DataCount == 0)
            {
                onComplete?.Invoke();
                return false;
            }

            return true;
        }

        /// <summary>
        /// 요청을 맞출 슬롯. 루프면 방향 규칙에 맞고 목표가 콘텐츠 안에 들어가는 사본을 고른다. request.Slot은 읽지 않는다.
        /// </summary>
        private int ResolveJumpSlot(int dataIndex, in AlignRequest request, LoopJumpDirection loopJumpDirection)
        {
            int dataCount = _layout.DataCount;
            dataIndex = Mathf.Clamp(dataIndex, 0, dataCount - 1);
            if (!_layout.IsLoop)
            {
                return dataIndex;
            }

            // 세트 k 사본의 목표 위치 = firstSetTarget + k × cycle
            float cycle = _layout.CycleExtent;
            float firstSetTarget = GetJumpPositionForSlot(dataIndex, request.ScrollerOffset, request.CellOffset, request.UseSpacing)
                + request.PixelOffset;
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

        /// <summary>요청의 목표 스크롤 위치 (스크롤 범위로 자르지 않음). 현재 배치·뷰포트로 계산한다. O(1), 할당 없음.</summary>
        private float GetRequestPosition(in AlignRequest request)
        {
            return GetJumpPositionForSlot(request.Slot, request.ScrollerOffset, request.CellOffset, request.UseSpacing)
                + request.PixelOffset;
        }

        /// <summary>요청의 목표(스크롤 범위로 자름)가 지금 위치와 같은지. 같으면 움직일 것이 없다.</summary>
        private bool IsAtRequestTarget(in AlignRequest request)
        {
            float position = ScrollPosition;
            float target = Mathf.Clamp(GetRequestPosition(in request), 0f, ScrollSize);
            return Mathf.Abs(target - position) <= GetVisibilityTolerance(position);
        }

        /// <summary>
        /// Nearest가 움직이지 않고 끝날 때. 진행 중인 트윈만 그 자리에서 멈추고(새 요청이 대신하므로 이전 완료 콜백은 없음) 완료 콜백을 부른다.
        /// 사용자 드래그·관성과 이전 정렬은 그대로 둔다.
        /// </summary>
        private void CompleteWithoutMoving(Action onComplete)
        {
            if (_tweening)
            {
                CancelTween();
                _alignmentActive = false;
            }

            onComplete?.Invoke();
        }

        /// <summary>ScrollIntoView의 Start·Center·End 요청 (슬롯은 호출자가 정한다). 셀 간격은 셀 영역에 넣지 않는다.</summary>
        private static AlignRequest CreateIntoViewRequest(ScrollAlign align, float margin)
        {
            switch (align)
            {
                case ScrollAlign.Center:
                    return new AlignRequest { ScrollerOffset = 0.5f, CellOffset = 0.5f };
                case ScrollAlign.End:
                    return new AlignRequest { ScrollerOffset = 1f, CellOffset = 1f, PixelOffset = margin };
                default:
                    return new AlignRequest { PixelOffset = -margin };
            }
        }

        /// <summary>
        /// 완전히 보이지 않는 셀을 Nearest로 맞출 요청. 여백까지 합쳐 뷰포트보다 크면 Start.
        /// 루프가 아니면 앞쪽에 걸리면 Start, 아니면 End (여백은 콘텐츠 끝에서 자른다). 루프면 방향 규칙으로 사본과 정렬을 고른다.
        /// </summary>
        private AlignRequest ResolveNearestRequest(int dataIndex, float margin, LoopJumpDirection loopJumpDirection)
        {
            AlignRequest start = CreateIntoViewRequest(ScrollAlign.Start, margin);
            AlignRequest end = CreateIntoViewRequest(ScrollAlign.End, margin);
            float position = ScrollPosition;
            if (!_layout.IsLoop)
            {
                GetMarginRange(dataIndex, margin, out float rangeStart, out float rangeEnd);
                bool alignStart = rangeEnd - rangeStart > ScrollRectSize
                    || rangeStart < position - GetVisibilityTolerance(position);
                AlignRequest chosen = alignStart ? start : end;
                chosen.Slot = dataIndex;
                return chosen;
            }

            if (_layout.GetSize(dataIndex) + 2f * margin > ScrollRectSize)
            {
                start.Slot = ResolveJumpSlot(dataIndex, in start, loopJumpDirection);
                return start;
            }

            switch (loopJumpDirection)
            {
                case LoopJumpDirection.Backward:
                    start.Slot = ResolveJumpSlot(dataIndex, in start, LoopJumpDirection.Backward);
                    return start;
                case LoopJumpDirection.Forward:
                    end.Slot = ResolveJumpSlot(dataIndex, in end, LoopJumpDirection.Forward);
                    return end;
                default:
                    start.Slot = ResolveJumpSlot(dataIndex, in start, LoopJumpDirection.Backward);
                    end.Slot = ResolveJumpSlot(dataIndex, in end, LoopJumpDirection.Forward);
                    float scrollSize = ScrollSize;
                    float startDistance = Mathf.Abs(Mathf.Clamp(GetRequestPosition(in start), 0f, scrollSize) - position);
                    float endDistance = Mathf.Abs(Mathf.Clamp(GetRequestPosition(in end), 0f, scrollSize) - position);
                    return startDistance <= endDistance ? start : end;
            }
        }

        /// <summary>
        /// 셀(앞뒤 margin 포함)이 실제 뷰포트(lookAhead 제외) 안에 완전히 들어 있는 슬롯. 루프면 그런 사본. 없으면 -1.
        /// dataIndex는 범위 안이어야 한다. O(1), 할당 없음.
        /// </summary>
        private int FindFullyVisibleSlot(int dataIndex, float margin)
        {
            float viewStart = ScrollPosition;
            float viewEnd = viewStart + ScrollRectSize;
            float tolerance = GetVisibilityTolerance(viewStart);
            int slot = dataIndex;
            if (_layout.IsLoop)
            {
                // 시작이 뷰포트 시작 이후인 첫 사본만 보면 된다. 그 앞 사본은 앞쪽이 잘리고, 뒤 사본은 이 사본보다 끝이 더 뒤다.
                float firstStart = _layout.GetSlotStart(dataIndex) - margin;
                int set = Mathf.Max(0, Mathf.CeilToInt((viewStart - tolerance - firstStart) / _layout.CycleExtent));
                if (set >= _layout.SetCount)
                {
                    return -1;
                }

                slot = set * _layout.DataCount + dataIndex;
            }

            GetMarginRange(slot, margin, out float start, out float end);
            return start >= viewStart - tolerance && end <= viewEnd + tolerance ? slot : -1;
        }

        /// <summary>
        /// 슬롯 셀과 앞뒤 margin 구간 (콘텐츠 좌표). 콘텐츠 끝 너머로는 스크롤할 수 없으므로 그쪽 여백은 콘텐츠 끝에서 자른다.
        /// </summary>
        private void GetMarginRange(int slot, float margin, out float start, out float end)
        {
            start = Mathf.Max(_layout.GetSlotStart(slot) - margin, 0f);
            end = Mathf.Min(_layout.GetSlotEnd(slot) + margin, _layout.ContentExtent);
        }

        private static float GetVisibilityTolerance(float position)
        {
            return VISIBILITY_TOLERANCE + Mathf.Abs(position) * VISIBILITY_RELATIVE_TOLERANCE;
        }

        private void StartAlignedTween(in AlignRequest request, TweenType tweenType, float tweenTime, Action onComplete, bool isSnap)
        {
            CancelTween();
            _scrollRect.StopMovement();

            _alignmentActive = true;
            _align = request;

            int slot = request.Slot;
            float from = ScrollPosition;
            float target = Mathf.Clamp(GetRequestPosition(in _align), 0f, ScrollSize);
            _alignedPosition = target;

            if (tweenType == TweenType.Immediate || tweenTime <= 0f || Mathf.Approximately(from, target))
            {
                MoveContentTo(target);
                UpdateActiveRange();
                CompleteTween(onComplete, isSnap, slot, false);
                return;
            }

            _tweenFrom = from;
            _tweenDuration = tweenTime;
            _tweenElapsed = 0f;
            _tweenType = tweenType;
            _tweenComplete = onComplete;
            _snapPending = isSnap;
            _tweenSerial++;
            SetTweening(true);
        }

        /// <summary>
        /// 트윈을 deltaTime만큼 진행한다. 목표는 매 프레임 요청에서 다시 계산하므로 재배치·뷰포트 크기 변화를 따라간다.
        /// LateUpdate가 부른다 (테스트가 직접 진행시킬 수 있게 internal). 할당 없음.
        /// </summary>
        internal void UpdateTween(float deltaTime)
        {
            if (!_tweening)
            {
                return;
            }

            _tweenElapsed += deltaTime;
            bool finished = _tweenElapsed >= _tweenDuration;
            float eased = CyScrollerEasing.Evaluate(_tweenType, finished ? 1f : _tweenElapsed / _tweenDuration, _customTweenCurve);

            int serial = _tweenSerial;
            MoveTweenTo(eased);
            UpdateActiveRange();

            // 범위 갱신 콜백이 트윈을 멈췄거나 새 이동을 시작했으면 그쪽에 맡긴다.
            if (!finished || !_tweening || serial != _tweenSerial)
            {
                return;
            }

            if (!SettleFinalTweenStep(eased, serial))
            {
                return;
            }

            Action complete = _tweenComplete;
            bool snapped = _snapPending;
            ClearTweenState();
            CompleteTween(complete, snapped, _align.Slot, true);
        }

        /// <summary>
        /// 마지막 걸음에서 트윈을 끝내기 전에 지금 배치의 목표에 놓는다. 할당 없음.
        /// 범위 갱신 콜백(셀 표시 이벤트·OnRecycled 등)이 미룬 재배치는 트윈 중에 처리해야 같은 데이터로 목표를 옮기는 트윈 경로를 탄다.
        /// 끝낸 뒤 처리하면 옛 배치의 목표에서 멈추고, 델리게이트를 다시 받는 재배치면 정렬도 잃는다.
        /// 처리하는 동안 콜백이 또 미룰 수 있어 <see cref="MAX_FINAL_STEP_FLUSHES"/>번까지만 처리하고, 남은 작업은 끝낸 뒤 처리한다.
        /// </summary>
        /// <returns>트윈을 끝내도 되면 true. 콜백이 트윈을 멈췄거나 새 이동을 시작했으면 false.</returns>
        private bool SettleFinalTweenStep(float eased, int serial)
        {
            int flushes = 0;
            while (true)
            {
                while (HasPendingWork && flushes < MAX_FINAL_STEP_FLUSHES)
                {
                    flushes++;
                    FlushPendingWork();
                    if (!_tweening || serial != _tweenSerial)
                    {
                        return false;
                    }
                }

                // 앞의 범위 갱신이나 위 처리가 재배치했으면 화면은 맨 앞 셀 기준 자리에 있다. 지금 배치의 목표로 다시 놓는다.
                MoveTweenTo(eased);
                if (HasPendingWork)
                {
                    return true;
                }

                // 목표 자리에서 범위를 맞춘다. 여기서 처음 보이는 셀의 콜백이 또 미루면 한 번 더 돈다.
                UpdateActiveRange();
                if (!_tweening || serial != _tweenSerial)
                {
                    return false;
                }

                if (!HasPendingWork || flushes >= MAX_FINAL_STEP_FLUSHES)
                {
                    return true;
                }
            }
        }

        /// <summary>트윈 시작점과 지금 배치의 목표 사이 eased 지점으로 옮긴다. 남은 관성 속도는 지운다.</summary>
        private void MoveTweenTo(float eased)
        {
            float target = Mathf.Clamp(GetRequestPosition(in _align), 0f, ScrollSize);
            MoveContentTo(Mathf.LerpUnclamped(_tweenFrom, target, eased));
            _scrollRect.velocity = Vector2.zero;
        }

        /// <summary>
        /// 트윈 중 재배치·뷰포트 크기 변화로 목표가 바뀐 뒤, 다음 프레임에 화면이 튀지 않도록 트윈 시작점을 다시 잡는다.
        /// 화면 위치와 목표 요청(슬롯)을 새 배치로 맞춘 뒤 부른다. O(1), 할당 없음.
        /// </summary>
        /// <remarks>
        /// 시작점을 화면과 같은 만큼만 옮기면 목표가 맨 앞 셀과 다르게 움직였을 때 다음 프레임에 진행률 × (목표 이동량 − 화면 이동량)만큼 튄다.
        /// 그래서 지금 화면이 새 목표로 가는 곡선의 현재 진행 지점에 오도록 시작점을 정해, 남은 거리를 곡선의 남은 진행에 싣는다.
        /// 목표와 화면이 같이 밀렸으면 결과는 시작점만 옮긴 것과 같다.
        /// 곡선이 끝값에서 다시 멀어지거나(Back·Elastic·Bounce) 1 직전에서 1로 뛰거나(EaseOutExpo·EaseInOutExpo) 모양을 알 수 없거나(Custom)
        /// 남은 진행이 아주 적으면 이 식이 차이를 크게 부풀린다. 그때는 화면이 곡선에서 벗어났을 때만 지금 화면에서 남은 시간 동안 같은 곡선으로 새로 간다
        /// (같이 밀렸으면 시작점만 옮겨 원래 곡선을 잇는다).
        /// </remarks>
        /// <param name="screenShift">재배치가 화면을 옮긴 거리 (맨 앞 셀 기준 복원). 뷰포트 크기 변화면 0.</param>
        private void RebaseTween(float screenShift)
        {
            _tweenFrom += screenShift;

            float progress = _tweenDuration > 0f ? _tweenElapsed / _tweenDuration : 1f;
            if (progress >= 1f)
            {
                // 마지막 걸음이다. 다음 이동이 바로 새 목표에 놓는다.
                return;
            }

            float eased = CyScrollerEasing.Evaluate(_tweenType, progress, _customTweenCurve);
            float target = Mathf.Clamp(GetRequestPosition(in _align), 0f, ScrollSize);
            float position = ScrollPosition;
            float remaining = 1f - eased;
            if (remaining >= TWEEN_REBASE_MIN_REMAINING && CyScrollerEasing.ConvergesMonotonically(_tweenType))
            {
                // LerpUnclamped(from, target, eased) == position 인 from. 이후 화면은 position에서 target까지 곡선의 남은 모양으로 간다.
                _tweenFrom = target + (position - target) / remaining;
                return;
            }

            if (Mathf.Abs(position - Mathf.LerpUnclamped(_tweenFrom, target, eased)) <= TWEEN_REBASE_TOLERANCE)
            {
                return;
            }

            _tweenFrom = position;
            _tweenDuration -= _tweenElapsed;
            _tweenElapsed = 0f;
        }

        /// <summary>
        /// 트윈(또는 즉시 이동)을 마무리한다. 상태를 먼저 확정하고 스냅샷한 값으로 알린다.
        /// 이벤트 핸들러가 새 점프를 시작해도 이번 결과(스냅 대상·완료 콜백)는 덮이지 않는다.
        /// 범위 갱신 콜백 안(즉시 스냅 등)이면 순환 보정과 새 위치의 범위 갱신이 콜백 뒤로 미뤄지므로
        /// ScrollerSnapped는 범위 갱신이 끝난 뒤 보낸다 (<see cref="RaisePendingSnapEvent"/>). 완료 콜백은 바로 부른다.
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
                if (_inRangeUpdate)
                {
                    _snapEventPending = true;
                    _snapEventSlot = cellIndex;
                    _snapEventDataIndex = snapDataIndex;
                }
                else
                {
                    ScrollerSnapped?.Invoke(this, cellIndex, snapDataIndex, GetCellViewAtCellIndex(cellIndex));
                }
            }

            complete?.Invoke();
        }

        /// <summary>
        /// 범위 갱신 콜백 안에서 끝난 스냅을 알린다. 범위 갱신을 마칠 때(<see cref="RaiseSnapEventAfterRangeUpdate"/>)와
        /// 배치를 다시 만들기 전(리로드·재배치)에 부른다. 범위 갱신 중에는 부르지 않는다.
        /// </summary>
        private void RaisePendingSnapEvent()
        {
            if (!_snapEventPending)
            {
                return;
            }

            // 핸들러 안의 이동·리로드가 범위를 다시 갱신해도 같은 알림을 또 보내지 않게 먼저 지운다.
            _snapEventPending = false;
            int slot = _snapEventSlot;
            ScrollerSnapped?.Invoke(this, slot, _snapEventDataIndex, GetCellViewAtCellIndex(slot));
        }

        /// <summary>트윈 상태만 지운다 (이벤트 없음).</summary>
        private void ClearTweenState()
        {
            _tweening = false;
            _tweenComplete = null;
            _snapPending = false;
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
        /// 뷰포트 크기가 바뀌었거나 재배치한 뒤 정렬을 다시 맞춘다. 트윈 중이면 할 일이 없다
        /// (목표는 매 프레임 다시 계산하고, 시작점은 <see cref="RebaseTween"/>이 다시 잡는다).
        /// 남아 있는 탄성·관성 속도는 멈춘다 (정렬 위치에서 미끄러져 나가지 않게).
        /// </summary>
        private void ReapplyAlignment()
        {
            if (_tweening)
            {
                return;
            }

            float target = GetRequestPosition(in _align);

            // 루프면 같은 정렬이 되는 사본 중 가운데 창 안의 것으로 옮긴다.
            int cycles = _layout.GetRecenterCycles(target);
            if (cycles != 0)
            {
                target -= cycles * _layout.CycleExtent;
                _align.Slot -= cycles * _layout.DataCount;
            }

            target = Mathf.Clamp(target, 0f, ScrollSize);
            MoveContentTo(target);
            _alignedPosition = target;
            if (!_dragging)
            {
                _scrollRect.StopMovement();
            }
        }

        /// <summary>
        /// 재배치 뒤 트윈이 계속 갈 슬롯. 같은 데이터(개수가 줄었으면 잘린 인덱스)를 고르고,
        /// 루프면 맨 앞 셀과의 사이클 차이를 지켜 재배치 전과 같은 방향의 사본으로 간다.
        /// </summary>
        /// <param name="dataIndex">재배치 전 요청의 데이터 인덱스.</param>
        /// <param name="anchorSlot">새 배치에서 맨 앞 셀 슬롯. 없으면 -1.</param>
        /// <param name="setOffset">재배치 전 요청 세트 − 맨 앞 셀 세트.</param>
        private int RemapAlignSlot(int dataIndex, int anchorSlot, int setOffset)
        {
            int dataCount = _layout.DataCount;
            int clamped = Mathf.Min(dataIndex, dataCount - 1);
            if (!_layout.IsLoop)
            {
                return clamped;
            }

            int anchorSet = anchorSlot >= 0 ? anchorSlot / dataCount : _layout.SetCount / 2;
            int set = Mathf.Clamp(anchorSet + setOffset, 0, _layout.SetCount - 1);
            return set * dataCount + clamped;
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
        /// 트윈 중에는 하지 않는다 (트윈 목표 사본은 콘텐츠 안에 있으므로 끝난 뒤 보정한다).
        /// 범위 갱신 콜백 안(즉시 점프 등)에서는 진행 중인 범위 계산이 옛 슬롯 번호를 쓰므로 미루고,
        /// 범위 갱신이 콜백 뒤 새 위치로 다시 맞추기 직전에 보정한다 (<see cref="ApplyActiveRange"/>).
        /// </summary>
        /// <returns>슬롯 번호에 더해진 값 (보정하지 않았거나 미뤘으면 0).</returns>
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

            if (_inRangeUpdate)
            {
                _recenterDeferred = true;
                return 0;
            }

            int slotShift = -cycles * _layout.DataCount;

            // 정렬 요청과 알릴 스냅은 슬롯으로 기억하므로 같은 만큼 옮긴다. 아래 ShiftActiveSlots가 셀을 모두 회수하는 경우
            // 회수 콜백이 새 이동을 시작할 수 있으므로 그 전에 옮긴다.
            if (_alignmentActive)
            {
                _align.Slot += slotShift;
            }

            if (_snapEventPending)
            {
                _snapEventSlot += slotShift;
            }

            // 보던 셀을 cycles 사이클 앞 사본 슬롯으로 옮기는 좌표 이동이다.
            // 정렬 위치와 드래그 기준점·직전 위치는 ShiftScrollPosition이 같이 옮긴다 (손가락 아래 콘텐츠·관성 속도가 튀지 않게).
            ShiftScrollPosition(-cycles * _layout.CycleExtent);
            ShiftActiveSlots(slotShift);

            return slotShift;
        }
    }
}
