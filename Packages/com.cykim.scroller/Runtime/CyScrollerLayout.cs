using System;
using UnityEngine;

namespace CyKim.Scroller
{
    /// <summary>
    /// 스크롤 축 1차원 레이아웃 모델. 셀 크기·간격·패딩으로 슬롯 위치를 계산하고
    /// 위치 구간에 걸치는 슬롯 범위를 이진 탐색으로 찾는다. Unity 오브젝트에 의존하지 않는다.
    /// </summary>
    /// <remarks>
    /// 좌표는 콘텐츠 시작(위·왼쪽)에서 스크롤 방향으로 잰 거리다.
    /// 루프 모드에서는 데이터 N개를 한 사이클로 보고 <see cref="SetCount"/>개 세트를 이어 붙인다.
    /// 슬롯 s의 데이터 인덱스는 s % N이다.
    /// </remarks>
    internal sealed class CyScrollerLayout
    {
        // 짧은 사이클(크기 0 셀 등)에서 세트 수가 폭주하지 않게 막는 상한.
        private const int MAX_LOOP_HALF_SETS = 512;

        // 아직 받지 않은 크기 (InsertSizes·InvalidateSize가 넣는다). SetSize는 음수를 0으로 자르므로 이 값은 그 둘만 만든다.
        private const float UNSET_SIZE = -1f;

        private float[] _sizes = Array.Empty<float>();
        private float[] _cycleStarts = Array.Empty<float>();

        // 앞에서부터 몇 개의 _cycleStarts가 지금 크기·간격에 맞는지. Build는 그다음부터만 다시 더한다.
        // 항목 i의 시작은 크기 [0, i)에만 달려 있으므로 i번 크기가 바뀌어도 i번 시작까지는 그대로다.
        // 끝 항목을 지우면 DataCount보다 클 수 있다 (지운 자리의 시작은 그 뒤에 붙일 항목의 시작으로 여전히 맞다).
        // 미룬 크기 변화(_deferred*)가 있으면 _cycleStarts는 그 변화 전 크기 기준이다.
        private int _validStarts;

        // 접두합을 다시 더하지 않고 미룬 크기 변화 (SetSizeDeferred). 항목 인덱스와 (지금 크기 − 접두합이 아는 크기).
        // 항목 j 시작 = _cycleStarts[j] + (인덱스 < j인 변화의 합). 크기 애니메이션처럼 몇 항목만 매 프레임 바뀔 때 Build를 O(1)로 둔다.
        // 다른 크기·개수 변경은 먼저 접는다(CommitDeferredSizes). 개수가 적으므로 선형으로 찾는다.
        private int[] _deferredIndices = Array.Empty<int>();
        private float[] _deferredDeltas = Array.Empty<float>();
        private int _deferredCount;

        public int DataCount { get; private set; }

        /// <summary>세트 수. 루프가 아니면 1, 루프면 3 이상의 홀수.</summary>
        public int SetCount { get; private set; } = 1;

        public int SlotCount => DataCount * SetCount;
        public bool IsLoop { get; private set; }
        public float Spacing { get; private set; }
        public float PaddingBefore { get; private set; }
        public float PaddingAfter { get; private set; }

        /// <summary>한 사이클 길이 = 셀 크기 합 + N × 간격 (사이클 사이 간격 포함).</summary>
        public float CycleExtent { get; private set; }

        /// <summary>콘텐츠 전체 길이 (패딩 포함).</summary>
        public float ContentExtent { get; private set; }

        /// <summary>가운데 세트 첫 슬롯 번호.</summary>
        public int MiddleSetFirstSlot => (SetCount / 2) * DataCount;

        /// <summary>가운데 세트 첫 셀 시작 위치.</summary>
        public float MiddleSetStart => PaddingBefore + (SetCount / 2) * CycleExtent;

        /// <summary>데이터 개수를 정하고 크기 버퍼를 확보한다. 크기는 0으로 초기화되지 않는다 (버퍼를 새로 만들면 내용을 옮기지 않는다).</summary>
        public void SetDataCount(int dataCount)
        {
            CommitDeferredSizes();
            dataCount = Mathf.Max(0, dataCount);
            if (_sizes.Length < dataCount)
            {
                int capacity = Mathf.Max(dataCount, _sizes.Length * 2);
                _sizes = new float[capacity];
                _cycleStarts = new float[capacity];
                _validStarts = 0;
            }

            DataCount = dataCount;
        }

        public void SetSize(int dataIndex, float size)
        {
            CommitDeferredSizes();
            _sizes[dataIndex] = size > 0f ? size : 0f;
            InvalidateStartsAfter(dataIndex);
        }

