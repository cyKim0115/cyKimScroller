using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace CyKim.Scroller
{
    // 증분 구조 변경: 삽입·삭제·이동을 연산 목록에 적어 두었다가 배치 끝에 한 번에 레이아웃·항목 ID·활성 셀·위치에 반영한다.
    public partial class CyScroller
    {
        private static readonly ProfilerMarker _applyUpdatesMarker = new ProfilerMarker("CyScroller.ApplyUpdates");

        private enum UpdateOpType : byte
        {
            Insert,
            Remove,
            Move,
        }

        /// <summary>기록한 연산. Insert·Remove는 (위치, 개수), Move는 (원래 인덱스, 옮길 인덱스). 인덱스는 앞 연산까지 반영한 기준이다.</summary>
        private struct UpdateOp
        {
            public UpdateOpType Type;
            public int A;
            public int B;
        }

        // BeginUpdates 깊이. 0보다 크면 범위 갱신·델리게이트 호출·리로드·재배치·정리를 바깥 EndUpdates까지 미룬다.
        private int _updateDepth;

        // 바깥 배치를 연 프레임과 그 배치를 이미 경고했는지. 배치가 프레임을 넘겨 열려 있으면 LateUpdate가 한 번 경고한다.
        private int _updatesBeganFrame;
        private bool _warnedOpenUpdates;

        // 배치에 기록한 연산과 개수 변화. 적용할 때는 예비 목록과 바꿔 끼워, 적용 중 콜백이 부른 연산이 섞이지 않게 한다.
        private List<UpdateOp> _updateOps = new List<UpdateOp>(8);
        private List<UpdateOp> _spareUpdateOps = new List<UpdateOp>(8);
        private int _updateCountDelta;

        // 배치 중 미룬 범위 갱신.
        private bool _rangeUpdateDeferred;

        // 배치가 대상이 지워진 트윈을 멈췄다. 적용을 마친 뒤 ScrollerTweeningChanged를 알린다.
        private bool _tweenCanceledByUpdates;

        // 활성 셀 맞추기 작업 목록 (용량을 재사용해 할당하지 않는다).
        private readonly List<CyScrollerCellView> _reconcileCells = new List<CyScrollerCellView>(32);
        private readonly List<int> _reconcileTargets = new List<int>(32);
        private readonly List<int> _reconcilePrevious = new List<int>(32);

        /// <summary>배치에 기록한 연산까지 반영한 개수. 인자를 자를 때 쓴다.</summary>
        private int PendingUpdateCount => _layout.DataCount + _updateCountDelta;

        /// <summary>
        /// 증분 변경 배치를 시작한다. 바깥 <see cref="EndUpdates"/>까지 <see cref="InsertCells"/>·<see cref="RemoveCells"/>·<see cref="MoveCell"/>을 모았다가 한 번에 적용한다.
        /// 중첩할 수 있고 할당하지 않는다. 반드시 <see cref="EndUpdates"/>와 짝을 맞춘다 (사이 코드가 예외를 던질 수 있으면 <c>finally</c>에서 부른다).
        /// </summary>
        /// <remarks>
        /// <para>배치 중에는 스크롤러가 배치 전 상태(개수·좌표·활성 셀·항목 ID)를 유지하고 범위 갱신·델리게이트 호출을 미룬다. 그 사이 사용자가 스크롤해도 셀은 배치 끝에 맞춘다.
        /// 들어온 리로드(<see cref="ReloadData()"/> 등)·<see cref="ReloadDataKeepingPosition"/>·재배치(<see cref="Spacing"/> 등)·<see cref="ClearActive"/>·<see cref="ClearRecycled"/>도 <see cref="EndUpdates"/>에서 처리한다.</para>
        /// <para>짝이 맞지 않아 배치가 닫히지 않으면 범위 갱신·리로드·재배치가 계속 미뤄져 스크롤해도 셀이 갱신되지 않는다.
        /// 배치가 프레임을 넘겨 열려 있으면 LateUpdate에서 경고를 한 번 남긴다.</para>
        /// <para>배치 중 점프·<see cref="ScrollIntoView"/>처럼 인덱스를 받는 이동은 배치 전 인덱스로 해석하고, 배치 끝에 진행 중인 다른 이동처럼 같은 항목으로 옮긴다.</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// scroller.BeginUpdates();
        /// try
        /// {
        ///     items.RemoveAt(3);
        ///     scroller.RemoveCells(3, 1);
        ///     items.Insert(0, pinned);
        ///     scroller.InsertCells(0, 1);
        /// }
        /// finally
        /// {
        ///     scroller.EndUpdates();
        /// }
        /// </code>
        /// </example>
        public void BeginUpdates()
        {
            if (_updateDepth == 0)
            {
                _updatesBeganFrame = Time.frameCount;
                _warnedOpenUpdates = false;
            }

            _updateDepth++;
        }

        /// <summary>
        /// 증분 변경 배치를 끝낸다. 바깥(마지막) 호출에서 모은 연산을 순서대로 레이아웃·항목 ID·활성 셀에 반영하고 보던 화면을 지킨다.
        /// <see cref="BeginUpdates"/>보다 많이 부르면 경고하고 무시한다.
        /// </summary>
        /// <remarks>
        /// <para>삽입분만 <see cref="ICyScrollerDelegate.GetCellViewSize"/>(ID 제공자면 <see cref="ICyScrollerItemIdProvider.GetItemId"/>)를 최종 인덱스로 묻는다.
        /// 남은 셀은 다시 바인딩하지 않고 인덱스만 바꿔 <see cref="CyScrollerCellView.OnDataIndexChanged"/>를 부른 뒤 새 위치로 옮긴다(<see cref="CyScrollerCellView.BindVersion"/> 그대로).
        /// 삭제된 항목의 셀은 회수하고(보이던 셀은 <see cref="CellViewDidEndDisplay"/>를 먼저), 새로 활성 범위에 들어온 자리만 델리게이트로 받는다. 표시 이벤트는 짝을 지킨다.</para>
        /// <para>뷰포트 맨 앞 항목보다 앞에서 생긴 크기 변화만큼 스크롤 위치를 옮겨 화면이 움직이지 않는다(드래그 중이면 손가락 기준점도).
        /// 맨 앞 항목이 지워지거나 <see cref="MoveCell"/>로 다른 자리로 옮겨지면 화면은 그 항목을 따라가지 않고, 그 자리에 온 다음 항목이 같은 거리에 온다
        /// (이동은 그 자리에서 지우고 새 자리에 삽입한 것과 같다. 같은 배치에서 그 빈자리에 삽입한 항목이 있으면 그 항목이 자리를 채운다). 결과는 스크롤 범위로 자른다.
        /// 진행 중인 트윈·점프 정렬과 보관한 앵커(<see cref="RestoreAnchor"/>)는 같은 항목을 향하게 옮긴다(옮겨진 항목도 따라간다).
        /// 트윈·정렬 대상이 지워지면 트윈을 멈추고(완료 콜백·스냅 이벤트 없음) 정렬을 푼다.</para>
        /// <para>연산을 반영한 개수가 <see cref="ICyScrollerDelegate.GetNumberOfCells"/>와 다르면(연산이 없는 배치 포함) 경고하고 <see cref="ReloadData(ReloadAnchor, float)"/>(FirstVisible)로 다시 읽는다.
        /// 루프 모드도 증분 대신 같은 리로드로 바꾸고, 델리게이트·셀 이벤트 콜백 안에서 끝난 배치는 같은 리로드를 범위 갱신이 끝난 뒤로 미룬다.
        /// 배치 중 다시 읽는 요청(리로드·<see cref="ReloadDataKeepingPosition"/>·축 전환·델리게이트 교체)이 있었으면 기록한 연산 대신 그 요청을 처리한다 (위치 기준은 배치 전 화면).</para>
        /// </remarks>
        public void EndUpdates()
        {
            if (_updateDepth <= 0)
            {
                Debug.LogWarning("[CyScroller] BeginUpdates 없이 EndUpdates가 불렸습니다. 무시합니다.", this);
                return;
            }

            if (_updateDepth == 1 && !_inRangeUpdate && _snapEventPending)
            {
                // 범위 갱신 콜백 안에서 끝난 스냅이 남아 있으면 슬롯 번호가 바뀌기 전에 알린다. 아직 배치 안이므로 핸들러의 요청도 이 배치를 따른다.
                try
                {
                    RaisePendingSnapEvent();
                }
                catch
                {
                    // 핸들러가 예외를 던져도 이 호출의 깊이는 닫고(바깥이면 적용) 예외를 올린다. 닫히지 않으면 범위 갱신·리로드가 계속 미뤄진다.
                    if (_updateDepth > 0)
                    {
                        _updateDepth--;
                        if (_updateDepth == 0)
                        {
                            FinishUpdates();
                        }
                    }

                    throw;
                }

                if (_updateDepth <= 0)
                {
                    // 핸들러가 배치를 끝냈다.
                    return;
                }
            }

            _updateDepth--;
            if (_updateDepth == 0)
            {
                FinishUpdates();
            }
        }

        /// <summary>LateUpdate에서 부른다. 배치가 프레임을 넘겨 열려 있으면(<see cref="EndUpdates"/> 누락, 사이 코드의 예외 등) 그 배치에 한 번 경고한다.</summary>
        private void WarnIfUpdatesLeftOpen()
        {
            if (_updateDepth <= 0 || _warnedOpenUpdates || Time.frameCount == _updatesBeganFrame)
            {
                return;
            }

            _warnedOpenUpdates = true;
            Debug.LogWarning(
                "[CyScroller] BeginUpdates로 연 배치가 프레임을 넘겨 열려 있습니다. EndUpdates를 부를 때까지 셀 범위 갱신·리로드·재배치가 미뤄집니다. " +
                "사이 코드가 예외를 던져도 닫히도록 EndUpdates는 finally에서 부르세요.", this);
        }

        /// <summary>
        /// dataIndex 앞에 항목 count개가 삽입됐다고 알린다. 델리게이트 데이터를 먼저 바꾼 뒤 부른다.
        /// 삽입분만 크기(와 항목 ID)를 묻고 남은 셀은 다시 바인딩하지 않는다. 뷰포트 맨 앞 항목보다 앞에 삽입하면 그만큼 스크롤 위치를 옮겨 화면이 움직이지 않는다.
        /// </summary>
        /// <param name="dataIndex">삽입 위치 (앞 연산까지 반영한 기준). [0, 개수] 밖이면 잘린다.</param>
        /// <param name="count">삽입한 개수. 0 이하이면 아무것도 하지 않는다.</param>
        /// <remarks>
        /// 배치(<see cref="BeginUpdates"/>) 밖에서 부르면 이 연산 하나로 바로 적용하므로 그때 <see cref="ICyScrollerDelegate.GetNumberOfCells"/>가 이 연산까지 반영해야 한다.
        /// 배치 안에서는 <see cref="EndUpdates"/> 때 최종 개수와 맞으면 된다. 로드 전에는 아무것도 하지 않는다 (첫 리로드가 전부 읽는다). 나머지 규칙은 <see cref="EndUpdates"/>.
        /// </remarks>
        public void InsertCells(int dataIndex, int count)
        {
            if (count <= 0)
            {
                return;
            }

            BeginUpdates();
            try
            {
                int at = Mathf.Clamp(dataIndex, 0, PendingUpdateCount);
                _updateOps.Add(new UpdateOp { Type = UpdateOpType.Insert, A = at, B = count });
                _updateCountDelta += count;
            }
            finally
            {
                EndUpdates();
            }
        }

        /// <summary>
        /// dataIndex부터 항목 count개가 삭제됐다고 알린다. 델리게이트 데이터를 먼저 바꾼 뒤 부른다.
        /// 삭제된 항목의 셀은 회수하고(보이던 셀은 <see cref="CellViewDidEndDisplay"/>를 먼저) 남은 셀은 다시 바인딩하지 않는다.
        /// </summary>
        /// <param name="dataIndex">삭제 시작 위치 (앞 연산까지 반영한 기준).</param>
        /// <param name="count">
        /// 삭제한 개수. 구간 [dataIndex, dataIndex + count)는 [0, 개수) 안쪽만 쓴다. 0 이하이거나 남는 구간이 없으면 아무것도 하지 않는다.
        /// </param>
        /// <remarks>호출 시점 규칙은 <see cref="InsertCells"/>와 같다. 트윈·정렬 대상이 지워지면 트윈을 멈추고(완료 콜백 없음) 정렬을 푼다.</remarks>
        public void RemoveCells(int dataIndex, int count)
        {
            if (count <= 0)
            {
                return;
            }

            BeginUpdates();
            try
            {
                int start = Mathf.Max(0, dataIndex);
                int end = (int)Math.Min((long)dataIndex + count, PendingUpdateCount);
                if (end > start)
                {
                    _updateOps.Add(new UpdateOp { Type = UpdateOpType.Remove, A = start, B = end - start });
                    _updateCountDelta -= end - start;
                }
            }
            finally
            {
                EndUpdates();
            }
        }

        /// <summary>
        /// fromDataIndex 항목을 빼서 toDataIndex 자리로 옮겼다고 알린다 (결과에서 그 항목은 toDataIndex에 있다). 델리게이트 데이터를 먼저 바꾼 뒤 부른다.
        /// 활성 셀은 다시 바인딩하지 않고 인덱스가 바뀐 셀만 <see cref="CyScrollerCellView.OnDataIndexChanged"/>를 받는다.
        /// </summary>
        /// <param name="fromDataIndex">옮긴 항목의 원래 인덱스 (앞 연산까지 반영한 기준). [0, 개수 − 1] 밖이면 잘린다.</param>
        /// <param name="toDataIndex">옮긴 뒤 인덱스. [0, 개수 − 1] 밖이면 잘린다. 잘린 두 값이 같으면 아무것도 하지 않는다.</param>
        /// <remarks>
        /// 호출 시점 규칙은 <see cref="InsertCells"/>와 같다. 뷰포트 맨 앞 항목을 옮기면 화면은 그 항목을 따라가지 않는다.
        /// 그 자리에서 지우고 새 자리에 삽입한 것처럼, 그 자리에 온 다음 항목이 같은 거리에 오고 새 자리가 맨 앞 항목보다 앞이면 그 크기만큼 보정한다.
        /// 진행 중인 트윈·점프 정렬의 대상이면 그 이동은 옮겨진 항목을 따라간다.
        /// </remarks>
        public void MoveCell(int fromDataIndex, int toDataIndex)
        {
            int last = PendingUpdateCount - 1;
            int from = Mathf.Clamp(fromDataIndex, 0, Mathf.Max(0, last));
            int to = Mathf.Clamp(toDataIndex, 0, Mathf.Max(0, last));
            if (last <= 0 || from == to)
            {
                return;
            }

            BeginUpdates();
            try
            {
                _updateOps.Add(new UpdateOp { Type = UpdateOpType.Move, A = from, B = to });
            }
            finally
            {
                EndUpdates();
            }
        }

        /// <summary>
        /// 바깥 EndUpdates. 기록한 연산을 증분으로 적용하거나(개수가 맞고 루프가 아닐 때), 앵커 보존 리로드로 바꾸거나, 다시 읽을 요청에 맡긴다.
        /// 그다음 배치 중 미룬 리로드·재배치·정리·범위 갱신을 처리한다.
        /// </summary>
        private void FinishUpdates()
        {
            List<UpdateOp> ops = _updateOps;
            int countDelta = _updateCountDelta;
            _updateCountDelta = 0;

            if (ops.Count > 0 && _inRangeUpdate)
            {
                // 범위 갱신 콜백 안에서 끝난 배치. 델리게이트는 이미 최종 상태이므로 범위 갱신 뒤 앵커 보존 리로드로 같은 결과를 낸다.
                ops.Clear();
                ScheduleStructuralReload();
                return;
            }

            // 적용하는 동안 콜백이 부른 연산은 예비 목록에 들어가게 바꿔 끼운다.
            _updateOps = _spareUpdateOps;
            _spareUpdateOps = ops;
            try
            {
                // 로드 전이면 첫 리로드가, 다시 읽을 요청(리로드·델리게이트 교체·축 전환·위치 유지 리로드)이 있으면 그 요청이 최종 데이터를 읽는다.
                bool rereads = _reloadPending || (_relayoutPending && _relayoutRequery);
                if (_initialized && _hasLoaded && !rereads)
                {
                    // 연산이 없는 배치도 개수를 맞춰 본다 (사이 코드가 예외로 연산을 알리지 못한 채 finally에서 닫힌 배치 등).
                    int expected = _layout.DataCount + countDelta;
                    int actual = _delegate != null ? Mathf.Max(0, _delegate.GetNumberOfCells(this)) : 0;
                    if (actual != expected)
                    {
                        Debug.LogWarning(
                            $"[CyScroller] 증분 연산을 반영한 개수({expected})가 GetNumberOfCells({actual})와 다릅니다. ReloadData(ReloadAnchor.FirstVisible)로 다시 읽습니다.", this);
                        ReloadData(ReloadAnchor.FirstVisible);
                    }
                    else if (ops.Count == 0)
                    {
                        // 바뀐 것이 없다.
                    }
                    else if (_loop || _layout.IsLoop)
                    {
                        // 루프는 같은 데이터가 여러 슬롯에 있어 증분으로 옮기지 않고 앵커를 지키는 전체 리로드로 바꾼다.
                        ReloadData(ReloadAnchor.FirstVisible);
                    }
                    else
                    {
                        ApplyIncrementalUpdates(ops);
                    }
                }
            }
            finally
            {
                ops.Clear();
                if (_tweenCanceledByUpdates)
                {
                    _tweenCanceledByUpdates = false;
                    ScrollerTweeningChanged?.Invoke(this, false);
                }
            }

            // 알림 핸들러가 새 배치를 열었으면 그 배치가 끝날 때 처리한다.
            if (_updateDepth == 0 && (_rangeUpdateDeferred || HasPendingWork))
            {
                _rangeUpdateDeferred = false;
                UpdateActiveRange();
            }
        }

        /// <summary>
        /// 연산을 레이아웃·항목 ID·위치·활성 셀에 증분으로 반영한다. 범위 갱신 중(_inRangeUpdate)으로 돌리므로 그 사이 사용자 코드가 부른
        /// 리로드·재배치·정리·연산은 끝난 뒤로 미뤄지고 범위 갱신은 호출자가 마지막에 한다. 사용자 코드 예외로 멈추면 앵커 보존 리로드를 미뤄 다음 갱신에 맞춘다.
        /// </summary>
        private void ApplyIncrementalUpdates(List<UpdateOp> ops)
        {
            using (_applyUpdatesMarker.Auto())
            {
                bool completed = false;
                _inRangeUpdate = true;
                try
                {
                    ApplyIncrementalUpdatesCore(ops);
                    completed = true;
                }
                finally
                {
                    _inRangeUpdate = false;
                    if (!completed)
                    {
                        ScheduleStructuralReload();
                    }
                }

                // 콜백이 콘텐츠를 옮겼거나 작업을 미뤘으면 범위 갱신이 맞춘다.
                _rangeUpdateDeferred = true;
                ApplyScrollbarVisibility();
            }
        }

        private void ApplyIncrementalUpdatesCore(List<UpdateOp> ops)
        {
            // 배치 중 미룬 정리부터 한다 (활성 셀을 파괴했으면 아래에서 새로 바인딩한다).
            ApplyPendingClears();

            int oldCount = _layout.DataCount;
            float previousPosition = ReadPosition(_appliedVertical);
            CyScrollerAnchor anchor = CaptureLayoutAnchor(false, out float overscroll);

            // 트윈·정렬 대상을 같은 항목으로 옮긴다. 지워졌으면 트윈을 멈추고(완료 콜백·스냅 이벤트 없음) 정렬을 푼다.
            bool tweening = _tweening;
            int alignIndex = -1;
            if (_alignmentActive)
            {
                alignIndex = MapThroughUpdates(ops, _align.Slot, out bool alignRemoved);
                if (alignRemoved)
                {
                    if (tweening)
                    {
                        ClearTweenState();
                        _tweenCanceledByUpdates = true;
                        tweening = false;
                    }

                    _alignmentActive = false;
                }
            }

            // 보관한 앵커도 같은 항목(지워졌으면 그 자리의 다음 항목)을 가리키게 한다. 지금 데이터의 인덱스일 때만 옮긴다.
            if (_hasPendingAnchor && _pendingAnchor.DataIndex >= 0 && _pendingAnchor.DataIndex < oldCount)
            {
                _pendingAnchor.DataIndex = MapThroughUpdates(ops, _pendingAnchor.DataIndex, out _);
            }

            ApplyUpdatesToLayout(ops, oldCount);
            RebuildLayout(false);

            // 위치: 트윈은 맨 앞 항목 기준 화면을 지키고 시작점을 다시 잡는다. 정렬은 같은 항목에 다시 맞추고, 아니면 맨 앞 항목 기준으로 지킨다.
            if (tweening)
            {
                ShiftScrollPosition(GetPreservedPositionDelta(ops, in anchor, overscroll, previousPosition));
                _align.Slot = alignIndex;
                RebaseTween(0f);
            }
            else if (_alignmentActive)
            {
                _align.Slot = alignIndex;
                ReapplyAlignment();
            }
            else if (_hasPendingAnchor && CanApplyPendingAnchor)
            {
                MoveToPendingAnchor();
            }
            else
            {
                ShiftScrollPosition(GetPreservedPositionDelta(ops, in anchor, overscroll, previousPosition));
            }

            ReconcileActiveCells(ops);
        }

        /// <summary>
        /// 화면을 지키려고 옮길 거리. 배치 전 뷰포트 맨 앞 항목(지워졌거나 다른 자리로 옮겨졌으면 그 자리에 온 다음 항목, <see cref="MapScreenAnchorThroughUpdates"/>)이
        /// 같은 거리로 뷰포트 시작에 오게 하고, 그 뒤가 모두 지워졌으면 끝으로 간다. 결과는 스크롤 범위로 자르되 드래그 중 가장자리 너머로 당긴 거리는 남긴다.
        /// </summary>
        private float GetPreservedPositionDelta(List<UpdateOp> ops, in CyScrollerAnchor anchor, float overscroll, float previousPosition)
        {
            float target = previousPosition;
            int count = _layout.DataCount;
            if (anchor.DataIndex >= 0 && count > 0)
            {
                int index = MapScreenAnchorThroughUpdates(ops, anchor.DataIndex);
                target = index < count ? _layout.GetSlotStart(index) + anchor.Offset : float.MaxValue;
            }

            float clamped = Mathf.Clamp(target, Mathf.Min(0f, overscroll), ScrollSize + Mathf.Max(0f, overscroll));
            return clamped - previousPosition;
        }

        /// <summary>
        /// 크기·항목 ID 배열을 연산 순서대로 옮기고(Array.Copy), 삽입한 자리만 델리게이트에 크기·ID를 최종 인덱스로 묻는다.
        /// ID → 인덱스 사전은 바뀐 가장 앞 자리부터 다시 맞춘다. 접두합은 다음 Build가 그 자리부터 다시 더한다.
        /// </summary>
        private void ApplyUpdatesToLayout(List<UpdateOp> ops, int oldCount)
        {
            int firstChanged = oldCount;
            for (int i = 0; i < ops.Count; i++)
            {
                UpdateOp op = ops[i];
                int changed = op.Type == UpdateOpType.Move ? Mathf.Min(op.A, op.B) : op.A;
                if (changed < firstChanged)
                {
                    firstChanged = changed;
                }
            }

            bool hasIds = _hasItemIds;
            if (hasIds)
            {
                RemoveItemIdEntries(firstChanged, oldCount);
            }

            for (int i = 0; i < ops.Count; i++)
            {
                UpdateOp op = ops[i];
                int count = _layout.DataCount;
                switch (op.Type)
                {
                    case UpdateOpType.Insert:
                        if (hasIds)
                        {
                            InsertItemIdSlots(op.A, op.B, count);
                        }

                        _layout.InsertSizes(op.A, op.B);
                        break;
                    case UpdateOpType.Remove:
                        if (hasIds)
                        {
                            Array.Copy(_itemIds, op.A + op.B, _itemIds, op.A, count - op.A - op.B);
                        }

                        _layout.RemoveSizes(op.A, op.B);
                        break;
                    default:
                        if (hasIds)
                        {
                            MoveItemIdSlot(op.A, op.B);
                        }

                        _layout.MoveSize(op.A, op.B);
                        break;
                }
            }

            // 아직 크기를 받지 않은 자리가 이번 배치에 삽입한 항목이다 (연산을 거치며 같이 옮겨졌다).
            int newCount = _layout.DataCount;
            bool fetchedIds = false;
            for (int i = firstChanged; i < newCount; i++)
            {
                if (!_layout.IsSizeUnset(i))
                {
                    continue;
                }

                _layout.SetSize(i, _delegate != null ? _delegate.GetCellViewSize(this, i) : 0f);
                if (hasIds)
                {
                    _itemIds[i] = _idProvider.GetItemId(this, i);
                    fetchedIds = true;
                }
            }

            if (hasIds)
            {
                AddItemIdEntries(firstChanged, newCount, fetchedIds);
            }
        }

        private void InsertItemIdSlots(int index, int count, int dataCount)
        {
            int required = dataCount + count;
            if (_itemIds.Length < required)
            {
                var ids = new long[Mathf.Max(required, _itemIds.Length * 2)];
                Array.Copy(_itemIds, ids, dataCount);
                _itemIds = ids;
            }

            Array.Copy(_itemIds, index, _itemIds, index + count, dataCount - index);
        }

        private void MoveItemIdSlot(int from, int to)
        {
            long id = _itemIds[from];
            if (from < to)
            {
                Array.Copy(_itemIds, from + 1, _itemIds, from, to - from);
            }
            else
            {
                Array.Copy(_itemIds, to, _itemIds, to + 1, from - to);
            }

            _itemIds[to] = id;
        }

        /// <summary>[start, count) 자리 ID 중 사전이 그 자리를 가리키는 것만 뺀다 (앞쪽 중복이 이긴 항목은 그대로 둔다).</summary>
        private void RemoveItemIdEntries(int start, int count)
        {
            for (int i = start; i < count; i++)
            {
                long id = _itemIds[i];
                if (_itemIdToIndex.TryGetValue(id, out int index) && index == i)
                {
                    _itemIdToIndex.Remove(id);
                }
            }
        }

        /// <summary>
        /// [start, count) 자리 ID를 사전에 넣는다. 뒤에서부터 넣어 같은 ID는 앞 인덱스가 남고, start 앞의 항목이 이긴 ID는 덮지 않는다.
        /// 새로 받은 ID가 있으면 다시 받을 때처럼 중복을 한 번 경고한다 (에디터·개발 빌드).
        /// </summary>
        private void AddItemIdEntries(int start, int count, bool fetchedIds)
        {
            for (int i = count - 1; i >= start; i--)
            {
                long id = _itemIds[i];
                if (!_itemIdToIndex.TryGetValue(id, out int index) || index >= start)
                {
                    _itemIdToIndex[id] = i;
                }
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (fetchedIds && _itemIdToIndex.Count != count)
            {
                WarnFirstDuplicateItemId(count);
            }
#endif
        }

        /// <summary>
        /// 연산을 반영한 배치·위치에 활성 셀을 맞춘다. 표시 끝 → 회수 → 인덱스 변경 알림·재배치 → 새 자리 바인딩 → 표시 시작 순서이고,
        /// 남은 셀은 다시 바인딩하지 않는다. 계속 보이는 셀에는 표시 이벤트가 없다. 활성 목록·범위는 사용자 코드를 부르기 전에 맞춘다.
        /// </summary>
        private void ReconcileActiveCells(List<UpdateOp> ops)
        {
            ComputeActiveRange(ScrollPosition, out int first, out int last, out int visibleFirst, out int visibleLast);

            // 옛 활성 셀(목록 i = 옛 슬롯 _activeFirst + i)마다 새 슬롯. 지워졌거나 새 활성 범위 밖이면 -1.
            int oldFirst = _activeFirst;
            List<int> targets = _reconcileTargets;
            targets.Clear();
            for (int i = 0; i < _activeCells.Count; i++)
            {
                int target = -1;
                if (_activeCells[i] != null)
                {
                    target = MapThroughUpdates(ops, oldFirst + i, out bool removed);
                    if (removed || target < first || target > last)
                    {
                        target = -1;
                    }
                }

                targets.Add(target);
            }

            // 1. 표시 끝: 빠지는 셀과 남지만 새 표시 범위 밖으로 가는 셀. 셀은 목록에 둔 채 알린다.
            for (int i = 0; i < _activeCells.Count; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                int target = targets[i];
                if (cell != null && cell.IsDisplayed && (target < visibleFirst || target > visibleLast))
                {
                    EndDisplay(cell);
                }
            }

            // 2. 회수: 목록에서 먼저 빼고(빈 자리) 풀에 돌려보낸다.
            for (int i = 0; i < _activeCells.Count; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                if (cell != null && targets[i] < 0)
                {
                    _activeCells[i] = null;
                    RecycleCell(cell);
                }
            }

            // 3. 새 활성 목록 (사용자 코드 없음): 남은 셀을 새 슬롯에 두고 나머지는 빈 자리로 둔다. 계속 보이는 셀은 새 표시 범위 안에 있다.
            List<CyScrollerCellView> previousCells = _reconcileCells;
            previousCells.Clear();
            for (int i = 0; i < _activeCells.Count; i++)
            {
                previousCells.Add(_activeCells[i]);
            }

            bool hasRange = first <= last;
            int length = hasRange ? last - first + 1 : 0;
            List<int> previousIndices = _reconcilePrevious;
            previousIndices.Clear();
            _activeCells.Clear();
            for (int i = 0; i < length; i++)
            {
                _activeCells.Add(null);
                previousIndices.Add(-1);
            }

            for (int i = 0; i < previousCells.Count; i++)
            {
                CyScrollerCellView cell = previousCells[i];
                int target = targets[i];
                if (cell == null || target < 0)
                {
                    continue;
                }

                _activeCells[target - first] = cell;
                previousIndices[target - first] = cell.DataIndex;
                cell.DataIndex = target;
                cell.CellIndex = target;
                AssignItemId(cell, target);
            }

            previousCells.Clear();
            _activeFirst = hasRange ? first : 0;
            _activeLast = hasRange ? last : -1;
            _visibleFirst = visibleFirst <= visibleLast ? visibleFirst : 0;
            _visibleLast = visibleFirst <= visibleLast ? visibleLast : -1;
            _cellPositionsDirty = true;

            // 4. 남은 셀: 인덱스가 바뀌었으면 알린 뒤 새 위치로 옮긴다 (다시 바인딩하지 않으므로 BindVersion은 그대로).
            for (int i = 0; i < length; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                if (cell == null)
                {
                    continue;
                }

                int slot = first + i;
                int previousIndex = previousIndices[i];
                if (previousIndex == slot)
                {
                    PositionCell(cell, slot);
                    continue;
                }

                try
                {
                    cell.OnDataIndexChanged(previousIndex);
                }
                finally
                {
                    PositionCell(cell, slot);
                }
            }

            // 5. 새로 활성 범위에 들어온 자리만 델리게이트로 받는다.
            for (int i = 0; i < length; i++)
            {
                if (_activeCells[i] != null)
                {
                    continue;
                }

                CyScrollerCellView added = BindSlot(first + i);
                _activeCells[i] = added;
                NotifyShown(added);
            }

            // 6. 표시 시작: 새 표시 범위에서 아직 보이지 않던 셀.
            for (int slot = _visibleFirst; slot <= _visibleLast; slot++)
            {
                BeginDisplay(GetActiveCellAt(slot));
            }
        }

        /// <summary>앵커 보존 리로드(FirstVisible)를 미뤄 둔다. 이미 미룬 리로드가 있으면 그 요청이 최종 데이터를 다시 읽으므로 그대로 둔다.</summary>
        private void ScheduleStructuralReload()
        {
            if (_reloadPending)
            {
                return;
            }

            _reloadPending = true;
            _pendingReloadAnchor = ReloadAnchor.FirstVisible;
            _pendingReloadFactor = 0f;
        }

        /// <summary>
        /// 화면 앵커(배치 전 뷰포트 맨 앞 항목)가 연산을 모두 거친 뒤 가리키는 인덱스. 그 항목이 제자리에 남아 있으면 그 항목을 따라간다(그 항목 자리에 삽입하면 그 앞에 들어간다).
        /// 지워지거나 다른 자리로 옮겨지면 그 항목이 비운 자리(앞 항목들 바로 뒤)를 따라가고, 끝에 그 자리에 온 항목(없으면 끝 = 개수)을 돌려준다.
        /// 비운 자리에 삽입한 항목은 그 자리를 채우고, 자리 앞의 삽입·삭제·이동만 자리를 옮긴다. 이동은 원래 자리 삭제 뒤 새 자리 삽입과 같다.
        /// O(연산 수), 할당 없음.
        /// </summary>
        /// <remarks>트윈·정렬 대상·보관한 앵커·활성 셀은 옮겨진 항목을 따라가야 하므로 <see cref="MapThroughUpdates"/>를 쓴다.</remarks>
        private static int MapScreenAnchorThroughUpdates(List<UpdateOp> ops, int index)
        {
            // true면 index는 항목이 아니라 자리(index번 항목 바로 앞)다.
            bool vacated = false;
            for (int i = 0; i < ops.Count; i++)
            {
                UpdateOp op = ops[i];
                switch (op.Type)
                {
                    case UpdateOpType.Insert:
                        // 항목 자리 삽입은 항목을 뒤로 밀고, 비운 자리 삽입은 그 자리를 채운다.
                        if (index > op.A || (index == op.A && !vacated))
                        {
                            index += op.B;
                        }

                        break;
                    case UpdateOpType.Remove:
                        if (index >= op.A + op.B)
                        {
                            index -= op.B;
                        }
                        else if (index > op.A || (index == op.A && !vacated))
                        {
                            // 앵커 항목이 지워졌거나, 비운 자리 앞뒤가 함께 지워졌다.
                            index = op.A;
                            vacated = true;
                        }

                        break;
                    default:
                        // 원래 자리에서 뺀다. 앵커 항목이면 그 자리를 비운다.
                        if (index > op.A)
                        {
                            index--;
                        }
                        else if (index == op.A && !vacated)
                        {
                            vacated = true;
                        }

                        // 새 자리에 넣는다.
                        if (index > op.B || (index == op.B && !vacated))
                        {
                            index++;
                        }

                        break;
                }
            }

            return index;
        }

        /// <summary>
        /// 배치 전 인덱스가 연산을 모두 거친 뒤의 인덱스. 그 항목이 지워졌으면 removed가 true이고, 그 자리에 온 다음 항목(없으면 끝 = 개수)을 따라간 인덱스다.
        /// 옮겨진 항목은 새 자리로 따라간다. O(연산 수), 할당 없음.
        /// </summary>
        private static int MapThroughUpdates(List<UpdateOp> ops, int index, out bool removed)
        {
            removed = false;
            for (int i = 0; i < ops.Count; i++)
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
                            index = op.A;
                            removed = true;
                        }

                        break;
                    default:
                        if (index == op.A)
                        {
                            index = op.B;
                        }
                        else if (op.A < op.B)
                        {
                            if (index > op.A && index <= op.B)
                            {
                                index--;
                            }
                        }
                        else if (index >= op.B && index < op.A)
                        {
                            index++;
                        }

                        break;
                }
            }

            return index;
        }
    }
}
