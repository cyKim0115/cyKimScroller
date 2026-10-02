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

        private float[] _sizes = Array.Empty<float>();
        private float[] _cycleStarts = Array.Empty<float>();

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

        /// <summary>데이터 개수를 정하고 크기 버퍼를 확보한다. 크기는 0으로 초기화되지 않는다.</summary>
        public void SetDataCount(int dataCount)
        {
            dataCount = Mathf.Max(0, dataCount);
            if (_sizes.Length < dataCount)
            {
                int capacity = Mathf.Max(dataCount, _sizes.Length * 2);
                _sizes = new float[capacity];
                _cycleStarts = new float[capacity];
            }

            DataCount = dataCount;
        }

        public void SetSize(int dataIndex, float size)
        {
            _sizes[dataIndex] = size > 0f ? size : 0f;
        }

        public float GetSize(int dataIndex) => _sizes[dataIndex];

        /// <summary>
        /// 크기를 다 넣은 뒤 호출한다. 루프 세트 수는 뷰포트·미리보기 길이에 따라 정한다.
        /// 한 사이클 길이가 0이면(셀 크기·간격이 모두 0) 루프를 끈다.
        /// </summary>
        public void Build(float spacing, float paddingBefore, float paddingAfter, bool loop,
            float viewportExtent, float lookAheadBefore = 0f, float lookAheadAfter = 0f)
        {
            Spacing = Mathf.Max(0f, spacing);
            PaddingBefore = paddingBefore;
            PaddingAfter = paddingAfter;

            float accumulated = 0f;
            for (int i = 0; i < DataCount; i++)
            {
                _cycleStarts[i] = accumulated;
                accumulated += _sizes[i] + Spacing;
            }

            CycleExtent = accumulated;
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
            return PaddingBefore + set * CycleExtent + _cycleStarts[dataIndex];
        }

        public float GetSlotSize(int slot) => _sizes[slot % DataCount];

        public float GetSlotEnd(int slot)
        {
            int set = slot / DataCount;
            int dataIndex = slot - set * DataCount;
            return PaddingBefore + set * CycleExtent + _cycleStarts[dataIndex] + _sizes[dataIndex];
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
    }
}