        /// <summary>
        /// dataIndex 크기를 바꾸되 접두합은 다시 더하지 않고 그 뒤 항목 위치에 변화량을 더해 계산한다 (크기 애니메이션의 한 걸음).
        /// 다음 <see cref="Build"/>는 O(1)이고, 위치 조회는 미룬 항목 수만큼 더 든다. 다른 크기·개수 변경이나 <see cref="CommitDeferredSizes"/>가 미룬 변화를 접는다.
        /// 루프 배치나 간격이 바뀌는 Build도 먼저 접는다. 미룬 항목 수가 늘 때 말고는 할당하지 않는다.
        /// </summary>
        public void SetSizeDeferred(int dataIndex, float size)
        {
            size = size > 0f ? size : 0f;
            float delta = size - _sizes[dataIndex];
            if (delta == 0f)
            {
                return;
            }

            _sizes[dataIndex] = size;
            if (dataIndex + 1 >= _validStarts)
            {
                // 이 뒤 시작은 어차피 다음 Build가 지금 크기로 다시 더한다.
                return;
            }

            for (int i = 0; i < _deferredCount; i++)
            {
                if (_deferredIndices[i] == dataIndex)
                {
                    _deferredDeltas[i] += delta;
                    return;
                }
            }

            if (_deferredCount == _deferredIndices.Length)
            {
                int capacity = Mathf.Max(4, _deferredCount * 2);
                Array.Resize(ref _deferredIndices, capacity);
                Array.Resize(ref _deferredDeltas, capacity);
            }

            _deferredIndices[_deferredCount] = dataIndex;
            _deferredDeltas[_deferredCount] = delta;
            _deferredCount++;
        }

        /// <summary>미룬 크기 변화가 있는지 (<see cref="SetSizeDeferred"/>).</summary>
        public bool HasDeferredSizes => _deferredCount > 0;

        /// <summary>
        /// 미룬 크기 변화를 접는다. 크기는 이미 지금 값이므로 가장 앞 미룬 자리 뒤 시작을 무효로 해 다음 <see cref="Build"/>가 그 자리부터 다시 더하게 한다.
        /// </summary>
        public void CommitDeferredSizes()
        {
            for (int i = 0; i < _deferredCount; i++)
            {
                InvalidateStartsAfter(_deferredIndices[i]);
            }

            _deferredCount = 0;
        }

        public float GetSize(int dataIndex) => _sizes[dataIndex];

        /// <summary>아직 받지 않은 크기인지 (<see cref="InsertSizes"/>로 끼웠거나 <see cref="InvalidateSize"/>로 지운 뒤 <see cref="SetSize"/> 전).</summary>
        public bool IsSizeUnset(int dataIndex) => _sizes[dataIndex] < 0f;

        /// <summary>
        /// index 크기를 받지 않은 상태로 되돌린다 (항목을 다시 받을 때). 자리는 그대로이고 <see cref="Build"/> 전에 <see cref="SetSize"/>로 채운다.
        /// index는 [0, DataCount) 안이어야 한다.
        /// </summary>
        public void InvalidateSize(int index)
        {
            CommitDeferredSizes();
            _sizes[index] = UNSET_SIZE;
            InvalidateStartsAfter(index);
        }

        /// <summary>
        /// index 앞에 크기 count개 자리를 끼운다 (뒤쪽은 Array.Copy로 민다). 끼운 자리는 받지 않은 크기라 <see cref="Build"/> 전에 <see cref="SetSize"/>로 채운다.
        /// 버퍼는 모자랄 때만 늘린다 (내용 유지). index는 [0, DataCount] 안이어야 한다.
        /// </summary>
        public void InsertSizes(int index, int count)
        {
            if (count <= 0)
            {
                return;
            }

            CommitDeferredSizes();
            EnsureCapacityPreserving(DataCount + count);
            Array.Copy(_sizes, index, _sizes, index + count, DataCount - index);
            for (int i = index; i < index + count; i++)
            {
                _sizes[i] = UNSET_SIZE;
            }

            DataCount += count;
            InvalidateStartsAfter(index);
        }

        /// <summary>[index, index + count) 크기를 빼고 뒤쪽을 당긴다. 구간은 [0, DataCount) 안이어야 한다.</summary>
        public void RemoveSizes(int index, int count)
        {
            if (count <= 0)
            {
                return;
            }

            CommitDeferredSizes();
            Array.Copy(_sizes, index + count, _sizes, index, DataCount - index - count);
            DataCount -= count;
            InvalidateStartsAfter(index);
        }

        /// <summary>fromIndex 크기를 빼서 toIndex 자리에 넣는다 (결과에서 그 크기는 toIndex에 있다). 두 인덱스는 [0, DataCount) 안이어야 한다.</summary>
        public void MoveSize(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex)
            {
                return;
            }

            CommitDeferredSizes();
            float size = _sizes[fromIndex];
            if (fromIndex < toIndex)
            {
                Array.Copy(_sizes, fromIndex + 1, _sizes, fromIndex, toIndex - fromIndex);
            }
            else
            {
                Array.Copy(_sizes, toIndex, _sizes, toIndex + 1, fromIndex - toIndex);
            }

