using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace CyKim.Scroller.Tests
{
    /// <summary>끝 근접 이벤트(ScrollerNearEdge): 한 번 알리고 잠그기, 멀어지거나 개수가 바뀌면 다시 열기, 짧은 콘텐츠, 루프, 핸들러 안 삽입, 할당.</summary>
    public class CyScrollerNearEdgeTests
    {
        private const float DISTANCE = 200f;

        private ScrollerFixture _fixture;
        private ListTestDelegate _data;
        private int _nextItem;
        private readonly List<ScrollEdge> _events = new List<ScrollEdge>(32);

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
            _data = null;
            _nextItem = 0;
            _events.Clear();
        }

        private CyScroller Scroller => _fixture.Scroller;

        /// <summary>항목 count개(크기 100)를 가진 List 델리게이트로 로드하고 끝 근접 거리를 200으로 둔다. 뷰포트 400.</summary>
        private void Create(int count, bool loop = false)
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(count, 100f), loop, reload: false);
            _data = new ListTestDelegate { Prefab = _fixture.Prefab };
            for (int i = 0; i < count; i++)
            {
                _data.Insert(i, _nextItem++, 100f);
            }

            Scroller.Delegate = _data;
            Scroller.ReloadData();
            Scroller.NearEdgeDistance = DISTANCE;
            Scroller.ScrollerNearEdge += (_, edge) => _events.Add(edge);
        }

        private void Append(int count)
        {
            int at = _data.Items.Count;
            for (int i = 0; i < count; i++)
            {
                _data.Insert(at + i, _nextItem++, 100f);
            }

            Scroller.InsertCells(at, count);
        }

        private void MoveTo(float position)
        {
            Scroller.ScrollPosition = position;
            Scroller.UpdateNearEdges();
        }

        [Test]
        public void ReachingEdges_NotifiesOnce_AndRearmsAfterMovingAway()
        {
            Create(50);   // 스크롤 길이 4600
            Scroller.UpdateNearEdges();
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start }, _events, "맨 위에서 시작하면 처음 쪽을 한 번 알린다");

            MoveTo(4300f);   // 끝까지 300
            Assert.AreEqual(1, _events.Count);
            MoveTo(4450f);   // 150
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start, ScrollEdge.End }, _events);

            // 머무르거나 다시 열 거리(300) 안에서 오가면 다시 알리지 않는다.
            MoveTo(4600f);
            MoveTo(4300f);
            MoveTo(4450f);
            Assert.AreEqual(2, _events.Count);

            // 300을 넘게 멀어졌다가 다시 오면 알린다.
            MoveTo(4200f);
            MoveTo(4500f);
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start, ScrollEdge.End, ScrollEdge.End }, _events);

            // 처음 쪽은 멀리 내려왔을 때 이미 다시 열렸으므로 돌아오면 알린다.
            MoveTo(100f);
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start, ScrollEdge.End, ScrollEdge.End, ScrollEdge.Start }, _events);
        }

        [Test]
        public void StartEdge_RearmsLikeEnd()
        {
            Create(50);
            Scroller.UpdateNearEdges();
            Assert.AreEqual(1, _events.Count);

            MoveTo(250f);    // 다시 열 거리(300) 안
            MoveTo(50f);
            Assert.AreEqual(1, _events.Count);
            MoveTo(400f);    // 열림
            MoveTo(150f);
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start, ScrollEdge.Start }, _events);
        }

        [Test]
        public void HandlerAppendingPage_RearmsByCountChange()
        {
            Create(50);
            Scroller.UpdateNearEdges();
            _events.Clear();

            // 끝 근처에서 한 페이지(10개)를 붙인다. 개수가 바뀌어 다시 열리지만 이제 멀어서 알리지 않는다.
            int appended = 0;
            Scroller.ScrollerNearEdge += (scroller, edge) =>
            {
                if (edge == ScrollEdge.End && appended < 2)
                {
                    appended++;
                    Append(appended == 1 ? 10 : 1);
                }
            };

            MoveTo(4450f);
            Assert.AreEqual(1, appended);
            Assert.AreEqual(60, Scroller.NumberOfCells);
            Scroller.UpdateNearEdges();
            Assert.AreEqual(1, _events.Count, "붙인 뒤 멀어졌으면 다시 알리지 않는다");

            // 새 끝 근처에서 다시 알리고, 이번에는 1개(100)만 붙여 여전히 가까우면 다음 판단에서 또 알린다.
            MoveTo(Scroller.ScrollSize - 50f);
            Assert.AreEqual(2, appended);
            Scroller.UpdateNearEdges();
            CollectionAssert.AreEqual(new[] { ScrollEdge.End, ScrollEdge.End, ScrollEdge.End }, _events);

            // 개수가 그대로인 리로드는 다시 열지 않는다.
            Scroller.ReloadDataKeepingPosition();
            Scroller.UpdateNearEdges();
            Assert.AreEqual(3, _events.Count);
        }

        [Test]
        public void ShortContent_NotifiesEndOnly_OncePerCount()
        {
            Create(2);   // 콘텐츠 200 < 뷰포트 400
            Scroller.UpdateNearEdges();
            Scroller.UpdateNearEdges();
            CollectionAssert.AreEqual(new[] { ScrollEdge.End }, _events, "처음 쪽은 알리지 않는다");

            Append(1);   // 아직 짧다
            Scroller.UpdateNearEdges();
            CollectionAssert.AreEqual(new[] { ScrollEdge.End, ScrollEdge.End }, _events);
        }

        [Test]
        public void BothEdgesNear_StartHandlerChangingData_SkipsStaleEnd()
        {
            Create(5);   // 스크롤 길이 100: 처음·끝 모두 가깝다
            Scroller.ScrollerNearEdge += (scroller, edge) =>
            {
                if (edge == ScrollEdge.Start && _data.Items.Count == 5)
                {
                    Append(10);
                }
            };

            Scroller.UpdateNearEdges();
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start }, _events, "처음 쪽 핸들러가 붙인 뒤에는 끝이 멀어 같은 판단의 끝 알림을 버린다");

            // 개수가 바뀌어 두 가장자리가 다시 열린다. 아직 맨 위이므로 처음 쪽만 다시 알린다.
            Scroller.UpdateNearEdges();
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start, ScrollEdge.Start }, _events);

            // 끝 쪽은 잠기지 않았으므로 끝에 가면 알린다.
            MoveTo(Scroller.ScrollSize);
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start, ScrollEdge.Start, ScrollEdge.End }, _events);
        }

        [Test]
        public void EmptyList_NotifiesEnd()
        {
            Create(0);
            Scroller.UpdateNearEdges();
            CollectionAssert.AreEqual(new[] { ScrollEdge.End }, _events);
        }

        [Test]
        public void LoopAndZeroDistance_NotifyNothing()
        {
            Create(10, loop: true);
            Scroller.UpdateNearEdges();
            Scroller.ScrollPosition = 0f;
            Scroller.UpdateNearEdges();
            Assert.AreEqual(0, _events.Count, "루프는 알리지 않는다");

            Scroller.Loop = false;
            Scroller.NearEdgeDistance = 0f;
            Scroller.ScrollPosition = 0f;
            Scroller.UpdateNearEdges();
            Assert.AreEqual(0, _events.Count, "거리 0이면 끈다");

            Scroller.NearEdgeDistance = DISTANCE;
            Scroller.UpdateNearEdges();
            CollectionAssert.AreEqual(new[] { ScrollEdge.Start }, _events, "다시 켜면 그 자리에서 판단한다");
        }

        [Test]
        public void InsideOpenBatch_WaitsUntilClosed()
        {
            Create(50);
            Scroller.UpdateNearEdges();
            _events.Clear();
            Scroller.ScrollPosition = 4500f;

            Scroller.BeginUpdates();
            Scroller.UpdateNearEdges();
            Assert.AreEqual(0, _events.Count, "배치가 열려 있으면 판단하지 않는다");
            Scroller.EndUpdates();

            Scroller.UpdateNearEdges();
            CollectionAssert.AreEqual(new[] { ScrollEdge.End }, _events);
        }

        [UnityTest]
        public IEnumerator LateUpdate_NotifiesAfterScrolling_AndHandlerCanInsert()
        {
            Create(30);
            Scroller.ScrollerNearEdge += (scroller, edge) =>
            {
                if (edge == ScrollEdge.End)
                {
                    Append(5);
                }
            };

            yield return null;
            Assert.Contains(ScrollEdge.Start, _events, "첫 프레임에 처음 쪽");

            Scroller.ScrollPosition = Scroller.ScrollSize;
            yield return null;
            Assert.Contains(ScrollEdge.End, _events);
            Assert.AreEqual(35, Scroller.NumberOfCells, "LateUpdate 핸들러 안에서 삽입했다");

            yield return null;
            Assert.AreEqual(35, Scroller.NumberOfCells, "붙인 뒤 멀어져 다시 알리지 않는다");
        }

        [Test]
        public void NearEdges_DoesNotAllocate()
        {
            Create(50);
            Scroller.UpdateNearEdges();
            NearEdgeCycle();
            NearEdgeCycle();

            Assert.That(() => NearEdgeCycle(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        private void NearEdgeCycle()
        {
            _events.Clear();
            MoveTo(2000f);
            MoveTo(4500f);
            MoveTo(2000f);
            MoveTo(50f);
            MoveTo(2000f);
        }
    }
}
