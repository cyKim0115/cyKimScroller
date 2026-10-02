using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace CyKim.Scroller.Tests
{
    /// <summary>
    /// 항목 값을 안정 ID로 주는 테스트 델리게이트. 항목은 (ID, 크기) 목록이고 앞뒤 삽입·삭제로 데이터 변경을 흉내 낸다.
    /// </summary>
    internal sealed class ItemIdTestDelegate : ICyScrollerDelegate, ICyScrollerItemIdProvider
    {
        public readonly List<long> Ids = new List<long>();
        public readonly List<float> Sizes = new List<float>();
        public CyScrollerCellView Prefab;
        public int GetItemIdCalls;

        /// <summary>켜면 풀을 거치지 않고 템플릿을 직접 Instantiate한다.</summary>
        public bool InstantiateDirectly;

        public int GetNumberOfCells(CyScroller scroller) => Ids.Count;

        public float GetCellViewSize(CyScroller scroller, int dataIndex) => Sizes[dataIndex];

        public long GetItemId(CyScroller scroller, int dataIndex)
        {
            GetItemIdCalls++;
            return Ids[dataIndex];
        }

        public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
        {
            var view = InstantiateDirectly
                ? (TestCellView)Object.Instantiate(Prefab, scroller.Container, false)
                : (TestCellView)scroller.GetCellView(Prefab);
            view.BoundData = dataIndex;
            view.BoundVersion = view.BindVersion;
            view.BoundItemId = view.HasItemId ? view.ItemId : -1L;
            return view;
        }

        public void Add(long id, float size)
        {
            Ids.Add(id);
            Sizes.Add(size);
        }

        public void Insert(int index, long id, float size)
        {
            Ids.Insert(index, id);
            Sizes.Insert(index, size);
        }

        public void RemoveAt(int index)
        {
            Ids.RemoveAt(index);
            Sizes.RemoveAt(index);
        }

        /// <summary>목록을 ID firstId부터 count개, 크기 size로 다시 채운다.</summary>
        public void Refill(int count, float size, long firstId)
        {
            Ids.Clear();
            Sizes.Clear();
            for (int i = 0; i < count; i++)
            {
                Add(firstId + i, size);
            }
        }
    }

    /// <summary>안정 항목 ID와 앵커: 캡처·복원, 위치 보존 리로드(ReloadAnchor), 준비 전 요청의 보관, ID로 찾는 정렬·트윈.</summary>
    public class CyScrollerAnchorTests
    {
        private const float EPSILON = 0.01f;

        // 1만 근처 위치는 RectTransform 기록 뒤 읽은 값이 0.01 넘게 어긋날 수 있다 (float 정밀도).
        private const float FAR_POSITION_EPSILON = 0.05f;

        // 항목 값(= ID). 데이터 인덱스와 겹치지 않게 띄운다.
        private const long ID_BASE = 1000L;

        private ScrollerFixture _fixture;

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
        }

        private CyScroller Scroller => _fixture.Scroller;

        /// <summary>ID_BASE + i를 ID로 주는 델리게이트로 count개(크기 size)를 로드한다.</summary>
        private ItemIdTestDelegate CreateWithIds(int count, float size = 100f, ScrollDirection direction = ScrollDirection.Vertical,
            bool loop = false, float spacing = 0f, RectOffset padding = null)
        {
            _fixture = ScrollerFixture.Create(direction, TestDelegate.Uniform(count, size), loop, spacing, padding, reload: false);
            var ids = new ItemIdTestDelegate { Prefab = _fixture.Prefab };
            ids.Refill(count, size, ID_BASE);
            Scroller.Delegate = ids;
            Scroller.ReloadData();
            return ids;
        }

        /// <summary>뷰포트 끝에서 데이터 셀 끝까지 거리 (루프가 아닐 때).</summary>
        private float TrailingGap(int dataIndex)
        {
            return Scroller.GetCellStart(dataIndex) + Scroller.GetCellSize(dataIndex) - (Scroller.ScrollPosition + Scroller.ScrollRectSize);
        }

        #region Capture

        [Test]
        public void CaptureAnchor_LeadingAndTrailing_DescribeEdgeCells()
        {
            CreateWithIds(100);

            // 뷰포트 [1050, 1450]: 앞 가장자리는 10번 [1000, 1100] 안, 뒤 가장자리는 14번 [1400, 1500] 안
            Scroller.ScrollPosition = 1050f;
            CyScrollerAnchor leading = Scroller.CaptureAnchor();
            Assert.IsTrue(leading.IsValid);
            Assert.IsFalse(leading.Trailing);
            Assert.AreEqual(10, leading.DataIndex);
            Assert.AreEqual(50f, leading.Offset, EPSILON, "뷰포트 시작 − 셀 시작");
            Assert.IsTrue(leading.HasItemId);
            Assert.AreEqual(ID_BASE + 10, leading.ItemId);

            CyScrollerAnchor trailing = Scroller.CaptureAnchor(true);
            Assert.IsTrue(trailing.Trailing);
            Assert.AreEqual(14, trailing.DataIndex);
            Assert.AreEqual(50f, trailing.Offset, EPSILON, "셀 끝 − 뷰포트 끝");
            Assert.AreEqual(ID_BASE + 14, trailing.ItemId);

            // 가장자리가 셀 경계에 맞닿으면 앞 기준은 그 경계에서 시작하는 셀, 뒤 기준은 그 경계에서 끝나는 셀이다.
            Scroller.ScrollPosition = 1000f;
            leading = Scroller.CaptureAnchor();
            trailing = Scroller.CaptureAnchor(true);
            Assert.AreEqual(10, leading.DataIndex);
            Assert.AreEqual(0f, leading.Offset, EPSILON);
            Assert.AreEqual(13, trailing.DataIndex);
            Assert.AreEqual(0f, trailing.Offset, EPSILON);
        }

        [Test]
        public void CaptureAnchor_WithoutIdProviderOrData()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f));
            CyScrollerAnchor anchor = Scroller.CaptureAnchor();
            Assert.IsTrue(anchor.IsValid);
            Assert.AreEqual(0, anchor.DataIndex);
            Assert.IsFalse(anchor.HasItemId, "델리게이트가 ID를 주지 않으면 ID 없이 적는다");
            Assert.AreEqual(0L, anchor.ItemId);
            Assert.AreEqual(-1, Scroller.FindDataIndexForItemId(0L));
            Assert.IsFalse(Scroller.GetCellViewAtDataIndex(0).HasItemId);

            _fixture.Delegate.Sizes = new float[0];
            Scroller.ReloadData();
            anchor = Scroller.CaptureAnchor(true);
            Assert.IsFalse(anchor.IsValid, "빈 목록이면 빈 앵커");
            Assert.AreEqual(-1, anchor.DataIndex);
            Assert.IsTrue(anchor.Trailing);
        }

        #endregion

        #region Restore

        [Test]
        public void CaptureRestore_RoundTrip_VerticalWithPaddingAndSpacing()
        {
            var sizes = new float[60];
            for (int i = 0; i < sizes.Length; i++)
            {
                sizes[i] = 40f + i * 37 % 120;
            }

            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, sizes, false, 10f, new RectOffset(0, 0, 30, 20));
            AssertRoundTrips();
        }

        [Test]
        public void CaptureRestore_RoundTrip_Horizontal()
        {
            var sizes = new float[60];
            for (int i = 0; i < sizes.Length; i++)
            {
                sizes[i] = 50f + i * 53 % 90;
            }

            _fixture = ScrollerFixture.Create(ScrollDirection.Horizontal, sizes, false, 6f, new RectOffset(25, 15, 0, 0));
            Assert.AreEqual(ScrollerFixture.VIEWPORT_WIDTH, Scroller.ScrollRectSize, EPSILON);
            AssertRoundTrips();
        }

        /// <summary>
        /// 앞 패딩 안·셀 사이 간격 안·셀 중간·뒤 패딩 안(스크롤 끝) 위치에서 앞·뒤 기준 앵커를 적고,
        /// 다른 곳으로 옮겼다가 복원하면 같은 위치여야 한다.
        /// </summary>
        private void AssertRoundTrips()
        {
            // 7번 끝과 8번 시작 사이 간격의 가운데. 이 점이 뷰포트 시작인 위치와 뷰포트 끝인 위치를 둘 다 본다.
            float gapPoint = Scroller.GetCellStart(7) + Scroller.GetCellSize(7) + Scroller.Spacing * 0.5f;
            Assert.Greater(gapPoint - Scroller.ScrollRectSize, 0f);
            float[] positions =
            {
                0f, 12.5f, gapPoint, gapPoint - Scroller.ScrollRectSize, 1234.5f,
                Scroller.ScrollSize * 0.5f, Scroller.ScrollSize - 7f, Scroller.ScrollSize,
            };

            for (int i = 0; i < positions.Length; i++)
            {
                AssertRoundTrip(positions[i]);
            }
        }

        private void AssertRoundTrip(float position)
        {
            Scroller.ScrollPosition = position;
            float expected = Scroller.ScrollPosition;
            float away = expected < Scroller.ScrollSize * 0.5f ? Scroller.ScrollSize : 0f;
            CyScrollerAnchor leading = Scroller.CaptureAnchor();
            CyScrollerAnchor trailing = Scroller.CaptureAnchor(true);

            Scroller.ScrollPosition = away;
            Scroller.RestoreAnchor(leading);
            Assert.AreEqual(expected, Scroller.ScrollPosition, EPSILON, $"앞 기준 {position} (data {leading.DataIndex}, offset {leading.Offset})");

            Scroller.ScrollPosition = away;
            Scroller.RestoreAnchor(trailing);
            Assert.AreEqual(expected, Scroller.ScrollPosition, EPSILON, $"뒤 기준 {position} (data {trailing.DataIndex}, offset {trailing.Offset})");
        }

        [Test]
        public void CaptureRestore_Loop_UsesMiddleWindowCopyAndKeepsItemAfterPrepend()
        {
            // 사이클 1000, 세트 5 → 가운데 세트 시작 2000, 순환 보정 창 [1500, 2500]
            ItemIdTestDelegate ids = CreateWithIds(10, loop: true);
            Assert.AreEqual(5, Scroller.Layout.SetCount);

            float[] positions = { 2340f, 1550f, 2450f, 2000f };
            for (int i = 0; i < positions.Length; i++)
            {
                Scroller.ScrollPosition = positions[i];
                CyScrollerAnchor leading = Scroller.CaptureAnchor();
                CyScrollerAnchor trailing = Scroller.CaptureAnchor(true);

                Scroller.ScrollPosition = 1800f;
                Scroller.RestoreAnchor(leading);
                Assert.AreEqual(positions[i], Scroller.ScrollPosition, EPSILON, $"앞 기준 {positions[i]}");

                Scroller.ScrollPosition = 1800f;
                Scroller.RestoreAnchor(trailing);
                Assert.AreEqual(positions[i], Scroller.ScrollPosition, EPSILON, $"뒤 기준 {positions[i]}");
            }

            // 창 밖(아직 순환 보정 전) 사본에서 적은 앵커도 창 안의 같은 화면 사본으로 복원한다.
            Scroller.ScrollPosition = 2600f;
            CyScrollerAnchor outside = Scroller.CaptureAnchor();
            CyScrollerAnchor outsideTrailing = Scroller.CaptureAnchor(true);
            Assert.AreEqual(6, outside.DataIndex);
            Assert.AreEqual(9, outsideTrailing.DataIndex);
            Scroller.RestoreAnchor(outside);
            Assert.AreEqual(1600f, Scroller.ScrollPosition, EPSILON);
            Scroller.ScrollPosition = 2000f;
            Scroller.RestoreAnchor(outsideTrailing);
            Assert.AreEqual(1600f, Scroller.ScrollPosition, EPSILON);

            // 맨 위가 3번(ID 1003)이고 셀 안 40일 때 앞에 2개를 넣으면 사이클 1200, 가운데 세트 시작 2400에서 5번 사본(2900) + 40
            Scroller.ScrollPosition = 2340f;
            ids.Insert(0, 1L, 100f);
            ids.Insert(0, 2L, 100f);
            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(2940f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(5, Scroller.StartDataIndex);
            Assert.AreEqual(ID_BASE + 3, Scroller.GetCellViewAtDataIndex(5).ItemId);
        }

        [Test]
        public void RestoreAnchor_UnknownItemId_FallsBackToDataIndex()
        {
            ItemIdTestDelegate ids = CreateWithIds(100);

            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 12, ItemId = 999999L, HasItemId = true, Offset = 30f });
            Assert.AreEqual(1230f, Scroller.ScrollPosition, EPSILON, "ID를 못 찾으면 DataIndex");

            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 12, ItemId = ID_BASE + 40, HasItemId = true, Offset = 30f });
            Assert.AreEqual(4030f, Scroller.ScrollPosition, EPSILON, "ID가 있으면 DataIndex보다 먼저 찾는다");

            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 500 });
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, EPSILON, "개수를 넘으면 마지막 항목, 결과는 스크롤 범위로 자른다");

            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = -1 });
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON, "빈 앵커는 처음");
            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = -1, Trailing = true });
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, EPSILON, "끝 기준 빈 앵커는 끝");

            // 맨 위 항목(ID 1030)이 지워지면 같은 인덱스(이제 ID 1031)를 맨 위에 둔다.
            Scroller.ScrollPosition = 3020f;
            ids.RemoveAt(30);
            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(3020f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(30, Scroller.StartDataIndex);
            Assert.AreEqual(ID_BASE + 31, Scroller.GetCellViewAtDataIndex(30).ItemId);
        }

        [UnityTest]
        public IEnumerator RestoreAnchor_StopsTweenInertiaAndAlignment()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int completed = 0;
            var anchor = new CyScrollerAnchor { DataIndex = 20, Offset = 10f };

            Scroller.JumpToDataIndex(300, 0f, 0f, false, TweenType.Linear, 1f, () => completed++);
            Assert.IsTrue(Scroller.IsTweening);
            Scroller.RestoreAnchor(anchor);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(2010f, Scroller.ScrollPosition, EPSILON);

            Scroller.LinearVelocity = 3000f;
            Scroller.RestoreAnchor(anchor);
            Assert.AreEqual(0f, Scroller.LinearVelocity, EPSILON, "관성을 멈춘다");

            // 점프 정렬도 끝낸다: 뷰포트가 커져도 다시 맞추지 않고 콘텐츠 시작 기준 위치를 지킨다.
            Scroller.JumpToDataIndex(50, 0.5f, 0.5f, false);
            Scroller.RestoreAnchor(anchor);
            ((RectTransform)_fixture.ScrollRect.transform).sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 600f);
            yield return null;
            yield return null;

            Assert.AreEqual(2010f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(0, completed, "멈춘 트윈의 완료 콜백은 부르지 않는다");
        }

        [UnityTest]
        public IEnumerator RestoreAnchor_MidDrag_MovesDragOriginWithoutEndingDrag()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            _fixture.ScrollRect.inertia = false;
            yield return null;

            GameObject target = _fixture.ScrollRect.gameObject;
            var start = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left, position = start };
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);
            eventData.position = start + new Vector2(0f, 100f);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
            yield return null;
            Assert.AreEqual(100f, Scroller.ScrollPosition, 1f);

            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 20 });
            Assert.AreEqual(2000f, Scroller.ScrollPosition, 1f);
            Assert.IsTrue(Scroller.IsDragging, "ScrollPosition 대입처럼 드래그는 끝내지 않는다");

            // 기준점이 옮겨졌으면 이후 50px 이동은 2000 + 50이다.
            eventData.position = start + new Vector2(0f, 150f);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
            yield return null;
            Assert.AreEqual(2050f, Scroller.ScrollPosition, 1f);

            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);
        }

        [Test]
        public void RestoreAnchor_InsideRangeCallback_RebuildsRangeAtNewPosition()
        {
            CreateWithIds(100);
            bool restored = false;
            Scroller.CellViewWillDisplay += (_, view) =>
            {
                if (!restored && view.DataIndex == 4)
                {
                    restored = true;
                    Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 0, ItemId = ID_BASE + 50, HasItemId = true });
                }
            };

            // 4번이 보이기 시작할 때 콜백이 50번으로 옮긴다 → 범위는 옛 위치 작업을 멈추고 새 위치로 다시 맞춘다.
            Scroller.ScrollPosition = 50f;
            Assert.IsTrue(restored);
            Assert.AreEqual(5000f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(50, Scroller.StartDataIndex);
            Assert.AreEqual(53, Scroller.EndDataIndex);
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                CyScrollerCellView view = Scroller.ActiveCellViews[i];
                Assert.IsTrue(view.IsDisplayed, $"data {view.DataIndex}");
                Assert.AreEqual(ID_BASE + view.DataIndex, view.ItemId);
            }
        }

        #endregion

        #region Pending Anchor

        [Test]
        public void RestoreAnchor_WhileEmpty_IsAppliedOnNextReload()
        {
            ItemIdTestDelegate ids = CreateWithIds(0);

            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 30, Offset = 20f });
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON);
            CyScrollerAnchor pending = Scroller.CaptureAnchor(true);
            Assert.AreEqual(30, pending.DataIndex, "적용 전에는 보관한 앵커를 그대로 돌려준다");
            Assert.AreEqual(20f, pending.Offset, EPSILON);
            Assert.IsFalse(pending.Trailing);

            ids.Refill(100, 100f, ID_BASE);
            Scroller.ReloadData();
            Assert.AreEqual(3020f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(30, Scroller.StartDataIndex);
            CyScrollerAnchor applied = Scroller.CaptureAnchor();
            Assert.AreEqual(ID_BASE + 30, applied.ItemId, "적용한 뒤에는 지금 화면을 적는다");

            // ID만 아는 끝 기준 앵커를 빈 목록에서 받아 두었다가, FirstVisible 리로드(보관한 앵커가 가려던 자리)에서 적용한다.
            ids.Refill(0, 100f, ID_BASE);
            Scroller.ReloadData();
            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 0, ItemId = ID_BASE + 50, HasItemId = true, Offset = 5f, Trailing = true });
            ids.Refill(100, 100f, ID_BASE);
            Scroller.ReloadData(ReloadAnchor.FirstVisible);
            Assert.AreEqual(5100f - 5f - 400f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void PendingAnchor_IsDiscardedByNewPositionRequests()
        {
            ItemIdTestDelegate ids = CreateWithIds(0);
            var anchor = new CyScrollerAnchor { DataIndex = 30, Offset = 20f };

            void StoreWhileEmpty()
            {
                ids.Refill(0, 100f, ID_BASE);
                Scroller.ReloadData();
                Scroller.RestoreAnchor(anchor);
            }

            void Load()
            {
                ids.Refill(100, 100f, ID_BASE);
                Scroller.ReloadData();
            }

            StoreWhileEmpty();
            Scroller.ScrollPosition = 0f;
            Load();
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON, "ScrollPosition 대입");

            StoreWhileEmpty();
            ids.Refill(100, 100f, ID_BASE);
            Scroller.ReloadData(0.5f);
            Assert.AreEqual(4800f, Scroller.ScrollPosition, EPSILON, "ReloadData(factor)");

            StoreWhileEmpty();
            ids.Refill(100, 100f, ID_BASE);
            Scroller.ReloadData(ReloadAnchor.End);
            Assert.AreEqual(9600f, Scroller.ScrollPosition, EPSILON, "ReloadData(End)");

            StoreWhileEmpty();
            ids.Refill(100, 100f, ID_BASE);
            Scroller.ReloadData(ReloadAnchor.Start);
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON, "ReloadData(Start)");

            StoreWhileEmpty();
            int jumped = 0;
            Scroller.JumpToDataIndex(10, jumpComplete: () => jumped++);
            Assert.AreEqual(1, jumped, "빈 목록 점프는 바로 끝난다");
            Load();
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON, "점프");

            StoreWhileEmpty();
            Scroller.ScrollIntoView(10);
            Load();
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON, "ScrollIntoView");

            StoreWhileEmpty();
            Scroller.Snap();
            Load();
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON, "Snap (맞출 셀이 없어도 버린다)");

            StoreWhileEmpty();
            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 40 });
            Load();
            Assert.AreEqual(4000f, Scroller.ScrollPosition, EPSILON, "새 RestoreAnchor가 대신한다");

            StoreWhileEmpty();
            Scroller.ReloadData(new CyScrollerAnchor { DataIndex = 45 });
            Load();
            Assert.AreEqual(4500f, Scroller.ScrollPosition, EPSILON, "ReloadData(anchor)가 대신하고, 데이터가 없으면 보관한다");
        }

        [UnityTest]
        public IEnumerator RestoreAnchor_BeforeFirstLoad_IsAppliedWithDelegateReload()
        {
            // 델리게이트를 넣었지만 아직 로드 전: RestoreAnchor가 미룬 리로드를 먼저 처리하고 바로 적용한다.
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), reload: false);
            var anchor = new CyScrollerAnchor { DataIndex = 25, Offset = 10f, Trailing = true };
            Scroller.RestoreAnchor(anchor);
            Assert.AreEqual(100, Scroller.NumberOfCells);
            Assert.AreEqual(2600f - 10f - 400f, Scroller.ScrollPosition, EPSILON, "25번 끝(2600)이 뷰포트 끝 + 10");

            // 델리게이트가 없을 때 받은 앵커는 나중에 넣은 델리게이트의 리로드(다음 LateUpdate)에서 적용한다.
            Scroller.Delegate = null;
            Scroller.RestoreAnchor(anchor);
            Assert.AreEqual(0, Scroller.NumberOfCells);
            Scroller.Delegate = _fixture.Delegate;
            yield return null;

            Assert.AreEqual(100, Scroller.NumberOfCells);
            Assert.AreEqual(2190f, Scroller.ScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator RestoreAnchor_WhileViewportIsZero_IsAppliedWhenViewportGrows()
        {
            CreateWithIds(100);
            var scrollRect = (RectTransform)_fixture.ScrollRect.transform;
            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 0f);
            yield return null;
            Assert.AreEqual(0f, Scroller.ScrollRectSize, EPSILON);

            // 끝 기준 앵커는 뷰포트 길이에 따라 위치가 달라진다: 30번 끝(3100)이 뷰포트 끝 + 20
            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 30, Offset = 20f, Trailing = true });
            Assert.AreEqual(30, Scroller.CaptureAnchor().DataIndex, "길이가 0인 동안은 보관한다");

            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, ScrollerFixture.VIEWPORT_HEIGHT);
            yield return null;
            Assert.AreEqual(3100f - 20f - 400f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(26, Scroller.StartDataIndex);
            Assert.AreEqual(30, Scroller.EndDataIndex);

            // 같은 프레임에 0이 됐다가 돌아와 크기 변화를 못 봐도 다음 LateUpdate에 적용한다.
            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 0f);
            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 50 });
            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, ScrollerFixture.VIEWPORT_HEIGHT);
            Assert.AreEqual(2680f, Scroller.ScrollPosition, EPSILON);
            yield return null;
            Assert.AreEqual(5000f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(50, Scroller.StartDataIndex);
        }

        /// <summary>
        /// 재배치(위치 유지 리로드·축 전환)로 데이터·뷰포트 길이가 생기면 그 재배치 안에서 보관한 앵커를 바로 적용한다.
        /// 프레임을 넘기지 않고 확인한다 (다음 LateUpdate의 대체 적용에 기대면 처음 항목이 한 프레임 보였다가 튄다).
        /// </summary>
        [Test]
        public void PendingAnchor_IsAppliedInsideRelayoutWithoutWaitingForFrame()
        {
            ItemIdTestDelegate ids = CreateWithIds(0);
            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 30, Offset = 20f });

            var displayed = new List<int>();
            Scroller.CellViewWillDisplay += (_, view) => displayed.Add(view.DataIndex);

            // 빈 목록에 데이터가 와서 위치 유지 리로드
            ids.Refill(100, 100f, ID_BASE);
            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(3020f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(30, Scroller.StartDataIndex);
            Assert.IsTrue(Scroller.CaptureAnchor(true).Trailing, "적용한 앵커는 비운다 (보관 중이면 앞 기준 앵커를 그대로 돌려준다)");
            CollectionAssert.AreEquivalent(new[] { 30, 31, 32, 33, 34 }, displayed, "처음 항목을 거치지 않고 앵커 자리의 셀만 보인다");

            // 세로 길이가 0인 동안 받은 앵커를 가로로 바꾸는 재배치(뷰포트 길이 300)에서 적용한다.
            var scrollRect = (RectTransform)_fixture.ScrollRect.transform;
            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 0f);
            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 60, Offset = 10f });
            Assert.AreEqual(60, Scroller.CaptureAnchor(true).DataIndex, "길이가 0인 동안은 보관한다");

            displayed.Clear();
            Scroller.ScrollDirection = ScrollDirection.Horizontal;
            Assert.AreEqual(ScrollerFixture.VIEWPORT_WIDTH, Scroller.ScrollRectSize, EPSILON);
            Assert.AreEqual(6010f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(60, Scroller.StartDataIndex);
            Assert.IsTrue(Scroller.CaptureAnchor(true).Trailing);
            CollectionAssert.AreEquivalent(new[] { 60, 61, 62, 63 }, displayed);
        }

        [UnityTest]
        public IEnumerator PendingAnchor_AutoSnapWaitsAndApplyingStopsInertiaAndSnap()
        {
            ItemIdTestDelegate ids = CreateWithIds(0);
            Scroller.Snapping = true;
            Scroller.SnapTweenTime = 0f;
            int snapped = 0;
            Scroller.ScrollerSnapped += (_, __, ___, ____) => snapped++;
            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 30, Offset = 20f });

            // 빈 목록을 끌었다 놓아 자동 스냅을 준비시킨다. 보관하는 동안 자동 스냅은 기다리므로 앵커를 버리지 않는다.
            var press = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            Scroller.OnBeginDrag(press);
            Scroller.OnEndDrag(press);
            yield return null;
            yield return null;
            Assert.AreEqual(30, Scroller.CaptureAnchor(true).DataIndex, "자동 스냅은 보관한 앵커를 버리지 않는다");

            // 튕긴 관성이 남은 채 데이터가 온다. 적용할 때 RestoreAnchor처럼 관성과 스냅 대기를 멈춘다.
            ids.Refill(100, 100f, ID_BASE);
            Scroller.LinearVelocity = 3000f;
            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(3020f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(0f, Scroller.LinearVelocity, EPSILON);

            // 남은 스냅 대기가 있었다면 가까운 셀(32번 가운데, 3050)로 옮겨 갔을 것이다.
            yield return null;
            yield return null;
            Assert.AreEqual(3020f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(0, snapped);
        }

        #endregion

        #region Reload Anchor

        [Test]
        public void ReloadData_StartEndAndFactor()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            Scroller.ScrollPosition = 3000f;

            Scroller.ReloadData(ReloadAnchor.End);
            Assert.AreEqual(9600f, Scroller.ScrollPosition, EPSILON);
            Scroller.ReloadData(ReloadAnchor.Start);
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON);
            Scroller.ReloadData(ReloadAnchor.Factor, 0.25f);
            Assert.AreEqual(2400f, Scroller.ScrollPosition, EPSILON);

            // 정수 0도 비율 오버로드로 간다 (ReloadAnchor 오버로드와 모호하지 않다).
            Scroller.ReloadData(0);
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void ReloadData_End_Loop_PutsLastItemEndAtViewportEnd()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);

            // 가운데 세트 9번 끝(3000) − 400 = 2600은 창 밖이라 한 사이클 앞 사본으로 간다.
            Scroller.ReloadData(ReloadAnchor.End);
            Assert.AreEqual(1600f, Scroller.ScrollPosition, EPSILON);
            int lastSlot = Scroller.GetCellViewIndexAtPosition(Scroller.ScrollPosition + Scroller.ScrollRectSize - 1f);
            Assert.AreEqual(9, Scroller.GetDataIndexForCellViewIndex(lastSlot));

            Scroller.ReloadData(ReloadAnchor.Start);
            Assert.AreEqual(2000f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void ReloadData_FirstVisible_WithItemIds_KeepsItemAfterPrependAndStopsTween()
        {
            ItemIdTestDelegate ids = CreateWithIds(100);
            Scroller.ScrollPosition = 3020f;

            ids.Insert(0, 1L, 50f);
            ids.Insert(0, 2L, 70f);
            ids.Insert(0, 3L, 30f);
            Scroller.ReloadData(ReloadAnchor.FirstVisible);
            Assert.AreEqual(150f + 3020f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(33, Scroller.StartDataIndex);

            // 전체 리로드이므로 트윈은 멈추고 완료 콜백을 부르지 않는다 (위치 유지 재배치는 이어 간다).
            int completed = 0;
            Scroller.JumpToDataIndex(90, 0f, 0f, false, TweenType.Linear, 1f, () => completed++);
            Scroller.UpdateTween(0.5f);
            float before = Scroller.ScrollPosition;
            Scroller.ReloadData(ReloadAnchor.FirstVisible);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(before, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(0, completed);
        }

        [Test]
        public void ReloadData_LastVisible_KeepsTrailingCellEndWhenVisibleCellsGrow()
        {
            ItemIdTestDelegate ids = CreateWithIds(100);

            // 뷰포트 [1050, 1450]: 14번 끝(1500)이 뷰포트 끝보다 50 아래
            Scroller.ScrollPosition = 1050f;
            for (int i = 10; i <= 14; i++)
            {
                ids.Sizes[i] = 120f;
            }

            Scroller.ReloadData(ReloadAnchor.LastVisible);

            // 14번 끝은 1000 + 5 × 120 = 1600. 앞 기준이면 1050에 남았을 것이다.
            Assert.AreEqual(1600f - 50f - 400f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(50f, TrailingGap(14), EPSILON);
            CyScrollerAnchor after = Scroller.CaptureAnchor(true);
            Assert.AreEqual(14, after.DataIndex);
            Assert.AreEqual(50f, after.Offset, EPSILON);
        }

        [Test]
        public void ReloadData_LastVisible_AtEnd_StaysAtEndWhenLastItemGrowsAndKeepsItWhenItemsAppended()
        {
            // 아래 여백 20, 50개 × 100 → 콘텐츠 5020, 스크롤 끝 4620 (끝에 붙어 있는 채팅)
            ItemIdTestDelegate ids = CreateWithIds(50, padding: new RectOffset(0, 0, 0, 20));
            Scroller.ReloadData(ReloadAnchor.End);
            Assert.AreEqual(4620f, Scroller.ScrollPosition, EPSILON);

            CyScrollerAnchor atEnd = Scroller.CaptureAnchor(true);
            Assert.AreEqual(49, atEnd.DataIndex);
            Assert.AreEqual(-20f, atEnd.Offset, EPSILON, "뷰포트 끝이 아래 여백 안이면 음수");

            // 마지막 메시지가 길어진다 → 끝 기준이면 그대로 끝에 붙어 있다.
            ids.Sizes[49] = 180f;
            Scroller.ReloadData(ReloadAnchor.LastVisible);
            Assert.AreEqual(4700f, Scroller.ScrollSize, EPSILON);
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, EPSILON);

            // 뒤에 3개가 붙는다 → 보던 마지막 메시지(ID 1049)의 끝이 뷰포트 끝에서 같은 거리에 남고, 새 항목은 그 아래에 붙는다.
            ids.Add(5000L, 100f);
            ids.Add(5001L, 100f);
            ids.Add(5002L, 100f);
            Scroller.ReloadData(ReloadAnchor.LastVisible);
            Assert.AreEqual(4700f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(5000f, Scroller.ScrollSize, EPSILON);
            Assert.AreEqual(-20f, TrailingGap(49), EPSILON);

            // 끝을 따라가려면 End로 다시 읽는다.
            Scroller.ReloadData(ReloadAnchor.End);
            Assert.AreEqual(5000f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void ReloadData_LastVisible_WithItemIds_KeepsTrailingItemAcrossPrependAndAppend()
        {
            ItemIdTestDelegate ids = CreateWithIds(50);
            Scroller.ReloadData(ReloadAnchor.End);
            Assert.AreEqual(4600f, Scroller.ScrollPosition, EPSILON);

            // 앞에 60짜리 5개(이전 기록 불러오기), 뒤에 2개(새 메시지)
            for (int i = 0; i < 5; i++)
            {
                ids.Insert(0, i + 1, 60f);
            }

            ids.Add(5000L, 100f);
            ids.Add(5001L, 100f);
            Scroller.ReloadData(ReloadAnchor.LastVisible);

            // ID 1049는 54번, 끝 300 + 5000 = 5300이 뷰포트 끝에 남는다.
            Assert.AreEqual(5300f - 400f, Scroller.ScrollPosition, EPSILON);
            CyScrollerAnchor trailing = Scroller.CaptureAnchor(true);
            Assert.AreEqual(54, trailing.DataIndex);
            Assert.AreEqual(ID_BASE + 49, trailing.ItemId);
            Assert.AreEqual(0f, trailing.Offset, EPSILON);
        }

        [Test]
        public void ReloadData_VisibleAnchors_FromEmptyList_GoToStartOrEnd()
        {
            ItemIdTestDelegate ids = CreateWithIds(0);

            // 빈 목록에서 적은 앞 기준은 처음, 뒤 기준은 끝 (첫 기록을 불러온 채팅은 맨 아래부터 보인다).
            ids.Refill(50, 100f, ID_BASE);
            Scroller.ReloadData(ReloadAnchor.LastVisible);
            Assert.AreEqual(4600f, Scroller.ScrollPosition, EPSILON);

            ids.Refill(0, 100f, ID_BASE);
            Scroller.ReloadData();
            ids.Refill(50, 100f, ID_BASE);
            Scroller.ReloadData(ReloadAnchor.FirstVisible);
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator ReloadData_LastVisible_InsideCallback_IsDeferredWithAnchorKind()
        {
            ItemIdTestDelegate ids = CreateWithIds(100);
            Scroller.ScrollPosition = 1050f;
            bool requested = false;
            float sizeDuringCallback = -1f;
            Scroller.CellViewWillDisplay += (_, view) =>
            {
                if (!requested && view.DataIndex == 15)
                {
                    requested = true;
                    for (int i = 10; i <= 15; i++)
                    {
                        ids.Sizes[i] = 120f;
                    }

                    Scroller.ReloadData(ReloadAnchor.LastVisible);
                    sizeDuringCallback = Scroller.GetCellSize(10);
                }
            };

            // 뷰포트 [1150, 1550]: 15번이 보이기 시작한다. 15번 끝(1600)이 뷰포트 끝보다 50 아래.
            Scroller.ScrollPosition = 1150f;
            Assert.IsTrue(requested);
            Assert.AreEqual(100f, sizeDuringCallback, EPSILON, "범위 갱신 중에는 미룬다");
            yield return null;

            // 끝 기준으로 처리됐다: 15번 끝 1000 + 6 × 120 = 1720 → 1720 − 50 − 400
            Assert.AreEqual(120f, Scroller.GetCellSize(10), EPSILON);
            Assert.AreEqual(1270f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(50f, TrailingGap(15), EPSILON);
        }

        [Test]
        public void ReloadDataKeepingPosition_WithItemIds_KeepsSameItemOnTopAfterPrependAndRemove()
        {
            ItemIdTestDelegate ids = CreateWithIds(100);
            Scroller.ScrollPosition = 3020f;
            Assert.AreEqual(30, Scroller.StartDataIndex);

            // 앞에 크기가 다른 3개 삽입 → ID 1030은 33번, 시작 150 + 3000
            ids.Insert(0, 1L, 50f);
            ids.Insert(0, 2L, 70f);
            ids.Insert(0, 3L, 30f);
            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(33, Scroller.StartDataIndex, "같은 항목이 맨 위에 남아야 한다");
            Assert.AreEqual(3170f, Scroller.ScrollPosition, EPSILON);
            CyScrollerCellView top = Scroller.GetCellViewAtDataIndex(33);
            Assert.IsTrue(top.HasItemId);
            Assert.AreEqual(ID_BASE + 30, top.ItemId);
            Assert.AreEqual(33, Scroller.FindDataIndexForItemId(ID_BASE + 30));

            // 앞쪽 10개 삭제 → ID 1030은 23번, 시작 2300
            for (int i = 0; i < 10; i++)
            {
                ids.RemoveAt(0);
            }

            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(23, Scroller.StartDataIndex);
            Assert.AreEqual(2320f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(ID_BASE + 30, Scroller.GetCellViewAtDataIndex(23).ItemId);
        }

        [Test]
        public void ReloadData_WithAnchor_RestoresAfterReload()
        {
            ItemIdTestDelegate ids = CreateWithIds(100);
            Scroller.ScrollPosition = 2345f;
            CyScrollerAnchor saved = Scroller.CaptureAnchor();
            Assert.AreEqual(ID_BASE + 23, saved.ItemId);

            // 다른 곳을 보는 사이 데이터가 바뀌었다(앞에 2개). 저장한 앵커로 다시 읽으면 같은 항목이 같은 오프셋으로 맨 위다.
            Scroller.ScrollPosition = 0f;
            ids.Insert(0, 1L, 100f);
            ids.Insert(0, 2L, 100f);
            Scroller.ReloadData(saved);
            Assert.AreEqual(25, Scroller.StartDataIndex);
            Assert.AreEqual(2545f, Scroller.ScrollPosition, EPSILON);
        }

        #endregion

        #region Alignment & Tween

        [UnityTest]
        public IEnumerator JumpAlignment_WithItemIds_SurvivesPrependAndIsReleasedWhenItemRemoved()
        {
            ItemIdTestDelegate ids = CreateWithIds(100);
            var scrollRect = (RectTransform)_fixture.ScrollRect.transform;

            // 50번(ID 1050) 가운데: 5050 − 200
            Scroller.JumpToDataIndex(50, 0.5f, 0.5f, false);
            Assert.AreEqual(4850f, Scroller.ScrollPosition, EPSILON);

            // 앞에 80짜리 3개 → ID 1050은 53번, 가운데 240 + 5050
            for (int i = 0; i < 3; i++)
            {
                ids.Insert(0, i + 1, 80f);
            }

            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(240f + 5050f - 200f, Scroller.ScrollPosition, EPSILON, "같은 항목의 가운데 정렬을 지켜야 한다");

            // 정렬이 살아 있다: 뷰포트가 커지면 같은 항목을 새 가운데로 맞춘다.
            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 600f);
            yield return null;
            yield return null;
            Assert.AreEqual(240f + 5050f - 300f, Scroller.ScrollPosition, EPSILON);

            // 정렬하던 항목이 지워지면 정렬을 풀고 맨 앞 항목(ID 1047, 셀 안 50)을 지킨다.
            float position = Scroller.ScrollPosition;
            ids.RemoveAt(53);
            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(position, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(ID_BASE + 47, Scroller.CaptureAnchor().ItemId);

            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, ScrollerFixture.VIEWPORT_HEIGHT);
            yield return null;
            yield return null;
            Assert.AreEqual(position, Scroller.ScrollPosition, EPSILON, "풀린 정렬은 뷰포트 크기 변화에 다시 맞추지 않는다");
        }

        [Test]
        public void Tween_WithItemIds_PrependDuringTweenArrivesAtSameItemOnce()
        {
            ItemIdTestDelegate ids = CreateWithIds(200);
            int completed = 0;

            // 0 → 100번(ID 1100) 시작 10000, 선형 1초. 절반에서 맨 위는 50번(ID 1050).
            Scroller.JumpToDataIndex(100, 0f, 0f, false, TweenType.Linear, 1f, () => completed++);
            Scroller.UpdateTween(0.5f);
            Assert.AreEqual(5000f, Scroller.ScrollPosition, FAR_POSITION_EPSILON);

            // 앞에 120짜리 5개 → 화면은 같은 항목(1050) 기준으로 600 밀리고, 목표도 같은 항목(1100, 이제 105번)으로 600 밀린다.
            for (int i = 0; i < 5; i++)
            {
                ids.Insert(0, i + 1, 120f);
            }

            Scroller.ReloadDataKeepingPosition();
            Assert.IsTrue(Scroller.IsTweening);
            Assert.AreEqual(5600f, Scroller.ScrollPosition, FAR_POSITION_EPSILON, "같은 항목이 맨 위에 남아야 한다");
            Assert.AreEqual(55, Scroller.StartDataIndex);

            // 화면과 목표가 같이 밀렸으므로 원래 직선을 600 옮긴 자리로 간다.
            Scroller.UpdateTween(0.25f);
            Assert.AreEqual(600f + 0.75f * 10000f, Scroller.ScrollPosition, FAR_POSITION_EPSILON);

            Scroller.UpdateTween(0.3f);
            Assert.AreEqual(1, completed);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(10600f, Scroller.ScrollPosition, FAR_POSITION_EPSILON);
            Assert.AreEqual(105, Scroller.StartDataIndex);
            Assert.AreEqual(ID_BASE + 100, Scroller.GetCellViewAtDataIndex(105).ItemId);

            Scroller.UpdateTween(0.1f);
            Assert.AreEqual(1, completed, "완료 콜백은 한 번만");
        }

        #endregion

        #region Item Id

        [Test]
        public void CellView_ItemId_IsSetBeforeBindingAndClearedWhenUnbound()
        {
            CreateWithIds(100);
            var first = (TestCellView)Scroller.GetCellViewAtDataIndex(0);
            Assert.IsTrue(first.HasItemId);
            Assert.AreEqual(ID_BASE, first.ItemId);
            Assert.AreEqual(ID_BASE, first.BoundItemId, "델리게이트가 바인딩하면서 읽은 값이 이번 항목의 ID여야 한다");

            Scroller.ScrollPosition = 50f;    // [0, 4]: 4번만 새로 들어온다
            Assert.AreSame(first, Scroller.GetCellViewAtDataIndex(0));
            Assert.AreEqual(ID_BASE, first.ItemId);

            Scroller.ScrollPosition = 100f;   // [1, 4]: 0번 회수, 새로 필요한 셀 없음
            Assert.IsFalse(first.HasItemId);
            Assert.AreEqual(0L, first.ItemId);

            Scroller.ScrollPosition = 150f;   // [1, 5]: 5번이 풀의 뷰를 재사용
            Assert.AreSame(first, Scroller.GetCellViewAtDataIndex(5));
            Assert.AreEqual(ID_BASE + 5, first.ItemId);
            Assert.AreEqual(ID_BASE + 5, first.BoundItemId);

            // 같은 데이터로 남는 갱신에서는 바뀌지 않고, ClearActive로 파괴할 셀은 바인딩과 함께 지운다.
            Scroller.RefreshActiveCellViews();
            Assert.AreEqual(ID_BASE + 5, first.ItemId);
            Scroller.ClearActive();
            Assert.IsFalse(first.HasItemId);
            Assert.AreEqual(0L, first.ItemId);
        }

        [Test]
        public void CellView_ItemId_IsSetForViewsNotTakenFromPool()
        {
            // 델리게이트가 GetCellView(prefab)를 거치지 않고 만든 뷰도 활성화할 때 ID를 받는다 (바인딩 코드 안에서는 아직 없다).
            ItemIdTestDelegate ids = CreateWithIds(10);
            ids.InstantiateDirectly = true;
            Scroller.ReloadData();

            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                var view = (TestCellView)Scroller.ActiveCellViews[i];
                Assert.IsTrue(view.HasItemId);
                Assert.AreEqual(ID_BASE + view.DataIndex, view.ItemId);
                Assert.AreEqual(-1L, view.BoundItemId);
            }
        }

        [Test]
        public void DelegateSwap_ToDelegateWithoutIds_ClearsItemIdsOnReload()
        {
            CreateWithIds(10);
            Assert.AreEqual(3, Scroller.FindDataIndexForItemId(ID_BASE + 3));

            Scroller.Delegate = _fixture.Delegate;   // ID를 주지 않는 델리게이트
            Scroller.ReloadData();

            Assert.AreEqual(-1, Scroller.FindDataIndexForItemId(ID_BASE + 3));
            Assert.IsFalse(Scroller.CaptureAnchor().HasItemId);
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                Assert.IsFalse(Scroller.ActiveCellViews[i].HasItemId);
                Assert.AreEqual(0L, Scroller.ActiveCellViews[i].ItemId);
            }
        }

        [Test]
        public void FindDataIndexForItemId_FollowsReloads()
        {
            ItemIdTestDelegate ids = CreateWithIds(10);
            Assert.AreEqual(3, Scroller.FindDataIndexForItemId(ID_BASE + 3));
            Assert.AreEqual(-1, Scroller.FindDataIndexForItemId(42L));
            int calls = ids.GetItemIdCalls;

            ids.Insert(0, 42L, 100f);
            Assert.AreEqual(-1, Scroller.FindDataIndexForItemId(42L), "다시 읽기 전에는 이전 데이터 기준");

            // 스크롤만으로는 ID를 다시 받지 않는다.
            Scroller.ScrollPosition = 300f;
            Assert.AreEqual(calls, ids.GetItemIdCalls);

            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(calls + 11, ids.GetItemIdCalls, "다시 읽을 때 항목마다 한 번");
            Assert.AreEqual(0, Scroller.FindDataIndexForItemId(42L));
            Assert.AreEqual(4, Scroller.FindDataIndexForItemId(ID_BASE + 3));
        }

        [Test]
        public void DuplicateItemIds_FirstIndexWinsAndWarnsOncePerReload()
        {
            ItemIdTestDelegate ids = CreateWithIds(10);
            ids.Ids[5] = ID_BASE + 2;
            ids.Ids[7] = ID_BASE + 2;
            ids.Ids[8] = ID_BASE + 1;

            int warnings = 0;
            void OnLog(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && message.Contains("ItemId"))
                {
                    warnings++;
                }
            }

            Application.logMessageReceived += OnLog;
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex(@"\[CyScroller\].*ItemId"));
                Scroller.ReloadData();
                Assert.AreEqual(1, warnings, "중복이 여럿이어도 리로드마다 최대 1회");
                Assert.AreEqual(2, Scroller.FindDataIndexForItemId(ID_BASE + 2), "앞 인덱스가 이긴다");
                Assert.AreEqual(1, Scroller.FindDataIndexForItemId(ID_BASE + 1));
                Assert.AreEqual(ID_BASE + 2, Scroller.GetCellViewAtDataIndex(2).ItemId);

                LogAssert.Expect(LogType.Warning, new Regex(@"\[CyScroller\].*ItemId"));
                Scroller.ReloadDataKeepingPosition();
                Assert.AreEqual(2, warnings);

                ids.Ids[5] = ID_BASE + 5;
                ids.Ids[7] = ID_BASE + 7;
                ids.Ids[8] = ID_BASE + 8;
                Scroller.ReloadData();
                Assert.AreEqual(2, warnings, "중복이 없으면 경고하지 않는다");
                Assert.AreEqual(5, Scroller.FindDataIndexForItemId(ID_BASE + 5));
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
        }

        /// <summary>
        /// 맨 앞 항목이 뒤쪽 중복 ID일 때, 데이터가 그대로인 복원·재배치·리로드는 첫 중복 항목으로 튀지 않고 제자리를 지킨다.
        /// 그 자리의 항목이 바뀌었을 때만 ID로 찾는다 (같은 ID가 여럿이면 앞 인덱스).
        /// </summary>
        [Test]
        public void DuplicateItemIds_UnchangedData_KeepsLaterDuplicateInPlace()
        {
            var duplicateWarning = new Regex(@"\[CyScroller\].*ItemId");
            ItemIdTestDelegate ids = CreateWithIds(100);
            ids.Ids[50] = ID_BASE + 20;   // 20번과 50번이 같은 ID
            LogAssert.Expect(LogType.Warning, duplicateWarning);
            Scroller.ReloadData();
            Assert.AreEqual(20, Scroller.FindDataIndexForItemId(ID_BASE + 20), "ID 조회는 앞 인덱스");

            Scroller.ScrollPosition = 5020f;
            CyScrollerAnchor anchor = Scroller.CaptureAnchor();
            Assert.AreEqual(50, anchor.DataIndex);
            Assert.AreEqual(ID_BASE + 20, anchor.ItemId);

            Scroller.ScrollPosition = 0f;
            Scroller.RestoreAnchor(anchor);
            Assert.AreEqual(5020f, Scroller.ScrollPosition, EPSILON, "캡처 → 복원 왕복");

            // 델리게이트를 다시 받지 않는 재배치: 50번 시작 10 + 5000
            Scroller.Padding = new RectOffset(0, 0, 10, 0);
            Assert.AreEqual(5030f, Scroller.ScrollPosition, EPSILON, "여백 재배치");
            Assert.AreEqual(50, Scroller.StartDataIndex);

            // 데이터가 그대로인 리로드 (다시 받을 때마다 중복 경고)
            LogAssert.Expect(LogType.Warning, duplicateWarning);
            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(5030f, Scroller.ScrollPosition, EPSILON, "ReloadDataKeepingPosition");

            LogAssert.Expect(LogType.Warning, duplicateWarning);
            Scroller.ReloadData(ReloadAnchor.FirstVisible);
            Assert.AreEqual(5030f, Scroller.ScrollPosition, EPSILON, "FirstVisible");

            // 점프 정렬 대상도 옮겨 가지 않는다: 50번 가운데 5060 − 200
            Scroller.JumpToDataIndex(50, 0.5f, 0.5f, false);
            Assert.AreEqual(4860f, Scroller.ScrollPosition, EPSILON);
            LogAssert.Expect(LogType.Warning, duplicateWarning);
            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(4860f, Scroller.ScrollPosition, EPSILON, "정렬 유지");

            // 루프 전환(재배치)도 같은 항목·오프셋
            Scroller.ScrollPosition = 5030f;
            Scroller.Loop = true;
            CyScrollerAnchor looped = Scroller.CaptureAnchor();
            Assert.AreEqual(50, looped.DataIndex, "루프 전환");
            Assert.AreEqual(20f, looped.Offset, EPSILON);

            // 앞에 하나가 끼어 50번 자리의 항목이 바뀌면 ID로 찾는다 → 같은 ID 중 앞 인덱스(옛 20번, 이제 21번)
            ids.Insert(0, 1L, 100f);
            LogAssert.Expect(LogType.Warning, duplicateWarning);
            Scroller.ReloadDataKeepingPosition();
            CyScrollerAnchor moved = Scroller.CaptureAnchor();
            Assert.AreEqual(21, moved.DataIndex);
            Assert.AreEqual(20f, moved.Offset, EPSILON);
        }

        #endregion

        #region Allocation

        private const float SWEEP_STEP = 53f;
        private const float SWEEP_END = 40000f;
        private const int ANCHOR_ROUND_TRIPS = 200;

        [Test]
        public void Scrolling_WithItemIds_DoesNotAllocateAfterWarmup()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(2000, 37f), false, 3f, reload: false);
            var ids = new ItemIdTestDelegate { Prefab = _fixture.Prefab };
            ids.Refill(2000, 37f, ID_BASE);
            Scroller.Delegate = ids;
            Scroller.LookAheadAfter = 100f;
            Scroller.ReloadData();
            int idCalls = ids.GetItemIdCalls;

            // 풀과 내부 리스트 용량을 채운다.
            Sweep();
            AnchorRoundTrips();
            Scroller.ScrollPosition = 0f;

            Assert.That(() => Sweep(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());

            // 앵커 캡처·복원과 ID 찾기도 할당하지 않는다.
            Assert.That(() => AnchorRoundTrips(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());

            Assert.AreEqual(idCalls, ids.GetItemIdCalls, "스크롤 중에는 ID를 다시 받지 않는다");
            Assert.AreEqual(ID_BASE + Scroller.StartDataIndex, Scroller.ActiveCellViews[0].ItemId);
        }

        private void Sweep()
        {
            for (float position = 0f; position < SWEEP_END; position += SWEEP_STEP)
            {
                Scroller.ScrollPosition = position;
            }
        }

        private void AnchorRoundTrips()
        {
            for (int i = 0; i < ANCHOR_ROUND_TRIPS; i++)
            {
                Scroller.ScrollPosition = i * 197f;
                CyScrollerAnchor anchor = Scroller.CaptureAnchor(i % 2 == 0);
                Scroller.ScrollPosition = SWEEP_END - i * 101f;
                Scroller.RestoreAnchor(in anchor);
                if (Scroller.FindDataIndexForItemId(anchor.ItemId) != anchor.DataIndex)
                {
                    Assert.Fail($"ID {anchor.ItemId}");
                }
            }
        }

        #endregion
    }
}