            _sizes[toIndex] = size;
            InvalidateStartsAfter(Mathf.Min(fromIndex, toIndex));
        }

        /// <summary>
        /// 크기를 다 넣은 뒤 호출한다. 루프 세트 수는 뷰포트·미리보기 길이에 따라 정한다.
        /// 한 사이클 길이가 0이면(셀 크기·간격이 모두 0) 루프를 끈다.
        /// 간격이 그대로면 접두합은 마지막 Build 뒤 크기가 바뀐 가장 앞 자리부터만 다시 더한다 (처음부터 더한 것과 같은 값).
        /// </summary>
        public void Build(float spacing, float paddingBefore, float paddingAfter, bool loop,
            float viewportExtent, float lookAheadBefore = 0f, float lookAheadAfter = 0f)
        {
            spacing = Mathf.Max(0f, spacing);
            if (loop || spacing != Spacing)
            {
                // 미룬 크기 변화는 루프가 아닌 같은 간격 배치에서만 이어 쓴다.
                CommitDeferredSizes();
            }

            if (spacing != Spacing)
            {
                _validStarts = 0;
            }

            Spacing = spacing;
            PaddingBefore = paddingBefore;
            PaddingAfter = paddingAfter;

            // 처음부터 더할 때도 저장한 앞 항목 시작에 (앞 항목 크기 + 간격)을 더하므로, 바뀐 자리부터 이어 더해도 같은 float 값이 나온다.
            int count = DataCount;
            int start = Mathf.Min(_validStarts, count);
            if (start == 0 && count > 0)
            {
                _cycleStarts[0] = 0f;
                start = 1;
            }

            for (int i = start; i < count; i++)
            {
                _cycleStarts[i] = _cycleStarts[i - 1] + (_sizes[i - 1] + Spacing);
            }

            _validStarts = count;
            CycleExtent = count > 0 ? _cycleStarts[count - 1] + GetDeferredOffset(count - 1) + (_sizes[count - 1] + Spacing) : 0f;
            IsLoop = loop && DataCount > 0 && CycleExtent > 0f;
            SetCount = IsLoop
                ? ComputeLoopSetCount(CycleExtent, viewportExtent, Spacing, lookAheadBefore, lookAheadAfter)
                : 1;

            ContentExtent = DataCount == 0
                ? PaddingBefore + PaddingAfter
                : PaddingBefore + SetCount * CycleExtent - Spacing + PaddingAfter;
        }

        /// <summary>
        /// 루프 세트 수 (2m+1, 최소 5).
        /// 스크롤 위치는 가운데 세트 시작 ±반 사이클 창 안에 유지된다. 그 창의 양 끝에서도 미리보기 구간 바깥으로
        /// 한 사이클씩 여유가 남아야, 점프·스냅이 반대 방향으로 잘리거나 드래그가 콘텐츠 끝 탄성에 걸리지 않는다.
        /// 앞쪽: m·cycle − 1.5·cycle ≥ lookAheadBefore / 뒤쪽: m·cycle ≥ viewport + lookAheadAfter + spacing + 0.5·cycle.
        /// </summary>
        internal static int ComputeLoopSetCount(float cycleExtent, float viewportExtent, float spacing,
            float lookAheadBefore = 0f, float lookAheadAfter = 0f)
        {
            if (cycleExtent <= 0f)
            {
                return 1;
            }

            int front = Mathf.CeilToInt(lookAheadBefore / cycleExtent + 1.5f);
            int back = Mathf.CeilToInt((viewportExtent + lookAheadAfter + spacing) / cycleExtent + 0.5f);
            int half = Mathf.Clamp(Mathf.Max(2, Mathf.Max(front, back)), 2, MAX_LOOP_HALF_SETS);
            return 2 * half + 1;
        }

        /// <summary>
        /// 루프에서 position을 가운데 세트 시작 ±반 사이클 창으로 옮기려면 빼야 할 사이클 수. 루프가 아니면 0.
        /// </summary>
        public int GetRecenterCycles(float position)
        {
            if (!IsLoop)
            {
                return 0;
            }

            float offset = position - MiddleSetStart;
            float halfCycle = CycleExtent * 0.5f;
            if (offset >= -halfCycle && offset <= halfCycle)
            {
                return 0;
            }

            return Mathf.RoundToInt(offset / CycleExtent);
        }

        public int SlotToDataIndex(int slot)
        {
            if (DataCount == 0)
            {
                return -1;
            }

            int index = slot % DataCount;
            return index < 0 ? index + DataCount : index;
        }

