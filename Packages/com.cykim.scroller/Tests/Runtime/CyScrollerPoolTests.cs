using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace CyKim.Scroller.Tests
{
    /// <summary>풀 프리웜(Prewarm·PrewarmAsync)과 식별자별 회수 상한(SetMaxPoolSize·DefaultMaxPoolSize).</summary>
    public class CyScrollerPoolTests
    {
        private const float TIMEOUT = 5f;

        private ScrollerFixture _fixture;
        private readonly List<CyScrollerCell> _instantiated = new List<CyScrollerCell>();

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
            _instantiated.Clear();
        }

        private CyScroller Scroller => _fixture.Scroller;

        private void Create(int count = 200, float size = 100f, bool reload = true)
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDataSource.Uniform(count, size), reload: reload);
            Scroller.CellCreated += (_, cell) => _instantiated.Add(cell);
        }

        [Test]
        public void Prewarm_FillsPool_AndFirstScrollDoesNotInstantiate()
        {
            Create(reload: false);
            Scroller.Prewarm(_fixture.Prefab, 8);
            Assert.AreEqual(8, Scroller.GetPooledCellCount());
            Assert.AreEqual(8, _instantiated.Count, "만든 셀마다 알린다");
            for (int i = 0; i < _instantiated.Count; i++)
            {
                Assert.IsFalse(_instantiated[i].gameObject.activeSelf, "풀 셀은 꺼 둔다");
                Assert.IsFalse(_instantiated[i].IsBound);
                Assert.AreEqual("Test", _instantiated[i].ReuseIdentifier);
            }

            // 첫 로드와 스크롤은 풀에서 꺼내 쓴다.
            Scroller.ReloadData();
            Scroller.ScrollPosition = 3000f;
            Scroller.ScrollPosition = 9000f;
            Assert.AreEqual(8, _instantiated.Count, "프리웜 뒤에는 새로 만들지 않는다");
            Assert.AreEqual(8, Scroller.ActiveCells.Count + Scroller.GetPooledCellCount());

            // 이미 있는 회수 셀까지 세어 모자란 만큼만 만든다.
            int recycled = Scroller.GetPooledCellCount();
            Scroller.Prewarm(_fixture.Prefab, recycled + 2);
            Assert.AreEqual(recycled + 2, Scroller.GetPooledCellCount());
            Assert.AreEqual(10, _instantiated.Count);
            Scroller.Prewarm(_fixture.Prefab, 1);
            Assert.AreEqual(10, _instantiated.Count, "이미 차 있으면 만들지 않는다");
        }

        [UnityTest]
        public IEnumerator MaxPoolSize_DestroysOldestFirst_AndClampsPrewarm()
        {
            Create();
            int before = _instantiated.Count;
            Scroller.Prewarm(_fixture.Prefab, 10);
            List<CyScrollerCell> prewarmed = _instantiated.GetRange(before, 10);

            Scroller.SetMaxPoolSize("Test", 3);
            Assert.AreEqual(3, Scroller.GetMaxPoolSize("Test"));
            Assert.AreEqual(3, Scroller.GetPooledCellCount(), "넘친 만큼 바로 줄인다");
            yield return null;
            for (int i = 0; i < 7; i++)
            {
                Assert.IsTrue(prewarmed[i] == null, $"오래된 {i}번째는 파괴");
            }

            for (int i = 7; i < 10; i++)
            {
                Assert.IsTrue(prewarmed[i] != null, $"최근 {i}번째는 남는다");
            }

            // 스크롤로 회수가 몰려도 상한을 넘지 않는다.
            Scroller.OverscanAfter = 400f;
            Scroller.ScrollPosition = 5000f;
            Scroller.OverscanAfter = 0f;
            Scroller.ScrollPosition = 5000f;
            Assert.LessOrEqual(Scroller.GetPooledCellCount(), 3);

            // 프리웜도 상한까지만 채운다.
            Scroller.Prewarm(_fixture.Prefab, 50);
            Assert.AreEqual(3, Scroller.GetPooledCellCount());

            // 식별자별 값이 없으면 기본 상한을 따른다. 0은 제한 없음.
            Scroller.DefaultMaxPoolSize = 2;
            Assert.AreEqual(2, Scroller.GetMaxPoolSize("Alt"));
            Scroller.Prewarm(_fixture.AltPrefab, 5);
            Assert.AreEqual(3 + 2, Scroller.GetPooledCellCount());
            Scroller.SetMaxPoolSize("Alt", 0);
            Scroller.Prewarm(_fixture.AltPrefab, 5);
            Assert.AreEqual(3 + 5, Scroller.GetPooledCellCount());
            Scroller.SetMaxPoolSize("Alt", -1);
            Assert.AreEqual(3 + 2, Scroller.GetPooledCellCount(), "기본 상한으로 돌아가면 바로 줄인다");
        }

        [UnityTest]
        public IEnumerator PrewarmAsync_FillsPoolOnCompletion_AndDoesNotOverRequest()
        {
            Create(reload: false);
            yield return null;   // DataSource를 넣은 뒤 자동 리로드: 활성 셀을 만든다
            int before = _instantiated.Count;
            Assert.AreEqual(0, Scroller.GetPooledCellCount());

            AsyncInstantiateOperation<CyScrollerCell> operation = Scroller.PrewarmAsync(_fixture.Prefab, 6);
            Assert.IsNotNull(operation);
            Assert.IsNull(Scroller.PrewarmAsync(_fixture.Prefab, 6), "진행 중인 요청이 채울 만큼은 다시 만들지 않는다");

            float timeout = TIMEOUT;
            while (!operation.isDone && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            yield return null;   // 완료 콜백이 돈 뒤
            Assert.IsTrue(operation.isDone);
            Assert.AreEqual(6, Scroller.GetPooledCellCount());
            Assert.AreEqual(before + 6, _instantiated.Count);
            for (int i = before; i < _instantiated.Count; i++)
            {
                Assert.IsFalse(_instantiated[i].gameObject.activeSelf);
            }

            // 활성 셀을 파괴하고 다시 로드하면 새 셀은 모두 비동기로 채운 풀에서 나온다.
            List<CyScrollerCell> prewarmed = _instantiated.GetRange(before, 6);
            Scroller.DestroyActiveCells();
            Scroller.ReloadData();
            Assert.AreEqual(before + 6, _instantiated.Count, "비동기로 채운 셀을 쓴다");
            int bound = 0;
            for (int i = 0; i < prewarmed.Count; i++)
            {
                bound += prewarmed[i].IsBound ? 1 : 0;
            }

            Assert.AreEqual(Scroller.ActiveCells.Count, bound, "활성 셀이 모두 프리웜한 셀이다");
            Assert.IsNull(Scroller.PrewarmAsync(_fixture.Prefab, 2), "이미 차 있으면 null");
        }

        [UnityTest]
        public IEnumerator PrewarmAsync_ScrollerDestroyedBeforeCompletion_DestroysResults()
        {
            Create(reload: false);
            AsyncInstantiateOperation<CyScrollerCell> operation = Scroller.PrewarmAsync(_fixture.Prefab, 4);
            Assert.IsNotNull(operation);
            Object.Destroy(Scroller);

            float timeout = TIMEOUT;
            while (!operation.isDone && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            yield return null;   // 완료 콜백이 돈 뒤
            yield return null;   // Destroy가 적용된 뒤
            Assert.IsTrue(operation.isDone);
            Assert.AreEqual(0, _instantiated.Count, "파괴된 스크롤러는 알리지 않는다");
            CyScrollerCell[] results = operation.Result;
            for (int i = 0; i < results.Length; i++)
            {
                Assert.IsTrue(results[i] == null, $"{i}번째 결과는 파괴");
            }
        }

        [Test]
        public void RecycleWithCap_DoesNotAllocate()
        {
            Create(400, 50f);
            Scroller.SetMaxPoolSize("Test", 64);
            Scroller.OverscanAfter = 100f;
            ScrollCycle();
            ScrollCycle();

            Assert.That(() => ScrollCycle(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        private void ScrollCycle()
        {
            Scroller.ScrollPosition = 3000f;
            Scroller.ScrollPosition = 9000f;
            Scroller.ScrollPosition = 15000f;
            Scroller.ScrollPosition = 0f;
        }
    }
}
