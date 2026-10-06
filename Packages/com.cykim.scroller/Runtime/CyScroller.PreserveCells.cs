using System.Collections.Generic;
using UnityEngine;

namespace CyKim.Scroller
{
    // 키 유지 리로드(PreserveCellsById): 위치를 지키며 다시 읽을 때 항목 ID가 같은 활성 셀을 회수·재바인딩하지 않고 새 인덱스로 옮겨 쓴다.
    public partial class CyScroller
    {
        // 다음 리로드·재배치는 키 유지 없이 셀을 모두 다시 바인딩한다. 그 작업을 기다리는 동안 버린 내용 갱신·다시 받기가 있거나
        // (그 작업이 대신 처리한다) 델리게이트가 바뀌었을 때 켠다. 리로드·재배치가 키 유지 여부를 정할 때 지운다.
        private bool _forceRebindOnReload;

        // 키 유지 리로드 작업 목록 (용량을 재사용해 할당하지 않는다). 옛 활성 셀마다 다시 읽기 전 화면 위치(셀 시작 − 뷰포트 시작)와,
        // 새 활성 범위의 슬롯마다 그 자리로 옮기기로 한 옛 셀 번호(-1 = 비었음).
        private readonly List<float> _preservedOffsets = new List<float>(32);
        private readonly List<int> _preservedSlotOwners = new List<int>(32);

        /// <summary>
        /// 지금 활성 셀을 키 유지 리로드로 옮길 수 있는지: 옵션이 켜져 있고, 델리게이트가 ID 제공자이고, 로드된 활성 셀이 그 ID로 바인딩돼 있고,
        /// 모두 다시 바인딩해야 할 사유(<see cref="_forceRebindOnReload"/>)가 없다.
        /// </summary>
        private bool CanPreserveCells =>
            _preserveCellsById && _idProvider != null && _hasItemIds && _hasLoaded && _activeCells.Count > 0 && !_forceRebindOnReload;

        /// <summary>
        /// 키 유지 리로드 본체 (<see cref="ReloadNow"/>가 키 유지로 정한 뒤 부른다). 순서는 일반 리로드와 같되 셀을 먼저 회수하지 않고,
        /// 다시 읽어 위치를 정한 뒤(<see cref="MoveAfterReload"/>) ID가 같은 활성 셀을 새 인덱스로 옮긴다(<see cref="ReconcilePreservedCells"/>).
        /// </summary>
        /// <remarks>
        /// 셀을 맞추기 전에 사용자 코드(미룬 정리의 표시 끝, 트윈 멈춤 알림, 델리게이트)가 부른 <see cref="RefreshCells"/>(그 안에서 닫은 갱신만 있는 배치 포함)는
        /// 새 인덱스로 보고 맞춘 뒤 남은 셀에 부른다 (델리게이트는 이미 새 데이터다). 다시 읽는 도중 사용자 코드 예외로 멈추면 활성 셀이 옛 배치에 남을 수 있으므로
        /// 모두 다시 바인딩하는 앵커 보존 리로드를 미뤄 다음 갱신에 맞춘다(다시 읽다 멈췄으면 반쯤 읽은 배치 대신 다시 읽기 전 화면으로 간다, <see cref="ScheduleRecoveryReload"/>).
        /// 셀을 맞추기 전에 사용자 코드가 닫은 배치의 미룬 정리·리로드·범위 갱신은 맞춘 뒤 범위 갱신에서 처리한다. 키 유지 재배치(<see cref="ApplyRelayout"/>)도 같은 규칙이다.
        /// </remarks>
        private void ReloadPreservingCells(ReloadAnchor anchor, float factor)
        {
            bool rebuilding = false;
            bool rebuilt = false;
            bool completed = false;
            CyScrollerAnchor screen = default;
            _deferRefreshes = true;
            try
            {
                ApplyPendingClears();

                CancelTween();
                _snapArmed = false;
                _alignmentActive = false;
                _scrollRect.StopMovement();

                // 보관한 앵커가 있으면 그쪽이 가려던 자리이므로 지금 화면 대신 그 앵커를 쓴다.
                // 다시 읽기 전 화면은 늘 적어 둔다. 다시 읽다 멈추면 복구 리로드가 반쯤 읽은 배치 대신 이 앵커로 간다.
                bool keepVisible = !_hasPendingAnchor && (anchor == ReloadAnchor.FirstVisible || anchor == ReloadAnchor.LastVisible);
                screen = CaptureLayoutAnchor(anchor == ReloadAnchor.LastVisible, out float overscroll);
                RecordPreservedCellOffsets();

                rebuilding = true;
                RebuildLayout(true);
                rebuilt = true;
                _hasLoaded = true;
                _lastViewportExtent = ScrollRectSize;
                MoveAfterReload(anchor, factor, keepVisible, in screen, overscroll);

                ReconcilePreservedCells();
                completed = true;
            }
            finally
            {
                _deferRefreshes = false;
                _deferredRefreshes.Clear();
                if (rebuilding && !completed)
                {
                    ScheduleRecoveryReload(!rebuilt, in screen);
                }
            }

            UpdateActiveRange();
            ApplyScrollbarVisibility();
        }

