using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CyKim.Scroller
{
    // 코드로 콘텐츠를 옮기는 공통 경로: 좌표 이동(루프 순환 보정 등), 항목 기준 앵커 캡처·복원(공개 API·재배치·보관한 앵커),
    // 안정 항목 ID 캐시, ScrollRect 드래그·속도 기준 맞추기.
    public partial class CyScroller
    {
        // ScrollRect가 가장자리 너머로 끈 거리를 줄일 때 쓰는 고무줄 계수 (ScrollRect.RubberDelta와 같은 값).
        private const float ELASTIC_RUBBER_COEFFICIENT = 0.55f;

        // 델리게이트가 함께 구현한 ID 제공자 (Delegate setter에서 한 번 캐스트).
        private ICyScrollerItemIdProvider _idProvider;

        // 마지막으로 델리게이트를 다시 받을 때 채운 항목 ID. _hasItemIds면 [0, DataCount) 구간이 지금 배치의 ID다.
        // 다시 받을 때만 O(N)으로 채우고(사전은 비운 뒤 다시 써서 용량 유지), 스크롤 중에는 배열만 읽는다.
        private long[] _itemIds = Array.Empty<long>();
        private readonly Dictionary<long, int> _itemIdToIndex = new Dictionary<long, int>();
        private bool _hasItemIds;

        // 데이터가 0개이거나 뷰포트 길이가 0이거나 아직 로드 전이라 적용하지 못한 RestoreAnchor 요청.
        // 코드로 부른 새 이동 요청(RestoreAnchor·위치를 정한 리로드·점프·ScrollIntoView·Snap·ScrollPosition 대입)이 오면 갈 셀이 없어도 버린다.
        // 드래그·휠 같은 사용자 입력은 버리지 않고, 그 뒤 자동 스냅은 보관하는 동안 기다린다.
        // 보관 중에는 트윈·정렬이 없다 (RestoreAnchor가 멈추고, 새 트윈은 보관한 앵커를 버린다).
        private bool _hasPendingAnchor;
        private CyScrollerAnchor _pendingAnchor;

        /// <summary>
        /// 지금 스크롤 위치를 항목 기준 앵커로 적는다. 저장해 두었다가 <see cref="RestoreAnchor"/>·<see cref="ReloadData(in CyScrollerAnchor)"/>로 되돌린다.
        /// 할당하지 않는다.
        /// </summary>
        /// <param name="trailing">
        /// false면 뷰포트 맨 앞(위·왼쪽)에 걸친 항목과 셀 시작 → 뷰포트 시작 거리,
        /// true면 뷰포트 맨 뒤(아래·오른쪽)에 걸친 항목과 뷰포트 끝 → 셀 끝 거리를 적는다 (<see cref="CyScrollerAnchor.Offset"/>).
        /// 가장자리가 셀 사이 간격 안이면 앞 기준은 간격 앞 셀, 뒤 기준은 간격 뒤 셀이다.
        /// </param>
        /// <returns>
        /// 델리게이트가 <see cref="ICyScrollerItemIdProvider"/>를 구현하면 항목 ID도 담는다. 빈 목록이거나 로드 전이면 빈 앵커(DataIndex −1).
        /// 데이터·뷰포트가 준비되기 전에 <see cref="RestoreAnchor"/>로 보관한 앵커가 있으면 그 앵커를 그대로 돌려준다 (trailing과 다를 수 있다).
        /// </returns>
        public CyScrollerAnchor CaptureAnchor(bool trailing = false)
        {
            if (_hasPendingAnchor)
            {
                return _pendingAnchor;
            }

            return CaptureLayoutAnchor(trailing, out _);
        }

        /// <summary>
        /// 앵커의 항목이 같은 거리로 뷰포트 같은 가장자리에 오도록 옮긴다. 결과는 스크롤 범위 안으로 자른다.
        /// <see cref="ScrollPosition"/> 대입처럼 진행 중인 트윈·관성·점프 정렬을 멈추고(트윈 완료 콜백 없음), 드래그 중이면 손가락 아래 기준점을 새 위치로 옮긴다.
        /// </summary>
        /// <remarks>
        /// <para>항목은 앵커에 ID가 있으면 ID로 찾는다. <see cref="CyScrollerAnchor.DataIndex"/> 자리의 항목이 같은 ID면 그 인덱스를, 아니면 <see cref="FindDataIndexForItemId"/>로 찾은 인덱스를 쓰므로
        /// 같은 ID가 여럿이어도 데이터가 그대로면 제자리로 돌아온다. ID가 없거나 못 찾으면 DataIndex(개수를 넘으면 마지막)를 쓴다.
        /// 빈 앵커(DataIndex 음수)는 <see cref="CyScrollerAnchor.Trailing"/>이면 끝, 아니면 처음으로 간다. 루프면 가운데 사이클 안의 사본에 맞춘다.</para>
        /// <para>아직 로드 전이거나 데이터가 0개이거나 뷰포트 길이가 0이면 앵커를 보관했다가, 다음 <see cref="ReloadData()"/>(데이터가 있을 때)나
        /// 뷰포트 길이가 0보다 커질 때 적용한다 (적용할 때도 남은 관성과 스냅 대기를 멈춘다). 그 사이 새 RestoreAnchor, 위치를 정한 리로드(<see cref="ReloadData(float)"/>·Start·End),
        /// 점프·<see cref="ScrollIntoView"/>·<see cref="Snap"/>, <see cref="ScrollPosition"/> 대입이 오면 갈 셀이 없어도 보관한 앵커는 버린다.
        /// 드래그·휠은 버리지 않고, 그 뒤 자동 스냅은 보관하는 동안 기다린다. 보관 중에는 <see cref="CaptureAnchor"/>가 이 앵커를 돌려준다.</para>
        /// <para>델리게이트·셀 이벤트 콜백 안에서 부르면 ScrollPosition 대입처럼 바로 옮긴다 (미룬 리로드·재배치가 있으면 그것이 끝난 뒤 적용).</para>
        /// </remarks>
        public void RestoreAnchor(in CyScrollerAnchor anchor)
        {
            CyScrollerAnchor request = anchor;
            if (!EnsureInitialized())
            {
                return;
            }

            CancelTween();
            _alignmentActive = false;
            _snapArmed = false;
            _scrollRect.StopMovement();

            _pendingAnchor = request;
            _hasPendingAnchor = true;

            if (!_inRangeUpdate)
            {
                // 미룬 리로드·재배치가 있으면 먼저 처리한다. 그쪽이 새 배치에 보관한 앵커를 적용한다.
                FlushPendingWork();
            }

            if (_hasPendingAnchor && CanApplyPendingAnchor)
            {
                MoveToPendingAnchor();
                UpdateActiveRange();
            }
        }

        /// <summary>
        /// 항목 ID의 지금 데이터 인덱스. 마지막으로 델리게이트를 다시 받을 때 채운 사전에서 찾는다 (O(1), 할당 없음).
        /// 없거나 델리게이트가 <see cref="ICyScrollerItemIdProvider"/>를 구현하지 않으면 −1. 같은 ID가 여럿이면 앞 인덱스.
        /// </summary>
        /// <remarks>데이터를 바꾼 뒤 아직 다시 읽지 않았으면 이전 데이터 기준 인덱스다.</remarks>
        public int FindDataIndexForItemId(long itemId)
        {
            if (!_hasItemIds)
            {
                return -1;
            }

            return _itemIdToIndex.TryGetValue(itemId, out int dataIndex) ? dataIndex : -1;
        }

        /// <summary>
        /// 레이아웃 좌표가 delta만큼 밀렸을 때(같은 셀이 delta만큼 뒤로 이동) 화면이 그대로 보이도록 스크롤 위치를 delta만큼 옮긴다.
        /// 진행 중인 트윈의 시작점과 정렬 위치도 같이 옮기고, 드래그 중이면 ScrollRect의 드래그 기준점과 직전 위치를 맞춘다.
        /// </summary>
        /// <remarks>
        /// 트윈 목표(스냅 포함)는 요청(슬롯·정렬 비율·여백)으로 매 프레임 현재 배치에서 다시 계산하므로 옮기지 않는다.
        /// 호출자가 레이아웃을 실제로 바꿨으면 목표도 따라간다. 루프 순환 보정처럼 같은 화면의 슬롯이 바뀌면 호출자가 요청 슬롯을 옮긴다.
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
        /// 보관한 앵커를 지금 배치에 적용할 수 있는지: 로드됐고, 데이터가 있고, 뷰포트 길이가 0보다 크고,
        /// 미룬 리로드·재배치가 없다 (있으면 그쪽이 새 배치에 적용한다).
        /// </summary>
        private bool CanApplyPendingAnchor =>
            _hasLoaded && !_reloadPending && !_relayoutPending && _layout.DataCount > 0 && ScrollRectSize > 0f;

        /// <summary>
        /// 보관한 앵커로 옮기고 지운다. 바로 적용하는 <see cref="RestoreAnchor"/>처럼 남은 관성과 자동 스냅 대기도 멈춘다
        /// (보관하는 동안 빈 목록을 끌거나 휠을 굴렸어도 복원한 자리에서 미끄러지거나 스냅으로 옮겨 가지 않게). 범위 갱신은 호출자가 한다.
        /// </summary>
        private void MoveToPendingAnchor()
        {
            CyScrollerAnchor anchor = _pendingAnchor;
            _hasPendingAnchor = false;
            _snapArmed = false;
            _scrollRect.StopMovement();
            RestoreAnchorPosition(in anchor, 0f);
        }

        /// <summary>
        /// 지금 배치에서 뷰포트 맨 앞(trailing이면 맨 뒤)에 걸친 데이터·항목 ID와 그 가장자리까지 거리. content가 맞춰진 축(이전 축일 수 있음)으로 읽는다.
        /// overscroll은 드래그 중 콘텐츠가 스크롤 범위 밖으로 나간 거리다 (앞쪽은 음수, 뒤쪽은 양수, 범위 안이거나 드래그 중이 아니면 0).
        /// 슬롯이 없으면 빈 앵커(DataIndex −1). 할당 없음.
        /// </summary>
        private CyScrollerAnchor CaptureLayoutAnchor(bool trailing, out float overscroll)
        {
            var anchor = new CyScrollerAnchor { DataIndex = -1, Trailing = trailing };
            overscroll = 0f;
            if (!_initialized)
            {
                return anchor;
            }

            float position = ReadPosition(_appliedVertical);

            // 스크롤 축이 바뀌었거나 ScrollRect가 꺼져(스크롤 잠금) 드래그를 처리하지 않으면 당긴 거리를 남기지 않는다.
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
                return anchor;
            }

            int slot;
            if (trailing)
            {
                float viewEnd = position + ReadViewportExtent(_appliedVertical);
                slot = _layout.GetTrailingSlotAtPosition(viewEnd);
                anchor.Offset = _layout.GetSlotEnd(slot) - viewEnd;
            }
            else
            {
                slot = _layout.GetSlotAtPosition(position);
                anchor.Offset = position - _layout.GetSlotStart(slot);
            }

            int dataIndex = _layout.SlotToDataIndex(slot);
            anchor.DataIndex = dataIndex;
            if (_hasItemIds)
            {
                anchor.ItemId = _itemIds[dataIndex];
                anchor.HasItemId = true;
            }

            return anchor;
        }

        /// <summary>
        /// 지금 배치에서 앵커의 항목이 같은 거리로 뷰포트 같은 가장자리에 오도록 옮긴다. 결과는 스크롤 범위로 자르되,
        /// 드래그 중 가장자리 너머로 당기고 있었으면(overscroll) 그 가장자리에서 당긴 거리까지는 남긴다 (손가락 아래 콘텐츠가 가장자리로 튀지 않게).
        /// 트윈·정렬 상태는 바꾸지 않는다.
        /// </summary>
        /// <returns>맞춘 셀의 슬롯 (루프면 고른 사본). 기준 셀이 없으면 -1.</returns>
        private int RestoreAnchorPosition(in CyScrollerAnchor anchor, float overscroll)
        {
            float position;
            int slot = -1;
            int dataIndex = ResolveAnchorDataIndex(in anchor);
            if (dataIndex < 0)
            {
                // 기준 셀이 없으면(빈 앵커·빈 목록) 처음, 끝 기준이면 끝. 그쪽으로 당기던 중이면 그 거리를 남긴다.
                if (anchor.Trailing)
                {
                    position = _layout.IsLoop ? GetEndPosition() : ScrollSize + Mathf.Max(0f, overscroll);
                }
                else
                {
                    position = _layout.IsLoop ? _layout.MiddleSetStart : Mathf.Min(0f, overscroll);
                }
            }
            else
            {
                slot = (_layout.IsLoop ? _layout.MiddleSetFirstSlot : 0) + dataIndex;
                position = anchor.Trailing
                    ? _layout.GetSlotEnd(slot) - anchor.Offset - ScrollRectSize
                    : _layout.GetSlotStart(slot) + anchor.Offset;

                // 같은 화면이 되는 사본 중 가운데 창 안의 것을 고른다 (콘텐츠 끝에서 잘리지 않게).
                int cycles = _layout.GetRecenterCycles(position);
                position -= cycles * _layout.CycleExtent;
                slot -= cycles * _layout.DataCount;
            }

            MoveContentTo(Mathf.Clamp(position, Mathf.Min(0f, overscroll), ScrollSize + Mathf.Max(0f, overscroll)));
            return slot;
        }

        /// <summary>
        /// 앵커가 가리키는 지금 데이터 인덱스. ID가 있으면 ID로 먼저 찾고(<see cref="ResolveItemIdIndex"/>), 없거나 못 찾으면 DataIndex(개수를 넘으면 마지막).
        /// 빈 앵커이거나 데이터가 없으면 -1.
        /// </summary>
        private int ResolveAnchorDataIndex(in CyScrollerAnchor anchor)
        {
            int count = _layout.DataCount;
            if (count == 0)
            {
                return -1;
            }

            if (anchor.HasItemId)
            {
                int found = ResolveItemIdIndex(anchor.ItemId, anchor.DataIndex);
                if (found >= 0)
                {
                    return found;
                }
            }

            return anchor.DataIndex < 0 ? -1 : Mathf.Min(anchor.DataIndex, count - 1);
        }

        /// <summary>
        /// 항목 ID의 지금 데이터 인덱스. 이전 인덱스(previousIndex) 자리의 항목이 같은 ID면 그 인덱스를 쓰고, 아니면 사전에서 찾는다(같은 ID가 여럿이면 앞 인덱스).
        /// 같은 ID가 여럿이어도 데이터가 그대로인 재배치·캡처 왕복에서는 보던 자리를 지킨다. 없으면 −1. O(1), 할당 없음.
        /// </summary>
        private int ResolveItemIdIndex(long itemId, int previousIndex)
        {
            if (_hasItemIds && previousIndex >= 0 && previousIndex < _layout.DataCount && _itemIds[previousIndex] == itemId)
            {
                return previousIndex;
            }

            return FindDataIndexForItemId(itemId);
        }

        /// <summary>
        /// 끝 위치 (스크롤 범위로 자르지 않음). 루프가 아니면 스크롤 끝, 루프면 마지막 항목 끝이 뷰포트 끝에 오는 가운데 창 안의 위치.
        /// </summary>
        private float GetEndPosition()
        {
            if (!_layout.IsLoop)
            {
                return ScrollSize;
            }

            float position = _layout.GetSlotEnd(_layout.MiddleSetFirstSlot + _layout.DataCount - 1) - ScrollRectSize;
            return position - _layout.GetRecenterCycles(position) * _layout.CycleExtent;
        }

        private float ReadViewportExtent(bool vertical)
        {
            if (_viewport == null)
            {
                return 0f;
            }

            Rect rect = _viewport.rect;
            return vertical ? rect.height : rect.width;
        }

        /// <summary>
        /// 델리게이트가 ID 제공자면 항목마다 ID를 받아 인덱스별 배열과 ID → 인덱스 사전을 채운다. 델리게이트를 다시 받을 때만 부른다 (O(N)).
        /// 사전은 비운 뒤 다시 써서 용량을 유지한다. 같은 ID가 여럿이면 앞 인덱스가 이기고, 에디터·개발 빌드에서는 한 번만 경고한다.
        /// </summary>
        private void RebuildItemIds(int count)
        {
            _itemIdToIndex.Clear();
            _hasItemIds = _idProvider != null;
            if (!_hasItemIds)
            {
                return;
            }

            if (_itemIds.Length < count)
            {
                _itemIds = new long[Mathf.Max(count, _itemIds.Length * 2)];
            }

            for (int i = 0; i < count; i++)
            {
                _itemIds[i] = _idProvider.GetItemId(this, i);
            }

            // 뒤에서부터 넣어 같은 ID는 앞 인덱스가 남게 한다 (항목마다 사전 연산 한 번).
            for (int i = count - 1; i >= 0; i--)
            {
                _itemIdToIndex[_itemIds[i]] = i;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_itemIdToIndex.Count != count)
            {
                WarnFirstDuplicateItemId(count);
            }
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>앞 인덱스에 밀린 첫 중복 ID를 한 번 경고한다. 중복이 있을 때만 부른다.</summary>
        private void WarnFirstDuplicateItemId(int count)
        {
            for (int i = 0; i < count; i++)
            {
                long itemId = _itemIds[i];
                int firstIndex = _itemIdToIndex[itemId];
                if (firstIndex != i)
                {
                    Debug.LogWarning(
                        $"[CyScroller] 같은 ItemId({itemId})를 가진 항목이 여럿입니다 (dataIndex {firstIndex}, {i}). ID로 찾을 때는 앞 인덱스를 씁니다.", this);
                    return;
                }
            }
        }
#endif

        /// <summary>셀 뷰에 지금 배치의 항목 ID를 넣는다. 핫패스(셀 활성화)에서 배열만 읽는다.</summary>
        private void AssignItemId(CyScrollerCellView cell, int dataIndex)
        {
            bool hasItemId = _hasItemIds && dataIndex >= 0;
            cell.ItemId = hasItemId ? _itemIds[dataIndex] : 0L;
            cell.HasItemId = hasItemId;
        }
    }
}
