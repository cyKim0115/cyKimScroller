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
        public IEnumerator Relayout_DuringJumpTween_RetargetsAndInvokesCallbackOnce()
        {
            const float TWEEN_TIME = 1f;
            const float TARGET = 5000f;
            const float NEW_TARGET = 50 * 110f;
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int completed = 0;
            int tweenEnds = 0;
            Scroller.ScrollerTweeningChanged += (_, tweening) =>
            {
                if (!tweening)
                {
                    tweenEnds++;
                }
            };

            Scroller.JumpToDataIndex(50, 0f, 0f, false, TweenType.Linear, TWEEN_TIME, () => completed++);

            // 맨 앞 셀이 0번이 아니게 될 때까지 간다. 0번이면 맨 앞 셀이 밀리지 않아 재배치 순간 검증이 비어 버린다.
            yield return WaitFor(() => Scroller.ScrollPosition > 1000f, 2f);
            Assert.IsTrue(Scroller.IsTweening);

            // 0 → 5000 선형 트윈이므로 지금 위치로 진행률을 안다.
            float before = Scroller.ScrollPosition;
            float progress = before / TARGET;
            int topData = Mathf.FloorToInt(before / 100f);
            Assert.Greater(topData, 0);
            Scroller.Spacing = 10f;

            float after = Scroller.ScrollPosition;
            Assert.AreEqual(topData * 110f + (before - topData * 100f), after, EPSILON,
                "재배치 순간에는 맨 앞 셀 기준으로 같은 화면이어야 한다");
            Assert.IsTrue(Scroller.IsTweening, "재배치가 트윈을 끝내면 안 된다");
            Assert.AreEqual(0, completed);

            // 이번 프레임 LateUpdate는 지금 읽은 dt로 진행한다. 다음 위치는 재배치 순간 화면에서 새 목표까지 남은 시간 동안 가는 직선 위다.
            // 시작점을 맨 앞 셀만큼만 옮기면 진행률 × (목표 이동량 500 − 맨 앞 셀 이동량)만큼 한 번에 튄다.
            float nextProgress = Mathf.Min(1f, progress + Time.unscaledDeltaTime / TWEEN_TIME);
            float expectedNext = NEW_TARGET + (after - NEW_TARGET) * (1f - nextProgress) / (1f - progress);
            yield return null;
            Assert.AreEqual(expectedNext, Scroller.ScrollPosition, 1f, "재배치 다음 프레임에 화면이 튀면 안 된다");

            yield return WaitFor(() => completed > 0, 3f);

            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, tweenEnds);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(NEW_TARGET, Scroller.ScrollPosition, EPSILON, "새 배치의 목표에서 끝나야 한다");

            yield return null;
            yield return null;
            Assert.AreEqual(1, completed, "완료 콜백은 한 번만 불린다");
        }

        [Test]
        public void JumpStartedInRangeCallbackOnFinalStep_IsNotCompletedEarly()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int firstCompleted = 0;
            int secondCompleted = 0;
            Scroller.JumpToDataIndex(30, 0f, 0f, false, TweenType.Linear, 1f, () => firstCompleted++);
            Scroller.UpdateTween(0.5f);

            bool started = false;
            Scroller.CellViewVisibilityChanged += view =>
            {
                if (!started && view.Active)
                {
                    started = true;
                    Scroller.JumpToDataIndex(10, 0f, 0f, false, TweenType.Linear, 1f, () => secondCompleted++);
                }
            };

            // 마지막 걸음: 3000으로 가며 새 셀이 보이고, 그 표시 이벤트에서 새 점프를 시작한다.
            Scroller.UpdateTween(0.6f);
            Assert.IsTrue(started);
            Assert.IsTrue(Scroller.IsTweening, "콜백에서 시작한 점프가 이전 트윈의 마지막 걸음에 끝나면 안 된다");
            Assert.AreEqual(0, firstCompleted, "대신된 점프의 완료 콜백은 부르지 않는다");
            Assert.AreEqual(0, secondCompleted);

            Scroller.UpdateTween(1.1f);
            Assert.AreEqual(1, secondCompleted);
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void RelayoutRequestedOnFinalTweenStep_EndsAtNewLayoutTarget()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int completed = 0;

            // 맨 위에서 30번(3000~3100)을 Nearest로 → End 목표 3100 − 400 = 2700
            Scroller.ScrollIntoView(30, ScrollAlign.Nearest, 0f, TweenType.Linear, 0.3f, () => completed++);
            Scroller.UpdateTween(0.2f);
            Assert.IsNull(Scroller.GetCellViewAtDataIndex(30), "30번은 마지막 걸음에서 처음 보여야 한다");

            // 30번이 처음 보일 때 크기를 재서(100 → 180) 위치 유지 리로드를 요청한다. 범위 갱신 중이라 미뤄진다.
            bool resized = false;
            Scroller.CellViewVisibilityChanged += view =>
            {
                if (!resized && view.Active && view.DataIndex == 30)
                {
                    resized = true;
                    _fixture.Delegate.Sizes[30] = 180f;
                    Scroller.ReloadDataKeepingPosition();
                }
            };

            Scroller.UpdateTween(0.2f);

            Assert.IsTrue(resized);
            Assert.AreEqual(1, completed);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(3180f - 400f, Scroller.ScrollPosition, EPSILON, "마지막 걸음에 미룬 재배치도 새 배치의 목표에서 끝나야 한다");
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(30));

            // 정렬도 살아 있어야 한다: 뷰포트가 커지면 같은 End 정렬로 다시 맞춘다.
            ((RectTransform)_fixture.ScrollRect.transform).sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 600f);
            _fixture.ScrollRect.onValueChanged.Invoke(Vector2.zero);
            Assert.AreEqual(3180f - 600f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void LoopSnap_RelayoutRequestedOnFinalStep_ReportsCellInNewLayout()
        {
            // 시작 2000 (가운데 세트), 사이클 1000
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);
            Scroller.SnapTweenType = TweenType.Linear;
            Scroller.SnapTweenTime = 1f;
            Scroller.SnapWatchOffset = 0f;
            Scroller.SnapJumpToOffset = 0f;
            Scroller.SnapCellCenterOffset = 0f;
            int snapped = 0;
            int snappedCell = -1;
            int snappedData = -1;
            CyScrollerCellView snappedView = null;
            Scroller.ScrollerSnapped += (_, cellIndex, dataIndex, view) =>
            {
                snapped++;
                snappedCell = cellIndex;
                snappedData = dataIndex;
                snappedView = view;
            };

            // 2380에서 맨 앞 셀 3번(슬롯 23, 2300)으로 스냅한다. 절반 지점(2340)까지 슬롯 23~27이 보인다.
            Scroller.ScrollPosition = 2380f;
            Scroller.Snap();
            Scroller.UpdateTween(0.5f);

            // 마지막 걸음에 슬롯 27이 회수될 때 목록을 5개로 줄이고 위치 유지 리로드를 요청한다 → 슬롯 번호가 바뀐다.
            bool shrunk = false;
            Scroller.CellViewVisibilityChanged += view =>
            {
                if (!shrunk)
                {
                    shrunk = true;
                    _fixture.Delegate.Sizes = TestDelegate.Uniform(5, 100f);
                    Scroller.ReloadDataKeepingPosition();
                }
            };

            Scroller.UpdateTween(0.6f);

            Assert.IsTrue(shrunk);
            Assert.AreEqual(1, snapped);
            Assert.AreEqual(3, snappedData);
            Assert.IsNotNull(snappedView, "스냅 이벤트의 슬롯은 새 배치 기준이어야 셀 뷰를 찾는다");
            Assert.AreEqual(3, snappedView.DataIndex);
            Assert.AreEqual(snappedCell, snappedView.CellIndex);
            Assert.AreEqual(Scroller.GetScrollPositionForCellViewIndex(snappedCell), Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void Relayout_LateInTween_NextStepDoesNotJump()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(200, 100f));
            int completed = 0;

            // 0 → 100번 End(10100 − 400 = 9700) 선형 1초. 95% 지점(9215)에서 간격 20을 준다.
            Scroller.ScrollIntoView(100, ScrollAlign.End, 0f, TweenType.Linear, 1f, () => completed++);
            Scroller.UpdateTween(0.95f);
            Assert.AreEqual(9215f, Scroller.ScrollPosition, FAR_POSITION_EPSILON);

            // 맨 앞 셀(92번, 오프셋 15)은 92 × 20만큼, 목표(100번 끝)는 100 × 20만큼 밀린다.
            Scroller.Spacing = 20f;
            float after = Scroller.ScrollPosition;
            Assert.AreEqual(92 * 120f + 15f, after, FAR_POSITION_EPSILON, "재배치 순간에는 맨 앞 셀 기준으로 같은 화면이어야 한다");

            // 다음 걸음은 지금 화면에서 새 목표까지 남은 시간(5%) 중 1%만큼만 간다.
            // 시작점을 맨 앞 셀만큼만 옮기면 0.95 × (2000 − 1840) = 152만큼 더 튄다.
            const float NEW_TARGET = 100 * 120f + 100f - 400f;
            Scroller.UpdateTween(0.01f);
            float expected = NEW_TARGET + (after - NEW_TARGET) * (1f - 0.96f) / (1f - 0.95f);
            Assert.AreEqual(expected, Scroller.ScrollPosition, FAR_POSITION_EPSILON, "재배치 다음 걸음에 화면이 튀면 안 된다");

            Scroller.UpdateTween(0.05f);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(NEW_TARGET, Scroller.ScrollPosition, FAR_POSITION_EPSILON);
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(100));
        }

        [Test]
        public void ViewportResize_DuringTween_NextStepDoesNotJump()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int completed = 0;

            // 0 → 50번 가운데(5050 − 200 = 4850). 절반 지점(2425)에서 뷰포트를 600으로 키우면 목표는 5050 − 300 = 4750.
            Scroller.JumpToDataIndex(50, 0.5f, 0.5f, false, TweenType.EaseInOutCubic, 1f, () => completed++);
            Scroller.UpdateTween(0.5f);
            Assert.AreEqual(2425f, Scroller.ScrollPosition, EPSILON);

            ((RectTransform)_fixture.ScrollRect.transform).sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 600f);
            _fixture.ScrollRect.onValueChanged.Invoke(Vector2.zero);
            Assert.AreEqual(2425f, Scroller.ScrollPosition, EPSILON, "크기가 바뀐 순간 화면은 그대로");
            Assert.IsTrue(Scroller.IsTweening);

            // 다음 걸음은 2425에서 4750까지 곡선의 남은 모양(남은 진행 비율)대로 간다.
            // 시작점을 그대로 두면 진행률 × 목표 이동량(0.5 × −100)만큼 튀고, 곡선을 처음부터 다시 그리면 거의 멈춘다.
            const float NEW_TARGET = 4750f;
            float eased = CyScrollerEasing.Evaluate(TweenType.EaseInOutCubic, 0.5f);
            float nextEased = CyScrollerEasing.Evaluate(TweenType.EaseInOutCubic, 0.6f);
            Scroller.UpdateTween(0.1f);
            float expected = NEW_TARGET + (2425f - NEW_TARGET) * (1f - nextEased) / (1f - eased);
            Assert.AreEqual(expected, Scroller.ScrollPosition, EPSILON, "크기가 바뀐 다음 걸음에 화면이 튀면 안 된다");

            Scroller.UpdateTween(0.5f);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(NEW_TARGET, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void PureShift_DuringOvershootTween_KeepsCurve()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(200, 100f));

            // 0 → 100번 시작(10000), 1을 넘었다 돌아오는 곡선.
            Scroller.ScrollIntoView(100, ScrollAlign.Start, 0f, TweenType.EaseOutBack, 1f);
            Scroller.UpdateTween(0.3f);

            // 맨 앞 셀(90번)보다 앞의 0~4번이 100씩 커진다 → 화면과 목표가 같이 500 밀린다.
            for (int i = 0; i < 5; i++)
            {
                _fixture.Delegate.Sizes[i] = 200f;
            }

            Scroller.ReloadDataKeepingPosition();

            // 같이 밀렸으면 곡선을 새로 시작하지 않고, 재배치가 없던 곡선을 500만큼 옮긴 자리로 이어 간다.
            const float SHIFT = 500f;
            Scroller.UpdateTween(0.1f);
            float expected = CyScrollerEasing.Evaluate(TweenType.EaseOutBack, 0.3f + 0.1f) * 10000f + SHIFT;
            Assert.AreEqual(expected, Scroller.ScrollPosition, FAR_POSITION_EPSILON);
        }

        [Test]
        public void Relayout_DuringOvershootTween_ContinuesFromScreen()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(200, 100f));
            int completed = 0;

            // 0 → 100번 시작(10000). 40% 지점에서는 이미 목표를 넘어 있다 (EaseOutBack ≈ 1.029 → 10290).
            Scroller.ScrollIntoView(100, ScrollAlign.Start, 0f, TweenType.EaseOutBack, 1f, () => completed++);
            Scroller.UpdateTween(0.4f);
            float before = Scroller.ScrollPosition;
            int topData = Mathf.FloorToInt(before / 100f);

            // 맨 앞 셀은 topData × 20, 목표는 100 × 20만큼 밀린다. 되돌아오는 곡선이라 남은 거리를 곡선에 싣지 않는다.
            Scroller.Spacing = 20f;
            float after = Scroller.ScrollPosition;
            Assert.AreEqual(topData * 120f + (before - topData * 100f), after, FAR_POSITION_EPSILON);

            // 1ms 걸음은 몇 px 안쪽이어야 한다. 시작점만 옮기면 곡선과 화면의 차이(약 40px)만큼 한 번에 튄다.
            Scroller.UpdateTween(0.001f);
            Assert.AreEqual(after, Scroller.ScrollPosition, 5f, "재배치 다음 걸음에 화면이 튀면 안 된다");

            Scroller.UpdateTween(1f);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(100 * 120f, Scroller.ScrollPosition, FAR_POSITION_EPSILON);
        }

        [UnityTest]
        public IEnumerator ReloadDataKeepingPosition_DuringTween_ContinuesFromShiftedStart()
        {
            const float TWEEN_TIME = 1f;
            const float TARGET = 6000f;
            const float GROWTH = 500f;
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int completed = 0;
            Scroller.JumpToDataIndex(60, 0f, 0f, false, TweenType.Linear, TWEEN_TIME, () => completed++);

            // 키울 셀 0~4(끝 500)가 뷰포트보다 앞에 있을 때까지 간다.
            yield return WaitFor(() => Scroller.ScrollPosition > 1000f, 2f);
            Assert.IsTrue(Scroller.IsTweening);

            // 0 → 6000 선형 트윈이므로 지금 위치로 진행률을 안다.
            float before = Scroller.ScrollPosition;
            float progress = before / TARGET;
            for (int i = 0; i < 5; i++)
            {
                _fixture.Delegate.Sizes[i] = 200f;
            }

            Scroller.ReloadDataKeepingPosition();
            Assert.AreEqual(before + GROWTH, Scroller.ScrollPosition, EPSILON, "맨 앞 셀 기준으로 같은 화면이어야 한다");
            Assert.IsTrue(Scroller.IsTweening, "데이터를 다시 받아도 트윈을 끝내지 않는다");

            // 목표(60번)도 500 밀렸으므로 다음 프레임은 500 → 6500 직선 위에 있어야 한다.
            // 시작점을 옮기지 않으면 500 × (1 − 진행률)만큼 뒤로 튄다.
            float nextProgress = Mathf.Min(1f, progress + Time.unscaledDeltaTime / TWEEN_TIME);
            float expectedNext = Mathf.LerpUnclamped(GROWTH, TARGET + GROWTH, nextProgress);
            yield return null;
            Assert.AreEqual(expectedNext, Scroller.ScrollPosition, 1f, "트윈 시작점도 같은 만큼 옮겨져야 한다");

            yield return WaitFor(() => completed > 0, 3f);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(TARGET + GROWTH, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(60, Scroller.StartDataIndex);
        }

        [UnityTest]
        public IEnumerator Shrink_DuringTween_ContinuesToClampedIndex()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            int completed = 0;
            Scroller.JumpToDataIndex(90, 0f, 0f, false, TweenType.Linear, 1f, () => completed++);
            yield return null;
            Assert.IsTrue(Scroller.IsTweening);

            // 50개로 줄이고 마지막 셀(49번, 4900~5900)을 뷰포트보다 크게 둔다. 49번 시작(4900)이 스크롤 끝(5500)보다 앞이므로
            // 잘린 인덱스로 가야만 4900에서 끝난다 (90번을 그대로 쓰면 다른 위치가 스크롤 끝으로 잘린다).
            float[] sizes = TestDelegate.Uniform(50, 100f);
            sizes[49] = 1000f;
            _fixture.Delegate.Sizes = sizes;
            Scroller.ReloadDataKeepingPosition();
            Assert.IsTrue(Scroller.IsTweening, "개수가 줄어도 잘린 인덱스를 향해 계속 간다");
            Assert.AreEqual(5500f, Scroller.ScrollSize, EPSILON);

            yield return WaitFor(() => completed > 0, 3f);

            Assert.AreEqual(1, completed);
            Assert.AreEqual(4900f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(49, Scroller.StartDataIndex);
        }

        [Test]
        public void Loop_ShrinkDuringForwardTween_ContinuesToClampedIndex()
        {
            // 시작 2000 (가운데 세트), 사이클 1000
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);
            int completed = 0;

            // 8번의 뒤쪽(Forward) 사본(슬롯 28, 2800)으로 간다.
            Scroller.JumpToDataIndex(8, 0f, 0f, false, TweenType.Linear, 1f, () => completed++, LoopJumpDirection.Forward);
            Scroller.UpdateTween(0.1f);

            // 5개로 줄이면 8번은 4번으로 잘리고, 맨 앞 셀(0번)과 같은 세트의 4번(앞으로 가는 쪽 사본)으로 가야 한다.
            // 잘리지 않은 8을 슬롯 번호에 쓰면 세트가 넘어가 다른 데이터(3번)에 멈춘다.
            _fixture.Delegate.Sizes = TestDelegate.Uniform(5, 100f);
            Scroller.ReloadDataKeepingPosition();
            Assert.IsTrue(Scroller.IsTweening);

            float previous = Scroller.ScrollPosition;
            for (int i = 0; i < 100 && Scroller.IsTweening; i++)
            {
                Scroller.UpdateTween(0.05f);
                if (Scroller.IsTweening)
                {
                    Assert.GreaterOrEqual(Scroller.ScrollPosition, previous - EPSILON, "줄어든 뒤에도 앞으로만 가야 한다");
                    previous = Scroller.ScrollPosition;
                }
            }

            Assert.AreEqual(1, completed);
            int slot = Scroller.GetCellViewIndexAtPosition(Scroller.ScrollPosition + 1f);
            Assert.AreEqual(4, Scroller.GetDataIndexForCellViewIndex(slot), "잘린 인덱스(4번) 시작이 뷰포트 맨 앞이어야 한다");
            Assert.AreEqual(Scroller.GetScrollPositionForCellViewIndex(slot), Scroller.ScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator Emptied_DuringTween_CompletesOnce()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            int completed = 0;
            Scroller.JumpToDataIndex(50, 0f, 0f, false, TweenType.Linear, 1f, () => completed++);
            yield return null;

            _fixture.Delegate.Sizes = new float[0];
            Scroller.ReloadDataKeepingPosition();

            Assert.AreEqual(1, completed, "갈 셀이 없어지면 빈 목록 점프처럼 바로 완료한다");
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON);

            yield return null;
            yield return null;
            Assert.AreEqual(1, completed);
        }

        [UnityTest]
        public IEnumerator ViewportResize_DuringTween_EndsAtNewTarget()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int completed = 0;
            Scroller.JumpToDataIndex(50, 0.5f, 0.5f, false, TweenType.Linear, 1f, () => completed++);
            yield return null;
            Assert.IsTrue(Scroller.IsTweening);

            ((RectTransform)_fixture.ScrollRect.transform).sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 600f);
            yield return WaitFor(() => completed > 0, 3f);

            Assert.AreEqual(1, completed);
            Assert.AreEqual(5050f - 300f, Scroller.ScrollPosition, EPSILON, "가운데 정렬 목표를 새 뷰포트로 다시 계산해야 한다");
        }

        [UnityTest]
        public IEnumerator Relayout_DuringSnapTween_StillSnapsOnce()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            Scroller.SnapTweenType = TweenType.Linear;
            Scroller.SnapTweenTime = 1f;
            int snapped = 0;
            int snappedData = -1;
            Scroller.ScrollerSnapped += (_, __, dataIndex, ___) =>
            {
                snapped++;
                snappedData = dataIndex;
            };

            // 뷰포트 가운데(1430)에 가장 가까운 14번을 가운데로
            Scroller.ScrollPosition = 1230f;
            Scroller.Snap();
            yield return null;
            Assert.IsTrue(Scroller.IsTweening);

            Scroller.Spacing = 10f;
            Assert.IsTrue(Scroller.IsTweening);

            yield return WaitFor(() => snapped > 0, 3f);
            Assert.AreEqual(1, snapped);
            Assert.AreEqual(14, snappedData);
            Assert.AreEqual(14 * 110f + 50f - 200f, Scroller.ScrollPosition, EPSILON);

            yield return null;
            Assert.AreEqual(1, snapped);
        }

        [UnityTest]
        public IEnumerator Loop_RelayoutDuringForwardTween_KeepsDirection()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);
            Scroller.ScrollPosition = 2400f;
            int completed = 0;

            // 2번의 뒤쪽(Forward) 사본(슬롯 32, 목표 3200)으로 간다.
            Scroller.JumpToDataIndex(2, 0f, 0f, false, TweenType.Linear, 1f, () => completed++, LoopJumpDirection.Forward);
            yield return null;
            Assert.IsTrue(Scroller.IsTweening);

            // 사이클이 1100이 된다. 맨 앞 셀 기준 사본 관계를 지키지 않으면 가운데 세트 사본(뒤쪽)으로 돌아간다.
            float previous = Scroller.ScrollPosition;
            Scroller.Spacing = 10f;
            Assert.GreaterOrEqual(Scroller.ScrollPosition, previous - EPSILON);
            previous = Scroller.ScrollPosition;

            float timeout = 3f;
            while (completed == 0 && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
                if (Scroller.IsTweening)
                {
                    Assert.GreaterOrEqual(Scroller.ScrollPosition, previous - EPSILON, "재배치 뒤에도 앞으로만 가야 한다");
                    previous = Scroller.ScrollPosition;
                }
            }

            Assert.AreEqual(1, completed);
            int slot = Scroller.GetCellViewIndexAtPosition(Scroller.ScrollPosition + 1f);
            Assert.AreEqual(2, Scroller.GetDataIndexForCellViewIndex(slot), "새 배치에서 2번 시작이 뷰포트 맨 앞이어야 한다");
            Assert.AreEqual(Scroller.GetScrollPositionForCellViewIndex(slot), Scroller.ScrollPosition, EPSILON);
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

        #region Scroll Into View

        [Test]
        public void ScrollIntoView_Nearest_AlreadyVisible_KeepsPositionAndCompletesImmediately()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            Scroller.ScrollPosition = 1000f;
            int completed = 0;

            // 뷰포트 [1000, 1400]: 10번은 시작에, 13번은 끝에 정확히 맞닿아 있다.
            Scroller.ScrollIntoView(10, onComplete: () => completed++);
            Scroller.ScrollIntoView(13, onComplete: () => completed++);
            Scroller.ScrollIntoView(11, ScrollAlign.Nearest, 0f, TweenType.Linear, 0.5f, () => completed++);

            Assert.AreEqual(3, completed);
            Assert.IsFalse(Scroller.IsTweening, "움직일 필요가 없으면 트윈도 시작하지 않는다");
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void ScrollIntoView_Nearest_AboveAlignsStartAndBelowAlignsEnd()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));

            Scroller.ScrollPosition = 1000f;
            Scroller.ScrollIntoView(5);
            Assert.AreEqual(500f, Scroller.ScrollPosition, EPSILON, "위쪽 셀은 시작을 뷰포트 시작에");

            Scroller.ScrollPosition = 1000f;
            Scroller.ScrollIntoView(20);
            Assert.AreEqual(2100f - 400f, Scroller.ScrollPosition, EPSILON, "아래쪽 셀은 끝을 뷰포트 끝에");

            // 뷰포트 [1030, 1430]에 걸친 셀: 앞쪽이 잘린 10번은 Start, 뒤쪽이 잘린 14번은 End
            Scroller.ScrollPosition = 1030f;
            Scroller.ScrollIntoView(10);
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);

            Scroller.ScrollPosition = 1030f;
            Scroller.ScrollIntoView(14);
            Assert.AreEqual(1500f - 400f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void ScrollIntoView_Nearest_MarginKeepsGap()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            Scroller.ScrollPosition = 1000f;

            // 10번은 여백 없이는 보이지만, 앞에 20을 남기려면 20 더 앞으로 가야 한다.
            Scroller.ScrollIntoView(10, margin: 20f);
            Assert.AreEqual(980f, Scroller.ScrollPosition, EPSILON);

            // 뷰포트 [980, 1380]: 13번(1300~1400) 뒤에 20을 남기려면 End로 1420 − 400
            Scroller.ScrollIntoView(13, margin: 20f);
            Assert.AreEqual(1020f, Scroller.ScrollPosition, EPSILON);
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(13, 20f));
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(10, 20f));

            Scroller.ScrollIntoView(13, margin: 20f);
            Assert.AreEqual(1020f, Scroller.ScrollPosition, EPSILON, "이미 여백까지 보이면 움직이지 않는다");
        }

        [Test]
        public void ScrollIntoView_Nearest_CellLargerThanViewportAlignsStart()
        {
            float[] sizes = TestDelegate.Uniform(40, 100f);
            sizes[10] = 600f;   // 1000~1600
            sizes[20] = 380f;   // 2500~2880
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, sizes);

            Scroller.ScrollIntoView(10);
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON, "아래쪽에 있어도 뷰포트보다 크면 Start");

            Scroller.ScrollPosition = 3000f;
            Scroller.ScrollIntoView(10);
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);

            Scroller.ScrollIntoView(10, margin: 20f);
            Assert.AreEqual(980f, Scroller.ScrollPosition, EPSILON);

            // 380 + 여백 20 × 2 = 420 > 400
            Scroller.ScrollPosition = 0f;
            Scroller.ScrollIntoView(20, margin: 20f);
            Assert.AreEqual(2480f, Scroller.ScrollPosition, EPSILON, "여백까지 합쳐 뷰포트보다 크면 Start");
        }

        [Test]
        public void ScrollIntoView_StartCenterEnd_AlignWithPaddingAndSpacing()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f), false, 10f,
                new RectOffset(0, 0, 30, 0));

            // 20번: 30 + 20 × 110 = 2230 ~ 2330
            Scroller.ScrollIntoView(20, ScrollAlign.Start, 15f);
            Assert.AreEqual(2230f - 15f, Scroller.ScrollPosition, EPSILON);

            Scroller.ScrollIntoView(20, ScrollAlign.Center, 15f);
            Assert.AreEqual(2230f + 50f - 200f, Scroller.ScrollPosition, EPSILON, "Center는 여백을 쓰지 않는다");

            Scroller.ScrollIntoView(20, ScrollAlign.End, 15f);
            Assert.AreEqual(2330f + 15f - 400f, Scroller.ScrollPosition, EPSILON);
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(20, 15f));

            // 목표는 스크롤 범위로 잘린다.
            Scroller.ScrollIntoView(0, ScrollAlign.Start, 50f);
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator ScrollIntoView_End_AlignmentSurvivesViewportResize()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            Scroller.ScrollIntoView(30, ScrollAlign.End, 15f);
            Assert.AreEqual(3100f + 15f - 400f, Scroller.ScrollPosition, EPSILON);

            ((RectTransform)_fixture.ScrollRect.transform).sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 600f);
            yield return null;
            yield return null;

            Assert.AreEqual(3100f + 15f - 600f, Scroller.ScrollPosition, EPSILON, "끝 정렬과 여백이 새 뷰포트 기준으로 유지돼야 한다");
        }

        [Test]
        public void ScrollIntoView_Horizontal_UsesWidth()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Horizontal, TestDelegate.Uniform(100, 100f));

            // 뷰포트 너비 300. 오른쪽 셀은 End: 1100 − 300
            Scroller.ScrollIntoView(10);
            Assert.AreEqual(800f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(-800f, _fixture.Content.anchoredPosition.x, EPSILON);
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(8));
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(10));
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(7));

            Scroller.ScrollIntoView(5);
            Assert.AreEqual(500f, Scroller.ScrollPosition, EPSILON, "왼쪽 셀은 Start");

            Scroller.ScrollIntoView(6);
            Assert.AreEqual(500f, Scroller.ScrollPosition, EPSILON);

            Scroller.ScrollIntoView(20, ScrollAlign.Center);
            Assert.AreEqual(2050f - 150f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void ScrollIntoView_Loop_VisibleCopyStaysOtherwisePicksNearestCopy()
        {
            // 시작 2000 (가운데 세트), 순환 보정 창 1500~2500, 사이클 1000
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true);
            int completed = 0;

            Scroller.ScrollIntoView(2, onComplete: () => completed++);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(2000f, Scroller.ScrollPosition, EPSILON, "사본(2200~2300)이 보이므로 움직이지 않는다");

            // 8번: 앞 사본 1800~1900의 Start(1800, 200 이동)가 뒤 사본 2800~2900의 End(2500, 500 이동)보다 가깝다.
            Scroller.ScrollIntoView(8);
            Assert.AreEqual(1800f, Scroller.ScrollPosition, EPSILON);
            int frontSlot = Scroller.GetCellViewIndexAtPosition(Scroller.ScrollPosition + 1f);
            Assert.AreEqual(8, Scroller.GetDataIndexForCellViewIndex(frontSlot), "앞쪽 사본의 시작이 뷰포트 맨 앞이어야 한다");
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(8));
            Scroller.ScrollPosition = 2000f;

            // 5번: 앞 사본 Start(1500, 500 이동)보다 뒤 사본 2500~2600의 End(2200, 200 이동)가 가깝다.
            Scroller.ScrollIntoView(5);
            Assert.AreEqual(2200f, Scroller.ScrollPosition, EPSILON);
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(5));

            // Backward: 9번 앞 사본(1900~2000)을 Start로
            Scroller.ScrollIntoView(9, loopJumpDirection: LoopJumpDirection.Backward);
            Assert.AreEqual(1900f, Scroller.ScrollPosition, EPSILON);
            int slot = Scroller.GetCellViewIndexAtPosition(Scroller.ScrollPosition + 1f);
            Assert.AreEqual(9, Scroller.GetDataIndexForCellViewIndex(slot));

            // 뷰포트 [1900, 2300]: 어느 사본이든 완전히 보이면 true
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(0));
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(2));
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(3));

            // Forward: 7번 뒤 사본(2700~2800)을 End로
            Scroller.ScrollIntoView(7, loopJumpDirection: LoopJumpDirection.Forward);
            Assert.AreEqual(2400f, Scroller.ScrollPosition, EPSILON);
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(7));
        }

        [Test]
        public void ScrollIntoView_ClampsIndex()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));

            Scroller.ScrollIntoView(500);
            Assert.AreEqual(9600f, Scroller.ScrollPosition, EPSILON, "99번으로 잘려 End");
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, EPSILON);

            Scroller.ScrollIntoView(-5);
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON, "0번으로 잘려 Start");
        }

        [Test]
        public void EmptyList_ScrollIntoViewCompletesAndQueriesReturnDefaults()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, new float[0]);

            bool completed = false;
            Scroller.ScrollIntoView(3, ScrollAlign.Center, 0f, TweenType.Linear, 0.5f, () => completed = true);
            Assert.IsTrue(completed);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(0));
            Assert.AreEqual(0f, Scroller.GetCellStart(0), EPSILON);
            Assert.AreEqual(0f, Scroller.GetCellSize(0), EPSILON);
        }

        [UnityTest]
        public IEnumerator ScrollIntoView_Tween_CompletesOnceAtTarget()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            int completed = 0;

            Scroller.ScrollIntoView(30, ScrollAlign.Nearest, 10f, TweenType.EaseInOutCubic, 0.3f, () => completed++);
            Assert.IsTrue(Scroller.IsTweening);
            Assert.AreEqual(0, completed);

            yield return WaitFor(() => completed > 0, 2f);
            Assert.AreEqual(1, completed);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(3100f + 10f - 400f, Scroller.ScrollPosition, EPSILON);
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(30, 10f));

            yield return null;
            yield return null;
            Assert.AreEqual(1, completed);
        }

        [UnityTest]
        public IEnumerator ScrollIntoView_NearestVisibleDuringTween_StopsTween()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            bool jumpCompleted = false;
            Scroller.JumpToDataIndex(300, 0f, 0f, false, TweenType.Linear, 1f, () => jumpCompleted = true);
            yield return null;
            Assert.IsTrue(Scroller.IsTweening);

            // 시작이 뷰포트 안에 있는 첫 셀은 완전히 보인다.
            float position = Scroller.ScrollPosition;
            int visible = Mathf.CeilToInt(position / 100f);
            int completed = 0;
            Scroller.ScrollIntoView(visible, onComplete: () => completed++);

            Assert.AreEqual(1, completed);
            Assert.IsFalse(Scroller.IsTweening, "이미 보이면 진행 중인 트윈을 그 자리에서 멈춘다");

            yield return null;
            yield return null;
            Assert.AreEqual(position, Scroller.ScrollPosition, EPSILON);
            Assert.IsFalse(jumpCompleted, "대신된 점프의 완료 콜백은 부르지 않는다");
        }

        [Test]
        public void ScrollIntoView_DuringDrag_EndsDragOnlyWhenMoving()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            GameObject target = _fixture.ScrollRect.gameObject;
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);
            Assert.IsTrue(Scroller.IsDragging);

            Scroller.ScrollIntoView(2);
            Assert.IsTrue(Scroller.IsDragging, "움직이지 않으면 드래그를 끝내지 않는다");

            Scroller.ScrollIntoView(50);
            Assert.IsFalse(Scroller.IsDragging);
            Assert.AreEqual(5100f - 400f, Scroller.ScrollPosition, EPSILON);

            ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
            Assert.AreEqual(4700f, Scroller.ScrollPosition, EPSILON, "남은 드래그 이벤트가 이동을 되돌리면 안 된다");
        }

        [Test]
        public void ScrollIntoView_NearestEdgeCellMargin_KeepsDragAndInertia()
        {
            // 앞뒤 여백 0: 첫·마지막 셀 바깥에는 여백 8을 둘 콘텐츠가 없다. 콘텐츠 끝까지 보이면 보이는 것이다.
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(100, 100f));
            GameObject target = _fixture.ScrollRect.gameObject;
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            int completed = 0;

            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(0, 8f));
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);
            Scroller.ScrollIntoView(0, margin: 8f, onComplete: () => completed++);
            Assert.IsTrue(Scroller.IsDragging, "맨 위에서 움직이지 않으면 드래그를 끝내지 않는다");
            Assert.AreEqual(1, completed);
            Assert.AreEqual(0f, Scroller.ScrollPosition, EPSILON);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);

            Scroller.ScrollPosition = Scroller.ScrollSize;   // 뷰포트 [9600, 10000]
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(99, 8f));
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(96, 8f), "앞에 콘텐츠가 있는 셀은 여백까지 보여야 한다");
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);
            Scroller.ScrollIntoView(99, margin: 8f, onComplete: () => completed++);
            Assert.IsTrue(Scroller.IsDragging, "맨 아래에서 움직이지 않으면 드래그를 끝내지 않는다");
            Assert.AreEqual(2, completed);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);

            Scroller.LinearVelocity = -300f;
            Scroller.ScrollIntoView(99, margin: 8f, onComplete: () => completed++);
            Assert.AreEqual(-300f, Scroller.LinearVelocity, EPSILON, "움직이지 않으면 관성을 멈추지 않는다");
            Assert.AreEqual(3, completed);
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void ScrollIntoView_NearestLargeCellAlreadyAtStart_KeepsDrag()
        {
            float[] sizes = TestDelegate.Uniform(40, 100f);
            sizes[10] = 600f;   // 1000~1600, 뷰포트(400)보다 크다
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, sizes);
            GameObject target = _fixture.ScrollRect.gameObject;
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            int completed = 0;

            // 이미 시작에 맞춰져 있으면 Nearest(Start) 목표가 지금 위치라 움직일 것이 없다.
            Scroller.ScrollPosition = 1000f;
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);
            Scroller.ScrollIntoView(10, onComplete: () => completed++);
            Assert.IsTrue(Scroller.IsDragging, "움직일 것이 없으면 드래그를 끝내지 않는다");
            Assert.AreEqual(1, completed);
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);

            // 셀 안쪽을 보고 있으면 시작으로 옮기고 드래그를 끝낸다.
            Scroller.ScrollPosition = 1100f;
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);
            Scroller.ScrollIntoView(10, onComplete: () => completed++);
            Assert.IsFalse(Scroller.IsDragging);
            Assert.AreEqual(2, completed);
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
        }

        [Test]
        public void IsDataIndexFullyVisible_ExcludesLookAheadAndHonorsMargin()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(1000, 100f));
            Scroller.LookAheadBefore = 150f;
            Scroller.LookAheadAfter = 150f;
            Scroller.ScrollPosition = 1000f;   // 뷰포트 [1000, 1400]

            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(10), "시작이 뷰포트 시작에 정확히 맞닿으면 보인다");
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(13), "끝이 뷰포트 끝에 정확히 맞닿으면 보인다");
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(9));
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(14));

            // lookAhead 구간 셀은 활성이어도 보이는 것이 아니다.
            Assert.IsNotNull(Scroller.GetCellViewAtDataIndex(9));
            Assert.IsNotNull(Scroller.GetCellViewAtDataIndex(14));

            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(10, 1f));
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(11, 100f), "[1000, 1300]");
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(11, 101f));
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(12, 100f), "[1100, 1400]");

            Scroller.ScrollPosition = 1050f;   // 뷰포트 [1050, 1450]
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(10), "앞쪽이 잘린 셀");
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(14), "뒤쪽이 잘린 셀");
            Assert.IsTrue(Scroller.IsDataIndexFullyVisible(11));
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(-1));
            Assert.IsFalse(Scroller.IsDataIndexFullyVisible(1000));
        }

        [Test]
        public void GetCellStartAndSize_IncludePaddingAndSpacing()
        {
            float[] sizes = { 50f, 150f, 50f, 150f, 50f, 150f, 50f, 150f, 50f, 150f };
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, sizes, false, 5f, new RectOffset(10, 20, 30, 40));

            // 시작: 30, 85, 240, 295, 450, 505, 660, 715, 870, 925
            Assert.AreEqual(30f, Scroller.GetCellStart(0), EPSILON);
            Assert.AreEqual(240f, Scroller.GetCellStart(2), EPSILON);
            Assert.AreEqual(925f, Scroller.GetCellStart(9), EPSILON);
            Assert.AreEqual(150f, Scroller.GetCellSize(1), EPSILON);
            Assert.AreEqual(50f, Scroller.GetCellSize(2), EPSILON);

            Assert.AreEqual(30f, Scroller.GetCellStart(-3), EPSILON, "범위 밖은 잘린다");
            Assert.AreEqual(925f, Scroller.GetCellStart(99), EPSILON);
            Assert.AreEqual(150f, Scroller.GetCellSize(99), EPSILON);
            Assert.AreEqual(Scroller.GetScrollPositionForDataIndex(4), Scroller.GetCellStart(4), EPSILON);
        }

        [Test]
        public void GetCellStart_Loop_UsesMiddleSetCopy()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(10, 100f), loop: true,
                padding: new RectOffset(0, 0, 30, 0));

            Assert.AreEqual(5, Scroller.Layout.SetCount);
            Assert.AreEqual(30f + 2 * 1000f + 300f, Scroller.GetCellStart(3), EPSILON);
            Assert.AreEqual(100f, Scroller.GetCellSize(3), EPSILON);
            Assert.AreEqual(Scroller.GetScrollPositionForDataIndex(3), Scroller.GetCellStart(3), EPSILON);
        }

        /// <summary>condition이 참이 되거나 timeout(초, unscaled)이 지날 때까지 프레임을 넘긴다.</summary>
        private static IEnumerator WaitFor(System.Func<bool> condition, float timeout)
        {
            while (!condition() && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
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

            // ShiftScrollPosition의 계약대로 레이아웃 좌표를 먼저 SHIFT만큼 민다 (앞 여백이 늘어난 것과 같다).
            // 트윈 목표는 요청(슬롯)으로 매 프레임 지금 레이아웃에서 다시 계산하므로 레이아웃이 밀려야 목표도 따라온다.
            CyScrollerLayout layout = Scroller.Layout;
            layout.Build(layout.Spacing, layout.PaddingBefore + SHIFT, layout.PaddingAfter, false, Scroller.ScrollRectSize);
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

        // 트윈을 프레임 대신 직접 진행시키는 간격과 상한.
        private const float TWEEN_STEP = 0.01f;
        private const int MAX_TWEEN_STEPS = 1000;

        [Test]
        public void TweenUpdate_DoesNotAllocateAfterWarmup()
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(2000, 37f), false, 3f);
            Scroller.LookAheadAfter = 100f;

            // 같은 경로로 풀과 내부 리스트 용량을 채운다.
            RunIntoViewTween();
            Scroller.ScrollPosition = 0f;

            int steps = 0;
            Assert.That(() => { steps = RunIntoViewTween(); }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());

            // 매 걸음 목표를 요청에서 다시 계산하고, 끝에서 완료 처리까지 한다.
            Assert.Greater(steps, 10);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(400 * 40f + 37f + 12f - 400f, Scroller.ScrollPosition, FAR_POSITION_EPSILON);
        }

        /// <summary>
        /// ScrollIntoView(Nearest → 아래쪽 셀이라 End) 트윈을 시작해 끝날 때까지 직접 진행시킨다. 진행한 걸음 수를 돌려준다.
        /// </summary>
        private int RunIntoViewTween()
        {
            Scroller.ScrollIntoView(400, ScrollAlign.Nearest, 12f, TweenType.EaseInOutCubic, 1f);
            int steps = 0;
            while (Scroller.IsTweening && steps < MAX_TWEEN_STEPS)
            {
                Scroller.UpdateTween(TWEEN_STEP);
                steps++;
            }

            return steps;
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
