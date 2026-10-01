using UnityEngine;
using UnityEngine.UI;

namespace CyKim.Scroller
{
    // 코드로 콘텐츠를 옮기는 공통 경로: 좌표 이동(루프 순환 보정 등), 맨 앞 셀 앵커 저장·복원, ScrollRect 드래그·속도 기준 맞추기.
    public partial class CyScroller
    {
        // ScrollRect가 가장자리 너머로 끈 거리를 줄일 때 쓰는 고무줄 계수 (ScrollRect.RubberDelta와 같은 값).
        private const float ELASTIC_RUBBER_COEFFICIENT = 0.55f;

        /// <summary>
        /// 레이아웃 좌표가 delta만큼 밀렸을 때(같은 셀이 delta만큼 뒤로 이동) 화면이 그대로 보이도록 스크롤 위치를 delta만큼 옮긴다.
        /// 진행 중인 트윈의 시작·목표(스냅 포함)와 정렬 위치도 같이 옮기고, 드래그 중이면 ScrollRect의 드래그 기준점과 직전 위치를 맞춘다.
        /// </summary>
        /// <remarks>
        /// 관성 속도는 그대로 둔다. 활성 셀 재배치·슬롯 번호·범위 갱신은 호출자가 한다. 할당 없음, O(1).
        /// 범위 갱신 중(델리게이트·셀 이벤트 콜백 안)에는 부르지 않는다.
        /// </remarks>
        internal void ShiftScrollPosition(float delta)
        {
            if (!_hasLoaded || delta == 0f)
            {
                return;
            }

            if (_tweening)
            {
                _tweenFrom += delta;
                _tweenTo += delta;
            }

            if (_alignmentActive)
            {
                _alignedPosition += delta;
            }

            MoveContentTo(ReadPosition(_appliedVertical) + delta);
        }

        /// <summary>
        /// 콘텐츠를 position으로 옮기고 ScrollRect의 드래그·속도 기준을 맞춘다. 스크롤러가 스크롤 위치를 바꾸는 경로는 모두 여기를 거친다.
        /// 트윈·정렬 상태는 바꾸지 않는다.
        /// </summary>
        private void MoveContentTo(float position)
        {
            SetScrollPositionInternal(position);
            SyncScrollRectAfterContentMove();
        }

        /// <summary>
        /// 스크롤러가 콘텐츠 위치를 직접 옮긴 뒤 ScrollRect 내부 상태를 맞춘다.
        /// 드래그 중이면 기준점을 다시 잡고, onValueChanged 밖이면 직전 위치도 맞춰 속도 계산에 순간이동이 섞이지 않게 한다.
        /// </summary>
        private void SyncScrollRectAfterContentMove()
        {
            if (!_dragging || _dragEventData == null)
            {
                return;
            }

            // ScrollRect는 드래그 시작 시점의 콘텐츠 위치(기준 위치)에 손가락 이동을 더해 드래그 위치를 계산한다.
            // 옮긴 위치를 새 기준으로 삼게 해서 손가락 아래 콘텐츠가 튀지 않게 한다.
            float position = ReadPosition(_appliedVertical);
            float dragOrigin = GetElasticDragOrigin(position);
            if (dragOrigin != position)
            {
                // 가장자리 너머로 당기는 중이면 ScrollRect가 기준 위치에 고무줄 감쇠를 다시 건다.
                // 감쇠 전 위치를 기준으로 잡아야 다음 OnDrag가 지금 위치를 그대로 내고 당긴 거리가 줄지 않는다.
                SetScrollPositionInternal(dragOrigin);
                _scrollRect.OnBeginDrag(_dragEventData);
                SetScrollPositionInternal(position);

                // OnBeginDrag가 감쇠 전 위치로 계산해 둔 경계를 지금 위치로 다시 계산한다 (정규화 위치 getter가 경계를 갱신한다).
                // onValueChanged 안이면 ScrollRect가 콜백 뒤에 이 경계를 직전 값으로 저장하므로 여기서 맞춰 둔다.
                _ = _scrollRect.verticalNormalizedPosition;
            }
            else
            {
                _scrollRect.OnBeginDrag(_dragEventData);
            }

            if (!_inValueChanged)
            {
                // onValueChanged 안이라면 ScrollRect가 콜백 뒤에 직접 UpdatePrevData를 한다.
                _scrollRect.Rebuild(CanvasUpdate.PostLayout);
            }
        }