        /// <summary>
        /// 키 유지 리로드·재배치가 사용자 코드 예외로 멈췄을 때 모두 다시 바인딩하는 앵커 보존 리로드를 미룬다(<see cref="ScheduleStructuralReload"/>).
        /// 다시 읽는 도중 멈췄으면(rebuildFailed) 배치가 반쯤 읽힌 채다(개수·크기·접두합·항목 ID가 서로 맞지 않는다). 복구 리로드가 그 배치에서 화면을 읽지 않도록
        /// 다시 읽기 전 화면 앵커(screen)를 보관한 앵커로 넘긴다. 이미 보관한 앵커가 있으면 그쪽이 가려던 자리이므로 그대로 둔다. 사용자 코드를 부르지 않는다.
        /// </summary>
        private void ScheduleRecoveryReload(bool rebuildFailed, in CyScrollerAnchor screen)
        {
            if (rebuildFailed && !_hasPendingAnchor)
            {
                _pendingAnchor = screen;
                _hasPendingAnchor = true;
            }

            ScheduleStructuralReload();
        }

        /// <summary>다시 읽기 전에 옛 활성 셀마다 화면 위치(셀 시작 − 뷰포트 시작)를 적는다. 루프에서 같은 항목의 사본을 고를 때 쓴다. 할당 없음.</summary>
        private void RecordPreservedCellOffsets()
        {
            List<float> offsets = _preservedOffsets;
            offsets.Clear();
            float position = ReadPosition(_appliedVertical);
            for (int i = 0; i < _activeCells.Count; i++)
            {
                offsets.Add(_layout.GetSlotStart(_activeFirst + i) - position);
            }
        }

