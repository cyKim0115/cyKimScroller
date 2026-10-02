using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Object = UnityEngine.Object;

namespace CyKim.Scroller.Tests
{
    /// <summary>셀 훅: 바인딩 버전, 실제 뷰포트 기준 표시 이벤트, 뷰포트 위치 훅.</summary>
    public class CyScrollerCellHookTests
    {
        private const float POSITION_EPSILON = 0.01f;
        private const float OFFSET_EPSILON = 0.0001f;

        private ScrollerFixture _fixture;

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
        }

        private CyScroller Scroller => _fixture.Scroller;

        private TestCellView CellAt(int dataIndex) => (TestCellView)Scroller.GetCellViewAtDataIndex(dataIndex);

        #region Bind Version

        [Test]
        public void BindVersion_IncrementsOnEveryBindAndRecycle()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            TestCellView view = CellAt(0);
            Assert.IsTrue(view.IsBound);
            int bound = view.BindVersion;
            Assert.Greater(bound, 0);
            Assert.AreEqual(bound, view.BoundVersion, "델리게이트가 바인딩하면서 읽은 값이 이번 바인딩의 값이어야 한다");

            // 같은 데이터로 남는 갱신은 버전을 바꾸지 않는다.
            Scroller.RefreshActiveCellViews();
            Scroller.ScrollPosition = 50f;   // [0, 4]: 4번만 새로 들어온다
            Assert.AreSame(view, CellAt(0));
            Assert.AreEqual(bound, view.BindVersion);

            // 풀로 돌아가면 바인딩이 풀리고 버전이 오른다. 회수 전에 시작한 비동기 작업은 버전이 달라 결과를 버린다.
            Scroller.ScrollPosition = 100f;  // [1, 4]: 0번 회수, 새로 필요한 셀 없음
            Assert.IsFalse(view.IsBound);
            Assert.AreEqual(-1, view.DataIndex);
            Assert.AreEqual(bound + 1, view.BindVersion);
            Assert.AreNotEqual(view.BoundVersion, view.BindVersion);

            // 다른 데이터로 다시 바인딩되면 또 오르고, 바인딩 코드는 새 값을 읽는다.
            Scroller.ScrollPosition = 150f;  // [1, 5]: 5번이 풀의 뷰를 재사용
            Assert.AreSame(view, CellAt(5));
            Assert.IsTrue(view.IsBound);
            Assert.AreEqual(bound + 2, view.BindVersion);
            Assert.AreEqual(view.BindVersion, view.BoundVersion);

