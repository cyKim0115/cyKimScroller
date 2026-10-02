using UnityEngine;

namespace CyKim.Scroller
{
    // 셀 훅: 실제 뷰포트 기준 표시 이벤트(표시 범위 장부)와 뷰포트 위치 훅. 바인딩 버전은 활성화·회수 경로에서 올린다.
    public partial class CyScroller
    {
        // 실제 뷰포트(lookAhead 제외)에 걸친 슬롯 범위. 항상 활성 범위 안이고, 비었으면 (0, -1).
        // 범위 안의 셀은 모두 IsDisplayed이고 밖의 셀은 아니다. 범위를 먼저 맞춘 뒤 사용자 코드를 부른다.
        private int _visibleFirst;
        private int _visibleLast = -1;

        // 위치 훅: 범위·레이아웃이 바뀌어 다음 패스에서 모든 활성 셀에 알려야 하는지와 마지막으로 알린 스크롤 위치·뷰포트 길이.
        private bool _cellPositionsDirty;
        private float _notifiedScrollPosition;
        private float _notifiedViewportSize;

        // 슬롯 활성화 중 GetCellView(prefab)가 내준 셀. 델리게이트가 다른 셀을 돌려주면 그 셀의 바인딩 버전을 따로 올린다.
        private CyScrollerCellView _pendingBoundCell;

        private bool HasVisibleRange => _visibleFirst <= _visibleLast;

        /// <summary>표시 범위(실제 뷰포트에 걸친 슬롯) 첫 슬롯. 비었으면 -1. 인스펙터·테스트용.</summary>
        internal int DisplayedFirstSlot => HasVisibleRange ? _visibleFirst : -1;

        /// <summary>표시 범위 마지막 슬롯. 비었으면 -1.</summary>
        internal int DisplayedLastSlot => HasVisibleRange ? _visibleLast : -1;

        /// <summary>
        /// 위치 훅 패스. 켜져 있고, 스크롤 위치·뷰포트 길이가 지난 패스와 다르거나 범위·레이아웃이 바뀌었으면 활성 셀마다 위치를 알린다.
        /// LateUpdate 끝에서 부른다 (테스트가 직접 부를 수 있게 internal). 레이아웃 캐시로만 계산하고 할당하지 않는다.
        /// </summary>
        internal void NotifyCellPositionsIfChanged()
        {
            if (!_notifyCellPositions || !_hasLoaded)
            {
                return;
            }

            float position = ScrollPosition;
            float viewportSize = ScrollRectSize;
            if (!_cellPositionsDirty && position == _notifiedScrollPosition && viewportSize == _notifiedViewportSize)
            {
                return;
            }

            _cellPositionsDirty = false;
            _notifiedScrollPosition = position;
            _notifiedViewportSize = viewportSize;

            bool completed = false;
            try
            {
                // 콜백이 범위를 바꿀 수 있으므로 개수를 매번 다시 읽는다.
                for (int i = 0; i < _activeCells.Count; i++)
                {
                    CyScrollerCellView cell = _activeCells[i];
                    if (cell != null)
                    {
                        NotifyCellPosition(cell, position, viewportSize);
                    }
                }

                completed = true;
            }
            finally
            {
                // 사용자 코드가 예외를 던졌으면 다음 패스에서 다시 알린다.
                _cellPositionsDirty |= !completed;
            }
        }

        /// <summary>
        /// 셀 하나에 뷰포트 위치를 알린다. (셀 기준점 − 뷰포트 시작) / 뷰포트 길이. 뷰포트 길이가 0이면 부르지 않는다.
        /// </summary>
        private void NotifyCellPosition(CyScrollerCellView cell, float position, float viewportSize)
        {
            int slot = cell.CellIndex;
            if (viewportSize <= 0f || slot < 0 || slot >= _layout.SlotCount)
            {
                return;
            }

            float pivotPoint = _layout.GetSlotStart(slot) + _layout.GetSlotSize(slot) * _cellPositionPivot;
            float normalizedOffset = (pivotPoint - position) / viewportSize;
            CellViewPositionChanged?.Invoke(this, cell, normalizedOffset);
            cell.OnViewportPositionChanged(normalizedOffset);
        }

        /// <summary>
        /// 실제 뷰포트 (position, position + viewportSize)에 걸치는 슬롯 범위를 활성 범위로 자른 것. 경계에 맞닿기만 한 슬롯은 넣지 않는다.
        /// 비었으면 (0, -1).
        /// </summary>
        private void GetVisibleSlotRange(float position, float viewportSize, int activeFirst, int activeLast, out int first, out int last)
        {
            _layout.GetSlotRange(position, position + viewportSize, out first, out last);
            first = Mathf.Max(first, activeFirst);
            last = Mathf.Min(last, activeLast);
            if (first > last)
            {
                first = 0;
                last = -1;
            }
        }

        private static bool IsSameRange(int firstA, int lastA, int firstB, int lastB)
        {
            return firstA > lastA ? firstB > lastB : firstA == firstB && lastA == lastB;
        }

