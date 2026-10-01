using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace CyKim.Scroller.Tests
{
    public class CyScrollerPlayModeTests
    {
        private const float EPSILON = 0.01f;

        // 3만 근처 위치는 플레이 모드에서 RectTransform anchoredPosition 기록 뒤 읽은 값이 0.02 안팎까지 어긋날 때가 있다 (float 정밀도).
        private const float FAR_POSITION_EPSILON = 0.05f;

        private ScrollerFixture _fixture;

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
        }

        private CyScroller Scroller => _fixture.Scroller;

        #region Virtualization

        [Test]
        public void ReloadData_CreatesOnlyVisibleCells()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));

            Assert.AreEqual(1000, Scroller.NumberOfCells);
            Assert.AreEqual(0, Scroller.StartDataIndex);
            Assert.AreEqual(3, Scroller.EndDataIndex, "뷰포트 400 / 셀 100 → 0..3");
            Assert.AreEqual(4, Scroller.ActiveCellViews.Count);
            Assert.AreEqual(4, _fixture.InstantiatedCount);
            Assert.AreEqual(100000f, Scroller.ContentSize, EPSILON);
            Assert.AreEqual(100000f - ScrollerFixture.VIEWPORT_HEIGHT, Scroller.ScrollSize, EPSILON);
        }

        [Test]
        public void Scrolling_ReusesRecycledViews()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));

            Scroller.ScrollPosition = 5000f;
            Assert.AreEqual(50, Scroller.StartDataIndex);
            Assert.AreEqual(53, Scroller.EndDataIndex);
            Assert.AreEqual(4, _fixture.InstantiatedCount, "겹치지 않는 범위로 이동해도 풀에서 재사용해야 한다");

            Scroller.ScrollPosition = 5050f;
            Assert.AreEqual(54, Scroller.EndDataIndex);
            Assert.AreEqual(5, _fixture.InstantiatedCount);

            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                var view = (TestCellView)Scroller.ActiveCellViews[i];
                Assert.AreEqual(view.DataIndex, view.BoundData);
                Assert.AreEqual(Scroller.StartCellViewIndex + i, view.CellIndex);
                Assert.IsTrue(view.Active);
                Assert.IsTrue(view.gameObject.activeSelf);
            }
        }

        [Test]
        public void RecycledViews_AreInactiveAndReset()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            var first = (TestCellView)Scroller.GetCellViewAtDataIndex(0);

            Scroller.ScrollPosition = 5000f;
            Scroller.ScrollPosition = 9000f;

            // 첫 셀은 재활용됐다가 다시 쓰였을 수 있으므로, 풀에 있는 셀만 검사한다.
            int recycled = Scroller.GetRecycledCellCount();
            Assert.GreaterOrEqual(first.RecycledCount, 1);
            Assert.AreEqual(0, recycled, "같은 개수가 보이므로 풀은 비어 있어야 한다");
        }

        [Test]
        public void Cells_ArePositionedFromLayoutWithPadding()
        {
            var padding = new RectOffset(10, 20, 30, 40);
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), false, 5f, padding);

            CyScrollerCellView cell = Scroller.GetCellViewAtDataIndex(2);
            Assert.IsNotNull(cell);

            float start = 30f + 2 * 105f;
            RectTransform rect = cell.RectTransform;
            Assert.AreEqual(-start, rect.offsetMax.y, EPSILON);
            Assert.AreEqual(-(start + 100f), rect.offsetMin.y, EPSILON);
            Assert.AreEqual(10f, rect.offsetMin.x, EPSILON);
            Assert.AreEqual(-20f, rect.offsetMax.x, EPSILON);

            Assert.AreEqual(30f + 100 * 100f + 99 * 5f + 40f, Scroller.ContentSize, EPSILON);
            Assert.AreEqual(Scroller.ContentSize, _fixture.Content.rect.height, EPSILON);
        }

        [Test]
        public void Cells_IgnorePrefabPivot()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), reload: false);
            ((RectTransform)_fixture.Prefab.transform).pivot = new Vector2(0.2f, 0.7f);
            Scroller.ReloadData();

            RectTransform rect = Scroller.GetCellViewAtDataIndex(1).RectTransform;
            Assert.AreEqual(-100f, rect.offsetMax.y, EPSILON);
            Assert.AreEqual(-200f, rect.offsetMin.y, EPSILON);
            Assert.AreEqual(0f, rect.offsetMin.x, EPSILON);
            Assert.AreEqual(0f, rect.offsetMax.x, EPSILON);
        }

        [Test]
        public void VariableSizes_AreRespected()
        {
            float[] sizes = { 50f, 150f, 50f, 150f, 50f, 150f, 50f, 150f };
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, sizes);

            // 시작: 0, 50, 200, 250, 400 → 뷰포트 400이면 0..3
            Assert.AreEqual(3, Scroller.EndDataIndex);
            Assert.AreEqual(-200f, Scroller.GetCellViewAtDataIndex(2).RectTransform.offsetMax.y, EPSILON);

            Scroller.ScrollPosition = 210f;
            Assert.AreEqual(2, Scroller.StartDataIndex);
            Assert.AreEqual(6, Scroller.EndDataIndex);
        }

        [Test]
        public void Horizontal_UsesWidthAndNegativeContentX()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Horizontal, TestDelegate.Uniform(100, 100f));

            Assert.IsTrue(_fixture.ScrollRect.horizontal);
            Assert.IsFalse(_fixture.ScrollRect.vertical);
            Assert.AreEqual(ScrollerFixture.VIEWPORT_WIDTH, Scroller.ScrollRectSize, EPSILON);
            Assert.AreEqual(2, Scroller.EndDataIndex, "뷰포트 300 / 셀 100 → 0..2");

            Scroller.ScrollPosition = 250f;
            Assert.AreEqual(-250f, _fixture.Content.anchoredPosition.x, EPSILON);
            Assert.AreEqual(2, Scroller.StartDataIndex);
            Assert.AreEqual(5, Scroller.EndDataIndex);

            RectTransform rect = Scroller.GetCellViewAtDataIndex(3).RectTransform;
            Assert.AreEqual(300f, rect.offsetMin.x, EPSILON);
            Assert.AreEqual(400f, rect.offsetMax.x, EPSILON);
        }

        [Test]
        public void LookAhead_ExtendsActiveRange()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            Scroller.ScrollPosition = 1000f;
            Assert.AreEqual(10, Scroller.StartDataIndex);
            Assert.AreEqual(13, Scroller.EndDataIndex);

            Scroller.LookAheadBefore = 150f;
            Scroller.LookAheadAfter = 250f;
            Assert.AreEqual(8, Scroller.StartDataIndex);
            Assert.AreEqual(16, Scroller.EndDataIndex);
        }

        [Test]
        public void EmptyList_HasNoCellsAndJumpCompletesImmediately()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, new float[0]);

            Assert.AreEqual(0, Scroller.ActiveCellViews.Count);
            Assert.AreEqual(-1, Scroller.StartDataIndex);
            Assert.AreEqual(0f, Scroller.ScrollSize, EPSILON);

            bool completed = false;
            Scroller.JumpToDataIndex(5, jumpComplete: () => completed = true);
            Assert.IsTrue(completed);
        }

        [Test]
        public void CellIdentifiers_DoNotSharePools()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(500, 50f), reload: false);
            _fixture.Delegate.AltPrefab = _fixture.AltPrefab;
            _fixture.Delegate.AltEvery = 3;
            Scroller.ReloadData();

            for (float position = 0f; position < 20000f; position += 77f)
            {
                Scroller.ScrollPosition = position;
                for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
                {
                    CyScrollerCellView view = Scroller.ActiveCellViews[i];
                    string expected = view.DataIndex % 3 == 0 ? "Alt" : "Test";
                    Assert.AreEqual(expected, view.CellIdentifier, $"data {view.DataIndex}");
                }
            }
        }

        [Test]
        public void RefreshActiveCellViews_CallsEveryActiveView()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            Scroller.RefreshActiveCellViews();

            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                Assert.AreEqual(1, ((TestCellView)Scroller.ActiveCellViews[i]).RefreshCount);
            }
        }

        [Test]
        public void ReloadDataKeepingPosition_AnchorsTopDataIndex()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            Scroller.ScrollPosition = 3020f;
            Assert.AreEqual(30, Scroller.StartDataIndex);

            for (int i = 0; i < 30; i++)
            {
                _fixture.Delegate.Sizes[i] = 50f;
            }

            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(30, Scroller.StartDataIndex);
            Assert.AreEqual(30 * 50f + 20f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void ClearAll_DestroysViewsAndReloadRecreates()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            Scroller.ScrollPosition = 5000f;
            Scroller.ClearAll();
            Assert.AreEqual(0, Scroller.ActiveCellViews.Count);
            Assert.AreEqual(0, Scroller.GetRecycledCellCount());

            Scroller.ReloadData();
            Assert.AreEqual(4, Scroller.ActiveCellViews.Count);
        }

        [Test]
        public void VisibilityChanged_FiresForShowAndHide()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            int shown = 0;
            int hidden = 0;
            Scroller.CellViewVisibilityChanged += view =>
            {
                if (view.Active)
                {
                    shown++;
                }
                else
                {
                    hidden++;
                }
            };

            Scroller.ScrollPosition = 100f;
            Assert.AreEqual(1, shown);
            Assert.AreEqual(1, hidden);
        }

        #endregion

        #region Jump & Tween

        [Test]
        public void Jump_Immediate_AlignsCellStartToViewportStart()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));

            Scroller.JumpToDataIndex(500, 0f, 0f, false);
            Assert.AreEqual(50000f, Scroller.ScrollPosition, EPSILON);
            Assert.IsNotNull(Scroller.GetCellViewAtDataIndex(500));
            Assert.AreEqual(500, Scroller.StartDataIndex);
        }

        [Test]
        public void Jump_CenterOffsets_CentersCell()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));

            Scroller.JumpToDataIndex(10, 0.5f, 0.5f, false);
            Assert.AreEqual(10 * 100f + 50f - 200f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void Jump_UseSpacing_IncludesHalfGaps()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), false, 20f);

            Scroller.JumpToDataIndex(10, 0f, 0f, true);
            Assert.AreEqual(10 * 120f - 10f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void Jump_ClampsToScrollableRange()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));

            Scroller.JumpToDataIndex(99, 0f, 0f, false);
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, EPSILON);

            Scroller.JumpToDataIndex(-5, 0.5f, 0.5f, false);
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator Jump_Tween_ReachesTargetAndInvokesCallbackOnce()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int completed = 0;
            int tweenEvents = 0;
            Scroller.ScrollerTweeningChanged += (_, __) => tweenEvents++;

            Scroller.JumpToDataIndex(300, 0f, 0f, false, TweenType.EaseInOutCubic, 0.2f, () => completed++);
            Assert.IsTrue(Scroller.IsTweening);

            float timeout = 2f;
            while (completed == 0 && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, completed);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(30000f, Scroller.ScrollPosition, FAR_POSITION_EPSILON);
            Assert.AreEqual(2, tweenEvents, "시작·종료 각 1회");
            Assert.IsNotNull(Scroller.GetCellViewAtDataIndex(300));
        }

        [UnityTest]
        public IEnumerator Jump_AlignmentSurvivesViewportResize()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            Scroller.JumpToDataIndex(50, 0.5f, 0.5f, false);
            Assert.AreEqual(5050f - 200f, Scroller.ScrollPosition, EPSILON);

            var scrollRect = (RectTransform)_fixture.ScrollRect.transform;
            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 600f);
            yield return null;
            yield return null;

            Assert.AreEqual(5050f - 300f, Scroller.ScrollPosition, EPSILON, "가운데 정렬이 새 뷰포트 기준으로 유지돼야 한다");
        }

        [UnityTest]
        public IEnumerator UserScroll_ReleasesAlignment()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            Scroller.JumpToDataIndex(50, 0.5f, 0.5f, false);
            Scroller.ScrollPosition = 3000f;

            var scrollRect = (RectTransform)_fixture.ScrollRect.transform;
            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 600f);
            yield return null;
            yield return null;

            Assert.AreEqual(3000f, Scroller.ScrollPosition, EPSILON, "직접 옮긴 뒤에는 시작 기준 위치를 유지한다");
            Assert.AreEqual(30, Scroller.StartDataIndex);
            Assert.AreEqual(35, Scroller.EndDataIndex, "커진 뷰포트만큼 셀이 늘어야 한다");
        }

        [UnityTest]
        public IEnumerator Relayout_DuringJumpTween_FinishesAtNewTargetAndInvokesCallback()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int completed = 0;
            Scroller.JumpToDataIndex(50, 0f, 0f, false, TweenType.Linear, 1f, () => completed++);
            yield return null;

            Scroller.Spacing = 10f;

            Assert.AreEqual(1, completed, "재배치가 점프를 끝내도 완료 콜백은 한 번 불려야 한다");
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(50 * 110f, Scroller.ScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator Relayout_MidDrag_ContentDoesNotJump()
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

            // 맨 앞 셀(1번, 오프셋 0) 기준으로 위치를 유지하므로 110으로 옮겨진다.
            Scroller.Spacing = 10f;
            Assert.AreEqual(110f, Scroller.ScrollPosition, 1f);

            // 드래그 기준점이 갱신됐다면 이후 50px 이동은 110 + 50이어야 한다.
            eventData.position = start + new Vector2(0f, 150f);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
            yield return null;
            Assert.AreEqual(160f, Scroller.ScrollPosition, 1f);

            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);
        }

        [UnityTest]
        public IEnumerator Jump_Tween_InterruptSkipsCallback()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            bool completed = false;

            Scroller.JumpToDataIndex(300, 0f, 0f, false, TweenType.Linear, 1f, () => completed = true);
            yield return null;
            Scroller.InterruptTween();

            yield return new WaitForSecondsRealtime(0.1f);
            Assert.IsFalse(completed);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.Less(Scroller.ScrollPosition, 30000f);
        }

        #endregion

        #region Loop

        [Test]
        public void Loop_StartsInMiddleSet()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);

            Assert.AreEqual(5, Scroller.Layout.SetCount);
            Assert.AreEqual(50, Scroller.NumberOfCellSlots);
            Assert.AreEqual(2000f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(0, Scroller.StartDataIndex);
            Assert.AreEqual(20, Scroller.StartCellViewIndex);
            Assert.AreEqual(0f, Scroller.NormalizedScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator Loop_RecentersWithoutRebinding()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);
            yield return null;

            // 가운데 세트(2000)에서 반 사이클(500)을 넘겨 이동
            Scroller.ScrollPosition = 2600f;
            Assert.AreEqual(6, Scroller.StartDataIndex);
            CyScrollerCellView before = Scroller.GetCellViewAtDataIndex(6);
            int calls = _fixture.Delegate.GetCellViewCalls;

            // ScrollRect.LateUpdate가 위치 변화를 감지해 onValueChanged → 순환 보정
            yield return null;

            Assert.AreEqual(1600f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(6, Scroller.StartDataIndex);
            Assert.AreEqual(16, Scroller.StartCellViewIndex);
            Assert.AreSame(before, Scroller.GetCellViewAtDataIndex(6), "같은 뷰가 슬롯만 옮겨져야 한다");
            Assert.AreEqual(calls, _fixture.Delegate.GetCellViewCalls, "순환 보정은 다시 바인딩하지 않는다");
            Assert.AreEqual(16, before.CellIndex);
            Assert.AreEqual(-1600f, before.RectTransform.offsetMax.y, EPSILON);
        }

        [Test]
        public void Loop_ShortCycleStillFillsViewport()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(2, 100f), loop: true);

            Assert.GreaterOrEqual(Scroller.Layout.SetCount, 5);
            Assert.AreEqual(4, Scroller.ActiveCellViews.Count);
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                Assert.IsNotNull(Scroller.ActiveCellViews[i]);
            }

            Scroller.JumpToDataIndex(1, 0.5f, 0.5f, false);

            // 뷰포트 가운데에 있는 슬롯이 데이터 1이고, 그 셀의 가운데가 뷰포트 가운데여야 한다.
            int centerSlot = Scroller.GetCellViewIndexAtPosition(Scroller.ScrollPosition + 200f);
            Assert.AreEqual(1, Scroller.GetDataIndexForCellViewIndex(centerSlot));
            float expected = Scroller.GetScrollPositionForCellViewIndex(centerSlot) + 50f - 200f;
            Assert.AreEqual(expected, Scroller.ScrollPosition, EPSILON, "짧은 루프에서도 가운데 정렬 점프가 맞아야 한다");
        }

        [Test]
        public void Loop_JumpDirectionPicksCopy()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);
            Scroller.ScrollPosition = 2500f;

            float forward = Scroller.GetJumpTargetPosition(2, 0f, 0f, false, LoopJumpDirection.Forward);
            float backward = Scroller.GetJumpTargetPosition(2, 0f, 0f, false, LoopJumpDirection.Backward);
            float closest = Scroller.GetJumpTargetPosition(2, 0f, 0f, false, LoopJumpDirection.Closest);

            Assert.AreEqual(3200f, forward, EPSILON);
            Assert.AreEqual(2200f, backward, EPSILON);
            Assert.AreEqual(2200f, closest, EPSILON);
        }

        [TestCase(LoopJumpDirection.Forward)]
        [TestCase(LoopJumpDirection.Backward)]
        public void Loop_JumpDirectionIsNeverReversedAtWindowEdge(LoopJumpDirection direction)
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);

            // 창의 양 끝 근처에서 모든 데이터로 점프해도 요청한 방향으로만 간다.
            float[] starts = { 1510f, 2490f };
            for (int s = 0; s < starts.Length; s++)
            {
                for (int data = 0; data < 10; data++)
                {
                    Scroller.ScrollPosition = starts[s];
                    float from = Scroller.ScrollPosition;
                    float target = Scroller.GetJumpTargetPosition(data, 0.5f, 0.5f, false, direction);
                    if (direction == LoopJumpDirection.Forward)
                    {
                        Assert.GreaterOrEqual(target, from - EPSILON, $"from {from} data {data}");
                    }
                    else
                    {
                        Assert.LessOrEqual(target, from + EPSILON, $"from {from} data {data}");
                    }
                }
            }
        }

        [Test]
        public void Loop_ShortCycleNormalizedRoundTrips()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(3, 100f), loop: true);

            float[] values = { 0f, 0.25f, 0.5f, 0.75f, 0.9f, 0.99f };
            for (int i = 0; i < values.Length; i++)
            {
                Scroller.NormalizedScrollPosition = values[i];
                Assert.AreEqual(values[i], Scroller.NormalizedScrollPosition, 0.001f, $"value {values[i]}");
            }

            // 맨 앞 셀 기준 유지도 콘텐츠 끝에서 잘리지 않는다.
            Scroller.NormalizedScrollPosition = 0.95f;
            float before = Scroller.NormalizedScrollPosition;
            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(before, Scroller.NormalizedScrollPosition, 0.001f);
        }

        [UnityTest]
        public IEnumerator Loop_DragAcrossWrap_StaysContinuous()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);
            _fixture.ScrollRect.inertia = false;
            yield return null;

            // 콘텐츠 전체(3세트 = 3000)보다 길게 끌어도 고무줄 저항 없이 따라와야 한다.
            const float STEP = 50f;
            const int STEPS = 61;
            GameObject target = _fixture.ScrollRect.gameObject;
            var start = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left, position = start };

            ExecuteEvents.Execute(target, eventData, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);
            Assert.IsTrue(Scroller.IsDragging);

            for (int i = 1; i <= STEPS; i++)
            {
                eventData.position = start + new Vector2(0f, STEP * i);
                ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
                yield return null;
                Assert.GreaterOrEqual(Scroller.ScrollPosition, 0f);
                Assert.LessOrEqual(Scroller.ScrollPosition, Scroller.ScrollSize);
            }

            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);
            yield return null;

            // 1000에서 3050 이동 → 사이클 안 위치 50 / 1000
            Assert.AreEqual(0.05f, Scroller.NormalizedScrollPosition, 0.002f);
            Assert.AreEqual(0, Scroller.StartDataIndex);
        }

        [UnityTest]
        public IEnumerator Loop_NoWrapWhileDragging_WhenDisabled()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);
            Scroller.LoopWhileDragging = false;
            _fixture.ScrollRect.inertia = false;
            yield return null;

            GameObject target = _fixture.ScrollRect.gameObject;
            var start = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left, position = start };
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);

            eventData.position = start + new Vector2(0f, 700f);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
            yield return null;
            Assert.AreEqual(2700f, Scroller.ScrollPosition, 1f, "드래그 중에는 순환 보정하지 않는다");

            // 관성이 없어도 놓는 순간 가운데 세트로 되돌린다.
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);
            Assert.AreEqual(1700f, Scroller.ScrollPosition, 1f, "놓으면 보정한다");
            Assert.AreEqual(7, Scroller.StartDataIndex);
        }

        [Test]
        public void ToggleLoop_KeepsTopDataIndex()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(50, 100f));
            Scroller.ScrollPosition = 2030f;

            Scroller.ToggleLoop();
            Assert.IsTrue(Scroller.Loop);
            Assert.AreEqual(20, Scroller.StartDataIndex);
            Assert.AreEqual(10000f + 2030f, Scroller.ScrollPosition, EPSILON);

            Scroller.ToggleLoop();
            Assert.IsFalse(Scroller.Loop);
            Assert.AreEqual(20, Scroller.StartDataIndex);
            Assert.AreEqual(2030f, Scroller.ScrollPosition, EPSILON);
        }

        #endregion

        #region Snap & Events

        [UnityTest]
        public IEnumerator Snap_AfterDragRelease_CentersNearestCell()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            Scroller.Snapping = true;
            Scroller.SnapTweenTime = 0.05f;
            Scroller.SnapTweenType = TweenType.Linear;

            int snappedData = -1;
            Scroller.ScrollerSnapped += (_, __, dataIndex, ___) => snappedData = dataIndex;

            Scroller.ScrollPosition = 1230f;
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            Scroller.OnBeginDrag(eventData);
            Scroller.OnEndDrag(eventData);

            float timeout = 2f;
            while (snappedData < 0 && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            // 뷰포트 가운데(1430)에 가장 가까운 셀 14를 가운데로: 1400 + 50 - 200
            Assert.AreEqual(14, snappedData);
            Assert.AreEqual(1250f, Scroller.ScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator MaxVelocity_ClampsInertia()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            Scroller.MaxVelocity = 500f;
            Scroller.ScrollPosition = 5000f;
            int scrollingEvents = 0;
            Scroller.ScrollerScrollingChanged += (_, scrolling) =>
            {
                if (scrolling)
                {
                    scrollingEvents++;
                }
            };

            Scroller.LinearVelocity = 5000f;
            yield return null;

            Assert.LessOrEqual(Mathf.Abs(Scroller.LinearVelocity), 500f + EPSILON);
            Assert.IsTrue(Scroller.IsScrolling);
            Assert.AreEqual(1, scrollingEvents);
        }

        [UnityTest]
        public IEnumerator Delegate_Setter_ReloadsOnNextFrame()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), reload: false);
            Assert.AreEqual(0, Scroller.ActiveCellViews.Count);

            yield return null;
            Assert.AreEqual(4, Scroller.ActiveCellViews.Count);
        }

        [Test]
        public void ReloadData_InsideDelegateCallback_IsDeferred()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            bool reentered = false;
            Scroller.CellViewVisibilityChanged += view =>
            {
                if (!reentered && view.Active)
                {
                    reentered = true;
                    Scroller.ReloadData();
                }
            };

            Assert.DoesNotThrow(() => Scroller.ScrollPosition = 1000f);
            Assert.AreEqual(Scroller.EndCellViewIndex - Scroller.StartCellViewIndex + 1, Scroller.ActiveCellViews.Count);
        }

        [Test]
        public void UserCodeException_KeepsActiveListConsistent()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            int calls = 0;
            Scroller.CellViewVisibilityChanged += view =>
            {
                calls++;
                if (calls == 2)
                {
                    throw new System.InvalidOperationException("test");
                }
            };

            Assert.Throws<System.InvalidOperationException>(() => Scroller.ScrollPosition = 1000f);

            // 예외 뒤에도 목록 길이와 범위가 맞고, 다음 갱신이 이어서 채운다.
            Assert.AreEqual(Scroller.EndCellViewIndex - Scroller.StartCellViewIndex + 1, Scroller.ActiveCellViews.Count);
            Scroller.ScrollPosition = 1010f;
            Assert.AreEqual(10, Scroller.StartDataIndex);
            Assert.AreEqual(14, Scroller.EndDataIndex);
            Assert.AreEqual(5, Scroller.ActiveCellViews.Count);
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                Assert.AreEqual(10 + i, Scroller.ActiveCellViews[i].DataIndex);
                Assert.IsTrue(Scroller.ActiveCellViews[i].gameObject.activeSelf);
            }
        }

        [UnityTest]
        public IEnumerator ContentAssignedAfterAddComponent_StillListensToScrollRect()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), reload: false);

            // content 없이 붙은 CyScroller를 흉내 낸다: 새 ScrollRect GameObject에 먼저 CyScroller를 붙이고 나중에 content를 연결.
            var go = new GameObject("LateScroll", typeof(RectTransform));
            go.transform.SetParent(_fixture.Root.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(300f, 400f);
            var scrollRect = go.AddComponent<UnityEngine.UI.ScrollRect>();
            var scroller = go.AddComponent<CyScroller>();

            var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
            content.SetParent(go.transform, false);
            scrollRect.content = content;
            scroller.Delegate = _fixture.Delegate;
            scroller.ReloadData();
            yield return null;

            // 사용자가 스크롤한 것처럼 content를 직접 옮기면 onValueChanged로 범위가 갱신돼야 한다.
            content.anchoredPosition = new Vector2(0f, 2000f);
            yield return null;

            Assert.AreEqual(20, scroller.StartDataIndex);
        }

        #endregion

        #region Review Regressions

        [UnityTest]
        public IEnumerator ReloadDataKeepingPosition_InsideGetCellView_RequeriesDelegate()
        {
            var loadMore = new LoadMoreDelegate(_fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), reload: false));
            Scroller.Delegate = loadMore;
            Scroller.ReloadData();
            Scroller.ScrollPosition = 600f;   // 마지막 셀(9)이 보이면 20개를 더 붙이고 위치 유지 리로드
            Assert.IsTrue(loadMore.Loaded);

            yield return null;

            Assert.AreEqual(30, Scroller.NumberOfCells, "콜백 안에서 요청한 리로드도 델리게이트를 다시 질의해야 한다");
            Assert.AreEqual(6, Scroller.StartDataIndex);
        }

        [UnityTest]
        public IEnumerator DelegateSwap_NeverCallsNewDelegateWithOldIndices()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(500, 100f));
            Scroller.ScrollPosition = 30000f;
            yield return null;

            var small = new TestDelegate { Sizes = TestDelegate.Uniform(20, 100f), Prefab = _fixture.Prefab };
            Scroller.Delegate = small;

            // 같은 프레임에 스크롤 이벤트가 와도 새 델리게이트는 새 범위로만 불린다.
            _fixture.Content.anchoredPosition = new Vector2(0f, 30100f);
            Assert.DoesNotThrow(() => Scroller.ScrollPosition = 30200f);
            yield return null;

            Assert.AreEqual(20, Scroller.NumberOfCells);
            Assert.LessOrEqual(Scroller.EndDataIndex, 19);
        }

        [UnityTest]
        public IEnumerator Scrollbar_StaysHiddenInLoopAndReturnsAfter()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(50, 100f), reload: false);
            UnityEngine.UI.Scrollbar scrollbar = _fixture.AddScrollbar();
            Scroller.ReloadData();
            yield return null;
            Assert.IsTrue(scrollbar.gameObject.activeSelf);

            Scroller.Loop = true;
            yield return null;
            yield return null;
            Assert.IsFalse(scrollbar.gameObject.activeSelf, "ScrollRect가 다시 켜면 안 된다");
            Assert.IsNull(_fixture.ScrollRect.verticalScrollbar);

            Scroller.Loop = false;
            yield return null;
            Assert.AreSame(scrollbar, _fixture.ScrollRect.verticalScrollbar);
            Assert.IsTrue(scrollbar.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator ScrollPositionSetter_StopsInertiaAndTween()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            Scroller.ScrollPosition = 5000f;
            Scroller.LinearVelocity = 3000f;
            Scroller.ScrollPosition = 0f;
            yield return null;
            yield return null;
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON, "관성이 남으면 안 된다");

            bool completed = false;
            Scroller.JumpToDataIndex(300, 0f, 0f, false, TweenType.Linear, 1f, () => completed = true);
            yield return null;
            Scroller.ScrollPosition = 100f;
            yield return null;
            Assert.IsFalse(Scroller.IsTweening);
            Assert.IsFalse(completed);
            Assert.AreEqual(100f, Scroller.ScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator Jump_BottomAlignmentSurvivesViewportGrowthWithoutDrift()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            Scroller.JumpToDataIndex(999, 1f, 1f, false);
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, EPSILON);

            ((RectTransform)_fixture.ScrollRect.transform).sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 700f);
            yield return new WaitForSecondsRealtime(0.3f);

            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, 1f, "탄성 속도로 미끄러지면 안 된다");
        }

        [UnityTest]
        public IEnumerator TapDuringSnapTween_RearmsAndSnapsAfterRelease()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            Scroller.Snapping = true;
            Scroller.SnapTweenType = TweenType.Linear;
            Scroller.SnapTweenTime = 0.3f;
            int snapped = 0;
            Scroller.ScrollerSnapped += (_, __, ___, ____) => snapped++;

            Scroller.ScrollPosition = 1230f;
            Scroller.Snap();
            yield return null;
            Assert.IsTrue(Scroller.IsTweening);

            // 누름(이미 뗀 상태로 기록) → 트윈 중단 → 손을 뗀 뒤 다시 스냅
            var tap = new PointerEventData(null) { button = PointerEventData.InputButton.Left, eligibleForClick = false };
            Scroller.OnInitializePotentialDrag(tap);
            Assert.IsFalse(Scroller.IsTweening);

            float timeout = 2f;
            while (snapped == 0 && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, snapped);
            Assert.AreEqual(1250f, Scroller.ScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator HeldPointer_DelaysSnapUntilRelease()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            Scroller.Snapping = true;
            Scroller.SnapTweenTime = 0f;
            Scroller.ScrollPosition = 1230f;

            var press = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            Scroller.OnBeginDrag(press);
            Scroller.OnEndDrag(press);
            press.eligibleForClick = true;   // 다시 눌러 붙잡은 상태
            Scroller.OnInitializePotentialDrag(press);
            yield return null;
            yield return null;
            Assert.AreEqual(1230f, Scroller.ScrollPosition, EPSILON, "누르고 있는 동안은 스냅하지 않는다");

            press.eligibleForClick = false;   // 손을 뗌
            yield return null;
            yield return null;
            Assert.AreEqual(1250f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void ScrollDirectionChange_KeepsTopDataIndexAndRequeriesSizes()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            Scroller.ScrollPosition = 2030f;
            int queries = _fixture.Delegate.GetCellViewCalls;

            _fixture.Delegate.Sizes = TestDelegate.Uniform(100, 80f);
            Scroller.ScrollDirection = ScrollDirection.Horizontal;

            Assert.AreEqual(20, Scroller.StartDataIndex);
            Assert.AreEqual(20 * 80f + 30f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(8000f, Scroller.ContentSize, EPSILON);
            Assert.Greater(_fixture.Delegate.GetCellViewCalls, queries);
        }

        [UnityTest]
        public IEnumerator DisabledScrollRect_IgnoresInputAndKeepsTween()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            _fixture.ScrollRect.enabled = false;
            bool completed = false;
            Scroller.JumpToDataIndex(100, 0f, 0f, false, TweenType.Linear, 0.2f, () => completed = true);

            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(_fixture.ScrollRect.gameObject, eventData, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(_fixture.ScrollRect.gameObject, eventData, ExecuteEvents.beginDragHandler);
            Assert.IsTrue(Scroller.IsTweening, "잠긴 스크롤의 입력이 트윈을 끊으면 안 된다");

            yield return new WaitForSecondsRealtime(0.4f);
            Assert.IsTrue(completed);
        }

        [UnityTest]
        public IEnumerator ClearAllAndReload_InsideCallback_AreDeferred()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            bool done = false;
            Scroller.CellViewVisibilityChanged += view =>
            {
                if (!done && view.Active)
                {
                    done = true;
                    Scroller.ClearAll();
                    Scroller.ReloadData();
                }
            };

            Assert.DoesNotThrow(() => Scroller.ScrollPosition = 50000f);
            yield return null;

            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(4, Scroller.ActiveCellViews.Count);
            Assert.LessOrEqual(_fixture.InstantiatedCount, 16, "한 프레임에 수백 개를 만들면 안 된다");
        }

        [Test]
        public void JumpDuringDrag_EndsDragSoItIsNotUndone()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(_fixture.ScrollRect.gameObject, eventData, ExecuteEvents.beginDragHandler);
            Assert.IsTrue(Scroller.IsDragging);

            Scroller.JumpToDataIndex(500, 0f, 0f, false);
            Assert.IsFalse(Scroller.IsDragging);

            ExecuteEvents.Execute(_fixture.ScrollRect.gameObject, eventData, ExecuteEvents.dragHandler);
            Assert.AreEqual(50000f, Scroller.ScrollPosition, EPSILON, "남은 드래그 이벤트가 점프를 되돌리면 안 된다");
        }

        [Test]
        public void ZeroSizedLoop_DoesNotActivateThousandsOfCells()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 0f), loop: true,
                padding: new RectOffset(0, 0, 10, 0));

            Assert.IsFalse(Scroller.Layout.IsLoop);
            Assert.LessOrEqual(Scroller.ActiveCellViews.Count, 100);
        }

        /// <summary>마지막 셀이 보이면 항목을 더 붙이고 위치 유지 리로드를 요청하는 델리게이트.</summary>
        private sealed class LoadMoreDelegate : ICyScrollerDelegate
        {
            private readonly ScrollerFixture _fixture;
            private int _count = 10;
            public bool Loaded;

            public LoadMoreDelegate(ScrollerFixture fixture)
            {
                _fixture = fixture;
            }

            public int GetNumberOfCells(CyScroller scroller) => _count;

            public float GetCellViewSize(CyScroller scroller, int dataIndex) => 100f;

            public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
            {
                var view = (TestCellView)scroller.GetCellView(_fixture.Prefab);
                view.BoundData = dataIndex;
                if (!Loaded && dataIndex == _count - 1)
                {
                    Loaded = true;
                    _count += 20;
                    scroller.ReloadDataKeepingPosition();
                }

                return view;
            }
        }

        #endregion

        #region Drag-safe Shift

        // 드래그 속도 비교: 포인터를 프레임 dt에 비례해 움직여 프레임 속도와 상관없이 같은 속도 샘플이 나오게 한다.
        private const float DRAG_SPEED = 1500f;
        private const float DRAG_PHASE_TIME = 0.3f;
        private const int DRAG_PHASE_MIN_FRAMES = 10;
        private const float DRAG_START_POSITION = 20000f;
        private const float SHIFT_DELTA = 3000f;

        // 순간이동이 섞이면 놓는 속도가 대략 10 × SHIFT_DELTA × e^-3 ≈ 1500 더 나온다. 섞이지 않으면 차이는 1% 미만.
        private const float VELOCITY_TOLERANCE = DRAG_SPEED * 0.1f;

        [UnityTest]
        public IEnumerator Shift_MidDrag_ReleaseVelocityMatchesUnshifted()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            yield return null;

            var result = new DragResult();
            yield return DragAndRelease(null, result);
            float unshifted = result.ReleaseVelocity;
            Assert.AreEqual(DRAG_SPEED, unshifted, VELOCITY_TOLERANCE, "기준 드래그는 포인터 속도로 수렴해야 한다");
            Assert.AreEqual(DRAG_START_POSITION + result.PointerTravel, result.ReleasePosition, 1f);

            // 좌표 이동을 직접 요청
            yield return DragAndRelease(() => Scroller.ShiftScrollPosition(SHIFT_DELTA), result);
            Assert.AreEqual(unshifted, result.ReleaseVelocity, VELOCITY_TOLERANCE, "ShiftScrollPosition의 이동량이 놓는 속도에 섞이면 안 된다");
            Assert.AreEqual(DRAG_START_POSITION + result.PointerTravel + SHIFT_DELTA, result.ReleasePosition, 1f,
                "옮긴 뒤에도 손가락을 그대로 따라가야 한다");

            // 뷰포트 위쪽 셀 100개가 30씩 커지는 재배치 → 맨 앞 셀 유지로 같은 만큼 이동
            yield return DragAndRelease(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    _fixture.Delegate.Sizes[i] = 130f;
                }

                Scroller.ReloadDataKeepingPosition();
            }, result);
            Assert.AreEqual(unshifted, result.ReleaseVelocity, VELOCITY_TOLERANCE, "재배치 위치 복원이 놓는 속도에 섞이면 안 된다");
            Assert.AreEqual(DRAG_START_POSITION + result.PointerTravel + SHIFT_DELTA, result.ReleasePosition, 1f,
                "재배치 뒤에도 손가락을 그대로 따라가야 한다");
        }

        [UnityTest]
        public IEnumerator Shift_DuringTween_MovesTweenEndpoints()
        {
            // 위치를 수천 단위로 둔다. 수만 단위에서는 RectTransform 왕복 오차가 EPSILON을 넘을 수 있다.
            // 트윈을 1초로 둬서 한 프레임에 끝까지 가 버리는 일(시작점 검증이 무의미해짐)을 피한다.
            const float TWEEN_TIME = 1f;
            const float TWEEN_TARGET = 3000f;
            const float SHIFT = 500f;
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int completed = 0;
            Scroller.JumpToDataIndex(30, 0f, 0f, false, TweenType.Linear, TWEEN_TIME, () => completed++);
            yield return null;
            Assert.IsTrue(Scroller.IsTweening);

            // 0 → 3000 선형 트윈이므로 지금 위치로 진행률을 안다.
            float before = Scroller.ScrollPosition;
            float progress = before / TWEEN_TARGET;
            Scroller.ShiftScrollPosition(SHIFT);
            Assert.AreEqual(before + SHIFT, Scroller.ScrollPosition, EPSILON, "옮긴 만큼만 움직여야 한다");
            Assert.IsTrue(Scroller.IsTweening, "좌표 이동은 트윈을 끊지 않는다");

            // 이번 프레임 LateUpdate는 지금 읽은 dt로 트윈을 진행한다. 시작·목표가 모두 옮겨졌으면 500 → 3500 직선 위에 있어야 한다.
            // 시작점을 옮기지 않으면(0 → 3500) 500 × (1 − 진행률)만큼 뒤로 튄다.
            float nextProgress = Mathf.Min(1f, progress + Time.unscaledDeltaTime / TWEEN_TIME);
            float expectedNext = Mathf.LerpUnclamped(SHIFT, TWEEN_TARGET + SHIFT, nextProgress);
            yield return null;
            Assert.AreEqual(expectedNext, Scroller.ScrollPosition, 1f, "트윈 시작점도 같은 만큼 옮겨져야 한다");

            float previous = Scroller.ScrollPosition;
            float timeout = 3f;
            while (completed == 0 && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
                Assert.GreaterOrEqual(Scroller.ScrollPosition, previous - EPSILON, "선형 트윈은 뒤로 가지 않는다");
                previous = Scroller.ScrollPosition;
            }

            Assert.AreEqual(1, completed);
            Assert.AreEqual(TWEEN_TARGET + SHIFT, Scroller.ScrollPosition, EPSILON, "트윈 목표도 같은 만큼 옮겨져야 한다");
        }

        // 가장자리 너머로 끄는 단계 (드래그 시작점 기준 스크롤 위치 방향 손가락 이동량). 1·2단계가 같아 손가락이 멈춘 프레임을 흉내 낸다.
        private static readonly float[] PULL_PAST_START_STEPS = { -60f, -100f, -100f, -140f };
        private static readonly float[] PULL_PAST_END_STEPS = { 60f, 100f, 100f, 140f };

        // 고무줄 감쇠 위치 비교 허용 오차. 가장자리로 자르면 50px 안팎, 감쇠를 한 번 더 걸면 20px 넘게 어긋난다.
        private const float PULL_EPSILON = 0.1f;

        [UnityTest]
        public IEnumerator Relayout_MidDragPastTopEdge_KeepsPullAndRubberBand()
        {
            yield return RelayoutWhilePullingPastStart(ScrollDirection.Vertical);
        }

        [UnityTest]
        public IEnumerator Relayout_MidDragPastLeftEdge_KeepsPullAndRubberBand()
        {
            yield return RelayoutWhilePullingPastStart(ScrollDirection.Horizontal);
        }

        /// <summary>
        /// 앞쪽 가장자리 너머로 당긴 채 손가락이 멈춘 프레임에 재배치(Spacing)한다.
        /// 맨 앞 셀(0번) 시작은 그대로이므로 당긴 위치와 이후 고무줄 감쇠가 재배치 없는 드래그와 같아야 한다.
        /// </summary>
        private IEnumerator RelayoutWhilePullingPastStart(ScrollDirection direction)
        {
            _fixture = ScrollerFixture.Create(direction, TestDelegate.Uniform(100, 100f));
            _fixture.ScrollRect.inertia = false;
            yield return null;

            var expected = new float[PULL_PAST_START_STEPS.Length];
            yield return PullPastEdge(0f, PULL_PAST_START_STEPS, expected, null);
            Assert.Less(expected[1], -10f, "기준 드래그는 앞쪽 가장자리 너머로 당겨져야 한다");
            Assert.Greater(expected[1], -100f, "당긴 거리는 고무줄 감쇠로 손가락 이동보다 짧다");

            var actual = new float[PULL_PAST_START_STEPS.Length];
            yield return PullPastEdge(0f, PULL_PAST_START_STEPS, actual, step =>
            {
                if (step == 2)
                {
                    float pulled = Scroller.ScrollPosition;
                    Scroller.Spacing = 10f;
                    Assert.AreEqual(pulled, Scroller.ScrollPosition, PULL_EPSILON, "재배치가 당긴 위치를 가장자리로 자르면 안 된다");
                }

                return null;
            });

            for (int i = 0; i < actual.Length; i++)
            {
                Assert.AreEqual(expected[i], actual[i], PULL_EPSILON, $"단계 {i}: 재배치 뒤에도 같은 손가락 위치는 같은 당김 위치여야 한다");
            }
        }

        [UnityTest]
        public IEnumerator Relayout_InScrollEventMidDragPastBottomEdge_FollowsMovedEdge()
        {
            const int GROWN_CELLS = 30;
            const float GROWTH = GROWN_CELLS * 100f;
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            _fixture.ScrollRect.inertia = false;
            yield return null;

            float bottom = Scroller.ScrollSize;
            var expected = new float[PULL_PAST_END_STEPS.Length];
            yield return PullPastEdge(bottom, PULL_PAST_END_STEPS, expected, null);
            Assert.Greater(expected[1], bottom + 10f, "기준 드래그는 아래쪽 가장자리 너머로 당겨져야 한다");

            // 1단계 이동을 알리는 스크롤 이벤트(onValueChanged 안)에서 위쪽 셀 30개를 100씩 키워 위치 유지 리로드
            // → 맨 앞 셀과 아래쪽 가장자리가 함께 3000 뒤로 간다.
            bool relayoutOnScroll = false;
            int scrolledEvents = 0;
            Scroller.ScrollerScrolled += (_, __, ___) =>
            {
                scrolledEvents++;
                if (!relayoutOnScroll)
                {
                    return;
                }

                relayoutOnScroll = false;
                for (int i = 0; i < GROWN_CELLS; i++)
                {
                    _fixture.Delegate.Sizes[i] = 200f;
                }

                Scroller.ReloadDataKeepingPosition();
            };

            // 재배치한 다음 프레임, 손가락이 그대로면 ScrollRect의 직전 위치·경계가 맞아 스크롤 이벤트가 다시 오지 않아야 한다.
            IEnumerator WaitIdleFrame()
            {
                int events = scrolledEvents;
                float position = Scroller.ScrollPosition;
                yield return null;
                Assert.AreEqual(events, scrolledEvents, "재배치 뒤 ScrollRect의 직전 경계가 어긋나면 안 된다");
                Assert.AreEqual(position, Scroller.ScrollPosition, PULL_EPSILON);
            }

            var actual = new float[PULL_PAST_END_STEPS.Length];
            yield return PullPastEdge(bottom, PULL_PAST_END_STEPS, actual, step =>
            {
                if (step == 1)
                {
                    relayoutOnScroll = true;
                }

                return step == 2 ? WaitIdleFrame() : null;
            });

            Assert.IsFalse(relayoutOnScroll, "스크롤 이벤트 안에서 재배치가 일어나야 한다");
            Assert.AreEqual(bottom + GROWTH, Scroller.ScrollSize, EPSILON);
            Assert.AreEqual(expected[0], actual[0], PULL_EPSILON);
            for (int i = 1; i < actual.Length; i++)
            {
                Assert.AreEqual(expected[i] + GROWTH, actual[i], PULL_EPSILON, $"단계 {i}: 옮겨진 가장자리에서 같은 거리만큼 당겨져 있어야 한다");
            }
        }

        [UnityTest]
        public IEnumerator Loop_JumpThenRecenter_KeepsAlignmentOnViewportResize()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);
            yield return null;

            // 뒤쪽(Forward) 사본(슬롯 28)으로 가운데 정렬 점프 → 목표 2650은 창(1500~2500) 밖이라 바로 슬롯 18 사본으로 순환 보정된다.
            Scroller.JumpToDataIndex(8, 0.5f, 0.5f, false, loopJumpDirection: LoopJumpDirection.Forward);
            Assert.AreEqual(1650f, Scroller.ScrollPosition, EPSILON);

            // ScrollRect가 이동을 알리는 프레임. 정렬 위치도 같이 옮겨졌어야 정렬이 풀리지 않는다.
            yield return null;
            yield return null;

            ((RectTransform)_fixture.ScrollRect.transform).sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 600f);
            yield return null;
            yield return null;

            Assert.AreEqual(1850f - 300f, Scroller.ScrollPosition, EPSILON, "순환 보정 뒤에도 점프 정렬이 유지돼야 한다");
            int centerSlot = Scroller.GetCellViewIndexAtPosition(Scroller.ScrollPosition + 300f);
            Assert.AreEqual(8, Scroller.GetDataIndexForCellViewIndex(centerSlot));
        }

        /// <summary>
        /// 포인터를 일정 속도로 위로 끌다가 놓는다. 앞 구간 마지막 프레임의 드래그 이동 직후(같은 프레임 LateUpdate 전)에
        /// midDrag를 한 번 부르고, 뒤 구간을 더 끈 뒤 놓는 순간의 속도·위치를 기록한다.
        /// </summary>
        private IEnumerator DragAndRelease(System.Action midDrag, DragResult result)
        {
            Scroller.ScrollPosition = DRAG_START_POSITION;
            yield return null;

            GameObject target = _fixture.ScrollRect.gameObject;
            var start = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left, position = start };
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);

            float travel = 0f;
            for (int phase = 0; phase < 2; phase++)
            {
                float elapsed = 0f;
                int frames = 0;
                while (elapsed < DRAG_PHASE_TIME || frames < DRAG_PHASE_MIN_FRAMES)
                {
                    yield return null;
                    float deltaTime = Time.unscaledDeltaTime;
                    travel += DRAG_SPEED * deltaTime;
                    eventData.position = start + new Vector2(0f, travel);
                    ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
                    elapsed += deltaTime;
                    frames++;
                }

                if (phase == 0)
                {
                    midDrag?.Invoke();
                }
            }

            // 마지막 드래그 프레임의 LateUpdate가 속도를 계산한 다음 프레임에서, 관성이 적용되기 전에 놓는다.
            yield return null;
            result.PointerTravel = travel;
            result.ReleasePosition = Scroller.ScrollPosition;
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);
            result.ReleaseVelocity = Scroller.LinearVelocity;
        }

        private sealed class DragResult
        {
            public float PointerTravel;
            public float ReleasePosition;
            public float ReleaseVelocity;
        }

        /// <summary>
        /// startPosition에서 손가락을 steps(시작점 기준, 스크롤 위치가 커지는 방향이 양수)대로 끌고,
        /// 단계마다 다음 프레임의 위치를 positions에 기록한 뒤 놓는다.
        /// beforeStep(i)는 i단계 이동 직전에 불리며, 돌려준 코루틴이 있으면 끝날 때까지 기다린다 (null이면 재배치 없는 기준 드래그).
        /// </summary>
        private IEnumerator PullPastEdge(float startPosition, float[] steps, float[] positions, System.Func<int, IEnumerator> beforeStep)
        {
            Scroller.ScrollPosition = startPosition;
            yield return null;

            // 세로는 손가락을 위로(+y), 가로는 왼쪽으로(−x) 끌어야 스크롤 위치가 커진다.
            Vector2 forward = Scroller.ScrollDirection == ScrollDirection.Vertical ? Vector2.up : Vector2.left;
            GameObject target = _fixture.ScrollRect.gameObject;
            var start = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left, position = start };
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);

            for (int i = 0; i < steps.Length; i++)
            {
                IEnumerator hook = beforeStep?.Invoke(i);
                if (hook != null)
                {
                    yield return hook;
                }

                eventData.position = start + forward * steps[i];
                ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
                yield return null;
                positions[i] = Scroller.ScrollPosition;
            }

            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);
        }

        #endregion

        #region Allocation

        [Test]
        public void Scrolling_DoesNotAllocateAfterWarmup()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(2000, 37f), false, 3f);
            Scroller.LookAheadAfter = 100f;

            // 풀과 내부 리스트 용량을 채운다.
            for (float position = 0f; position < 40000f; position += 53f)
            {
                Scroller.ScrollPosition = position;
            }

            Scroller.ScrollPosition = 0f;

            Assert.That(() =>
            {
                for (float position = 0f; position < 40000f; position += 53f)
                {
                    Scroller.ScrollPosition = position;
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        private const float LOOP_SWEEP_STEP = 53f;
        private const int LOOP_SWEEP_CYCLES = 4;

        [Test]
        public void LoopScrollSweep_WithRecenter_DoesNotAllocateAfterWarmup()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(40, 37f), loop: true, spacing: 3f);
            Scroller.LookAheadBefore = 60f;
            Scroller.LookAheadAfter = 100f;
            int steps = Mathf.CeilToInt(Scroller.Layout.CycleExtent * LOOP_SWEEP_CYCLES / LOOP_SWEEP_STEP);

            // 풀과 내부 리스트 용량을 채운다.
            SweepLoop(steps, LOOP_SWEEP_STEP);
            SweepLoop(steps, -LOOP_SWEEP_STEP);

            int recentered = 0;
            Assert.That(() =>
            {
                recentered += SweepLoop(steps, LOOP_SWEEP_STEP);
                recentered += SweepLoop(steps, -LOOP_SWEEP_STEP);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());

            // 앞뒤로 네 사이클씩 지나면 방향마다 네 번 안팎 순환 보정된다.
            Assert.GreaterOrEqual(recentered, LOOP_SWEEP_CYCLES * 2 - 2, "순환 보정 구간을 지나야 한다");
        }

        /// <summary>
        /// 위치를 step씩 옮기고, ScrollRect가 LateUpdate에서 하듯 onValueChanged를 알려 순환 보정 경로를 탄다.
        /// 순환 보정이 일어난 횟수를 돌려준다.
        /// </summary>
        private int SweepLoop(int steps, float step)
        {
            int recentered = 0;
            for (int i = 0; i < steps; i++)
            {
                float target = Scroller.ScrollPosition + step;
                Scroller.ScrollPosition = target;
                _fixture.ScrollRect.onValueChanged.Invoke(Vector2.zero);
                if (Mathf.Abs(Scroller.ScrollPosition - target) > 1f)
                {
                    recentered++;
                }
            }

            return recentered;
        }

        #endregion
    }
}
