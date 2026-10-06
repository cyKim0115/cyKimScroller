using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace CyKim.Scroller
{
    // 증분 변경: 삽입·삭제·이동(구조)과 내용 갱신·다시 받기(부분 갱신)를 연산 목록에 적어 두었다가 배치 끝에 한 번에 레이아웃·항목 ID·활성 셀·위치에 반영한다.
    public partial class CyScroller
    {
        private static readonly ProfilerMarker _applyUpdatesMarker = new ProfilerMarker("CyScroller.ApplyUpdates");

        private enum UpdateOpType : byte
        {
            Insert,
            Remove,
            Move,

            /// <summary>내용만 바뀜 (<see cref="RefreshCells"/>). 레이아웃·바인딩은 그대로다.</summary>
            Refresh,

            /// <summary>항목을 다시 받음 (<see cref="ReloadCellView"/>). 자리는 그대로이고 크기(와 ID)를 다시 묻고 활성 셀은 다시 바인딩한다.</summary>
            Reload,

            /// <summary>
            /// 크기만 바뀜 (<see cref="ResizeCellView"/>). 자리·바인딩은 그대로다. Duration이 0이면 크기(와 ID)를 다시 묻고 바로 바꾸고,
            /// 0보다 크면 배치 끝에 목표 크기를 물어 크기 애니메이션을 시작한다 (<see cref="UpdateResizeAnimations"/>).
            /// </summary>
            Resize,

            /// <summary>
            /// 크기 애니메이션 한 걸음. 델리게이트에 묻지 않고 Size로 바꾼다. 자리·바인딩은 그대로다. 배치에는 기록되지 않는다.
            /// 중간 걸음은 접두합을 다시 더하지 않고 미루고(<see cref="CyScrollerLayout.SetSizeDeferred"/>), 마지막 걸음(B = 1)은 접어서 한 번 다시 더한다.
            /// </summary>
            SetSize,
        }

        /// <summary>
        /// 기록한 연산. Insert·Remove·Refresh는 (위치, 개수), Move는 (원래 인덱스, 옮길 인덱스), Reload·Resize·SetSize는 (위치). 인덱스는 앞 연산까지 반영한 기준이다.
        /// Mask는 Refresh가 알린 changeMask다. Resize·SetSize는 Anchor로 화면 기준을, Resize는 Duration·Tween으로 애니메이션을, SetSize는 Size로 바꿀 크기와 B로 마지막 걸음(1)인지를 준다.
        /// </summary>
        private struct UpdateOp
        {
            public UpdateOpType Type;
            public int A;
            public int B;
            public int Mask;
            public ResizeAnchor Anchor;
            public float Duration;
            public TweenType Tween;
            public float Size;
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

        // 활성 셀 맞추기 작업 목록 (용량을 재사용해 할당하지 않는다). 마스크는 옛 활성 셀마다 배치에서 모은 changeMask다.
        private readonly List<CyScrollerCellView> _reconcileCells = new List<CyScrollerCellView>(32);
        private readonly List<int> _reconcileTargets = new List<int>(32);
        private readonly List<int> _reconcilePrevious = new List<int>(32);
        private readonly List<int> _reconcileMasks = new List<int>(32);

        // 증분 변경을 적용하거나 키 유지 리로드·재배치로 다시 읽는 동안(활성 셀이 아직 옛 인덱스일 수 있는 동안) 사용자 코드가 부른 RefreshCells
        // (그 안에서 끝난 갱신만 있는 배치 포함). 셀을 새 인덱스로 맞춘 뒤 남은 셀에 배치의 마스크와 OR로 합쳐 부른다 (용량을 재사용한다).
        // 켜져 있는 동안 끝난 배치는 범위 갱신을 하지 않는다 (배치가 반쯤 읽혔을 수 있다, FinishUpdates).
        private bool _deferRefreshes;
        private readonly List<UpdateOp> _deferredRefreshes = new List<UpdateOp>(4);

        // 콜백 안에서 끝난 갱신만 있는 배치가 활성 셀마다 모은 changeMask. 사용자 코드를 부르기 전에 연산 목록을 비우려고 먼저 모아 두고,
        // 그 안에서 같은 일이 다시 일어나도 겹치지 않게 뒤에 쌓았다가 끝나면 덜어 낸다 (용량을 재사용한다).
        private readonly List<int> _callbackRefreshMasks = new List<int>(32);

        /// <summary>배치에 기록한 연산까지 반영한 개수. 인자를 자를 때 쓴다.</summary>
        private int PendingUpdateCount => _layout.DataCount + _updateCountDelta;

        /// <summary>
        /// 증분 변경 배치를 시작한다. 바깥 <see cref="EndUpdates"/>까지 <see cref="InsertCells"/>·<see cref="RemoveCells"/>·<see cref="MoveCell"/>과
        /// <see cref="RefreshCells"/>·<see cref="ReloadCellView"/>를 모았다가 한 번에 적용한다.
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
        /// 진행 중인 트윈·점프 정렬과 보관한 앵커(<see cref="RestoreAnchor"/>)는 같은 항목을 향하게 옮긴다(진행 중인 트윈·보관한 앵커는 옮겨진 항목도 따라가고,
        /// 끝난 점프의 정렬은 대상이 옮겨지면 풀려 화면을 지킨다).
        /// 트윈·정렬 대상이 지워지면 트윈을 멈추고(완료 콜백·스냅 이벤트 없음) 정렬을 푼다.</para>
        /// <para>연산을 반영한 개수가 <see cref="ICyScrollerDelegate.GetNumberOfCells"/>와 다르면(연산이 없는 배치 포함) 경고하고 <see cref="ReloadData(ReloadAnchor, float)"/>(FirstVisible)로 다시 읽는다.
        /// 루프 모드도 증분 대신 같은 리로드로 바꾸고, 델리게이트·셀 이벤트 콜백 안에서 끝난 배치는 같은 리로드를 범위 갱신이 끝난 뒤로 미룬다.
        /// 갱신(<see cref="RefreshCells"/>)만 있는 배치는 레이아웃을 바꾸지 않으므로 어느 쪽이든 리로드하지 않는다
        /// (루프 모드는 사본 셀마다 부르고, 콜백 안에서 끝났으면 개수를 맞춰 보지 않고 배치 밖 호출처럼 바로 부른다).
        /// 이렇게 바꾼 리로드는 <see cref="PreserveCellsById"/>와 무관하게 모든 셀을 다시 바인딩한다.
        /// 배치 중 다시 읽는 요청(리로드·<see cref="ReloadDataKeepingPosition"/>·축 전환·델리게이트 교체)이 있었으면 기록한 연산 대신 그 요청을 처리한다 (위치 기준은 배치 전 화면).
        /// 기록한 연산에 내용 갱신·다시 받기가 있었으면 그 요청이 키 유지 리로드여도 모든 셀을 다시 바인딩한다 (구조 연산만 있으면 키 유지를 따른다).</para>
        /// <para><see cref="ReloadCellView"/>한 항목은 크기(와 ID)를 다시 묻고 활성 셀을 다시 바인딩한다. <see cref="RefreshCells"/>로 알린 항목은 남은 셀마다 모은 changeMask로
        /// <see cref="CyScrollerCellView.RefreshCellView(int)"/>를 한 번 부른다(인덱스 변경 알림·재배치 뒤, 새 자리 바인딩·표시 시작 전). 새로 바인딩하는 셀에는 부르지 않는다.</para>
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
        /// 진행 중인 트윈의 대상이면 트윈은 옮겨진 항목을 따라간다. 끝난 점프의 정렬 대상이면 정렬을 풀고 화면을 지킨다(정렬이 따라가면 화면이 그 항목으로 튄다).
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
        /// dataIndex부터 항목 count개의 내용이 바뀌었다고 알린다 (크기·개수·셀 종류는 그대로). 그 항목의 활성 셀에만 <see cref="CyScrollerCellView.RefreshCellView(int)"/>를 부른다.
        /// 델리게이트 데이터를 먼저 바꾼 뒤 부른다. 다시 바인딩하지 않고(<see cref="CyScrollerCellView.BindVersion"/> 그대로) 크기도 다시 묻지 않는다.
        /// </summary>
        /// <param name="dataIndex">시작 위치 (배치 안이면 앞 연산까지 반영한 기준).</param>
        /// <param name="count">
        /// 개수. 구간 [dataIndex, dataIndex + count)는 [0, 개수) 안쪽만 쓴다. 0 이하이거나 남는 구간이 없으면 아무것도 하지 않는다.
        /// </param>
        /// <param name="changeMask">무엇이 바뀌었는지 알리는 사용자 정의 비트 플래그 (기본 ~0 = 모두). 셀 뷰에 그대로 전달하고, 0이면 아무것도 하지 않는다.</param>
        /// <remarks>
        /// <para>배치 밖에서는 바로 부른다. 레이아웃을 바꾸지 않으므로 루프 모드에서도 리로드하지 않고 같은 데이터의 사본 셀마다 부르며, 델리게이트·셀 이벤트 콜백 안에서도 바로 부른다
        /// (증분 변경을 적용하거나 키 유지 리로드·재배치(<see cref="PreserveCellsById"/>)로 다시 읽는 중이면 활성 셀을 새 인덱스로 맞춘 뒤 부른다).
        /// 리로드·재배치가 기다리고 있으면 그쪽이 셀을 모두 다시 바인딩하므로 부르지 않는다
        /// (기다리는 리로드가 키 유지 리로드(<see cref="PreserveCellsById"/>)여도 이 갱신을 버리지 않게 모두 다시 바인딩한다).
        /// 활성 범위 밖 항목은 나중에 활성화될 때 최신 데이터로 바인딩되므로 부르지 않는다.
        /// 키 유지 리로드는 내용 변경을 감지하지 않으므로, 리로드로 데이터를 바꿨으면 리로드한 뒤 새 인덱스로 부른다.</para>
        /// <para>배치(<see cref="BeginUpdates"/>) 안에서는 항목마다 changeMask를 OR로 모았다가 <see cref="EndUpdates"/>에서 남은 셀마다 한 번 부른다.
        /// 뒤따른 삽입·삭제·이동이 인덱스를 옮기면 모은 값도 같은 항목을 따라간다(순차 의미론). 지워진 항목과 배치 끝에 새로 바인딩되는 셀
        /// (새로 활성 범위에 들어온 항목, 삽입한 항목, <see cref="ReloadCellView"/>한 항목)은 이미 최신 데이터로 바인딩되므로 부르지 않는다.
        /// 배치가 리로드로 바뀌면(개수 불일치, 루프 모드나 콜백 안에서 끝난 배치의 삽입·삭제·이동·다시 받기) 셀을 모두 다시 바인딩하므로 부르지 않는다.
        /// 갱신만 있는 배치는 리로드로 바뀌지 않는다. 콜백 안에서 끝났으면 배치 밖 호출처럼 바로(증분 변경을 적용하거나 키 유지 리로드·재배치로 다시 읽는 중이면
        /// 활성 셀을 새 인덱스로 맞춘 뒤) 셀마다 모은 changeMask로 한 번 부른다.</para>
        /// <para>크기나 셀 종류(프리팹)가 바뀌면 <see cref="ReloadCellView"/>를 쓴다.</para>
        /// </remarks>
        public void RefreshCells(int dataIndex, int count, int changeMask = ~0)
        {
            if (count <= 0 || changeMask == 0)
            {
                return;
            }

            int start = Mathf.Max(0, dataIndex);
            if (_updateDepth > 0)
            {
                // 배치 안: 적어 두었다가 EndUpdates에서 같은 항목의 남은 셀에 모아 부른다.
                int pendingEnd = (int)Math.Min((long)dataIndex + count, PendingUpdateCount);
                if (pendingEnd > start)
                {
                    _updateOps.Add(new UpdateOp { Type = UpdateOpType.Refresh, A = start, B = pendingEnd - start, Mask = changeMask });
                }

                return;
            }

            if (_deferRefreshes)
            {
                // 증분 변경을 적용하는 중(콜백 안)이라 활성 셀이 아직 배치 전 인덱스일 수 있다. 셀을 새 인덱스로 맞춘 뒤 남은 셀에 부른다.
                int deferredEnd = (int)Math.Min((long)dataIndex + count, int.MaxValue);
                if (deferredEnd > start)
                {
                    _deferredRefreshes.Add(new UpdateOp { Type = UpdateOpType.Refresh, A = start, B = deferredEnd - start, Mask = changeMask });
                }

                return;
            }

            int end = (int)Math.Min((long)dataIndex + count, _layout.DataCount);
            if (end <= start || !_hasLoaded)
            {
                return;
            }

            if (_reloadPending || _relayoutPending)
            {
                // 기다리는 리로드·재배치가 셀을 다시 바인딩한다. 키 유지 리로드여도 이 갱신을 버리지 않게 모두 다시 바인딩하게 한다.
                _forceRebindOnReload = true;
                return;
            }

            RefreshCellsNow(start, end, changeMask);
        }

        /// <summary>
        /// 활성 셀마다 <see cref="CyScrollerCellView.RefreshCellView(int)"/>를 changeMask로 부른다. 모든 항목에 <see cref="RefreshCells"/>를 부른 것과 같다
        /// (크기는 다시 계산하지 않고, 루프 모드면 사본 셀마다, 배치 안이면 <see cref="EndUpdates"/>에서 남은 셀마다 한 번, changeMask가 0이면 아무것도 하지 않는다).
        /// </summary>
        /// <param name="changeMask">무엇이 바뀌었는지 알리는 사용자 정의 비트 플래그.</param>
        public void RefreshActiveCellViews(int changeMask)
        {
            RefreshCells(0, int.MaxValue, changeMask);
        }

        /// <summary>
        /// dataIndex 항목을 다시 받는다. 크기(와 항목 ID)를 다시 묻고, 활성 셀이 있으면 회수한 뒤 델리게이트 <see cref="ICyScrollerDelegate.GetCellView"/>로 다시 받아 바인딩한다.
        /// 셀 종류(<see cref="CyScrollerCellView.CellIdentifier"/>)가 바뀌어도 된다. 델리게이트 데이터를 먼저 바꾼 뒤 부른다.
        /// </summary>
        /// <param name="dataIndex">다시 받을 항목 (배치 안이면 앞 연산까지 반영한 기준). [0, 개수) 밖이면 아무것도 하지 않는다.</param>
        /// <remarks>
        /// <para>새 바인딩이므로 <see cref="CyScrollerCellView.BindVersion"/>이 늘고, 보이던 셀이면 <see cref="CellViewDidEndDisplay"/> → 회수 → 새 셀 바인딩 →
        /// <see cref="CellViewWillDisplay"/> 순서로 표시 이벤트 짝을 지킨다. 활성 셀이 없으면 크기(와 ID)만 갱신한다(<see cref="ICyScrollerDelegate.GetCellView"/>는 부르지 않는다).
        /// 크기가 바뀌면 <see cref="InsertCells"/>와 같은 규칙으로 뷰포트 맨 앞 항목보다 앞의 크기 변화만큼 스크롤 위치를 옮겨 화면을 지키고(맨 앞 항목 자신이면 같은 거리를 지킨다),
        /// 진행 중인 트윈·점프 정렬·보관한 앵커는 같은 항목을 계속 향한다.</para>
        /// <para>호출 시점 규칙은 <see cref="InsertCells"/>와 같다. 배치 안에서는 <see cref="EndUpdates"/>까지 미루고 뒤따른 연산의 인덱스 이동을 따라가며, 같은 배치에서 지워지면 아무것도 하지 않는다.
        /// 루프 모드와 델리게이트·셀 이벤트 콜백 안에서는 다른 증분 변경처럼 앵커 보존 리로드로 바뀐다.</para>
        /// </remarks>
        public void ReloadCellView(int dataIndex)
        {
            if (dataIndex < 0 || dataIndex >= PendingUpdateCount)
            {
                return;
            }

            BeginUpdates();
            try
            {
                _updateOps.Add(new UpdateOp { Type = UpdateOpType.Reload, A = dataIndex });
            }
            finally
            {
                EndUpdates();
            }
        }

        /// <summary>
        /// 바깥 EndUpdates. 기록한 연산을 증분으로 적용하거나(개수가 맞고 루프가 아닐 때, 갱신만 있으면 루프여도), 앵커 보존 리로드로 바꾸거나, 다시 읽을 요청에 맡긴다.
        /// 그다음 배치 중 미룬 리로드·재배치·정리·범위 갱신을 처리한다.
        /// 콜백 안(범위 갱신 중)에서 끝난 배치는 삽입·삭제·이동·다시 받기가 있으면 리로드를 미루고, 갱신만 있으면 배치 밖 갱신처럼 바로 처리한다(<see cref="ApplyRefreshesInCallback"/>).
        /// </summary>
        private void FinishUpdates()
        {
            List<UpdateOp> ops = _updateOps;
            int countDelta = _updateCountDelta;
            _updateCountDelta = 0;

            if (ops.Count > 0 && _inRangeUpdate)
            {
                if (ChangesLayout(ops))
                {
                    // 범위 갱신 콜백 안에서 끝난 삽입·삭제·이동·다시 받기. 델리게이트는 이미 최종 상태이므로 범위 갱신 뒤 앵커 보존 리로드로 같은 결과를 낸다.
                    ops.Clear();
                    ScheduleStructuralReload();
                }
                else
                {
                    // 갱신만 있으면 레이아웃·인덱스가 그대로이므로 배치 밖 RefreshCells처럼 처리한다 (리로드하지 않는다).
                    ApplyRefreshesInCallback(ops);
                }

                return;
            }

            // 적용하는 동안 콜백이 부른 연산은 예비 목록에 들어가게 바꿔 끼운다. 콜백 안에서 끝난 빈 배치는 적용할 것이 없으므로 바꾸지 않는다
            // (바깥에서 적용 중인 목록이 다시 기록 목록이 되면 그 뒤 콜백이 기록한 연산이 적용 중인 목록에 섞인다).
            bool swapLists = !_inRangeUpdate;
            if (swapLists)
            {
                _updateOps = _spareUpdateOps;
                _spareUpdateOps = ops;
            }

            try
            {
                // 로드 전이면 첫 리로드가, 다시 읽을 요청(리로드·델리게이트 교체·축 전환·위치 유지 리로드)이 있으면 그 요청이 최종 데이터를 읽는다.
                bool rereads = _reloadPending || (_relayoutPending && _relayoutRequery);
                if (rereads && HasContentUpdates(ops))
                {
                    // 기록한 내용 갱신·다시 받기를 그 요청이 대신 처리한다. 키 유지 리로드여도 셀을 모두 다시 바인딩하게 한다.
                    _forceRebindOnReload = true;
                }

                if (_initialized && _hasLoaded && !rereads)
                {
                    // 연산이 없는 배치도 개수를 맞춰 본다 (사이 코드가 예외로 연산을 알리지 못한 채 finally에서 닫힌 배치 등).
                    // 리로드로 바꾸는 경우는 PreserveCellsById와 무관하게 모든 셀을 다시 바인딩한다 (알리지 못한 변경·버린 내용 갱신까지 맞춘다).
                    int expected = _layout.DataCount + countDelta;
                    int actual = _delegate != null ? Mathf.Max(0, _delegate.GetNumberOfCells(this)) : 0;
                    if (actual != expected)
                    {
                        Debug.LogWarning(
                            $"[CyScroller] 증분 연산을 반영한 개수({expected})가 GetNumberOfCells({actual})와 다릅니다. ReloadData(ReloadAnchor.FirstVisible)로 다시 읽습니다.", this);
                        RequestReload(ReloadAnchor.FirstVisible, 0f, false);
                    }
                    else if (ops.Count == 0)
                    {
                        // 바뀐 것이 없다.
                    }
                    else if (!ChangesLayout(ops))
                    {
                        // 내용 갱신만 있다. 레이아웃·인덱스가 그대로이므로 루프 모드에서도 활성 셀(사본 포함)에 바로 부른다.
                        ApplyRefreshes(ops);
                    }
                    else if (_loop || _layout.IsLoop)
                    {
                        if (OnlyResizes(ops))
                        {
                            // 크기 변경(과 내용 갱신)만 있다. 리로드 대신 크기를 바로 바꾸고 위치를 지키는 재배치로 맞춘다 (트윈·정렬은 이어 간다).
                            ApplyLoopResizes(ops);
                        }
                        else
                        {
                            // 루프는 같은 데이터가 여러 슬롯에 있어 증분으로 옮기지 않고 앵커를 지키는 전체 리로드로 바꾼다.
                            RequestReload(ReloadAnchor.FirstVisible, 0f, false);
                        }
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
            // 콜백 안(범위 갱신 중)에서 끝난 배치는 범위를 맞출 수 없으므로 미룬 범위 갱신을 지우지 않고 바깥 갱신에 남긴다 (지우면 바깥 배치가 미룬 것까지 잃는다).
            // 키 유지 리로드·재배치가 셀을 새 인덱스로 맞추기 전(다시 읽는 중의 델리게이트·트윈 멈춤 알림 안)에 끝난 배치도 같다. 배치가 반쯤 읽혔을 수 있으므로
            // 그 리로드·재배치가 셀을 맞춘 뒤 범위 갱신에서 미룬 정리·리로드까지 처리한다.
            if (_updateDepth == 0 && !_inRangeUpdate && !_deferRefreshes && (_rangeUpdateDeferred || HasPendingWork))
            {
                _rangeUpdateDeferred = false;
                UpdateActiveRange();
            }
        }

        /// <summary>
        /// 연산을 레이아웃·항목 ID·위치·활성 셀에 증분으로 반영한다. 범위 갱신 중(_inRangeUpdate)으로 돌리므로 그 사이 사용자 코드가 부른
        /// 리로드·재배치·정리·연산은 끝난 뒤로 미뤄지고 범위 갱신은 호출자가 마지막에 한다. 사용자 코드 예외로 멈추면 앵커 보존 리로드를 미뤄 다음 갱신에 맞춘다.
        /// 활성 셀을 새 인덱스로 맞추기 전에 사용자 코드가 부른 <see cref="RefreshCells"/>는 맞춘 뒤 남은 셀에 부른다 (<see cref="ReconcileActiveCells"/>).
        /// </summary>
        private void ApplyIncrementalUpdates(List<UpdateOp> ops)
        {
            using (_applyUpdatesMarker.Auto())
            {
                bool completed = false;
                _inRangeUpdate = true;
                _deferRefreshes = true;
                try
                {
                    ApplyIncrementalUpdatesCore(ops);
                    completed = true;
                }
                finally
                {
                    _inRangeUpdate = false;
                    _deferRefreshes = false;
                    _deferredRefreshes.Clear();
                    _reconcileCells.Clear();
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

        /// <summary>
        /// 갱신(<see cref="RefreshCells"/>)만 있는 배치. 레이아웃·인덱스가 그대로이므로 활성 셀(루프 사본 포함)마다 그 항목에 모은 changeMask로
        /// <see cref="CyScrollerCellView.RefreshCellView(int)"/>를 한 번 부른다. 사용자 코드는 범위 갱신 중으로 돌려 활성 목록이 바뀌지 않게 하고(그 사이 요청은 미룬다),
        /// 콘텐츠가 옮겨졌으면 호출자(<see cref="FinishUpdates"/>)가 범위를 맞춘다. 재배치가 기다리면 그쪽이 셀을 모두 다시 바인딩하고,
        /// 배치 중 미룬 <see cref="ClearActive"/>가 있으면 셀을 파괴하므로 부르지 않는다. 키 유지 리로드·재배치가 셀을 새 인덱스로 맞추기 전이면
        /// (다시 읽는 중의 델리게이트·트윈 멈춤 알림 안에서 닫은 배치) 배치 밖 <see cref="RefreshCells"/>처럼 맞춘 뒤 남은 셀에 부르도록 미룬다.
        /// </summary>
        private void ApplyRefreshes(List<UpdateOp> ops)
        {
            if (_deferRefreshes)
            {
                // 활성 셀이 아직 옛 인덱스다. 셀을 새 인덱스로 맞춘 뒤 남은 셀에 부른다 (갱신만 있는 배치라 인덱스가 지금 데이터 기준이므로 그대로 옮겨 적는다).
                for (int i = 0; i < ops.Count; i++)
                {
                    _deferredRefreshes.Add(ops[i]);
                }

                return;
            }

            if (_relayoutPending || _clearActivePending)
            {
                // 재배치가 셀을 다시 바인딩한다 (갱신을 버리지 않게 키 유지 없이).
                _forceRebindOnReload |= _relayoutPending;
                return;
            }

            float position = ScrollPosition;
            _inRangeUpdate = true;
            try
            {
                for (int i = 0; i < _activeCells.Count; i++)
                {
                    CyScrollerCellView cell = _activeCells[i];
                    if (cell == null)
                    {
                        continue;
                    }

                    int mask = GetRefreshMask(ops, cell.DataIndex);
                    if (mask != 0)
                    {
                        cell.RefreshCellView(mask);
                    }
                }
            }
            finally
            {
                _inRangeUpdate = false;
            }

            if (HasMovedFrom(position) || _recenterDeferred || _snapEventPending)
            {
                _rangeUpdateDeferred = true;
            }
        }

        /// <summary>
        /// 델리게이트·셀 이벤트 콜백 안(범위 갱신 중)에서 끝난 갱신만 있는 배치. 배치 밖 <see cref="RefreshCells"/>와 같은 경로로 처리하고 리로드하지 않는다.
        /// 증분 변경을 적용하는 중이라 활성 셀이 아직 배치 전 인덱스일 수 있으면 셀을 새 인덱스로 맞춘 뒤 부르도록 미루고,
        /// 아니면 활성 셀(루프 사본 포함)마다 그 항목에 모은 changeMask로 한 번 부른다. 리로드·재배치가 기다리면 그쪽이 셀을 다시 바인딩하므로 부르지 않는다.
        /// 끝나면 연산 목록은 비어 있다. 범위는 바깥 범위 갱신이 맞춘다. 할당 없음 (작업 목록 용량을 재사용한다).
        /// </summary>
        /// <remarks>
        /// 연산 목록은 바꿔 끼우지 않는다 (예비 목록이 바깥에서 적용 중인 목록일 수 있다). 대신 셀별 마스크를 먼저 모으고 목록을 비운 뒤 사용자 코드를 부르므로,
        /// <see cref="CyScrollerCellView.RefreshCellView(int)"/> 안에서 연 배치도 이 목록을 다시 쓸 수 있다.
        /// </remarks>
        private void ApplyRefreshesInCallback(List<UpdateOp> ops)
        {
            if (_deferRefreshes)
            {
                for (int i = 0; i < ops.Count; i++)
                {
                    _deferredRefreshes.Add(ops[i]);
                }

                ops.Clear();
                return;
            }

            if (!_hasLoaded || _reloadPending || _relayoutPending)
            {
                // 기다리는 리로드·재배치가 셀을 다시 바인딩한다 (갱신을 버리지 않게 키 유지 없이).
                _forceRebindOnReload |= _reloadPending || _relayoutPending;
                ops.Clear();
                return;
            }

            List<int> masks = _callbackRefreshMasks;
            int start = masks.Count;
            int count = _activeCells.Count;
            for (int i = 0; i < count; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                masks.Add(cell != null ? GetRefreshMask(ops, cell.DataIndex) : 0);
            }

            ops.Clear();
            try
            {
                // 범위 갱신 중이라 활성 목록은 그대로다. 그래도 목록이 줄었으면 남은 자리까지만 부른다.
                for (int i = 0; i < count && i < _activeCells.Count; i++)
                {
                    int mask = masks[start + i];
                    CyScrollerCellView cell = _activeCells[i];
                    if (mask != 0 && cell != null)
                    {
                        cell.RefreshCellView(mask);
                    }
                }
            }
            finally
            {
                masks.RemoveRange(start, count);
            }
        }

        /// <summary>
        /// 배치 밖 갱신. [start, end) 항목의 활성 셀(루프 사본 포함)마다 <see cref="CyScrollerCellView.RefreshCellView(int)"/>를 부른다.
        /// 사용자 코드는 범위 갱신 중으로 돌려 활성 목록이 바뀌지 않게 하고(그 사이 리로드·정리·범위 갱신은 미룬다), 끝난 뒤 콘텐츠가 옮겨졌거나 미룬 작업이 있으면 범위를 맞춘다.
        /// 콜백 안(이미 범위 갱신 중)이면 바깥 범위 갱신이 맞춘다.
        /// </summary>
        private void RefreshCellsNow(int start, int end, int changeMask)
        {
            bool nested = _inRangeUpdate;
            float position = ScrollPosition;
            _inRangeUpdate = true;
            try
            {
                for (int i = 0; i < _activeCells.Count; i++)
                {
                    CyScrollerCellView cell = _activeCells[i];
                    if (cell != null && cell.DataIndex >= start && cell.DataIndex < end)
                    {
                        cell.RefreshCellView(changeMask);
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

        /// <summary>갱신 연산 중 index 항목을 덮는 것들의 changeMask를 OR로 모은다. 인덱스가 그대로인 연산 목록에 쓴다. O(연산 수), 할당 없음.</summary>
        private static int GetRefreshMask(List<UpdateOp> ops, int index)
        {
            int mask = 0;
            for (int i = 0; i < ops.Count; i++)
            {
                UpdateOp op = ops[i];
                if (op.Type == UpdateOpType.Refresh && index >= op.A && index - op.A < op.B)
                {
                    mask |= op.Mask;
                }
            }

            return mask;
        }

        /// <summary>레이아웃이나 바인딩을 바꾸는 연산(삽입·삭제·이동·다시 받기)이 있는지. 갱신만 있으면 false.</summary>
        private static bool ChangesLayout(List<UpdateOp> ops)
        {
            for (int i = 0; i < ops.Count; i++)
            {
                if (ops[i].Type != UpdateOpType.Refresh)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>내용을 바꾸는 연산(갱신·다시 받기)이 있는지. 구조 연산(삽입·삭제·이동)만 있으면 false.</summary>
        private static bool HasContentUpdates(List<UpdateOp> ops)
        {
            for (int i = 0; i < ops.Count; i++)
            {
                UpdateOpType type = ops[i].Type;
                if (type == UpdateOpType.Refresh || type == UpdateOpType.Reload)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyIncrementalUpdatesCore(List<UpdateOp> ops)
        {
            // 배치 중 미룬 정리부터 한다 (활성 셀을 파괴했으면 아래에서 새로 바인딩한다).
            ApplyPendingClears();

            int oldCount = _layout.DataCount;
            float previousPosition = ReadPosition(_appliedVertical);
            CyScrollerAnchor anchor = CaptureLayoutAnchor(false, out float overscroll);

            // 크기 변경이 셀 가장자리(Start·End)를 지키라고 했으면 배치 전 그 가장자리 위치를 적어 둔다 (마지막 요청을 따른다).
            int edgeItem = FindResizeEdgeItem(ops, out ResizeAnchor edgeAnchor);
            float edgeBefore = edgeItem < 0 ? 0f
                : edgeAnchor == ResizeAnchor.Start ? _layout.GetSlotStart(edgeItem) : _layout.GetSlotEnd(edgeItem);

            // 진행 중인 크기 애니메이션도 같은 항목을 따라간다. 지워지거나 같은 항목을 다시 받거나 크기를 새로 요청했으면 버린다.
            MapResizeAnimationsThroughUpdates(ops);

            // 트윈·정렬 대상을 같은 항목으로 옮긴다. 지워졌으면 트윈을 멈추고(완료 콜백·스냅 이벤트 없음) 정렬을 푼다.
            // 끝난 점프의 정렬 대상이 MoveCell로 옮겨졌으면 정렬을 푼다. 따라가면 화면이 옮겨진 항목으로 튀므로 보던 화면을 지킨다(맨 앞 항목 이동과 같은 규칙).
            // 진행 중인 트윈은 옮겨진 항목을 따라간다.
            bool tweening = _tweening;
            int alignIndex = -1;
            if (_alignmentActive)
            {
                alignIndex = MapThroughUpdates(ops, _align.Slot, out bool alignRemoved, out bool alignMoved);
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
                else if (alignMoved && !tweening)
                {
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

            // 위치: 트윈은 맨 앞 항목(크기 변경이 가장자리를 정했으면 그 가장자리) 기준 화면을 지키고 시작점을 다시 잡는다.
            // 정렬은 같은 항목에 다시 맞추고, 아니면 같은 기준으로 지킨다.
            float preservedDelta = edgeItem >= 0
                ? GetResizeEdgeDelta(ops, edgeItem, edgeAnchor, edgeBefore, in anchor, overscroll, previousPosition)
                : GetPreservedPositionDelta(ops, in anchor, overscroll, previousPosition);
            if (tweening)
            {
                ShiftScrollPosition(preservedDelta);
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
                ShiftScrollPosition(preservedDelta);
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
        /// 크기·항목 ID 배열을 연산 순서대로 옮기고(Array.Copy), 삽입한 자리와 다시 받을 항목(Reload)·바로 바꿀 크기 변경(Resize)만 델리게이트에 크기·ID를 최종 인덱스로 묻는다.
        /// 애니메이션 걸음(SetSize)은 묻지 않고 그 크기로 바꾸고, 애니메이션할 크기 변경은 끝에 목표 크기만 물어 애니메이션을 시작한다.
        /// ID → 인덱스 사전은 구조 연산이 바꾼 가장 앞 자리(다시 받은 항목의 ID가 바뀌었으면 그 자리)부터 다시 맞춘다. 접두합은 다음 Build가 바뀐 자리부터 다시 더한다.
        /// </summary>
        private void ApplyUpdatesToLayout(List<UpdateOp> ops, int oldCount)
        {
            // 구조 연산(삽입·삭제·이동)이 바꾼 가장 앞 자리. 그 앞 항목은 자리와 ID가 그대로다(삽입한 항목도 모두 이 뒤에 있다).
            // 다시 받을 항목은 자리를 바꾸지 않으므로 크기를 찾기 시작할 자리에만 넣는다 (연산을 거쳐도 이 자리 앞으로는 가지 않는다).
            int firstChanged = oldCount;
            int firstQueried = oldCount;
            for (int i = 0; i < ops.Count; i++)
            {
                UpdateOp op = ops[i];
                switch (op.Type)
                {
                    case UpdateOpType.Insert:
                    case UpdateOpType.Remove:
                        firstChanged = Mathf.Min(firstChanged, op.A);
                        break;
                    case UpdateOpType.Move:
                        firstChanged = Mathf.Min(firstChanged, Mathf.Min(op.A, op.B));
                        break;
                    case UpdateOpType.Reload:
                        firstQueried = Mathf.Min(firstQueried, op.A);
                        break;
                    case UpdateOpType.Resize:
                        if (op.Duration <= 0f)
                        {
                            firstQueried = Mathf.Min(firstQueried, op.A);
                        }

                        break;
                }
            }

            firstQueried = Mathf.Min(firstQueried, firstChanged);

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
                    case UpdateOpType.Move:
                        if (hasIds)
                        {
                            MoveItemIdSlot(op.A, op.B);
                        }

                        _layout.MoveSize(op.A, op.B);
                        break;
                    case UpdateOpType.Reload:
                        // 자리는 그대로 두고 크기만 받지 않은 상태로 만든다. ID 자리에는 옛 ID가 남아 뒤따른 연산과 같이 옮겨진다.
                        _layout.InvalidateSize(op.A);
                        break;
                    case UpdateOpType.Resize:
                        // 바로 바꾸는 크기 변경은 다시 받기처럼 크기를 다시 묻는다. 애니메이션은 아래에서 최종 인덱스로 목표만 묻는다.
                        if (op.Duration <= 0f)
                        {
                            _layout.InvalidateSize(op.A);
                        }

                        break;
                    case UpdateOpType.SetSize:
                        // 중간 걸음은 접두합을 다시 더하지 않는다 (10만 항목 앞쪽이어도 Build가 O(1)). 마지막 걸음은 접어서 한 번만 다시 더한다.
                        if (op.B != 0)
                        {
                            _layout.SetSize(op.A, op.Size);
                        }
                        else
                        {
                            _layout.SetSizeDeferred(op.A, op.Size);
                        }

                        break;
                }
            }

            // 아직 크기를 받지 않은 자리가 이번 배치에 삽입했거나 다시 받을 항목이다 (연산을 거치며 같이 옮겨졌다).
            int newCount = _layout.DataCount;
            bool fetchedIds = false;
            for (int i = firstQueried; i < newCount; i++)
            {
                if (!_layout.IsSizeUnset(i))
                {
                    continue;
                }

                _layout.SetSize(i, _delegate != null ? _delegate.GetCellViewSize(this, i) : 0f);
                if (!hasIds)
                {
                    continue;
                }

                long id = _idProvider.GetItemId(this, i);
                if (i < firstChanged)
                {
                    // 구조 연산 앞에서 다시 받은 항목이다. ID가 그대로면 사전도 그대로다.
                    if (id == _itemIds[i])
                    {
                        continue;
                    }

                    // ID가 바뀌었으면 사전을 이 자리부터 다시 맞춘다 (같은 ID가 여럿이면 앞 인덱스가 이기는 규칙을 지킨다).
                    // 아직 덮어쓰지 않았으므로 이 자리부터는 옛 ID다.
                    RemoveItemIdEntries(i, firstChanged);
                    firstChanged = i;
                }

                _itemIds[i] = id;
                fetchedIds = true;
            }

            if (hasIds)
            {
                AddItemIdEntries(firstChanged, newCount, fetchedIds);
            }

            StartRequestedResizeAnimations(ops);
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
        /// 연산을 반영한 배치·위치에 활성 셀을 맞춘다. 옛 활성 셀마다 연산을 거친 새 인덱스와 배치에서 모은 changeMask를 구해(<see cref="MapActiveCellThroughUpdates"/>)
        /// <see cref="ApplyReconcileTargets"/>로 맞춘다. 남은 셀은 다시 바인딩하지 않는다(다시 받을 항목의 셀은 회수하고 새로 바인딩한다).
        /// </summary>
        private void ReconcileActiveCells(List<UpdateOp> ops)
        {
            ComputeActiveRange(ScrollPosition, out int first, out int last, out int visibleFirst, out int visibleLast);

            // 옛 활성 셀(목록 i = 옛 슬롯 _activeFirst + i)마다 새 슬롯과 배치에서 모은 changeMask.
            // 지워졌거나 다시 받을 항목이거나 새 활성 범위 밖이면 슬롯은 -1.
            int oldFirst = _activeFirst;
            List<int> targets = _reconcileTargets;
            List<int> masks = _reconcileMasks;
            targets.Clear();
            masks.Clear();
            for (int i = 0; i < _activeCells.Count; i++)
            {
                int target = -1;
                int mask = 0;
                if (_activeCells[i] != null)
                {
                    target = MapActiveCellThroughUpdates(ops, oldFirst + i, out mask);
                    if (target < first || target > last)
                    {
                        target = -1;
                    }
                }

                targets.Add(target);
                masks.Add(mask);
            }

            ApplyReconcileTargets(first, last, visibleFirst, visibleLast);
        }

        /// <summary>
        /// 옛 활성 셀(목록 i)을 <see cref="_reconcileTargets"/>[i] 새 슬롯(-1이면 회수)으로 옮겨 새 활성 범위 [first, last]·표시 범위에 맞춘다.
        /// 표시 끝 → 회수 → 인덱스 변경 알림·재배치 → 내용 갱신(<see cref="_reconcileMasks"/>와 맞추기 전에 미룬 갱신) → 새 자리 바인딩 → 표시 시작 순서이고,
        /// 남은 셀은 다시 바인딩하지 않는다. 계속 보이는 셀에는 표시 이벤트가 없다. 활성 목록·범위는 인덱스 변경 알림 전에 맞춘다.
        /// 증분 변경과 키 유지 리로드가 같이 쓴다. 새 슬롯은 [first, last] 안이고 서로 달라야 한다(루프면 데이터 인덱스는 슬롯에서 구한다). 할당 없음.
        /// </summary>
        private void ApplyReconcileTargets(int first, int last, int visibleFirst, int visibleLast)
        {
            List<int> targets = _reconcileTargets;
            List<int> masks = _reconcileMasks;

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

                int dataIndex = _layout.SlotToDataIndex(target);
                _activeCells[target - first] = cell;
                previousIndices[target - first] = cell.DataIndex;
                cell.DataIndex = dataIndex;
                cell.CellIndex = target;
                AssignItemId(cell, dataIndex);
            }

            _activeFirst = hasRange ? first : 0;
            _activeLast = hasRange ? last : -1;
            _visibleFirst = visibleFirst <= visibleLast ? visibleFirst : 0;
            _visibleLast = visibleFirst <= visibleLast ? visibleLast : -1;
            _cellPositionsDirty = true;

            // 남은 셀이 새 인덱스를 가졌다. 이 뒤에 사용자 코드가 부른 RefreshCells는 바로 적용한다.
            _deferRefreshes = false;

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
                if (previousIndex == cell.DataIndex)
                {
                    // 같은 항목 자리(루프면 다른 사본 슬롯일 수 있다)다. 알림 없이 옮긴다.
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

            // 5. 내용 갱신: 남은 셀마다 배치에서 모은 changeMask와 적용 중(셀을 맞추기 전) 콜백이 부른 RefreshCells를 OR로 합쳐 한 번 부른다.
            //    새로 바인딩할 셀(새 자리·삽입·다시 받을 항목)은 최신 데이터로 바인딩하므로 부르지 않는다. 리로드·재배치가 기다리면 그쪽이 셀을 다시 바인딩한다
            //    (키 유지 리로드여도 이 갱신을 버리지 않게 모두 다시 바인딩하게 한다).
            if (_reloadPending || _relayoutPending)
            {
                bool dropped = _deferredRefreshes.Count > 0;
                for (int i = 0; i < targets.Count && !dropped; i++)
                {
                    dropped = targets[i] >= 0 && masks[i] != 0;
                }

                _forceRebindOnReload |= dropped;
            }
            else
            {
                for (int i = 0; i < previousCells.Count; i++)
                {
                    CyScrollerCellView cell = previousCells[i];
                    if (cell == null || targets[i] < 0)
                    {
                        continue;
                    }

                    int mask = masks[i] | GetRefreshMask(_deferredRefreshes, cell.DataIndex);
                    if (mask != 0)
                    {
                        cell.RefreshCellView(mask);
                    }
                }
            }

            previousCells.Clear();
            _deferredRefreshes.Clear();

            // 6. 새로 활성 범위에 들어온 자리만 델리게이트로 받는다.
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

            // 7. 표시 시작: 새 표시 범위에서 아직 보이지 않던 셀.
            for (int slot = _visibleFirst; slot <= _visibleLast; slot++)
            {
                BeginDisplay(GetActiveCellAt(slot));
            }
        }

        /// <summary>
        /// 앵커 보존 리로드(FirstVisible)를 미뤄 둔다. 이미 미룬 리로드가 있으면 그 요청이 최종 데이터를 다시 읽으므로 그대로 둔다.
        /// 기록한 다시 받기·내용 갱신까지 반영해야 하므로 <see cref="PreserveCellsById"/>와 무관하게 셀을 모두 다시 바인딩한다 (이미 미룬 키 유지 리로드도).
        /// </summary>
        private void ScheduleStructuralReload()
        {
            _forceRebindOnReload = true;
            if (_reloadPending)
            {
                return;
            }

            _reloadPending = true;
            _pendingReloadAnchor = ReloadAnchor.FirstVisible;
            _pendingReloadFactor = 0f;
            _pendingReloadPreservesCells = false;
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
                    case UpdateOpType.Move:
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
                    default:
                        // 갱신·다시 받기는 항목을 옮기지 않는다 (다시 받은 크기는 레이아웃이 반영한다).
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
            return MapThroughUpdates(ops, index, out removed, out _);
        }

        /// <summary><see cref="MapThroughUpdates(List{UpdateOp}, int, out bool)"/>에 더해, 그 항목 자신이 <see cref="MoveCell"/>로 옮겨졌으면 moved가 true다 (지워지기 전까지).</summary>
        private static int MapThroughUpdates(List<UpdateOp> ops, int index, out bool removed, out bool moved)
        {
            removed = false;
            moved = false;
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
                    case UpdateOpType.Move:
                        if (!removed && index == op.A && op.A != op.B)
                        {
                            moved = true;
                        }

                        index = MoveIndex(index, op.A, op.B);
                        break;
                    default:
                        // 갱신·다시 받기는 항목을 옮기지 않는다.
                        break;
                }
            }

            return index;
        }

        /// <summary>
        /// 배치 전 활성 셀의 인덱스가 연산을 모두 거친 뒤의 인덱스 (옮겨진 항목은 따라간다, <see cref="MapThroughUpdates"/>와 같다).
        /// 그 항목이 지워졌거나 다시 받을 항목(<see cref="ReloadCellView"/>)이면 셀을 회수하므로 -1이다.
        /// 지나온 갱신(<see cref="RefreshCells"/>)이 그 항목을 덮으면 changeMask를 OR로 모아 refreshMask에 준다. O(연산 수), 할당 없음.
        /// </summary>
        private static int MapActiveCellThroughUpdates(List<UpdateOp> ops, int index, out int refreshMask)
        {
            refreshMask = 0;
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
                            refreshMask = 0;
                            return -1;
                        }

                        break;
                    case UpdateOpType.Move:
                        index = MoveIndex(index, op.A, op.B);
                        break;
                    case UpdateOpType.Refresh:
                        if (index >= op.A && index - op.A < op.B)
                        {
                            refreshMask |= op.Mask;
                        }

                        break;
                    case UpdateOpType.Reload:
                        // 다시 받을 항목은 새로 바인딩한다 (모은 갱신은 쓸 일이 없다).
                        if (index == op.A)
                        {
                            refreshMask = 0;
                            return -1;
                        }

                        break;
                    default:
                        // 크기 변경은 셀을 그대로 두고 크기·위치만 바꾼다 (다시 바인딩하지 않는다).
                        break;
                }
            }

            return index;
        }

        /// <summary>from 항목을 빼서 to 자리에 넣었을 때(<see cref="MoveCell"/>) index 항목의 새 인덱스.</summary>
        private static int MoveIndex(int index, int from, int to)
        {
            if (index == from)
            {
                return to;
            }

            if (from < to)
            {
                return index > from && index <= to ? index - 1 : index;
            }

            return index >= to && index < from ? index + 1 : index;
        }
    }
}