        /// <summary>
        /// 키 유지 리로드의 셀 맞추기. 다시 읽은 배치와 맞춘 위치에서 옛 활성 셀마다 같은 ID 항목의 새 슬롯을 찾고(<see cref="FindPreservedSlot"/>),
        /// 증분 변경과 같은 순서로 맞춘다(<see cref="ApplyReconcileTargets"/>): 표시 끝 → 회수(지워진 ID·새 활성 범위 밖) → 인덱스 변경 알림·재배치 →
        /// 맞추기 전에 미룬 내용 갱신 → 새 자리 바인딩 → 표시 시작. 옮긴 셀은 다시 바인딩하지 않고(<see cref="CyScrollerCellView.BindVersion"/> 그대로) 새 크기로 배치한다.
        /// </summary>
        /// <remarks>
        /// 범위 갱신 중으로 돌려 사용자 코드가 부른 리로드·재배치·정리를 끝난 뒤로 미루고, 셀을 새 인덱스로 맞추기 전에 부른 <see cref="RefreshCells"/>는 맞춘 뒤 남은 셀에 부른다.
        /// 사용자 코드 예외로 멈추면 모두 다시 바인딩하는 앵커 보존 리로드를 미뤄 다음 갱신에 맞춘다. 범위 갱신은 호출자가 한다. 할당 없음.
        /// </remarks>
        private void ReconcilePreservedCells()
        {
            bool completed = false;
            _inRangeUpdate = true;
            _deferRefreshes = true;
            try
            {
                float position = ScrollPosition;
                ComputeActiveRange(position, out int first, out int last, out int visibleFirst, out int visibleLast);
                MapPreservedCells(position, first, last);
                ApplyReconcileTargets(first, last, visibleFirst, visibleLast);
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
        }

        /// <summary>
        /// 옛 활성 셀마다 옮길 새 슬롯(없으면 -1)을 <see cref="_reconcileTargets"/>에 적는다. 내용 갱신 마스크는 없다(키 유지 리로드는 내용 변경을 감지하지 않는다).
        /// 항목은 셀의 ID로 찾는다(이전 인덱스 자리에 같은 ID가 남아 있으면 그 자리, <see cref="ResolveItemIdIndex"/>). 한 슬롯에는 셀 하나만 옮긴다. O(활성 셀 수), 할당 없음.
        /// </summary>
        private void MapPreservedCells(float position, int first, int last)
        {
            List<int> targets = _reconcileTargets;
            List<int> masks = _reconcileMasks;
            List<int> owners = _preservedSlotOwners;
            targets.Clear();
            masks.Clear();
            owners.Clear();

            int length = first <= last ? last - first + 1 : 0;
            for (int i = 0; i < length; i++)
            {
                owners.Add(-1);
            }

            List<float> offsets = _preservedOffsets;
            for (int i = 0; i < _activeCells.Count; i++)
            {
                CyScrollerCellView cell = _activeCells[i];
                int target = -1;
                if (cell != null && cell.HasItemId && length > 0)
                {
                    int dataIndex = ResolveItemIdIndex(cell.ItemId, cell.DataIndex);
                    if (dataIndex >= 0)
                    {
                        // 다시 읽는 사이 사용자 코드가 활성 목록을 바꿨으면 적어 둔 화면 위치가 없을 수 있다 (사본 고르기에만 쓴다).
                        float offset = i < offsets.Count ? offsets[i] : 0f;
                        target = FindPreservedSlot(dataIndex, position + offset, first, last);
                    }
                }

                if (target >= 0)
                {
                    owners[target - first] = i;
                }

                targets.Add(target);
                masks.Add(0);
            }
        }

        /// <summary>
        /// dataIndex 항목의 새 활성 범위 [first, last] 안 빈 슬롯. 루프가 아니면 그 인덱스 자리 하나뿐이다.
        /// 루프면 그 항목의 사본 중 셀이 화면에 있던 자리(idealStart = 새 위치 기준 옛 셀 시작)에 가장 가까운 빈 사본부터 바깥으로 찾는다. 없으면 -1.
        /// 보통 O(1), 사본이 이미 차 있을수록 사본 수만큼까지, 할당 없음.
        /// </summary>
        private int FindPreservedSlot(int dataIndex, float idealStart, int first, int last)
        {
            List<int> owners = _preservedSlotOwners;
            if (!_layout.IsLoop)
            {
                return dataIndex >= first && dataIndex <= last && owners[dataIndex - first] < 0 ? dataIndex : -1;
            }

            // 범위 안 첫 사본과 사본 수. 같은 항목의 사본은 한 사이클(데이터 개수 슬롯, CycleExtent 거리)씩 떨어져 있다.
            int dataCount = _layout.DataCount;
            int firstCopy = first + (dataIndex - first % dataCount + dataCount) % dataCount;
            if (firstCopy > last)
            {
                return -1;
            }

            int copies = (last - firstCopy) / dataCount + 1;
            int nearest = Mathf.Clamp(Mathf.RoundToInt((idealStart - _layout.GetSlotStart(firstCopy)) / _layout.CycleExtent), 0, copies - 1);
            for (int step = 0; step < copies; step++)
            {
                int after = nearest + step;
                if (after < copies)
                {
                    int slot = firstCopy + after * dataCount;
                    if (owners[slot - first] < 0)
                    {
                        return slot;
                    }
                }

                int before = nearest - step;
                if (step > 0 && before >= 0)
                {
                    int slot = firstCopy + before * dataCount;
                    if (owners[slot - first] < 0)
                    {
                        return slot;
                    }
                }
            }

            return -1;
        }
    }
}