        private CyScrollerCellView GetActiveCellAt(int slot) => _activeCells[slot - _activeFirst];

        /// <summary>
        /// 표시 범위를 [first, last] 안으로 줄이고 벗어난 셀에 표시 끝을 알린다. 겹치지 않으면 모두 끝낸다 (뒤에서부터).
        /// 한 칸씩 범위를 먼저 줄인 뒤 알린다. 콜백이 콘텐츠를 position에서 옮기면 남은 셀은 두고 false를 돌려준다
        /// (호출자가 새 위치로 다시 맞춘다).
        /// </summary>
        private bool ShrinkVisibleRange(int first, int last, float position)
        {
            if (!HasVisibleRange)
            {
                return true;
            }

            if (first > last || last < _visibleFirst || first > _visibleLast)
            {
                while (HasVisibleRange)
                {
                    int slot = _visibleLast;
                    _visibleLast--;
                    EndDisplayAt(slot);
                    if (HasMovedFrom(position))
                    {
                        return false;
                    }
                }

                return true;
            }

            while (HasVisibleRange && _visibleFirst < first)
            {
                int slot = _visibleFirst;
                _visibleFirst++;
                EndDisplayAt(slot);
                if (HasMovedFrom(position))
                {
                    return false;
                }
            }

            while (HasVisibleRange && _visibleLast > last)
            {
                int slot = _visibleLast;
                _visibleLast--;
                EndDisplayAt(slot);
                if (HasMovedFrom(position))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 표시 범위를 [first, last]로 맞추고(지금 활성 범위로 자른다) 새로 걸친 셀에 표시 시작을 알린다. 셀 활성화가 끝난 뒤 부른다.
        /// 콜백이 콘텐츠를 position에서 옮기면 남은 셀에는 알리지 않는다 (옛 위치 기준으로는 뷰포트 밖일 수 있다. 호출자가 새 위치로 다시 맞춘다).
        /// </summary>
        private void GrowVisibleRange(int first, int last, float position)
        {
            first = Mathf.Max(first, _activeFirst);
            last = Mathf.Min(last, _activeLast);
            if (!ShrinkVisibleRange(first, last, position) || first > last)
            {
                return;
            }

            if (!HasVisibleRange)
            {
                _visibleFirst = first;
                _visibleLast = first;
                BeginDisplay(GetActiveCellAt(first));
                if (HasMovedFrom(position))
                {
                    return;
                }
            }

            // 지금 활성 범위 안인지도 매번 확인한다 (표시 범위 ⊆ 활성 범위 유지).
            while (HasVisibleRange && _visibleFirst > first && _visibleFirst > _activeFirst)
            {
                _visibleFirst--;
                BeginDisplay(GetActiveCellAt(_visibleFirst));
                if (HasMovedFrom(position))
                {
                    return;
                }
            }

            while (HasVisibleRange && _visibleLast < last && _visibleLast < _activeLast)
            {
                _visibleLast++;
                BeginDisplay(GetActiveCellAt(_visibleLast));
                if (HasMovedFrom(position))
                {
                    return;
                }
            }
        }

        /// <summary>보이던 셀 모두에 표시 끝을 알린다 (뒤에서부터). 표시 범위가 비었으면 아무것도 하지 않는다.</summary>
        private void EndDisplayAll()
        {
            while (HasVisibleRange)
            {
                int slot = _visibleLast;
                _visibleLast--;
                EndDisplayAt(slot);
            }
        }

        /// <summary>표시 범위에서 막 뺀 슬롯의 셀에 표시 끝을 알린다. 범위가 비었으면 (0, -1)로 정리한 뒤 부른다.</summary>
        private void EndDisplayAt(int slot)
        {
            CyScrollerCellView cell = GetActiveCellAt(slot);
            if (_visibleFirst > _visibleLast)
            {
                _visibleFirst = 0;
                _visibleLast = -1;
            }

            EndDisplay(cell);
        }

        /// <summary>
        /// 표시 시작. 표시 플래그를 먼저 세우고 이벤트 → 가상 메서드 순으로 부른다.
        /// 이벤트 핸들러가 예외를 던져도 <see cref="CyScrollerCellView.OnBecameVisible"/>은 불러 짝을 맞춘다.
        /// </summary>
        private void BeginDisplay(CyScrollerCellView cell)
        {
            if (cell == null || cell.IsDisplayed)
            {
                return;
            }

            cell.IsDisplayed = true;
            try
            {
                CellViewWillDisplay?.Invoke(this, cell);
            }
            finally
            {
                cell.OnBecameVisible();
            }
        }

        /// <summary>표시 끝. <see cref="BeginDisplay"/>와 같은 순서·예외 규칙으로 짝을 맞춘다.</summary>
        private void EndDisplay(CyScrollerCellView cell)
        {
            if (cell == null || !cell.IsDisplayed)
            {
                return;
            }

            cell.IsDisplayed = false;
            try
            {
                CellViewDidEndDisplay?.Invoke(this, cell);
            }
            finally
            {
                cell.OnBecameHidden();
            }
        }
    }
}
