using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace CyKim.Scroller.Tests
{
    /// <summary>정착 상태(IsSettled·ScrollerSettled·OnScrollerSettled)와 고속 스크롤 플래그(IsFastScrolling·히스테리시스).</summary>
    public class CyScrollerScrollStateTests
    {
        private const float TIMEOUT = 5f;

        private ScrollerFixture _fixture;
        private int _settledEvents;
        private readonly List<bool> _fastEvents = new List<bool>(16);

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
            _settledEvents = 0;
            _fastEvents.Clear();
        }

        private CyScroller Scroller => _fixture.Scroller;

        private void Create(int count = 200, float size = 100f)
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(count, size));
            Scroller.ScrollerSettled += _ => _settledEvents++;
            Scroller.ScrollerFastScrollingChanged += (_, fast) => _fastEvents.Add(fast);
        }

        private IEnumerator WaitUntilSettledEvent(int expected)
        {
            float timeout = TIMEOUT;
            while (_settledEvents < expected && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void AssertActiveCellsSettled(int expected, string context)
        {
            IReadOnlyList<CyScrollerCellView> cells = Scroller.ActiveCellViews;
            Assert.Greater(cells.Count, 0, context);
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                Assert.AreEqual(expected, view.SettledCount, $"{context}: {view.DataIndex}번 OnScrollerSettled");
            }
        }

        [UnityTest]
        public IEnumerator FirstLoad_IsSettled_AndRaisesNothing()
        {
            Create();
            yield return null;
            yield return null;

            Assert.IsTrue(Scroller.IsSettled);
            Assert.IsFalse(Scroller.IsFastScrolling);
            Assert.AreEqual(0, _settledEvents, "첫 로드 직후에는 정착 이벤트가 없다");
            AssertActiveCellsSettled(0, "first load");
        }

        [UnityTest]
        public IEnumerator Fling_RaisesSettledOnceAfterInertia_AndCellHooksOnce()
        {
            Create(400);
            _fixture.ScrollRect.inertia = true;
            _fixture.ScrollRect.decelerationRate = 0.01f;   // 빨리 멈추게 (약 1.2초)
            yield return null;

            Scroller.LinearVelocity = 3000f;
            yield return null;
            Assert.IsFalse(Scroller.IsSettled, "관성 중");

            yield return WaitUntilSettledEvent(1);
            Assert.AreEqual(1, _settledEvents);
            Assert.IsTrue(Scroller.IsSettled);
            Assert.Greater(Scroller.ScrollPosition, 100f, "관성으로 움직였다");
            AssertActiveCellsSettled(1, "after fling");

            // 머무르는 동안 다시 오지 않는다.
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.AreEqual(1, _settledEvents);
        }

        [UnityTest]
        public IEnumerator TweenJump_RaisesSettledAfterCompletion()
        {
            Create();
            yield return null;

            int completed = 0;
            Scroller.JumpToDataIndex(80, tweenType: TweenType.Linear, tweenTime: 1f, jumpComplete: () => completed++);
            Assert.IsFalse(Scroller.IsSettled, "트윈 중");
            yield return null;
            Assert.AreEqual(0, _settledEvents);

            yield return WaitUntilSettledEvent(1);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, _settledEvents);
            AssertActiveCellsSettled(1, "after tween");
        }

        [UnityTest]
        public IEnumerator WheelSnap_StaysUnsettledUntilSnapTweenEnds()
        {
            Create();
            Scroller.Snapping = true;
            Scroller.SnapWatchOffset = 0f;
            Scroller.SnapJumpToOffset = 0f;
            Scroller.SnapCellCenterOffset = 0f;
            yield return null;

            // 휠로 셀 사이에 멈춘다: 스냅 대기 → 스냅 트윈 → 정착.
            Scroller.ScrollPosition = 250f;
            var eventData = new PointerEventData(EventSystem.current) { scrollDelta = Vector2.zero };
            ExecuteEvents.Execute(_fixture.ScrollRect.gameObject, eventData, ExecuteEvents.scrollHandler);
            Assert.IsFalse(Scroller.IsSettled, "스냅 대기 중");

            yield return WaitUntilSettledEvent(1);
            Assert.AreEqual(1, _settledEvents, "스냅이 끝난 뒤 한 번");
            Assert.AreEqual(0f, Scroller.ScrollPosition % 100f, 0.05f, "셀 경계에 스냅");
            AssertActiveCellsSettled(1, "after snap");
        }

        [UnityTest]
        public IEnumerator ElasticOverscroll_SettlesOnceInsideRange()
        {
            Create(10);
            _fixture.ScrollRect.inertia = true;
            yield return null;

            // 끝(600)에서 더 밀어 가장자리를 넘긴다. 되돌아오는 꼭짓점에서 속도가 0을 지나도 정착으로 보지 않는다.
            Scroller.ScrollPosition = Scroller.ScrollSize;
            Scroller.LinearVelocity = 3000f;
            yield return WaitUntilSettledEvent(1);
            Assert.AreEqual(1, _settledEvents);
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, 0.5f, "범위 안으로 돌아온 뒤 정착");

            float wait = 1f;
            while (wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, _settledEvents, "한 번만");
        }

        [UnityTest]
        public IEnumerator EmptyList_WheelWithSnapping_StillSettles()
        {
            Create(0);
            Scroller.Snapping = true;
            yield return null;

            var eventData = new PointerEventData(EventSystem.current) { scrollDelta = Vector2.zero };
            ExecuteEvents.Execute(_fixture.ScrollRect.gameObject, eventData, ExecuteEvents.scrollHandler);
            Assert.IsFalse(Scroller.IsSettled, "스냅 대기 중");

            float timeout = TIMEOUT;
            while (!Scroller.IsSettled && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(Scroller.IsSettled, "맞출 셀이 없으면 스냅 대기를 푼다");
        }

        [Test]
        public void TweenStart_DoesNotReusePreviousStepSpeed()
        {
            Create(400);
            Scroller.JumpToDataIndex(351, tweenType: TweenType.Linear, tweenTime: 1f);   // 0.1초 뒤 3510: 셀 사이
            Scroller.UpdateTween(0.1f);
            Assert.Greater(Scroller.ScrollSpeed, 1200f, "빠른 트윈 걸음");
            Scroller.UpdateScrollState(Scroller.ScrollSpeed);
            Assert.IsTrue(Scroller.IsFastScrolling);

            // 탭으로 멈추고 같은 프레임에 스냅이 새 트윈을 시작한다: 첫 걸음 전 속도는 0이다.
            Scroller.InterruptTween();
            Scroller.Snapping = true;
            Scroller.Snap();
            Assert.IsTrue(Scroller.IsTweening);
            Assert.AreEqual(0f, Scroller.ScrollSpeed);
            Scroller.UpdateScrollState(Scroller.ScrollSpeed);
            Assert.IsFalse(Scroller.IsFastScrolling);
        }

        [Test]
        public void FastScrolling_UsesHysteresis_WithViewportRelativeThresholds()
        {
            Create();

            // 뷰포트 400: 들어감 3 × 400 = 1200, 나옴 1.5 × 400 = 600.
            float[] speeds = { 1100f, 1250f, 900f, 650f, 600f, 590f, 1199f, 1200f, 0f };
            bool[] expected = { false, true, true, true, true, false, false, true, false };
            for (int i = 0; i < speeds.Length; i++)
            {
                Scroller.UpdateScrollState(speeds[i]);
                Assert.AreEqual(expected[i], Scroller.IsFastScrolling, $"{speeds[i]} px/s");
            }

            CollectionAssert.AreEqual(new[] { true, false, true, false }, _fastEvents, "경계에서 깜빡이지 않고 바뀔 때만 알린다");

            // 나오는 값이 들어가는 값보다 크면 들어가는 값을 쓴다. 들어가는 값이 0이면 끈다.
            _fastEvents.Clear();
            Scroller.FastScrollExitThreshold = 5f;
            Scroller.UpdateScrollState(1300f);
            Scroller.UpdateScrollState(1250f);
            Assert.IsTrue(Scroller.IsFastScrolling, "1200 이상이면 계속");
            Scroller.FastScrollEnterThreshold = 0f;
            Scroller.UpdateScrollState(100000f);
            Assert.IsFalse(Scroller.IsFastScrolling);
            CollectionAssert.AreEqual(new[] { true, false }, _fastEvents);

            // 나오는 값이 0이면 완전히 멈출 때 나온다.
            Scroller.FastScrollEnterThreshold = 3f;
            Scroller.FastScrollExitThreshold = 0f;
            Scroller.UpdateScrollState(1300f);
            Scroller.UpdateScrollState(5f);
            Assert.IsTrue(Scroller.IsFastScrolling);
            Scroller.UpdateScrollState(0f);
            Assert.IsFalse(Scroller.IsFastScrolling);
        }

        [Test]
        public void Settled_FollowsSpeedThreshold_AndFiresOnTransitionOnly()
        {
            Create();
            Scroller.UpdateScrollState(500f);
            Assert.AreEqual(0, _settledEvents);
            Scroller.UpdateScrollState(11f);
            Assert.AreEqual(0, _settledEvents, "임계값(10) 위");
            Scroller.UpdateScrollState(10f);
            Assert.AreEqual(1, _settledEvents);
            Scroller.UpdateScrollState(0f);
            Assert.AreEqual(1, _settledEvents, "정착해 있는 동안은 다시 오지 않는다");
            AssertActiveCellsSettled(1, "threshold");

            Scroller.SettleVelocityThreshold = 0f;
            Scroller.UpdateScrollState(5f);
            Scroller.UpdateScrollState(0f);
            Assert.AreEqual(2, _settledEvents);
        }

        [Test]
        public void SettledHandler_StartingTween_SkipsCellHooksUntilNextSettle()
        {
            Create();
            bool jumped = false;
            Scroller.ScrollerSettled += scroller =>
            {
                if (!jumped)
                {
                    jumped = true;
                    scroller.JumpToDataIndex(50, tweenType: TweenType.Linear, tweenTime: 0.2f);
                }
            };

            Scroller.UpdateScrollState(100f);
            Scroller.UpdateScrollState(0f);
            Assert.AreEqual(1, _settledEvents);
            Assert.IsTrue(Scroller.IsTweening);
            AssertActiveCellsSettled(0, "handler moved again");

            for (int i = 0; i < 10; i++)
            {
                Scroller.UpdateTween(0.05f);
                Scroller.UpdateScrollState(Scroller.IsTweening ? 2000f : 0f);
            }

            Assert.AreEqual(2, _settledEvents);
            AssertActiveCellsSettled(1, "next settle");
        }

        [Test]
        public void ScrollState_DoesNotAllocate()
        {
            Create();
            Scroller.UpdateScrollState(5000f);
            Scroller.UpdateScrollState(0f);
            Scroller.UpdateScrollState(5000f);
            Scroller.UpdateScrollState(0f);

            Assert.That(() =>
            {
                Scroller.UpdateScrollState(5000f);
                Scroller.UpdateScrollState(800f);
                Scroller.UpdateScrollState(0f);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
