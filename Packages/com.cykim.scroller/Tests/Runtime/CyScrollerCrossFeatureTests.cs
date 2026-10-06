using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace CyKim.Scroller.Tests
{
    /// <summary>
    /// 기능이 겹칠 때: 크기 애니메이션 × 끝 근접, 끝 근접 핸들러 삽입 × 점프 트윈 × 정착·고속 스크롤,
    /// 풀 미리 채우기·회수 상한 × 끝 근접 페이지 불러오기·크기 애니메이션·정착 알림, 모두 켠 상태의 할당.
    /// </summary>
    public class CyScrollerCrossFeatureTests
    {
        private const float EPSILON = 0.01f;
        private const float POSITION_EPSILON = 0.5f;
        private const float NEAR_DISTANCE = 200f;
        private const float TIMEOUT = 5f;
        private const int ITEM_BASE = 1000;

        private ScrollerFixture _fixture;
        private ListTestDelegate _data;
        private int _nextItem;
        private int _settledEvents;
        private readonly List<ScrollEdge> _edgeEvents = new List<ScrollEdge>(32);
        private readonly List<bool> _fastEvents = new List<bool>(16);

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
            _data = null;
            _nextItem = 0;
            _settledEvents = 0;
            _edgeEvents.Clear();
            _fastEvents.Clear();
        }

        private CyScroller Scroller => _fixture.Scroller;

        /// <summary>항목 count개(크기 size)를 가진 List 델리게이트를 넣는다. reload가 false면 미리 채우기 등을 한 뒤 직접 ReloadData를 부른다. 뷰포트 400.</summary>
        private void Create(int count, float size = 100f, bool reload = true)
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(count, size), reload: false);
            _data = new ListTestDelegate { Prefab = _fixture.Prefab };
            for (int i = 0; i < count; i++)
            {
                _data.Insert(i, ITEM_BASE + _nextItem++, size);
            }

            Scroller.Delegate = _data;
            Scroller.ScrollerSettled += _ => _settledEvents++;
            Scroller.ScrollerFastScrollingChanged += (_, fast) => _fastEvents.Add(fast);
            Scroller.ScrollerNearEdge += (_, edge) => _edgeEvents.Add(edge);
            if (reload)
            {
                Scroller.ReloadData();
            }
        }

        private void Append(int count, float size = 100f)
        {
            int at = _data.Items.Count;
            for (int i = 0; i < count; i++)
            {
                _data.Insert(at + i, ITEM_BASE + _nextItem++, size);
            }

            Scroller.InsertCells(at, count);
        }

        /// <summary>크기 애니메이션을 한 걸음 진행하고 LateUpdate처럼 끝 근접을 판단한다.</summary>
        private void StepResize(float deltaTime)
        {
            Scroller.UpdateResizeAnimations(deltaTime);
            Scroller.UpdateNearEdges();
        }

        private IEnumerator WaitUntil(System.Func<bool> condition)
        {
            float timeout = TIMEOUT;
            while (!condition() && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>활성 셀마다 지금 데이터의 항목에 바인딩돼 있고 RectTransform이 레이아웃 자리에 있는지.</summary>
        private void AssertActiveCellsMatch(string context)
        {
            IReadOnlyList<CyScrollerCellView> cells = Scroller.ActiveCellViews;
            Assert.Greater(cells.Count, 0, context);
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                Assert.IsNotNull(view, $"{context}: 빈 자리 {i}");
                int index = view.DataIndex;
                Assert.AreEqual(_data.Items[index], view.BoundData, $"{context}: {index}번에 바인딩된 항목");

                float start = Scroller.GetCellStart(index);
                Assert.AreEqual(-start, view.RectTransform.offsetMax.y, 0.05f, $"{context}: {index}번 위치");
                Assert.AreEqual(-(start + Scroller.GetCellSize(index)), view.RectTransform.offsetMin.y, 0.05f, $"{context}: {index}번 끝");
            }
        }

        #region 크기 변경 × 끝 근접

        [Test]
        public void ResizeAnimation_ChangingTail_LatchesAndRearmsNearEnd_WithoutCountChange()
        {
            Create(50);   // 스크롤 길이 4600
            Scroller.NearEdgeDistance = NEAR_DISTANCE;
            Scroller.ScrollPosition = 4200f;   // 끝까지 400
            Scroller.UpdateNearEdges();
            Assert.AreEqual(0, _edgeEvents.Count);

            // 화면 뒤 46~49번을 100 → 10으로 줄인다 (1초, 전체 360). 개수는 그대로다.
            for (int i = 46; i < 50; i++)
            {
                _data.Sizes[i] = 10f;
                Scroller.ResizeCellView(i, 1f, TweenType.Linear);
            }

            StepResize(0.25f);   // 끝까지 310
            StepResize(0.25f);   // 220
            Assert.AreEqual(0, _edgeEvents.Count, "아직 거리 밖");
            StepResize(0.25f);   // 130
            CollectionAssert.AreEqual(new[] { ScrollEdge.End }, _edgeEvents, "애니메이션 중간 걸음의 스크롤 길이로 판단한다");
            Assert.IsTrue(Scroller.IsResizing);
            StepResize(0.25f);   // 40
            Assert.IsFalse(Scroller.IsResizing);
            Assert.AreEqual(1, _edgeEvents.Count, "잠긴 채로 더 가까워져도 한 번");
            Assert.AreEqual(4200f, Scroller.ScrollPosition, EPSILON, "Auto: 화면 뒤 항목은 위치를 바꾸지 않는다");
            Assert.AreEqual(4240f, Scroller.ScrollSize, EPSILON);

            // 다시 키우면 남은 거리가 다시 열 거리(300)를 넘어 열린다. 멀어지는 동안에는 알리지 않는다.
            for (int i = 46; i < 50; i++)
            {
                _data.Sizes[i] = 100f;
                Scroller.ResizeCellView(i, 1f, TweenType.Linear);
            }

            for (int step = 0; step < 4; step++)
            {
                StepResize(0.25f);
            }

            Assert.AreEqual(1, _edgeEvents.Count);
            Scroller.ScrollPosition = Scroller.ScrollSize;
            Scroller.UpdateNearEdges();
            CollectionAssert.AreEqual(new[] { ScrollEdge.End, ScrollEdge.End }, _edgeEvents, "열린 뒤 끝에 가면 다시 알린다");
            AssertActiveCellsMatch("after resize");
        }

        [UnityTest]
        public IEnumerator ShrinkingVisibleCellsAtEnd_ClampsInsideRange_WithoutOverscrollSettle()
        {
            Create(50);
            Scroller.NearEdgeDistance = NEAR_DISTANCE;
            Scroller.ScrollPosition = Scroller.ScrollSize;   // 4600
            yield return null;
            yield return null;
            CollectionAssert.AreEqual(new[] { ScrollEdge.End }, _edgeEvents);

            // 보이는 44·45번을 줄인다. Auto는 맨 앞 항목(42번)을 지키지만 스크롤 길이가 줄어 범위 안으로 잘린다.
            _data.Sizes[44] = 50f;
            _data.Sizes[45] = 50f;
            Scroller.ResizeCellView(44, 0.3f, TweenType.Linear);
            Scroller.ResizeCellView(45, 0.3f, TweenType.Linear);
            yield return WaitUntil(() => !Scroller.IsResizing);
            for (int i = 0; i < 10; i++)
            {
                Assert.LessOrEqual(Scroller.ScrollPosition, Scroller.ScrollSize + EPSILON, "가장자리 너머로 나가지 않는다");
                yield return null;
            }

            Assert.IsFalse(Scroller.IsResizing);
            Assert.AreEqual(4500f, Scroller.ScrollSize, EPSILON);
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, POSITION_EPSILON, "새 끝에 붙어 있다");
            Assert.IsTrue(Scroller.IsSettled);
            Assert.AreEqual(0, _settledEvents, "Elastic 되돌아오기가 없어 정착 이벤트도 없다");
            CollectionAssert.AreEqual(new[] { ScrollEdge.End }, _edgeEvents, "개수가 그대로라 잠긴 채");
            AssertActiveCellsMatch("after shrink at end");
        }

        #endregion

        #region 끝 근접 × 트윈 × 정착

        [UnityTest]
        public IEnumerator TweenJumpToEnd_HandlerAppendsPage_ArrivesOnRecomputedTarget_AndSettlesOnce()
        {
            Create(30);   // 스크롤 길이 2600
            Scroller.NearEdgeDistance = NEAR_DISTANCE;
            int appended = 0;
            Scroller.ScrollerNearEdge += (_, edge) =>
            {
                if (edge == ScrollEdge.End)
                {
                    appended++;
                    Append(10);
                }
            };

            yield return null;
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start }, _edgeEvents, "첫 프레임에 처음 쪽");

            // 29번을 맨 위에 맞추는 점프는 지금은 끝(2600)에서 잘린다. 도중에 끝 근접 핸들러가 한 페이지를 붙이면 맞출 수 있게 된다.
            int completed = 0;
            Scroller.JumpToDataIndex(29, tweenType: TweenType.Linear, tweenTime: 0.5f, jumpComplete: () => completed++);
            yield return WaitUntil(() => _settledEvents > 0);

            Assert.AreEqual(1, appended, "트윈 도중 한 번 붙인다");
            Assert.AreEqual(40, Scroller.NumberOfCells);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, _settledEvents, "트윈이 끝난 뒤 한 번 정착");
            Assert.AreEqual(Scroller.GetCellStart(29), Scroller.ScrollPosition, POSITION_EPSILON, "늘어난 배치에서 다시 계산한 목표에 도착");
            CollectionAssert.AreEqual(new[] { true, false }, _fastEvents, "빠른 트윈(약 5800px/s)이 고속 스크롤을 켰다가 끝나며 끈다");

            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.AreEqual(1, _settledEvents);
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start, ScrollEdge.End }, _edgeEvents, "붙인 뒤 멀어져 다시 알리지 않는다");
            AssertActiveCellsMatch("after tween");
        }

        #endregion

        #region 풀

        [Test]
        public void NearEdgePaging_DrawsFromPrewarmedPool_WithinCap()
        {
            Create(20, reload: false);
            Scroller.SetMaxRecycled("Test", 8);
            Scroller.Prewarm(_fixture.Prefab, 8);
            int instantiated = _fixture.InstantiatedCount;
            Assert.AreEqual(8, instantiated);

            Scroller.ReloadData();
            Scroller.NearEdgeDistance = 300f;
            Scroller.ScrollerNearEdge += (_, edge) =>
            {
                if (edge == ScrollEdge.End)
                {
                    Append(10);
                }
            };

            // 끝까지 내릴 때마다 한 페이지씩 붙는다 (개수가 바뀌어 다시 열린다).
            for (int page = 0; page < 5; page++)
            {
                Scroller.ScrollPosition = Scroller.ScrollSize;
                Scroller.UpdateNearEdges();
                Assert.AreEqual(20 + 10 * (page + 1), Scroller.NumberOfCells, $"{page}번째 페이지");
                AssertActiveCellsMatch($"page {page}");
            }

            Assert.AreEqual(instantiated, _fixture.InstantiatedCount, "페이지를 붙이고 내려가도 미리 채운 셀만 쓴다");
            Assert.LessOrEqual(Scroller.GetRecycledCellCount(), 8);
            Assert.AreEqual(8, Scroller.ActiveCellViews.Count + Scroller.GetRecycledCellCount(), "만든 셀이 모두 활성이거나 풀에 있다");
        }

        [Test]
        public void ResizeAnimation_PullsCellsFromPool_AndTrimsRecycledOnGrowBack()
        {
            Create(100, reload: false);
            Scroller.Prewarm(_fixture.Prefab, 8);
            Scroller.ReloadData();
            int instantiated = _fixture.InstantiatedCount;
            int activeBefore = Scroller.ActiveCellViews.Count;

            var resized = new List<TestCellView>();
            var versions = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                var view = (TestCellView)Scroller.ActiveCellViews[i];
                resized.Add(view);
                versions.Add(view.BindVersion);
            }

            // 0~3번을 100 → 25로 줄이면 뒤 항목이 화면에 들어온다. 새 셀은 풀에서 꺼낸다.
            for (int i = 0; i < 4; i++)
            {
                _data.Sizes[i] = 25f;
                Scroller.ResizeCellView(i, 0.4f, TweenType.Linear);
            }

            for (int step = 0; step < 4; step++)
            {
                Scroller.UpdateResizeAnimations(0.1f);
                AssertActiveCellsMatch($"shrink {step}");
            }

            int activeShrunk = Scroller.ActiveCellViews.Count;
            Assert.Greater(activeShrunk, activeBefore, "줄어든 만큼 셀이 더 보인다");
            Assert.AreEqual(instantiated, _fixture.InstantiatedCount, "풀에서 꺼내 쓴다");

            // 풀 상한을 지금 회수 수로 두고 되돌리면, 화면을 벗어난 셀을 회수하며 상한을 넘는 만큼 버린다.
            int cap = Scroller.GetRecycledCellCount();
            Scroller.SetMaxRecycled("Test", cap);
            for (int i = 0; i < 4; i++)
            {
                _data.Sizes[i] = 100f;
                Scroller.ResizeCellView(i, 0.4f, TweenType.Linear);
            }

            for (int step = 0; step < 4; step++)
            {
                Scroller.UpdateResizeAnimations(0.1f);
                Assert.LessOrEqual(Scroller.GetRecycledCellCount(), cap, $"grow {step}: 상한");
                AssertActiveCellsMatch($"grow {step}");
            }

            Assert.AreEqual(activeBefore, Scroller.ActiveCellViews.Count);
            Assert.AreEqual(cap, Scroller.GetRecycledCellCount(), "넘친 셀은 버리고 상한까지 남긴다");
            for (int i = 0; i < 4; i++)
            {
                Assert.AreSame(resized[i], Scroller.ActiveCellViews[i], $"{i}번은 같은 셀");
                Assert.AreEqual(versions[i], resized[i].BindVersion, $"{i}번은 다시 바인딩하지 않는다");
            }
        }

        [UnityTest]
        public IEnumerator Fling_SettledHooksReachActiveCellsOnly_NotPooledCells()
        {
            Create(400, reload: false);
            var created = new List<CyScrollerCellView>();
            Scroller.CellViewInstantiated += (_, cell) => created.Add(cell);
            Scroller.Prewarm(_fixture.Prefab, 12);
            Scroller.ReloadData();
            _fixture.ScrollRect.inertia = true;
            _fixture.ScrollRect.decelerationRate = 0.01f;
            yield return null;

            Scroller.LinearVelocity = 3000f;
            yield return WaitUntil(() => _settledEvents > 0);
            Assert.AreEqual(1, _settledEvents);

            int active = 0;
            int pooled = 0;
            for (int i = 0; i < created.Count; i++)
            {
                var view = (TestCellView)created[i];
                if (view.Active)
                {
                    active++;
                    Assert.AreEqual(1, view.SettledCount, $"활성 {view.DataIndex}번");
                }
                else
                {
                    pooled++;
                    Assert.AreEqual(0, view.SettledCount, "풀에 있는 셀은 알리지 않는다");
                    Assert.IsFalse(view.gameObject.activeSelf);
                }
            }

            Assert.AreEqual(Scroller.ActiveCellViews.Count, active);
            Assert.Greater(pooled, 0, "미리 채운 셀이 풀에 남아 있다");
        }

        #endregion

        #region 할당

        [Test]
        public void AllFeaturesOn_ScrollResizeSettleNearEdge_DoesNotAllocate()
        {
            Create(400, 50f);   // 스크롤 길이 19600
            Scroller.LookAheadAfter = 100f;
            Scroller.SetMaxRecycled("Test", 64);
            Scroller.NearEdgeDistance = 300f;

            // 연산 목록·애니메이션 목록·풀 용량을 채운다.
            AllFeaturesCycle();
            AllFeaturesCycle();
            _settledEvents = 0;
            _fastEvents.Clear();
            _edgeEvents.Clear();

            Assert.That(() => AllFeaturesCycle(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.Greater(_settledEvents, 0, "정착 경로를 거쳤다");
            Assert.Greater(_fastEvents.Count, 0, "고속 스크롤 경로를 거쳤다");
            Assert.Greater(_edgeEvents.Count, 0, "끝 근접 경로를 거쳤다");
            AssertActiveCellsMatch("after cycles");
        }

        /// <summary>
        /// 빠르게 내려가 보이는 셀을 애니메이션으로 키우며 정착하고, 끝에서 End 앵커로 되돌리며 정착한 뒤 맨 위로 돌아온다.
        /// 끝나면 크기는 처음과 같다. 한 번에 이벤트가 몇 개씩만 쌓여 목록 용량 안에서 는다 (측정 전에 비운다).
        /// </summary>
        private void AllFeaturesCycle()
        {
            Scroller.ScrollPosition = 3000f;
            Scroller.UpdateScrollState(5000f);
            Scroller.UpdateNearEdges();

            _data.Sizes[62] = 80f;
            Scroller.ResizeCellView(62, 0.2f, TweenType.Linear);
            for (int i = 0; i < 5; i++)
            {
                Scroller.UpdateResizeAnimations(0.05f);
                Scroller.UpdateScrollState(0f);
                Scroller.UpdateNearEdges();
            }

            Scroller.ScrollPosition = Scroller.ScrollSize;
            Scroller.UpdateScrollState(800f);
            Scroller.UpdateNearEdges();

            _data.Sizes[62] = 50f;
            Scroller.ResizeCellView(62, 0.2f, TweenType.Linear, ResizeAnchor.End);
            for (int i = 0; i < 5; i++)
            {
                Scroller.UpdateResizeAnimations(0.05f);
                Scroller.UpdateScrollState(0f);
                Scroller.UpdateNearEdges();
            }

            Scroller.ScrollPosition = 0f;
            Scroller.UpdateScrollState(0f);
            Scroller.UpdateNearEdges();
        }

        #endregion
    }
}