            // 리로드는 회수 + 재바인딩이다.
            int beforeReload = view.BindVersion;
            Scroller.ReloadData();
            Assert.Greater(view.BindVersion, beforeReload);
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                var active = (TestCellView)Scroller.ActiveCellViews[i];
                Assert.AreEqual(active.BindVersion, active.BoundVersion, $"data {active.DataIndex}");
            }
        }

        [Test]
        public void BindVersion_CountsViewsNotTakenFromPool()
        {
            // 델리게이트가 GetCellView(prefab)를 거치지 않고 직접 만든 뷰도 바인딩마다 센다.
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), reload: false);
            Scroller.Delegate = new DirectInstantiateDelegate(_fixture);
            Scroller.ReloadData();

            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                Assert.AreEqual(1, Scroller.ActiveCellViews[i].BindVersion);
            }

            TestCellView first = CellAt(0);
            Scroller.ScrollPosition = 100f;   // 0번 회수
            Assert.IsFalse(first.IsBound);
            Assert.AreEqual(2, first.BindVersion);
        }

        #endregion

        #region Display Events

        [Test]
        public void DisplayEvents_LookAheadCellsAreActiveButNotDisplayed()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), reload: false);
            Scroller.LookAheadBefore = 250f;
            Scroller.LookAheadAfter = 250f;
            var recorder = new DisplayRecorder(Scroller);
            Scroller.ReloadData();

            // 뷰포트 [1000, 1400]: 10~13번이 보이고, 미리보기 구간 [750, 1650]의 7~9·14~16번은 활성이지만 보이지 않는다.
            Scroller.ScrollPosition = 1000f;
            Assert.AreEqual(7, Scroller.StartDataIndex);
            Assert.AreEqual(16, Scroller.EndDataIndex);
            AssertDisplayedData(recorder, 10, 13, "1000");
            recorder.ClearLog();

            // 14번이 미리보기 구간에서 뷰포트로 들어온다. 7번은 활성 범위에서 빠지지만 보이던 셀이 아니라 표시 끝이 없다.
            int instantiated = _fixture.InstantiatedCount;
            Scroller.ScrollPosition = 1050f;
            CollectionAssert.AreEqual(new[] { 14 }, recorder.WillData);
            CollectionAssert.IsEmpty(recorder.EndData);
            Assert.AreEqual(instantiated, _fixture.InstantiatedCount, "이미 활성인 셀은 다시 만들지 않는다");
            recorder.ClearLog();

            // 10번이 뷰포트를 벗어난다. 미리보기 구간에 남아 활성은 유지한다.
            Scroller.ScrollPosition = 1100f;
            CollectionAssert.AreEqual(new[] { 10 }, recorder.EndData);
            CollectionAssert.IsEmpty(recorder.WillData);
            Assert.IsTrue(CellAt(10).Active);
            Assert.IsFalse(CellAt(10).IsDisplayed);
            AssertDisplayedData(recorder, 11, 14, "1100");
            recorder.AssertVirtualCallsMatchEvents("lookahead");
        }

        [Test]
        public void DisplayEvents_ReloadAndClearActive_EndDisplayBeforeRecycleOrDestroy()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), reload: false);
            Scroller.LookAheadBefore = 150f;
            Scroller.LookAheadAfter = 150f;
            var recorder = new DisplayRecorder(Scroller);
            Scroller.CellViewWillRecycle += view => recorder.Log.Add("recycle " + view.DataIndex);
            Scroller.CellViewVisibilityChanged += view =>
            {
                if (view.Active)
                {
                    recorder.Log.Add("active " + view.DataIndex);
                }
            };

            Scroller.ReloadData();
            Scroller.ScrollPosition = 1000f;   // 보이는 10~13, 활성 8~15
            recorder.ClearLog();

            // 리로드: 보이던 셀은 회수 전에 표시 끝, 새 셀은 활성화 뒤에 표시 시작.
            Scroller.ReloadData();
            CollectionAssert.AreEquivalent(new[] { 10, 11, 12, 13 }, recorder.EndData);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, recorder.WillData);
            for (int data = 10; data <= 13; data++)
            {
                Assert.Less(recorder.Log.IndexOf("end " + data), recorder.Log.IndexOf("recycle " + data), $"{data}: 표시 끝이 회수보다 먼저");
            }

            for (int data = 0; data <= 3; data++)
            {
                Assert.Less(recorder.Log.IndexOf("active " + data), recorder.Log.IndexOf("will " + data), $"{data}: 활성화가 표시 시작보다 먼저");
            }

            AssertDisplayedMatchesViewport(recorder, "reload");
            recorder.ClearLog();

            // ClearActive: 보이던 셀(0~3)만 표시 끝을 받는다. 미리보기 구간 셀(4·5)은 받지 않는다. 파괴할 셀은 바인딩이 풀린다.
            var cleared = new List<TestCellView>();
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                cleared.Add((TestCellView)Scroller.ActiveCellViews[i]);
            }

            Assert.AreEqual(6, cleared.Count);
            Scroller.ClearActive();
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, recorder.EndData);
            CollectionAssert.IsEmpty(recorder.Displayed);
            Assert.AreEqual(-1, Scroller.DisplayedFirstSlot);
            foreach (TestCellView view in cleared)
            {
                Assert.IsFalse(view.IsBound);
                Assert.IsFalse(view.IsDisplayed);
                Assert.AreNotEqual(view.BoundVersion, view.BindVersion, "파괴할 셀도 바인딩 버전이 올라 늦은 비동기 결과를 버린다");
            }

            recorder.ClearLog();

            Scroller.ReloadData();
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, recorder.WillData);
            AssertDisplayedMatchesViewport(recorder, "reload after clear");
            recorder.AssertVirtualCallsMatchEvents("reload/clear");
        }

        [Test]
        public void DestroyingScroller_UnbindsActiveCellsWithoutCallingUserCode()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), reload: false);
            Scroller.LookAheadAfter = 150f;
            var recorder = new DisplayRecorder(Scroller);
            Scroller.ReloadData();   // 보이는 0~3, 미리보기 구간 4·5

            var views = new List<TestCellView>();
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                views.Add((TestCellView)Scroller.ActiveCellViews[i]);
            }

            Assert.AreEqual(6, views.Count);
            int userCalls = 0;
            Scroller.CellViewWillRecycle += _ => userCalls++;
            Scroller.CellViewVisibilityChanged += _ => userCalls++;
            recorder.ClearLog();

            // 팝업을 닫듯 캔버스째 파괴한다. 셀은 content 아래라 같이 파괴된다.
            Object.DestroyImmediate(_fixture.Root);

            CollectionAssert.IsEmpty(recorder.EndData, "파괴 순서가 정해져 있지 않으므로 표시 끝 등 사용자 코드를 부르지 않는다");
            Assert.AreEqual(0, userCalls);
            foreach (TestCellView view in views)
            {
                Assert.IsFalse(view.IsBound, $"data {view.BoundData}");
                Assert.IsFalse(view.Active, $"data {view.BoundData}");
                Assert.AreNotEqual(view.BoundVersion, view.BindVersion, $"data {view.BoundData}: 바인딩 때 기억한 버전과 달라 늦은 비동기 결과를 버린다");
                Assert.AreEqual(view.BoundData <= 3, view.IsDisplayed, $"data {view.BoundData}: 표시 끝 없이 마지막 값으로 남아 셀의 OnDestroy에서 정리할 수 있다");
                Assert.AreEqual(0, view.BecameHiddenCount);
            }
        }

        [Test]
        public void DisplayEvents_LoopRecenterDoesNotFire()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true, reload: false);
            Scroller.LookAheadBefore = 150f;
            Scroller.LookAheadAfter = 150f;
            var recorder = new DisplayRecorder(Scroller);
            Scroller.ReloadData();   // 가운데 세트 2000에서 시작, 사이클 1000

            // 순환 보정 창(1500~2500) 밖. 보이는 슬롯 26~29 (데이터 6~9)
            Scroller.ScrollPosition = 2600f;
            AssertDisplayedMatchesViewport(recorder, "2600");
            Assert.AreEqual(26, Scroller.DisplayedFirstSlot);
            Assert.AreEqual(29, Scroller.DisplayedLastSlot);

            var views = new List<CyScrollerCellView>();
            var versions = new List<int>();
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                views.Add(Scroller.ActiveCellViews[i]);
                versions.Add(Scroller.ActiveCellViews[i].BindVersion);
            }

            recorder.ClearLog();

            // ScrollRect가 LateUpdate에서 하듯 위치 변화를 알린다 → 한 사이클 앞(1600)으로 순환 보정
            _fixture.ScrollRect.onValueChanged.Invoke(Vector2.zero);

            Assert.AreEqual(1600f, Scroller.ScrollPosition, POSITION_EPSILON);
            CollectionAssert.IsEmpty(recorder.WillData, "같은 데이터로 슬롯 번호만 옮기면 표시 이벤트가 없다");
            CollectionAssert.IsEmpty(recorder.EndData);
            Assert.AreEqual(16, Scroller.DisplayedFirstSlot);
            Assert.AreEqual(19, Scroller.DisplayedLastSlot);
            Assert.AreEqual(views.Count, Scroller.ActiveCellViews.Count);
            for (int i = 0; i < views.Count; i++)
            {
                Assert.AreSame(views[i], Scroller.ActiveCellViews[i]);
                Assert.AreEqual(versions[i], views[i].BindVersion, "순환 보정은 다시 바인딩하지 않는다");
            }

            AssertDisplayedMatchesViewport(recorder, "recentered");
        }

        [TestCase(RangeCallback.DidEndDisplay)]
        [TestCase(RangeCallback.WillRecycle)]
        [TestCase(RangeCallback.OnRecycled)]
        [TestCase(RangeCallback.Recycled)]
        [TestCase(RangeCallback.GetCellView)]
        [TestCase(RangeCallback.Activated)]
        [TestCase(RangeCallback.WillDisplay)]
        public void DisplayEvents_LoopJumpInsideRangeCallback_RebuildsAtNewPosition(RangeCallback callback)
        {
            // 데이터 2000개 × 100 → 사이클 200000, 세트 5, 가운데 세트 시작 400000, 순환 보정 창 300000~500000.
            // 뷰포트 400 + 미리보기 앞뒤 100: 400000이면 활성 슬롯 3999~4004, 보이는 슬롯 4000~4003.
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(2000, 100f), loop: true, reload: false);
            Scroller.LookAheadBefore = 100f;
            Scroller.LookAheadAfter = 100f;
            var recorder = new DisplayRecorder(Scroller);
            var viewport = new ViewportProbe(Scroller);
            Scroller.ReloadData();
            Assert.AreEqual(400000f, Scroller.ScrollPosition, POSITION_EPSILON);

            // 400120으로 옮기면 한 번의 범위 갱신에서 표시 끝(0번) → 회수(1999번) → 활성화(5·6번) → 표시 시작(4·5번) 콜백이 모두 불린다.
            // 그중 한 자리에서 0번의 뒤쪽 사본(600000)으로 즉시 점프한다. 순환 보정 창 밖이라 한 사이클 앞(400000)으로 보정되며 슬롯 번호가 2000 준다.
            bool jumped = false;
            HookOnce(callback, () =>
            {
                jumped = true;
                Scroller.JumpToDataIndex(0, 0f, 0f, false, loopJumpDirection: LoopJumpDirection.Forward);
            });

            int calls = _fixture.Delegate.GetCellViewCalls;
            Assert.DoesNotThrow(() => Scroller.ScrollPosition = 400120f);
            Assert.IsTrue(jumped, "콜백이 불려야 한다");

            // 콜백 뒤 옛 위치 기준 작업을 멈추고, 미룬 순환 보정을 한 뒤 새 위치로 다시 맞춘다.
            Assert.AreEqual(400000f, Scroller.ScrollPosition, POSITION_EPSILON);
            Assert.AreEqual(3999, Scroller.StartCellViewIndex, "뷰포트 + 미리보기 구간만 활성");
            Assert.AreEqual(4004, Scroller.EndCellViewIndex);
            Assert.AreEqual(6, Scroller.ActiveCellViews.Count);
            Assert.AreEqual(4000, Scroller.DisplayedFirstSlot);
            Assert.AreEqual(4003, Scroller.DisplayedLastSlot);
            Assert.LessOrEqual(_fixture.Delegate.GetCellViewCalls - calls, 16, "옛 범위 경계까지 한 사이클 분량(2000개)을 활성화하면 안 된다");
            Assert.Greater(viewport.WillDisplayCount, 0);
            Assert.AreEqual(0, viewport.OutsideCount, "표시 시작은 그 순간 뷰포트에 걸친 셀에만 온다");
            AssertDisplayedMatchesViewport(recorder, callback.ToString());
            recorder.AssertVirtualCallsMatchEvents(callback.ToString());

            // ScrollRect가 다음 LateUpdate에서 위치 변화를 알려도 그대로다.
            _fixture.ScrollRect.onValueChanged.Invoke(Vector2.zero);
            Assert.AreEqual(4000, Scroller.DisplayedFirstSlot);
            Assert.AreEqual(4003, Scroller.DisplayedLastSlot);
            AssertDisplayedMatchesViewport(recorder, callback + " next update");
        }

        [TestCase(RangeCallback.DidEndDisplay)]
        [TestCase(RangeCallback.WillRecycle)]
        [TestCase(RangeCallback.OnRecycled)]
        [TestCase(RangeCallback.Recycled)]
        [TestCase(RangeCallback.GetCellView)]
        [TestCase(RangeCallback.Activated)]
        [TestCase(RangeCallback.WillDisplay)]
        public void DisplayEvents_JumpInsideRangeCallback_RangeFollowsNewPosition(RangeCallback callback)
        {
            // 루프가 아니어도 콜백 안에서 옮긴 위치로 범위를 다시 맞춘다. 옛 위치 범위가 남으면
            // (스크롤 이벤트 안이었으면 ScrollRect가 옮긴 위치를 직전 위치로 저장해) 다음 스크롤까지 빈 뷰포트가 보인다.
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f), reload: false);
            var recorder = new DisplayRecorder(Scroller);
            var viewport = new ViewportProbe(Scroller);
            Scroller.ReloadData();

            bool jumped = false;
            HookOnce(callback, () =>
            {
                jumped = true;
                Scroller.JumpToDataIndex(500, 0f, 0f, false);
            });

            // 120: 표시 끝(0번) → 회수(0번) → 활성화(4·5번) → 표시 시작(4·5번)
            Scroller.ScrollPosition = 120f;
            Assert.IsTrue(jumped, "콜백이 불려야 한다");
            Assert.AreEqual(50000f, Scroller.ScrollPosition, POSITION_EPSILON);
            Assert.AreEqual(500, Scroller.StartDataIndex);
            Assert.AreEqual(503, Scroller.EndDataIndex);
            Assert.AreEqual(0, viewport.OutsideCount, "표시 시작은 그 순간 뷰포트에 걸친 셀에만 온다");
            AssertDisplayedData(recorder, 500, 503, callback.ToString());
            recorder.AssertVirtualCallsMatchEvents(callback.ToString());
        }

        [Test]
        public void SnapInsideRangeCallback_ReportsCellAfterRangeUpdate([Values] RangeCallback callback, [Values] bool loop)
        {
            // 셀 100, 뷰포트 400. 루프는 10개(사이클 1000, 가운데 세트 2000에서 시작, 순환 보정 창 1500~2500), 아니면 1000개.
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(loop ? 10 : 1000, 100f), loop);
            Scroller.SnapTweenType = TweenType.Immediate;
            Scroller.SnapWatchOffset = 0f;
            Scroller.SnapJumpToOffset = 0f;
            Scroller.SnapCellCenterOffset = 0f;

            int snapped = 0;
            int snappedCell = -1;
            int snappedData = -1;
            CyScrollerCellView snappedView = null;
            bool readyAtEvent = false;
            Scroller.ScrollerSnapped += (scroller, cellIndex, dataIndex, view) =>
            {
                snapped++;
                snappedCell = cellIndex;
                snappedData = dataIndex;
                snappedView = view;
                readyAtEvent = view != null && view.CellIndex == cellIndex && view.IsDisplayed
                    && scroller.GetCellViewAtCellIndex(cellIndex) == view;
            };

            // 범위 갱신 콜백 안에서 맨 앞 셀(6번)을 뷰포트 맨 앞에 즉시 스냅한다. 콜백 안에서는 새 위치의 셀이 아직 활성화되지 않았을 수 있다.
            // 루프: 2650 → 슬롯 26(2600)은 순환 보정 창 밖이라, 콜백 뒤로 미룬 순환 보정이 한 사이클 앞(1600, 슬롯 16)으로 옮긴다.
            // 루프가 아니면 650 → 600(슬롯 6).
            HookOnce(callback, () => Scroller.Snap());
            Scroller.ScrollPosition = loop ? 2650f : 650f;

            int expectedSlot = loop ? 16 : 6;
            Assert.AreEqual(1, snapped);
            Assert.AreEqual(expectedSlot * 100f, Scroller.ScrollPosition, POSITION_EPSILON);
            Assert.AreEqual(6, snappedData);
            Assert.AreEqual(expectedSlot, snappedCell, "순환 보정 뒤 슬롯으로 알린다");
            Assert.IsTrue(readyAtEvent, "범위를 새 위치로 다시 맞춘 뒤 알려 그 슬롯의 셀 뷰가 활성·표시 중이어야 한다");
            Assert.AreEqual(6, snappedView.DataIndex);
            Assert.AreSame(snappedView, Scroller.GetCellViewAtCellIndex(snappedCell));
            Assert.AreEqual(expectedSlot, Scroller.StartCellViewIndex);
            Assert.AreEqual(expectedSlot + 3, Scroller.EndCellViewIndex);
        }

        [UnityTest]
        public IEnumerator SnapThenReloadInsideRangeCallback_ReportsOnceAfterDeferredRecenter()
        {
            // 루프 10개 × 100 (사이클 1000, 가운데 세트 2000에서 시작, 순환 보정 창 1500~2500), 뷰포트 400.
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);
            Scroller.SnapTweenType = TweenType.Immediate;
            Scroller.SnapWatchOffset = 0f;
            Scroller.SnapJumpToOffset = 0f;
            Scroller.SnapCellCenterOffset = 0f;

            int snapped = 0;
            int snappedCell = -1;
            bool readyAtEvent = false;
            Scroller.ScrollerSnapped += (scroller, cellIndex, dataIndex, view) =>
            {
                snapped++;
                snappedCell = cellIndex;
                readyAtEvent = dataIndex == 6 && view != null && view.DataIndex == 6 && view.CellIndex == cellIndex;
            };

            // 표시 시작 콜백 안에서 6번(슬롯 26, 2600)에 즉시 스냅하고 위치 유지 리로드를 요청한다. 범위 갱신은 리로드를 기다리며 멈추지만
            // 스냅은 콜백이 미룬 순환 보정(1600, 슬롯 16)을 끝낸 뒤 리로드 전에 한 번 알린다.
            HookOnce(RangeCallback.WillDisplay, () =>
            {
                Scroller.Snap();
                Scroller.ReloadDataKeepingPosition();
            });
            Scroller.ScrollPosition = 2650f;

            Assert.AreEqual(1, snapped);
            Assert.AreEqual(16, snappedCell, "순환 보정 뒤 슬롯으로 알린다");
            Assert.IsTrue(readyAtEvent, "알릴 때 그 슬롯의 셀 뷰가 활성이어야 한다");

            // 미룬 리로드는 다음 LateUpdate에 처리된다. 스냅은 다시 알리지 않는다.
            yield return null;
            Assert.AreEqual(1, snapped);
            Assert.AreEqual(1600f, Scroller.ScrollPosition, POSITION_EPSILON);
            Assert.AreEqual(16, Scroller.StartCellViewIndex);
        }

        [UnityTest]
        public IEnumerator DisplayEvents_CallbackThatKeepsMoving_StopsAfterBoundedPassesAndRetriesInLateUpdate()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f), reload: false);
            var recorder = new DisplayRecorder(Scroller);
            Scroller.ReloadData();
            yield return null;

            // 표시 시작마다 1000씩 옮기는 콜백. 스스로 멈추지 않으므로 다시 맞추기에 상한이 없으면 안전 한도(MOVE_GUARD)까지 옮긴다.
            // 스크롤 이벤트의 범위 갱신이 끝나면(ScrollerScrolled) 옮기기를 멈춰, 그 범위 갱신 안에서 옮긴 횟수를 따로 센다.
            const int MOVE_GUARD = CyScroller.MAX_RANGE_PASSES * 5;
            int moves = 0;
            int movesInScrollEvent = -1;
            bool moving = true;
            Scroller.CellViewWillDisplay += (scroller, view) =>
            {
                if (moving && moves < MOVE_GUARD)
                {
                    moves++;
                    scroller.ScrollPosition += 1000f;
                }
            };
            Scroller.ScrollerScrolled += (_, __, ___) =>
            {
                if (moving)
                {
                    moving = false;
                    movesInScrollEvent = moves;
                }
            };

            // 사용자가 스크롤한 것처럼 content를 옮긴다. ScrollRect LateUpdate의 스크롤 이벤트 안에서 콜백이 위치를 옮기면
            // ScrollRect가 그 위치를 직전 위치로 저장해 다음 스크롤 이벤트가 오지 않으므로, 같은 프레임 스크롤러 LateUpdate가 이어서 맞춰야 한다.
            _fixture.Content.anchoredPosition = new Vector2(0f, 3000f);
            yield return null;

            Assert.AreEqual(CyScroller.MAX_RANGE_PASSES, movesInScrollEvent, "범위 갱신 한 번에 다시 맞추는 횟수는 상한까지");
            Assert.AreEqual(CyScroller.MAX_RANGE_PASSES, moves, "재시도는 멈춘 콜백 아래에서 마지막 위치로 맞추기만 한다");
            float expected = 3000f + 1000f * CyScroller.MAX_RANGE_PASSES;
            Assert.AreEqual(expected, Scroller.ScrollPosition, POSITION_EPSILON);
            int top = Mathf.RoundToInt(expected / 100f);
            Assert.AreEqual(top, Scroller.StartDataIndex, "스크롤러 LateUpdate가 마지막 위치로 다시 맞춘다");
            AssertDisplayedData(recorder, top, top + 3, "retried");
            recorder.AssertVirtualCallsMatchEvents("keeps moving");
        }

        [Test]
        public void DisplayEvents_Horizontal()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Horizontal, TestDelegate.Uniform(100, 100f), reload: false);
            Scroller.LookAheadAfter = 120f;
            var recorder = new DisplayRecorder(Scroller);
            Scroller.ReloadData();

            // 뷰포트 너비 300: 0~2번이 보이고 미리보기 구간(~420)의 3·4번은 활성만
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, recorder.WillData);
            Assert.AreEqual(4, Scroller.EndDataIndex);
            Assert.IsFalse(CellAt(3).IsDisplayed);
            recorder.ClearLog();

            // 뷰포트 [250, 550]: 보이는 2~5, 활성 2~6
            Scroller.ScrollPosition = 250f;
            CollectionAssert.AreEqual(new[] { 0, 1 }, recorder.EndData);
            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, recorder.WillData);
            AssertDisplayedData(recorder, 2, 5, "horizontal 250");
            recorder.AssertVirtualCallsMatchEvents("horizontal");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisplayEvents_StayPairedAcrossRandomOperations(bool loop)
        {
            var sizes = new float[loop ? 40 : 300];
            for (int i = 0; i < sizes.Length; i++)
            {
                sizes[i] = 40f + (i * 37 % 7) * 20f;
            }

            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, sizes, loop, 5f, new RectOffset(0, 0, 30, 30), reload: false);
            Scroller.LookAheadBefore = 90f;
            Scroller.LookAheadAfter = 130f;
            var recorder = new DisplayRecorder(Scroller);
            Scroller.ReloadData();

            var random = new System.Random(20261001);
            for (int step = 0; step < 400; step++)
            {
                int op = random.Next(20);
                if (op < 12)
                {
                    Scroller.ScrollPosition += random.Next(-250, 251);
                }
                else if (op < 15)
                {
                    Scroller.ScrollPosition = (float)(random.NextDouble() * Scroller.ScrollSize);
                }
                else if (op < 17)
                {
                    Scroller.JumpToDataIndex(random.Next(sizes.Length), 0.5f, 0.5f, false);
                }
                else if (op == 17)
                {
                    Scroller.ScrollIntoView(random.Next(sizes.Length));
                }
                else if (op == 18)
                {
                    sizes[random.Next(sizes.Length)] = 40f + random.Next(8) * 20f;
                    Scroller.ReloadDataKeepingPosition();
                }
                else
                {
                    Scroller.LookAheadAfter = random.Next(0, 300);
                }

                if (loop)
                {
                    // ScrollRect가 LateUpdate에서 하듯 위치 변화를 알려 순환 보정 경로도 탄다.
                    _fixture.ScrollRect.onValueChanged.Invoke(Vector2.zero);
                }

                AssertDisplayedMatchesViewport(recorder, $"step {step} op {op}");
            }

            recorder.AssertVirtualCallsMatchEvents("random");
        }

        [Test]
        public void BecameVisibleAndHidden_FollowEachEventImmediately()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), reload: false);
            Scroller.LookAheadAfter = 100f;
            var recorder = new DisplayRecorder(Scroller);
            Scroller.ReloadData();

            float[] positions = { 30f, 170f, 1000f, 960f, 2000f, 0f };
            for (int i = 0; i < positions.Length; i++)
            {
                Scroller.ScrollPosition = positions[i];

                // 이벤트를 받을 때 그 셀의 가상 메서드는 아직이고, 다음 이벤트 전에는 불려 있어야 한다 (DisplayRecorder가 매 이벤트 확인).
                recorder.AssertVirtualCallsMatchEvents($"position {positions[i]}");
                for (int c = 0; c < Scroller.ActiveCellViews.Count; c++)
                {
                    var view = (TestCellView)Scroller.ActiveCellViews[c];
                    Assert.AreEqual(view.IsDisplayed, view.BecameVisibleCount - view.BecameHiddenCount == 1, $"data {view.DataIndex}");
                }
            }

            Assert.Greater(recorder.WillCount, 10);
            Assert.Greater(recorder.EndCount, 5);
        }

        [UnityTest]
        public IEnumerator DisplayEvents_ReloadInsideWillDisplay_IsDeferredAndStaysPaired()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f), reload: false);
            var recorder = new DisplayRecorder(Scroller);
            Scroller.ReloadData();
            bool reloaded = false;
            Scroller.CellViewWillDisplay += (scroller, view) =>
            {
                if (!reloaded && view.DataIndex == 50)
                {
                    reloaded = true;
                    scroller.ReloadData();
                }
            };

            Assert.DoesNotThrow(() => Scroller.ScrollPosition = 5000f);
            Assert.IsTrue(reloaded);
            Assert.AreEqual(5000f, Scroller.ScrollPosition, POSITION_EPSILON, "콜백 안 리로드는 범위 갱신이 끝난 뒤 처리한다");
            AssertDisplayedMatchesViewport(recorder, "deferred");

            yield return null;

            Assert.AreEqual(0f, Scroller.ScrollPosition, POSITION_EPSILON);
            AssertDisplayedData(recorder, 0, 3, "after reload");
            recorder.AssertVirtualCallsMatchEvents("reentrant reload");
        }

        [Test]
        public void DisplayEvents_UserCodeExceptions_KeepBookkeepingConsistent()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), reload: false);
            var recorder = new DisplayRecorder(Scroller);
            Scroller.ReloadData();

            int willCalls = 0;
            Scroller.CellViewWillDisplay += (_, __) =>
            {
                if (++willCalls == 2)
                {
                    throw new System.InvalidOperationException("will");
                }
            };

            Assert.Throws<System.InvalidOperationException>(() => Scroller.ScrollPosition = 1000f);

            // 던진 셀까지는 보이는 것으로 기록돼 있고(가상 메서드도 불림), 다음 갱신이 나머지를 채운다.
            Scroller.ScrollPosition = 1010f;
            AssertDisplayedData(recorder, 10, 14, "after will exception");

            int endCalls = 0;
            Scroller.CellViewDidEndDisplay += (_, __) =>
            {
                if (++endCalls == 2)
                {
                    throw new System.InvalidOperationException("end");
                }
            };

            Assert.Throws<System.InvalidOperationException>(() => Scroller.ScrollPosition = 5000f);
            Assert.AreEqual(Scroller.EndCellViewIndex - Scroller.StartCellViewIndex + 1, Scroller.ActiveCellViews.Count);

            Scroller.ScrollPosition = 5010f;
            AssertDisplayedData(recorder, 50, 54, "after end exception");
            Assert.AreEqual(Scroller.EndCellViewIndex - Scroller.StartCellViewIndex + 1, Scroller.ActiveCellViews.Count);
            recorder.AssertVirtualCallsMatchEvents("exceptions");
        }

        #endregion

        #region Position Hook

        [UnityTest]
        public IEnumerator PositionHook_ReportsPivotPointRelativeToViewport()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f), reload: false);
            Scroller.LookAheadBefore = 200f;
            Scroller.NotifyCellPositions = true;
            Scroller.ReloadData();

            // 10번 가운데(1050)를 뷰포트 가운데에: 뷰포트 [850, 1250], 활성 6~12
            Scroller.JumpToDataIndex(10, 0.5f, 0.5f, false);
            yield return null;
            Assert.AreEqual(0.5f, CellAt(10).LastNormalizedOffset, OFFSET_EPSILON, "셀 가운데가 뷰포트 가운데면 0.5");
            Assert.AreEqual(0.25f, CellAt(9).LastNormalizedOffset, OFFSET_EPSILON);
            Assert.AreEqual(1f, CellAt(12).LastNormalizedOffset, OFFSET_EPSILON, "가운데가 뷰포트 끝에 걸친 셀은 1");
            Assert.AreEqual(-0.5f, CellAt(6).LastNormalizedOffset, OFFSET_EPSILON, "미리보기 구간 셀은 0 미만");

            // 같은 활성 범위 안에서 움직이면 다음 LateUpdate에 모든 활성 셀이 새 값을 받는다.
            Scroller.ScrollPosition = 860f;
            yield return null;
            Assert.AreEqual((1050f - 860f) / 400f, CellAt(10).LastNormalizedOffset, OFFSET_EPSILON);
            Assert.AreEqual((650f - 860f) / 400f, CellAt(6).LastNormalizedOffset, OFFSET_EPSILON);

            Scroller.CellPositionPivot = 0f;
            yield return null;
            Assert.AreEqual((1000f - 860f) / 400f, CellAt(10).LastNormalizedOffset, OFFSET_EPSILON, "피벗 0 = 셀 시작");

            Scroller.CellPositionPivot = 1f;
            yield return null;
            Assert.AreEqual((1100f - 860f) / 400f, CellAt(10).LastNormalizedOffset, OFFSET_EPSILON, "피벗 1 = 셀 끝");

            // 뷰포트 길이가 바뀌면 다시 계산한다.
            ((RectTransform)_fixture.ScrollRect.transform).sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 600f);
            yield return null;
            Assert.AreEqual((1100f - 860f) / 600f, CellAt(10).LastNormalizedOffset, OFFSET_EPSILON);
            AssertActiveOffsets("resized", 860f);
        }

        [Test]
        public void PositionHook_Horizontal()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Horizontal, TestDelegate.Uniform(100, 100f), reload: false);
            Scroller.NotifyCellPositions = true;
            Scroller.ReloadData();
            Scroller.ScrollPosition = 250f;   // 뷰포트 [250, 550]
            Scroller.NotifyCellPositionsIfChanged();

            Assert.AreEqual((350f - 250f) / 300f, CellAt(3).LastNormalizedOffset, OFFSET_EPSILON, "가로는 왼쪽 가장자리가 0");
            Assert.AreEqual((550f - 250f) / 300f, CellAt(5).LastNormalizedOffset, OFFSET_EPSILON);
            AssertActiveOffsets("horizontal", 250f);
        }

        [Test]
        public void PositionHook_NewCellsGetValueOnActivation()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f), reload: false);
            Scroller.NotifyCellPositions = true;
            Scroller.ReloadData();

            // 리로드 직후, LateUpdate를 기다리지 않고 모든 활성 셀이 값을 받았다.
            AssertActiveOffsets("reload", 0f);
            int frame = Time.frameCount;

            // 겹치지 않는 범위로 옮기면 들어온 셀 모두가 그 자리에서 받는다.
            Scroller.ScrollPosition = 1030f;
            AssertActiveOffsets("jump", 1030f);
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                Assert.AreEqual(frame, ((TestCellView)Scroller.ActiveCellViews[i]).LastPositionFrame);
            }

            // 겹치는 범위로 옮기면 새로 들어온 셀만 바로 받고, 남은 셀은 LateUpdate 패스에서 받는다.
            Scroller.ScrollPosition = 1130f;   // [11, 15]: 15번만 새로
            Assert.AreEqual((1550f - 1130f) / 400f, CellAt(15).LastNormalizedOffset, OFFSET_EPSILON);
            Assert.AreEqual((1150f - 1030f) / 400f, CellAt(11).LastNormalizedOffset, OFFSET_EPSILON, "남은 셀은 패스 전까지 이전 값");

            Scroller.NotifyCellPositionsIfChanged();
            AssertActiveOffsets("pass", 1130f);
        }

        [UnityTest]
        public IEnumerator PositionHook_DisabledMakesNoCalls()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int events = 0;
            Scroller.CellViewPositionChanged += (_, __, ___) => events++;
            Assert.IsFalse(Scroller.NotifyCellPositions, "기본값은 꺼짐");

            Scroller.ScrollPosition = 3000f;
            yield return null;
            Scroller.ScrollPosition = 3050f;
            yield return null;
            Assert.AreEqual(0, events);
            Assert.AreEqual(0, TotalPositionCalls(), "꺼져 있으면 새 셀·이동 모두 부르지 않는다");

            // 켜면 다음 LateUpdate에 지금 활성 셀 모두가 한 번씩 받는다.
            Scroller.NotifyCellPositions = true;
            yield return null;
            Assert.AreEqual(Scroller.ActiveCellViews.Count, events);
            AssertActiveOffsets("enabled", 3050f);

            // 다시 끄면 더는 부르지 않는다.
            Scroller.NotifyCellPositions = false;
            int calls = TotalPositionCalls();
            Scroller.ScrollPosition = 6000f;
            yield return null;
            Scroller.ScrollPosition = 6020f;
            yield return null;
            Assert.AreEqual(calls, TotalPositionCalls());
            Assert.AreEqual(calls, events);
        }

        [UnityTest]
        public IEnumerator PositionHook_NoCallsInFramesWithoutChanges()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f), reload: false);
            Scroller.NotifyCellPositions = true;
            int events = 0;
            Scroller.CellViewPositionChanged += (_, __, ___) => events++;
            Scroller.ReloadData();
            yield return null;
            yield return null;

            int settled = events;
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.AreEqual(settled, events, "위치·범위·크기가 그대로인 프레임에는 부르지 않는다");

            // 한 번 움직이면 그 프레임 패스에서 활성 셀마다 한 번만 받는다 (새로 들어온 4번은 활성화 때 한 번 더).
            Scroller.ScrollPosition = 30f;
            Assert.AreEqual(settled + 1, events, "새로 들어온 셀은 활성화 즉시");
            yield return null;
            Assert.AreEqual(settled + 1 + Scroller.ActiveCellViews.Count, events);

            int moved = events;
            yield return null;
            yield return null;
            Assert.AreEqual(moved, events);
        }

        [UnityTest]
        public IEnumerator PositionHook_ChangesAfterScrollerLateUpdate()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f), reload: false);
            Scroller.NotifyCellPositions = true;
            Scroller.ReloadData();
            var probe = _fixture.Root.AddComponent<LateUpdateProbe>();
            yield return null;

            // 스크롤러 LateUpdate 뒤(같은 프레임)에 겹치지 않는 범위로 옮긴다. 새로 들어온 셀은 그 자리에서, 렌더 전에 값을 받는다.
            probe.Pending = () => Scroller.ScrollPosition = 3000f;
            yield return null;
            Assert.Greater(probe.RanFrame, 0);
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                Assert.AreEqual(probe.RanFrame, ((TestCellView)Scroller.ActiveCellViews[i]).LastPositionFrame, "같은 프레임에 받아야 한다");
            }

            AssertActiveOffsets("late jump", 3000f);

            // 스크롤러 LateUpdate 뒤에 같은 범위 안에서 옮기면, 새로 들어온 셀만 바로 받고 남은 셀은 다음 프레임에 반영된다.
            probe.Pending = () => Scroller.ScrollPosition = 3040f;   // [30, 34]: 34번만 새로
            yield return null;
            TestCellView cell = CellAt(30);
            Assert.AreEqual((3050f - 3000f) / 400f, cell.LastNormalizedOffset, OFFSET_EPSILON, "다음 LateUpdate 전에는 이전 값");
            Assert.AreEqual((3450f - 3040f) / 400f, CellAt(34).LastNormalizedOffset, OFFSET_EPSILON, "새 셀은 바로");

            yield return null;
            Assert.AreEqual((3050f - 3040f) / 400f, cell.LastNormalizedOffset, OFFSET_EPSILON, "다음 프레임에는 반영");
            AssertActiveOffsets("next frame", 3040f);
        }

        #endregion

        #region Allocation

        private const float SWEEP_STEP = 53f;
        private const float SWEEP_END = 40000f;
        private const int LOOP_SWEEP_CYCLES = 4;

        [Test]
        public void CellHooks_ScrollSweep_DoesNotAllocateAfterWarmup()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(2000, 37f), false, 3f, reload: false);
            Scroller.LookAheadBefore = 60f;
            Scroller.LookAheadAfter = 100f;
            Scroller.NotifyCellPositions = true;
            var counter = new HookCounter(Scroller);
            Scroller.ReloadData();

            // 풀과 내부 리스트 용량을 채운다.
            Sweep();
            Scroller.ScrollPosition = 0f;
            Scroller.NotifyCellPositionsIfChanged();
            counter.Reset();
            int displayedAtStart = CountDisplayed();

            Assert.That(() => Sweep(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());

            // 셀 높이 40(간격 포함)으로 40000을 지나므로 약 1000개가 뷰포트에 들어온다.
            Assert.Greater(counter.WillDisplay, 900, "표시 이벤트 경로를 지나야 한다");
            Assert.Greater(counter.PositionChanged, 5000, "위치 훅 경로를 지나야 한다");
            Assert.AreEqual(CountDisplayed() - displayedAtStart, counter.WillDisplay - counter.DidEndDisplay, "스윕 동안에도 짝이 맞는다");
        }

        [Test]
        public void CellHooks_LoopSweepWithRecenter_DoesNotAllocateAfterWarmup()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(40, 37f), loop: true, spacing: 3f, reload: false);
            Scroller.LookAheadBefore = 60f;
            Scroller.LookAheadAfter = 100f;
            Scroller.NotifyCellPositions = true;
            var counter = new HookCounter(Scroller);
            Scroller.ReloadData();
            int steps = Mathf.CeilToInt(Scroller.Layout.CycleExtent * LOOP_SWEEP_CYCLES / SWEEP_STEP);

            // 풀과 내부 리스트 용량을 채운다.
            SweepLoop(steps, SWEEP_STEP);
            SweepLoop(steps, -SWEEP_STEP);
            counter.Reset();
            int displayedAtStart = CountDisplayed();

            Assert.That(() =>
            {
                SweepLoop(steps, SWEEP_STEP);
                SweepLoop(steps, -SWEEP_STEP);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());

            Assert.Greater(counter.WillDisplay, 100);
            Assert.Greater(counter.PositionChanged, 1000);
            Assert.AreEqual(CountDisplayed() - displayedAtStart, counter.WillDisplay - counter.DidEndDisplay);
        }

        private void Sweep()
        {
            for (float position = 0f; position < SWEEP_END; position += SWEEP_STEP)
            {
                Scroller.ScrollPosition = position;
                Scroller.NotifyCellPositionsIfChanged();
            }
        }

        /// <summary>위치를 step씩 옮기고 ScrollRect가 LateUpdate에서 하듯 onValueChanged를 알려 순환 보정 경로를 탄 뒤 위치 훅 패스를 돈다.</summary>
        private void SweepLoop(int steps, float step)
        {
            for (int i = 0; i < steps; i++)
            {
                Scroller.ScrollPosition = Scroller.ScrollPosition + step;
                _fixture.ScrollRect.onValueChanged.Invoke(Vector2.zero);
                Scroller.NotifyCellPositionsIfChanged();
            }
        }

        private int CountDisplayed()
        {
            int count = 0;
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                if (Scroller.ActiveCellViews[i].IsDisplayed)
                {
                    count++;
                }
            }

            return count;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 활성 셀마다 실제 뷰포트에 걸치는지 직접 계산해 IsDisplayed·기록된 표시 상태와 비교한다.
        /// 보이는 셀 수가 맞으면 활성이 아닌 셀이 표시 중으로 남아 있지 않다는 뜻이다.
        /// </summary>
        private void AssertDisplayedMatchesViewport(DisplayRecorder recorder, string context)
        {
            float viewStart = Scroller.ScrollPosition;
            float viewEnd = viewStart + Scroller.ScrollRectSize;
            int expected = 0;
            IReadOnlyList<CyScrollerCellView> active = Scroller.ActiveCellViews;
            for (int i = 0; i < active.Count; i++)
            {
                CyScrollerCellView view = active[i];
                float start = Scroller.GetScrollPositionForCellViewIndex(view.CellIndex);
                float end = start + Scroller.GetCellSize(view.DataIndex);
                bool overlaps = start < viewEnd && end > viewStart;
                Assert.AreEqual(overlaps, view.IsDisplayed, $"{context}: data {view.DataIndex} (slot {view.CellIndex}) IsDisplayed");
                Assert.AreEqual(overlaps, recorder.Displayed.Contains(view), $"{context}: data {view.DataIndex} 표시 이벤트 짝");
                if (overlaps)
                {
                    expected++;
                }
            }

            Assert.AreEqual(expected, recorder.Displayed.Count, $"{context}: 표시 중인 셀 수");
            Assert.AreEqual(0, recorder.PairingErrors, $"{context}: 짝 오류");
            Assert.AreEqual(0, recorder.OrderErrors, $"{context}: 가상 메서드 순서 오류");
        }

        /// <summary>표시 중인 데이터가 정확히 [firstData, lastData]인지 (루프가 아닐 때).</summary>
        private void AssertDisplayedData(DisplayRecorder recorder, int firstData, int lastData, string context)
        {
            AssertDisplayedMatchesViewport(recorder, context);
            var displayed = new List<int>();
            foreach (CyScrollerCellView view in recorder.Displayed)
            {
                displayed.Add(view.DataIndex);
            }

            var expected = new List<int>();
            for (int data = firstData; data <= lastData; data++)
            {
                expected.Add(data);
            }

            CollectionAssert.AreEquivalent(expected, displayed, context);
        }

        /// <summary>활성 셀마다 마지막으로 받은 위치가 position 기준 기대값인지.</summary>
        private void AssertActiveOffsets(string context, float position)
        {
            IReadOnlyList<CyScrollerCellView> cells = Scroller.ActiveCellViews;
            Assert.Greater(cells.Count, 0, context);
            float viewportSize = Scroller.ScrollRectSize;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                float pivot = Scroller.GetScrollPositionForCellViewIndex(view.CellIndex)
                    + Scroller.GetCellSize(view.DataIndex) * Scroller.CellPositionPivot;
                Assert.AreEqual((pivot - position) / viewportSize, view.LastNormalizedOffset, OFFSET_EPSILON, $"{context}: data {view.DataIndex}");
            }
        }

        /// <summary>풀에 있는 셀까지 포함한 위치 훅 호출 수 합.</summary>
        private int TotalPositionCalls()
        {
            int total = 0;
            TestCellView[] views = _fixture.Content.GetComponentsInChildren<TestCellView>(true);
            for (int i = 0; i < views.Length; i++)
            {
                total += views[i].PositionCallCount;
            }

            return total;
        }

        /// <summary>범위 갱신 중 사용자 코드가 불리는 자리.</summary>
        public enum RangeCallback
        {
            /// <summary><see cref="CyScroller.CellViewDidEndDisplay"/></summary>
            DidEndDisplay,

            /// <summary><see cref="CyScroller.CellViewWillRecycle"/></summary>
            WillRecycle,

            /// <summary><see cref="CyScrollerCellView.OnRecycled"/></summary>
            OnRecycled,

            /// <summary><see cref="CyScroller.CellViewVisibilityChanged"/> (회수, Active false)</summary>
            Recycled,

            /// <summary>델리게이트 <see cref="ICyScrollerDelegate.GetCellView"/></summary>
            GetCellView,

            /// <summary><see cref="CyScroller.CellViewVisibilityChanged"/> (활성화, Active true)</summary>
            Activated,

            /// <summary><see cref="CyScroller.CellViewWillDisplay"/></summary>
            WillDisplay,
        }

        /// <summary>
        /// callback 자리에서 처음 한 번만 action을 부른다. 지금 활성 셀에만 OnRecycled 훅을 거므로 범위 갱신 전에 부른다.
        /// 다른 구독보다 뒤에 걸어 같은 이벤트의 기록·확인이 먼저 끝나게 한다.
        /// </summary>
        private void HookOnce(RangeCallback callback, System.Action action)
        {
            bool fired = false;

            void Fire()
            {
                if (!fired)
                {
                    fired = true;
                    action();
                }
            }

            switch (callback)
            {
                case RangeCallback.DidEndDisplay:
                    Scroller.CellViewDidEndDisplay += (_, __) => Fire();
                    break;
                case RangeCallback.WillRecycle:
                    Scroller.CellViewWillRecycle += _ => Fire();
                    break;
                case RangeCallback.OnRecycled:
                    for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
                    {
                        ((TestCellView)Scroller.ActiveCellViews[i]).RecycledHook = _ => Fire();
                    }

                    break;
                case RangeCallback.Recycled:
                    Scroller.CellViewVisibilityChanged += view =>
                    {
                        if (!view.Active)
                        {
                            Fire();
                        }
                    };
                    break;
                case RangeCallback.GetCellView:
                    _fixture.Delegate.GetCellViewHook = (_, __) => Fire();
                    break;
                case RangeCallback.Activated:
                    Scroller.CellViewVisibilityChanged += view =>
                    {
                        if (view.Active)
                        {
                            Fire();
                        }
                    };
                    break;
                case RangeCallback.WillDisplay:
                    Scroller.CellViewWillDisplay += (_, __) => Fire();
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(callback), callback, null);
            }
        }

        /// <summary>표시 시작을 받은 순간 그 셀이 실제 뷰포트에 걸쳐 있었는지 센다.</summary>
        private sealed class ViewportProbe
        {
            public int WillDisplayCount;
            public int OutsideCount;

            public ViewportProbe(CyScroller scroller)
            {
                scroller.CellViewWillDisplay += OnWillDisplay;
            }

            private void OnWillDisplay(CyScroller scroller, CyScrollerCellView view)
            {
                WillDisplayCount++;
                float start = scroller.GetScrollPositionForCellViewIndex(view.CellIndex);
                float end = start + scroller.GetCellSize(view.DataIndex);
                float viewStart = scroller.ScrollPosition;
                if (!(start < viewStart + scroller.ScrollRectSize && end > viewStart))
                {
                    OutsideCount++;
                }
            }
        }

        /// <summary>
        /// 표시 이벤트를 셀별로 기록한다. 같은 셀에 표시 시작이 두 번 오거나 짝 없는 표시 끝이 오면 짝 오류,
        /// 가상 메서드가 이벤트 바로 뒤(다음 이벤트 전)에 불리지 않으면 순서 오류로 센다.
        /// </summary>
        private sealed class DisplayRecorder
        {
            public readonly HashSet<CyScrollerCellView> Displayed = new HashSet<CyScrollerCellView>();
            public readonly List<int> WillData = new List<int>();
            public readonly List<int> EndData = new List<int>();
            public readonly List<string> Log = new List<string>();
            public int PairingErrors;
            public int OrderErrors;
            public int WillCount;
            public int EndCount;

            private readonly Dictionary<CyScrollerCellView, int> _willCounts = new Dictionary<CyScrollerCellView, int>();
            private readonly Dictionary<CyScrollerCellView, int> _endCounts = new Dictionary<CyScrollerCellView, int>();
            private TestCellView _lastView;

            public DisplayRecorder(CyScroller scroller)
            {
                scroller.CellViewWillDisplay += OnWillDisplay;
                scroller.CellViewDidEndDisplay += OnDidEndDisplay;
            }

            public void ClearLog()
            {
                CheckLastView();
                WillData.Clear();
                EndData.Clear();
                Log.Clear();
            }

            /// <summary>기록된 모든 셀의 가상 메서드 호출 수가 이벤트 수와 같은지.</summary>
            public void AssertVirtualCallsMatchEvents(string context)
            {
                CheckLastView();
                foreach (KeyValuePair<CyScrollerCellView, int> pair in _willCounts)
                {
                    Assert.AreEqual(pair.Value, ((TestCellView)pair.Key).BecameVisibleCount, $"{context}: OnBecameVisible 수");
                }

                foreach (KeyValuePair<CyScrollerCellView, int> pair in _endCounts)
                {
                    Assert.AreEqual(pair.Value, ((TestCellView)pair.Key).BecameHiddenCount, $"{context}: OnBecameHidden 수");
                }

                Assert.AreEqual(0, PairingErrors, $"{context}: 짝 오류");
                Assert.AreEqual(0, OrderErrors, $"{context}: 순서 오류");
            }

            private void OnWillDisplay(CyScroller scroller, CyScrollerCellView view)
            {
                CheckLastView();
                var cell = (TestCellView)view;

                // 활성화된 뒤에, 표시 플래그가 선 채로 오고, 같은 셀에 두 번 연속 오면 안 된다.
                if (!view.Active || !view.IsDisplayed || !Displayed.Add(view))
                {
                    PairingErrors++;
                }

                int count = Increment(_willCounts, view);
                if (cell.BecameVisibleCount != count - 1)
                {
                    OrderErrors++;
                }

                WillCount++;
                WillData.Add(view.DataIndex);
                Log.Add("will " + view.DataIndex);
                _lastView = cell;
            }

            private void OnDidEndDisplay(CyScroller scroller, CyScrollerCellView view)
            {
                CheckLastView();
                var cell = (TestCellView)view;

                // 회수·파괴 전(아직 활성)에, 표시 플래그가 내려진 채로 오고, 표시 시작을 받은 셀에만 와야 한다.
                if (!view.Active || view.IsDisplayed || !Displayed.Remove(view))
                {
                    PairingErrors++;
                }

                int count = Increment(_endCounts, view);
                if (cell.BecameHiddenCount != count - 1)
                {
                    OrderErrors++;
                }

                EndCount++;
                EndData.Add(view.DataIndex);
                Log.Add("end " + view.DataIndex);
                _lastView = cell;
            }

            /// <summary>직전 이벤트를 받은 셀의 가상 메서드가 다음 이벤트 전에 불렸는지.</summary>
            private void CheckLastView()
            {
                if (_lastView == null)
                {
                    return;
                }

                if (_lastView.BecameVisibleCount != Get(_willCounts, _lastView) || _lastView.BecameHiddenCount != Get(_endCounts, _lastView))
                {
                    OrderErrors++;
                }

                _lastView = null;
            }

            private static int Increment(Dictionary<CyScrollerCellView, int> counts, CyScrollerCellView view)
            {
                int count = Get(counts, view) + 1;
                counts[view] = count;
                return count;
            }

            private static int Get(Dictionary<CyScrollerCellView, int> counts, CyScrollerCellView view)
            {
                return counts.TryGetValue(view, out int count) ? count : 0;
            }
        }

        /// <summary>GC 측정용. 메서드 그룹을 구독 때 한 번만 델리게이트로 만들어 호출 중에는 할당하지 않는다.</summary>
        private sealed class HookCounter
        {
            public int WillDisplay;
            public int DidEndDisplay;
            public int PositionChanged;

            public HookCounter(CyScroller scroller)
            {
                scroller.CellViewWillDisplay += OnWillDisplay;
                scroller.CellViewDidEndDisplay += OnDidEndDisplay;
                scroller.CellViewPositionChanged += OnPositionChanged;
            }

            public void Reset()
            {
                WillDisplay = 0;
                DidEndDisplay = 0;
                PositionChanged = 0;
            }

            private void OnWillDisplay(CyScroller scroller, CyScrollerCellView view) => WillDisplay++;

            private void OnDidEndDisplay(CyScroller scroller, CyScrollerCellView view) => DidEndDisplay++;

            private void OnPositionChanged(CyScroller scroller, CyScrollerCellView view, float normalizedOffset) => PositionChanged++;
        }

        /// <summary>풀을 거치지 않고 템플릿을 직접 Instantiate해서 돌려주는 델리게이트.</summary>
        private sealed class DirectInstantiateDelegate : ICyScrollerDelegate
        {
            private readonly ScrollerFixture _fixture;

            public DirectInstantiateDelegate(ScrollerFixture fixture)
            {
                _fixture = fixture;
            }

            public int GetNumberOfCells(CyScroller scroller) => 100;

            public float GetCellViewSize(CyScroller scroller, int dataIndex) => 100f;

            public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
            {
                TestCellView view = Object.Instantiate(_fixture.Prefab, scroller.Container, false);
                view.BoundData = dataIndex;
                view.BoundVersion = view.BindVersion;
                return view;
            }
        }

        #endregion
    }
}
