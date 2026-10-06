using UnityEngine;

namespace CyKim.Scroller
{
    // 끝 근접 알림: 콘텐츠 처음·끝까지 남은 거리가 NearEdgeDistance 이하가 되면 가장자리마다 한 번 알리고 잠근다.
    // LateUpdate 끝에서 판단하므로 범위 갱신 콜백 밖이다. 할당 없음.
    public partial class CyScroller
    {
        // 잠긴 가장자리를 다시 여는 거리 배율. 경계에서 오가도 반복해 알리지 않게 한다.
        private const float NEAR_EDGE_REARM_FACTOR = 1.5f;

        private bool _nearStartLatched;
        private bool _nearEndLatched;

        // 래치를 맞춘 데이터 개수. 개수가 바뀌면(리로드·삽입·삭제) 두 가장자리를 다시 연다. -1이면 아직 판단 전.
        private int _nearEdgeCount = -1;

        /// <summary>
        /// 콘텐츠 처음(<see cref="ScrollEdge.Start"/>)·끝(<see cref="ScrollEdge.End"/>)까지 남은 거리가 <see cref="NearEdgeDistance"/> 이하가 됐을 때 가장자리마다 한 번.
        /// LateUpdate 끝에서 알리므로 핸들러에서 <see cref="InsertCells"/>·<see cref="ReloadData()"/>를 불러도 된다.
        /// </summary>
        /// <remarks>
        /// <para>알린 가장자리는 잠긴다. 남은 거리가 <see cref="NearEdgeDistance"/> × 1.5를 넘게 멀어지거나, 데이터 개수가 바뀌면(리로드·삽입·삭제가 끝난 뒤) 다시 열린다.
        /// 그래서 다음 페이지를 붙였는데도 아직 가장자리 근처면 다시 알리고, 개수가 그대로인 리로드는 다시 알리지 않는다.</para>
        /// <para>콘텐츠가 뷰포트보다 짧으면(스크롤할 길이가 없으면) <see cref="ScrollEdge.End"/>만 알린다(화면을 채울 만큼 더 불러오는 용도). 빈 목록도 같다.
        /// 루프 모드와 뷰포트 길이가 0일 때는 알리지 않는다. 처음 로드한 뒤에도 조건이 맞으면 알린다(맨 위에서 시작하면 Start).</para>
        /// </remarks>
        public event ScrollerNearEdgeHandler ScrollerNearEdge;

        /// <summary>
        /// 끝 근접으로 볼 남은 거리(px). 0이면 <see cref="ScrollerNearEdge"/>를 알리지 않는다 (기본).
        /// 남은 거리는 처음 쪽이 <see cref="ScrollPosition"/>, 끝 쪽이 <see cref="ScrollSize"/> − <see cref="ScrollPosition"/>이다.
        /// </summary>
        public float NearEdgeDistance
        {
            get => _nearEdgeDistance;
            set => _nearEdgeDistance = Mathf.Max(0f, value);
        }

        /// <summary>
        /// 끝 근접을 판단하고 새로 가까워진 가장자리를 알린다. LateUpdate가 부른다 (테스트가 직접 부를 수 있게 internal). 할당 없음.
        /// 증분 변경 배치가 열려 있거나 범위 갱신 중이면 건너뛴다(다음 LateUpdate가 판단한다).
        /// </summary>
        internal void UpdateNearEdges()
        {
            if (!_hasLoaded || _updateDepth > 0 || _inRangeUpdate)
            {
                return;
            }

            if (_nearEdgeDistance <= 0f || _loop || _layout.IsLoop || ScrollRectSize <= 0f)
            {
                // 다시 켜지면 그 개수에서 다시 판단한다.
                _nearEdgeCount = -1;
                return;
            }

            int count = _layout.DataCount;
            if (count != _nearEdgeCount)
            {
                _nearEdgeCount = count;
                _nearStartLatched = false;
                _nearEndLatched = false;
            }

            // 가장자리 너머로 당긴 거리(Elastic)는 남은 거리에 넣지 않는다 (짧은 콘텐츠를 당겼다 놓아도 다시 열리지 않게).
            float scrollSize = ScrollSize;
            float position = Mathf.Clamp(ScrollPosition, 0f, scrollSize);
            float rearm = _nearEdgeDistance * NEAR_EDGE_REARM_FACTOR;
            bool raiseStart = false;
            bool raiseEnd = false;

            // 짧은 콘텐츠는 처음과 끝이 같으므로 끝만 알린다.
            float toStart = position;
            if (_nearStartLatched)
            {
                _nearStartLatched = toStart <= rearm;
            }
            else if (scrollSize > 0f && toStart <= _nearEdgeDistance)
            {
                _nearStartLatched = true;
                raiseStart = true;
            }

            float toEnd = scrollSize - position;
            if (_nearEndLatched)
            {
                _nearEndLatched = toEnd <= rearm;
            }
            else if (toEnd <= _nearEdgeDistance)
            {
                _nearEndLatched = true;
                raiseEnd = true;
            }

            if (raiseStart)
            {
                ScrollerNearEdge?.Invoke(this, ScrollEdge.Start);

                // 처음 쪽 핸들러가 데이터를 바꾸거나 옮겼으면 끝 쪽은 지금 상태로 다시 본다 (낡은 판단으로 알리지 않게).
                if (raiseEnd && !IsStillNearEnd(count))
                {
                    raiseEnd = false;
                    _nearEndLatched = false;
                }
            }

            if (raiseEnd)
            {
                ScrollerNearEdge?.Invoke(this, ScrollEdge.End);
            }
        }

        /// <summary>판단한 개수 그대로이고 지금도 끝 근처인지. 개수가 바뀌었으면 다음 판단이 다시 연다.</summary>
        private bool IsStillNearEnd(int count)
        {
            if (!_hasLoaded || _loop || _layout.IsLoop || _layout.DataCount != count)
            {
                return false;
            }

            float scrollSize = ScrollSize;
            return scrollSize - Mathf.Clamp(ScrollPosition, 0f, scrollSize) <= _nearEdgeDistance;
        }
    }
}