        /// <summary>
        /// 가장자리 너머로 당긴 위치(Elastic)를 만드는 ScrollRect 드래그 기준 위치. 고무줄 감쇠를 거꾸로 계산한다.
        /// 스크롤 범위 안이거나 Elastic이 아니면 position을 그대로 돌려준다.
        /// </summary>
        private float GetElasticDragOrigin(float position)
        {
            if (_scrollRect.movementType != ScrollRect.MovementType.Elastic)
            {
                return position;
            }

            float scrollSize = ScrollSize;
            float distance;
            if (position < 0f)
            {
                distance = -position;
            }
            else if (position > scrollSize)
            {
                distance = position - scrollSize;
            }
            else
            {
                return position;
            }

            // 보이는 거리 d = (1 − 1 / (x·k / v + 1)) · v 를 x로 풀면 x = d·v / (k·(v − d)). v = 뷰포트 길이, k = 고무줄 계수.
            float viewSize = ScrollRectSize;
            if (distance >= viewSize)
            {
                // 고무줄로는 나올 수 없는 거리 (그 사이 뷰포트가 줄었을 때 등). 지금 위치를 기준으로 둔다.
                return position;
            }

            float raw = distance * viewSize / (ELASTIC_RUBBER_COEFFICIENT * (viewSize - distance));
            return position < 0f ? -raw : scrollSize + raw;
        }

        /// <summary>
        /// 뷰포트 맨 앞에 걸친 데이터와 그 셀 시작에서의 오프셋. content가 맞춰진 축(이전 축일 수 있음)으로 읽는다.
        /// overscroll은 드래그 중 콘텐츠가 스크롤 범위 밖으로 나간 거리다 (앞쪽은 음수, 뒤쪽은 양수, 범위 안이거나 드래그 중이 아니면 0).
        /// </summary>
        private void CaptureAnchor(out int dataIndex, out float offset, out float overscroll)
        {
            float position = ReadPosition(_appliedVertical);

            // 스크롤 축이 바뀌었거나 ScrollRect가 꺼져(스크롤 잠금) 드래그를 처리하지 않으면 당긴 거리를 남기지 않는다.
            overscroll = 0f;
            if (_dragging && AcceptsInput && IsVertical == _appliedVertical)
            {
                float scrollSize = ScrollSize;
                if (position < 0f)
                {
                    overscroll = position;
                }
                else if (position > scrollSize)
                {
                    overscroll = position - scrollSize;
                }
            }

            if (_layout.SlotCount == 0)
            {
                dataIndex = -1;
                offset = 0f;
                return;
            }

            int slot = _layout.GetSlotAtPosition(position);
            dataIndex = _layout.SlotToDataIndex(slot);
            offset = position - _layout.GetSlotStart(slot);
        }

        /// <summary>
        /// 새 배치에서 같은 데이터가 같은 오프셋으로 뷰포트 맨 앞에 오도록 옮긴다. 결과는 스크롤 범위로 자르되,
        /// 드래그 중 가장자리 너머로 당기고 있었으면 그 가장자리에서 당긴 거리까지는 남긴다 (손가락 아래 콘텐츠가 가장자리로 튀지 않게).
        /// </summary>
        private void RestoreAnchor(int dataIndex, float offset, float overscroll)
        {
            float position;
            if (dataIndex < 0 || _layout.DataCount == 0)
            {
                // 기준 셀이 없으면 콘텐츠 시작. 앞쪽으로 당기던 중이면 그 거리를 남긴다.
                position = _layout.IsLoop ? _layout.MiddleSetStart : Mathf.Min(0f, overscroll);
            }
            else
            {
                int clamped = Mathf.Min(dataIndex, _layout.DataCount - 1);
                int slot = (_layout.IsLoop ? _layout.MiddleSetFirstSlot : 0) + clamped;
                position = _layout.GetSlotStart(slot) + offset;

                // 같은 화면이 되는 사본 중 가운데 창 안의 것을 고른다 (콘텐츠 끝에서 잘리지 않게).
                position -= _layout.GetRecenterCycles(position) * _layout.CycleExtent;
            }

            MoveContentTo(Mathf.Clamp(position, Mathf.Min(0f, overscroll), ScrollSize + Mathf.Max(0f, overscroll)));
        }
    }
}
