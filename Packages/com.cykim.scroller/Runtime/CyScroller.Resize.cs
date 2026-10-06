using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace CyKim.Scroller
{
    // 셀 크기 변경: 항목 크기를 델리게이트에 다시 묻고, 셀은 다시 바인딩하지 않은 채 크기·위치만 바꾼다.
    // 시간을 주면 LateUpdate마다 크기를 보간해 증분 변경 경로(SetSize 연산)로 반영한다.
    public partial class CyScroller
    {
        private static readonly ProfilerMarker _resizeMarker = new ProfilerMarker("CyScroller.Resize");

        /// <summary>진행 중인 크기 애니메이션 하나. DataIndex는 지금 데이터 기준이고 증분 변경을 따라 옮겨진다.</summary>
        private struct ResizeAnimation
        {
            public int DataIndex;
            public float From;
            public float To;
            public float Elapsed;
            public float Duration;
            public TweenType Tween;
            public ResizeAnchor Anchor;
        }

        // 진행 중인 크기 애니메이션과 한 걸음에 쓸 연산 목록 (용량을 재사용해 할당하지 않는다).
        private readonly List<ResizeAnimation> _resizeAnimations = new List<ResizeAnimation>(4);
        private readonly List<UpdateOp> _resizeStepOps = new List<UpdateOp>(4);

        /// <summary>
        /// 셀 크기 애니메이션(<see cref="ResizeCellView"/>에 시간을 준 요청)이 진행 중인지.
        /// </summary>
        public bool IsResizing => _resizeAnimations.Count > 0;

        /// <summary>
        /// dataIndex 항목의 크기를 델리게이트(<see cref="ICyScrollerDelegate.GetCellViewSize"/>)에 다시 묻고 그 크기로 바꾼다.
        /// 셀은 다시 바인딩하지 않고(<see cref="CyScrollerCellView.BindVersion"/> 그대로) 크기·위치만 바꾼다. 델리게이트 크기를 먼저 바꾼 뒤 부른다.
        /// </summary>
        /// <param name="dataIndex">크기가 바뀐 항목 (배치 안이면 앞 연산까지 반영한 기준). [0, 개수) 밖이면 아무것도 하지 않는다.</param>
        /// <param name="duration">0 이하면 바로 바꾼다. 0보다 크면 지금 크기에서 새 크기까지 이 시간(초, unscaled) 동안 바꾼다.</param>
        /// <param name="tweenType">크기 애니메이션 곡선. <see cref="TweenType.Custom"/>은 <see cref="CustomTweenCurve"/>를 쓰고, <see cref="TweenType.Immediate"/>는 바로 바꾼다.</param>
        /// <param name="anchor">화면에서 제자리에 둘 기준 (<see cref="ResizeAnchor"/>). 결과는 스크롤 범위로 잘린다.</param>
        /// <remarks>
        /// <para>크기의 기준은 계속 델리게이트다. 셀이 스스로 크기를 정하지 않고, 바뀐 크기를 델리게이트가 돌려준 뒤 이 메서드로 알린다.
        /// 항목 ID 제공자면 ID도 다시 묻는다(바로 바꿀 때, 루프 모드 제외). 셀 종류(프리팹)나 내용이 바뀌었으면 <see cref="ReloadCellView"/>·<see cref="RefreshCells"/>를 쓴다.</para>
        /// <para>호출 시점 규칙은 <see cref="InsertCells"/>와 같다. 배치(<see cref="BeginUpdates"/>) 안에서는 <see cref="EndUpdates"/>까지 미뤄 뒤따른 연산의 인덱스 이동을 따라가고,
        /// 같은 배치에서 지워지거나 다시 받거나(<see cref="ReloadCellView"/>) 크기를 다시 요청하면 앞 요청은 버린다.
        /// 배치에 <see cref="ResizeAnchor.Start"/>·<see cref="ResizeAnchor.End"/> 요청이 여럿이면 마지막 요청의 가장자리를 지킨다.
        /// 점프·스냅·<see cref="ScrollIntoView"/> 정렬이 유지되는 중이면 정렬이 앵커보다 먼저다. 진행 중인 트윈은 새 배치의 목표로 이어 간다.</para>
        /// <para>애니메이션은 LateUpdate에서 매 프레임 크기를 바꾸고 기준에 맞춰 스크롤 위치를 옮긴다(드래그 중이면 손가락 기준점도). 같은 항목에 새 요청이 오면 지금 크기에서 이어 간다.
        /// 그 항목이 지워지거나 <see cref="ReloadCellView"/>·<see cref="ReloadData()"/>·<see cref="ReloadDataKeepingPosition"/>처럼 크기를 다시 읽으면 애니메이션을 버리고 그 크기를 따른다.
        /// 증분 변경 배치가 열려 있는 동안에는 멈췄다가 닫히면 이어 간다.</para>
        /// <para>루프 모드에서는 애니메이션 없이 바로 바꾸고, 위치를 지키는 재배치로 맞추므로 활성 셀을 다시 바인딩하고 앵커는 <see cref="ResizeAnchor.Auto"/>를 따른다.
        /// 델리게이트·셀 이벤트 콜백 안에서 부르면 다른 증분 변경처럼 범위 갱신 뒤 앵커 보존 리로드로 바뀐다(셀을 다시 바인딩하고 애니메이션 없이 바로 바뀐다).
        /// 로드 전에는 아무것도 하지 않는다(첫 리로드가 전부 읽는다).</para>
        /// </remarks>
        public void ResizeCellView(int dataIndex, float duration = 0f, TweenType tweenType = TweenType.EaseOutCubic,
            ResizeAnchor anchor = ResizeAnchor.Auto)
        {
            if (dataIndex < 0 || dataIndex >= PendingUpdateCount)
            {
                return;
            }

            BeginUpdates();
            try
            {
                _updateOps.Add(new UpdateOp
                {
                    Type = UpdateOpType.Resize,
                    A = dataIndex,
                    Anchor = anchor,
                    Duration = duration > 0f && tweenType != TweenType.Immediate ? duration : 0f,
                    Tween = tweenType,
                });
            }
            finally
            {
                EndUpdates();
            }
        }

        /// <summary>
        /// 크기 애니메이션을 deltaTime만큼 진행한다. 애니메이션마다 크기 한 걸음(SetSize 연산)을 만들어 증분 변경 경로로 한 번에 반영한다.
        /// 중간 걸음은 레이아웃 접두합을 다시 더하지 않으므로 항목 수와 무관하게 활성 셀 수만큼 든다(마지막 걸음만 바뀐 자리부터 한 번 다시 더한다).
        /// LateUpdate가 트윈보다 먼저 부른다 (트윈 목표가 바뀐 배치를 따라가게. 테스트가 직접 진행시킬 수 있게 internal). 할당 없음.
        /// 증분 변경 배치·범위 갱신 중이거나 미룬 리로드·재배치가 남아 있으면 이번에는 건너뛴다. 루프 모드면 남은 애니메이션을 바로 끝낸다.
        /// </summary>
        internal void UpdateResizeAnimations(float deltaTime)
        {
            if (_resizeAnimations.Count == 0 || !_hasLoaded || _updateDepth > 0 || _inRangeUpdate || HasPendingWork || _rebuildFailed)
            {
                return;
            }

            using (_resizeMarker.Auto())
            {
                if (_loop || _layout.IsLoop)
                {
                    FinishResizeAnimationsNow();
                    return;
                }

                List<UpdateOp> ops = _resizeStepOps;
                ops.Clear();
                for (int i = 0; i < _resizeAnimations.Count; i++)
                {
                    ResizeAnimation animation = _resizeAnimations[i];
                    animation.Elapsed += deltaTime;
                    bool finished = animation.Elapsed >= animation.Duration;
                    float size = animation.To;
                    if (!finished)
                    {
                        float eased = CyScrollerEasing.Evaluate(animation.Tween, animation.Elapsed / animation.Duration, _customTweenCurve);
                        size = animation.From + (animation.To - animation.From) * eased;
                    }

                    _resizeAnimations[i] = animation;
                    ops.Add(new UpdateOp
                    {
                        Type = UpdateOpType.SetSize,
                        A = animation.DataIndex,
                        B = finished ? 1 : 0,
                        Anchor = animation.Anchor,
                        Size = size,
                    });
                }

                // 끝난 애니메이션은 적용하기 전에 뺀다 (적용 중 콜백이 미룬 리로드가 목록을 비울 수 있다).
                for (int i = _resizeAnimations.Count - 1; i >= 0; i--)
                {
                    if (_resizeAnimations[i].Elapsed >= _resizeAnimations[i].Duration)
                    {
                        _resizeAnimations.RemoveAt(i);
                    }
                }

                ApplyIncrementalUpdates(ops);
                ops.Clear();

                // 콜백이 콘텐츠를 옮겼거나 작업을 미뤘으면 범위 갱신이 맞춘다 (FinishUpdates와 같다).
                _rangeUpdateDeferred = false;
                UpdateActiveRange();
            }
        }

        /// <summary>남은 애니메이션을 모두 목표 크기로 바꾸고 위치를 지키는 재배치로 맞춘다 (루프 모드).</summary>
        private void FinishResizeAnimationsNow()
        {
            int count = _layout.DataCount;
            for (int i = 0; i < _resizeAnimations.Count; i++)
            {
                ResizeAnimation animation = _resizeAnimations[i];
                if (animation.DataIndex < count)
                {
                    _layout.SetSize(animation.DataIndex, animation.To);
                }
            }

            _resizeAnimations.Clear();
            RelayoutKeepingPosition(false, false);
        }

        /// <summary>크기를 다시 읽을 때(리로드·델리게이트를 다시 받는 재배치) 진행 중인 애니메이션을 버린다. 다시 읽은 크기가 최종 값이다.</summary>
        private void ClearResizeAnimations()
        {
            _resizeAnimations.Clear();
        }

        /// <summary>연산이 크기 변경(과 내용 갱신)뿐인지. 루프 모드에서 리로드 대신 재배치로 처리할 수 있는 배치다.</summary>
        private static bool OnlyResizes(List<UpdateOp> ops)
        {
            bool hasResize = false;
            for (int i = 0; i < ops.Count; i++)
            {
                UpdateOpType type = ops[i].Type;
                if (type == UpdateOpType.Resize)
                {
                    hasResize = true;
                }
                else if (type != UpdateOpType.Refresh)
                {
                    return false;
                }
            }

            return hasResize;
        }

        /// <summary>
        /// 루프 모드의 크기 변경 배치. 요청한 항목의 크기(ID는 제외)를 바로 다시 묻고(애니메이션 없이) 위치를 지키는 재배치로 맞춘다.
        /// 재배치는 활성 셀을 다시 바인딩하므로 함께 기록한 내용 갱신은 따로 부르지 않는다.
        /// 크기를 묻는 동안은 범위 갱신 중으로 돌려, 델리게이트가 부른 연산·리로드는 순회 중인 연산 목록에 섞이지 않고 미뤄진다(재배치가 처리한다).
        /// </summary>
        private void ApplyLoopResizes(List<UpdateOp> ops)
        {
            bool nested = _inRangeUpdate;
            _inRangeUpdate = true;
            try
            {
                for (int i = 0; i < ops.Count; i++)
                {
                    UpdateOp op = ops[i];
                    if (op.Type != UpdateOpType.Resize || op.A >= _layout.DataCount)
                    {
                        continue;
                    }

                    for (int a = _resizeAnimations.Count - 1; a >= 0; a--)
                    {
                        if (_resizeAnimations[a].DataIndex == op.A)
                        {
                            _resizeAnimations.RemoveAt(a);
                        }
                    }

                    _layout.SetSize(op.A, _delegate != null ? _delegate.GetCellViewSize(this, op.A) : 0f);
                }
            }
            finally
            {
                _inRangeUpdate = nested;
            }

            RelayoutKeepingPosition(false, false);
        }

        /// <summary>
        /// 배치에서 셀 가장자리(<see cref="ResizeAnchor.Start"/>·<see cref="ResizeAnchor.End"/>)를 지키라는 마지막 크기 변경(바로 바꾸는 요청이나 애니메이션 걸음)의 배치 전 인덱스.
        /// 없거나 그 항목이 이 배치에서 삽입된 것이면 -1 (맨 앞 항목 기준으로 지킨다). O(연산 수), 할당 없음.
        /// </summary>
        private int FindResizeEdgeItem(List<UpdateOp> ops, out ResizeAnchor anchor)
        {
            anchor = ResizeAnchor.Auto;
            for (int i = ops.Count - 1; i >= 0; i--)
            {
                UpdateOp op = ops[i];
                bool resizesNow = op.Type == UpdateOpType.SetSize || (op.Type == UpdateOpType.Resize && op.Duration <= 0f);
                if (!resizesNow || op.Anchor == ResizeAnchor.Auto)
                {
                    continue;
                }

                int item = UnmapThroughUpdates(ops, i, op.A);
                if (item < 0 || item >= _layout.DataCount)
                {
                    return -1;
                }

                anchor = op.Anchor;
                return item;
            }

            return -1;
        }

        /// <summary>
        /// 배치 전 edgeItem 항목의 가장자리(edgeBefore)가 같은 화면 위치에 남도록 옮길 거리. 결과는 스크롤 범위로 자르되 드래그 중 가장자리 너머로 당긴 거리는 남긴다.
        /// 그 항목이 지워졌으면 맨 앞 항목 기준(<see cref="GetPreservedPositionDelta"/>)으로 지킨다.
        /// </summary>
        private float GetResizeEdgeDelta(List<UpdateOp> ops, int edgeItem, ResizeAnchor edgeAnchor, float edgeBefore,
            in CyScrollerAnchor anchor, float overscroll, float previousPosition)
        {
            int index = MapThroughUpdates(ops, edgeItem, out bool removed);
            if (removed || index >= _layout.DataCount)
            {
                return GetPreservedPositionDelta(ops, in anchor, overscroll, previousPosition);
            }

            float edgeAfter = edgeAnchor == ResizeAnchor.Start ? _layout.GetSlotStart(index) : _layout.GetSlotEnd(index);
            float target = previousPosition + (edgeAfter - edgeBefore);
            float clamped = Mathf.Clamp(target, Mathf.Min(0f, overscroll), ScrollSize + Mathf.Max(0f, overscroll));
            return clamped - previousPosition;
        }

        /// <summary>
        /// 진행 중인 애니메이션의 항목 인덱스를 연산 전체에 맞춰 옮긴다. 그 항목이 지워졌거나, 다시 받거나(<see cref="ReloadCellView"/>) 크기를 새로 요청했으면 버린다
        /// (새 요청이 애니메이션이면 지금 크기에서 이어 간다, <see cref="StartRequestedResizeAnimations"/>). 할당 없음.
        /// </summary>
        private void MapResizeAnimationsThroughUpdates(List<UpdateOp> ops)
        {
            for (int i = _resizeAnimations.Count - 1; i >= 0; i--)
            {
                ResizeAnimation animation = _resizeAnimations[i];
                int index = MapResizeIndex(ops, 0, animation.DataIndex);
                if (index < 0)
                {
                    _resizeAnimations.RemoveAt(i);
                }
                else if (index != animation.DataIndex)
                {
                    animation.DataIndex = index;
                    _resizeAnimations[i] = animation;
                }
            }
        }

        /// <summary>
        /// 배치의 애니메이션 크기 변경마다 최종 인덱스로 목표 크기를 묻고 지금 크기에서 시작한다. 뒤따른 연산이 지웠거나 다시 받거나 크기를 다시 요청한 요청은 건너뛰고,
        /// 지금 크기와 같으면 시작하지 않는다. <see cref="ApplyUpdatesToLayout"/> 끝(크기를 다 받은 뒤)에 부른다.
        /// </summary>
        private void StartRequestedResizeAnimations(List<UpdateOp> ops)
        {
            for (int i = 0; i < ops.Count; i++)
            {
                UpdateOp op = ops[i];
                if (op.Type != UpdateOpType.Resize || op.Duration <= 0f)
                {
                    continue;
                }

                int index = MapResizeIndex(ops, i + 1, op.A);
                if (index < 0 || index >= _layout.DataCount)
                {
                    continue;
                }

                float target = Mathf.Max(0f, _delegate != null ? _delegate.GetCellViewSize(this, index) : 0f);
                float from = _layout.GetSize(index);
                if (from == target)
                {
                    continue;
                }

                _resizeAnimations.Add(new ResizeAnimation
                {
                    DataIndex = index,
                    From = from,
                    To = target,
                    Duration = op.Duration,
                    Tween = op.Tween,
                    Anchor = op.Anchor,
                });
            }
        }

        /// <summary>
        /// ops[start..]를 거친 index 항목의 인덱스. 그 항목이 지워졌거나 다시 받거나(Reload) 크기를 다시 요청했으면(Resize) -1이다.
        /// 내용 갱신·애니메이션 걸음은 항목을 옮기지 않는다. O(연산 수), 할당 없음.
        /// </summary>
        private static int MapResizeIndex(List<UpdateOp> ops, int start, int index)
        {
            for (int i = start; i < ops.Count; i++)
            {
                UpdateOp op = ops[i];
                switch (op.Type)
                {
                    case UpdateOpType.Insert:
                        if (index >= op.A)
                        {
                            index += op.B;
                        }

                        break;
                    case UpdateOpType.Remove:
                        if (index >= op.A + op.B)
                        {
                            index -= op.B;
                        }
                        else if (index >= op.A)
                        {
                            return -1;
                        }

                        break;
                    case UpdateOpType.Move:
                        index = MoveIndex(index, op.A, op.B);
                        break;
                    case UpdateOpType.Reload:
                    case UpdateOpType.Resize:
                        if (index == op.A)
                        {
                            return -1;
                        }

                        break;
                }
            }

            return index;
        }

        /// <summary>
        /// ops[0, end)를 거꾸로 거쳐 index 항목의 배치 전 인덱스를 구한다. 그 항목이 이 배치에서 삽입된 것이면 -1. O(연산 수), 할당 없음.
        /// </summary>
        private static int UnmapThroughUpdates(List<UpdateOp> ops, int end, int index)
        {
            for (int i = end - 1; i >= 0; i--)
            {
                UpdateOp op = ops[i];
                switch (op.Type)
                {
                    case UpdateOpType.Insert:
                        if (index >= op.A + op.B)
                        {
                            index -= op.B;
                        }
                        else if (index >= op.A)
                        {
                            return -1;
                        }

                        break;
                    case UpdateOpType.Remove:
                        if (index >= op.A)
                        {
                            index += op.B;
                        }

                        break;
                    case UpdateOpType.Move:
                        // 옮긴 결과를 되돌린다: to 자리 항목은 from에서 왔다.
                        index = MoveIndex(index, op.B, op.A);
                        break;
                }
            }

            return index;
        }
    }
}
