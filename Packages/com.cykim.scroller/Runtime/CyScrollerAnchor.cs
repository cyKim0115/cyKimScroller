using System;

namespace CyKim.Scroller
{
    /// <summary>
    /// 스크롤 위치를 "어느 항목이 뷰포트 가장자리에서 얼마나 떨어져 있었는지"로 적은 값.
    /// <see cref="CyScroller.CaptureAnchor"/>로 얻고 <see cref="CyScroller.RestoreAnchor"/>·<see cref="CyScroller.ReloadData(in CyScrollerAnchor)"/>로 되돌린다.
    /// </summary>
    /// <remarks>
    /// 스크롤 좌표 대신 항목을 기준으로 삼으므로 셀 크기·개수가 바뀌어도 같은 항목을 같은 자리에 둘 수 있다.
    /// 항목 ID(<see cref="ICyScrollerItemIdProvider"/>)가 있으면 앞쪽에 항목이 삽입·삭제돼도 같은 항목을 찾는다.
    /// 직렬화할 수 있어 화면을 닫았다 다시 열 때 위치를 되살리는 데 쓸 수 있다.
    /// </remarks>
    [Serializable]
    public struct CyScrollerAnchor
    {
        /// <summary>
        /// 기준 항목의 데이터 인덱스. ID가 없거나 ID로 항목을 못 찾으면 이 인덱스를 쓴다 (개수를 넘으면 마지막 항목).
        /// 음수면 빈 앵커다 (빈 목록에서 캡처). 빈 앵커는 <see cref="Trailing"/>이면 끝, 아니면 처음으로 복원된다.
        /// </summary>
        public int DataIndex;

        /// <summary>기준 항목의 안정 ID. <see cref="HasItemId"/>가 false면 쓰지 않는다.</summary>
        public long ItemId;

        /// <summary>
        /// <see cref="ItemId"/>가 유효한지. true면 복원할 때 <see cref="DataIndex"/>보다 ID로 먼저 찾는다
        /// (<see cref="DataIndex"/> 자리의 항목이 같은 ID면 그 자리라서, 같은 ID가 여럿이어도 데이터가 그대로면 제자리로 돌아온다).
        /// </summary>
        public bool HasItemId;

        /// <summary>
        /// 기준 항목 셀과 뷰포트 가장자리 사이의 스크롤 축 거리.
        /// <see cref="Trailing"/>이 false면 셀 시작 → 뷰포트 시작 거리(뷰포트 시작 − 셀 시작)로, 셀이 뷰포트 앞으로 잘려 나간 길이다.
        /// true면 뷰포트 끝 → 셀 끝 거리(셀 끝 − 뷰포트 끝)로, 셀이 뷰포트 뒤로 잘려 나간 길이다.
        /// 셀이 그 가장자리에 걸쳐 있으면 0 이상 셀 크기 미만이고, 가장자리가 패딩 안이면 음수, 셀 사이 간격 안이면 셀 크기 이상이다.
        /// </summary>
        public float Offset;

        /// <summary>
        /// false면 뷰포트 시작(위·왼쪽)에 걸친 항목 기준, true면 뷰포트 끝(아래·오른쪽)에 걸친 항목 기준.
        /// 끝 기준은 그 항목이나 앞쪽 셀 크기가 바뀌어도 셀 끝이 뷰포트 끝에서 같은 거리에 남는다 (아래쪽을 기준으로 보는 채팅 등).
        /// </summary>
        public bool Trailing;

        /// <summary>기준 항목이 있는지 (<see cref="DataIndex"/> 0 이상). 빈 목록에서 캡처한 앵커는 false.</summary>
        public bool IsValid => DataIndex >= 0;
    }
}