        public float GetSlotStart(int slot)
        {
            int set = slot / DataCount;
            int dataIndex = slot - set * DataCount;
            return PaddingBefore + set * CycleExtent + _cycleStarts[dataIndex] + GetDeferredOffset(dataIndex);
        }

        public float GetSlotSize(int slot) => _sizes[slot % DataCount];

        public float GetSlotEnd(int slot)
        {
            int set = slot / DataCount;
            int dataIndex = slot - set * DataCount;
            return PaddingBefore + set * CycleExtent + _cycleStarts[dataIndex] + GetDeferredOffset(dataIndex) + _sizes[dataIndex];
        }

        /// <summary>
        /// 구간 (from, to)에 조금이라도 걸치는 슬롯 범위. 걸치는 슬롯이 없으면 first &gt; last.
        /// 경계가 정확히 맞닿는 슬롯(end == from, start == to)은 포함하지 않는다.
        /// </summary>
        public void GetSlotRange(float from, float to, out int first, out int last)
        {
            int count = SlotCount;
            if (count == 0 || to <= from)
            {
                first = 0;
                last = -1;
                return;
            }

            // end > from 인 첫 슬롯
            int lo = 0;
            int hi = count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (GetSlotEnd(mid) > from)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            first = lo;

            // start >= to 인 첫 슬롯의 바로 앞
            lo = first;
            hi = count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (GetSlotStart(mid) >= to)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            last = lo - 1;

            if (first > last)
            {
                first = 0;
                last = -1;
            }
        }

        /// <summary>start ≤ position 인 마지막 슬롯. 범위 밖이면 0 또는 마지막 슬롯으로 잘린다. 슬롯이 없으면 -1.</summary>
        public int GetSlotAtPosition(float position)
        {
            int count = SlotCount;
            if (count == 0)
            {
                return -1;
            }

            int lo = 0;
            int hi = count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (GetSlotStart(mid) > position)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            return Mathf.Clamp(lo - 1, 0, count - 1);
        }

        /// <summary>
        /// end ≥ position 인 첫 슬롯 (<see cref="GetSlotAtPosition"/>을 뒤쪽 기준으로 뒤집은 짝). position이 셀 사이 간격 안이면 간격 뒤 슬롯이다.
        /// 범위 밖이면 0 또는 마지막 슬롯으로 잘린다. 슬롯이 없으면 -1.
        /// </summary>
        public int GetTrailingSlotAtPosition(float position)
        {
            int count = SlotCount;
            if (count == 0)
            {
                return -1;
            }

            int lo = 0;
            int hi = count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (GetSlotEnd(mid) >= position)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            return Mathf.Min(lo, count - 1);
        }

        /// <summary>
        /// position에 가장 가까운 슬롯. 간격(gap) 안이면 앞뒤 셀 중 가장자리가 더 가까운 쪽을 고른다.
        /// </summary>
        public int GetNearestSlot(float position)
        {
            int slot = GetSlotAtPosition(position);
            if (slot < 0 || slot + 1 >= SlotCount)
            {
                return slot;
            }

            float end = GetSlotEnd(slot);
            if (position <= end)
            {
                return slot;
            }

            float nextStart = GetSlotStart(slot + 1);
            return position - end <= nextStart - position ? slot : slot + 1;
        }

        /// <summary>dataIndex 앞 항목의 미룬 크기 변화 합 (<see cref="SetSizeDeferred"/>). 미룬 것이 없으면 0. 할당 없음.</summary>
        private float GetDeferredOffset(int dataIndex)
        {
            float offset = 0f;
            for (int i = 0; i < _deferredCount; i++)
            {
                if (_deferredIndices[i] < dataIndex)
                {
                    offset += _deferredDeltas[i];
                }
            }

            return offset;
        }

        /// <summary>index 자리 크기가 바뀌었다. index번 시작까지는 그대로이고 그 뒤는 다음 Build가 다시 더한다.</summary>
        private void InvalidateStartsAfter(int index)
        {
            if (index + 1 < _validStarts)
            {
                _validStarts = index + 1;
            }
        }

        /// <summary>
        /// 버퍼가 required보다 작으면 두 배 이상으로 늘리고 옛 버퍼를 통째로 옮긴다.
        /// _validStarts는 DataCount보다 클 수 있으므로(끝 항목을 지운 직후면 DataCount번 시작도 맞은 값이다) [0, DataCount)만 옮기면 안 된다.
        /// </summary>
        private void EnsureCapacityPreserving(int required)
        {
            if (_sizes.Length >= required)
            {
                return;
            }

            int capacity = Mathf.Max(required, _sizes.Length * 2);
            var sizes = new float[capacity];
            var cycleStarts = new float[capacity];
            Array.Copy(_sizes, sizes, _sizes.Length);
            Array.Copy(_cycleStarts, cycleStarts, _cycleStarts.Length);
            _sizes = sizes;
            _cycleStarts = cycleStarts;
        }
    }
}
