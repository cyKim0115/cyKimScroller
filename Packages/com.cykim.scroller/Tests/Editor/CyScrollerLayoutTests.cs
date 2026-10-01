using NUnit.Framework;

namespace CyKim.Scroller.Tests
{
    public class CyScrollerLayoutTests
    {
        private const float EPSILON = 0.0001f;

        private static CyScrollerLayout CreateUniform(int count, float size, float spacing = 0f,
            float paddingBefore = 0f, float paddingAfter = 0f, bool loop = false, float viewport = 400f)
        {
            var layout = new CyScrollerLayout();
            layout.SetDataCount(count);
            for (int i = 0; i < count; i++)
            {
                layout.SetSize(i, size);
            }

            layout.Build(spacing, paddingBefore, paddingAfter, loop, viewport);
            return layout;
        }

        [Test]
        public void Empty_ContentIsPaddingOnly_AndRangeIsEmpty()
        {
            CyScrollerLayout layout = CreateUniform(0, 100f, 10f, 5f, 7f);

            Assert.AreEqual(12f, layout.ContentExtent, EPSILON);
            layout.GetSlotRange(0f, 400f, out int first, out int last);
            Assert.Greater(first, last);
            Assert.AreEqual(-1, layout.GetSlotAtPosition(0f));
        }

        [Test]
        public void Uniform_ExtentAndStartsIncludeSpacingAndPadding()
        {
            // 10개 × 40 + 9 × 10 간격 + 앞 5 + 뒤 7
            CyScrollerLayout layout = CreateUniform(10, 40f, 10f, 5f, 7f);

            Assert.AreEqual(5f + 400f + 90f + 7f, layout.ContentExtent, EPSILON);
            Assert.AreEqual(5f, layout.GetSlotStart(0), EPSILON);
            Assert.AreEqual(55f, layout.GetSlotStart(1), EPSILON);
            Assert.AreEqual(5f + 9 * 50f, layout.GetSlotStart(9), EPSILON);
            Assert.AreEqual(5f + 9 * 50f + 40f, layout.GetSlotEnd(9), EPSILON);
        }

        [Test]
        public void Range_UsesHalfOpenBoundaries()
        {
            CyScrollerLayout layout = CreateUniform(100, 100f);

            // 정확히 맞닿는 셀은 제외: [100, 400) → 슬롯 1..3
            layout.GetSlotRange(100f, 400f, out int first, out int last);
            Assert.AreEqual(1, first);
            Assert.AreEqual(3, last);

            layout.GetSlotRange(150f, 450f, out first, out last);
            Assert.AreEqual(1, first);
            Assert.AreEqual(4, last);
        }

        [Test]
        public void Range_ClampsToContentEdges()
        {
            CyScrollerLayout layout = CreateUniform(5, 100f);

            layout.GetSlotRange(-200f, 50f, out int first, out int last);
            Assert.AreEqual(0, first);
            Assert.AreEqual(0, last);

            layout.GetSlotRange(450f, 2000f, out first, out last);
            Assert.AreEqual(4, first);
            Assert.AreEqual(4, last);

            layout.GetSlotRange(600f, 900f, out first, out last);
            Assert.Greater(first, last, "콘텐츠 밖 구간은 비어야 한다");
        }

        [Test]
        public void Range_InsideGapIsEmpty()
        {
            CyScrollerLayout layout = CreateUniform(3, 10f, 100f);

            // 셀 0: [0,10), 간격 [10,110), 셀 1: [110,120)
            layout.GetSlotRange(20f, 100f, out int first, out int last);
            Assert.Greater(first, last);
        }

        [Test]
        public void VariableSizes_FindsCorrectSlots()
        {
            var layout = new CyScrollerLayout();
            float[] sizes = { 10f, 200f, 30f, 400f, 50f };
            layout.SetDataCount(sizes.Length);
            for (int i = 0; i < sizes.Length; i++)
            {
                layout.SetSize(i, sizes[i]);
            }

            layout.Build(0f, 0f, 0f, false, 100f);

            // 시작: 0, 10, 210, 240, 640
            Assert.AreEqual(240f, layout.GetSlotStart(3), EPSILON);
            Assert.AreEqual(3, layout.GetSlotAtPosition(300f));

            layout.GetSlotRange(205f, 245f, out int first, out int last);
            Assert.AreEqual(1, first);
            Assert.AreEqual(3, last);
        }

        [Test]
        public void NegativeSize_IsTreatedAsZero()
        {
            var layout = new CyScrollerLayout();
            layout.SetDataCount(2);
            layout.SetSize(0, -50f);
            layout.SetSize(1, 20f);
            layout.Build(0f, 0f, 0f, false, 100f);

            Assert.AreEqual(20f, layout.ContentExtent, EPSILON);
        }

        [Test]
        public void NearestSlot_PicksCloserEdgeInsideGap()
        {
            CyScrollerLayout layout = CreateUniform(3, 10f, 100f);

            // 셀 0 끝 10, 셀 1 시작 110
            Assert.AreEqual(0, layout.GetNearestSlot(40f));
            Assert.AreEqual(1, layout.GetNearestSlot(80f));
            Assert.AreEqual(1, layout.GetNearestSlot(115f));
        }

        [Test]
        public void Loop_LongCycleUsesFiveSets()
        {
            CyScrollerLayout layout = CreateUniform(10, 100f, 0f, 0f, 0f, true, 400f);

            Assert.AreEqual(5, layout.SetCount);
            Assert.AreEqual(50, layout.SlotCount);
            Assert.AreEqual(1000f, layout.CycleExtent, EPSILON);
            Assert.AreEqual(5000f, layout.ContentExtent, EPSILON);
            Assert.AreEqual(2000f, layout.MiddleSetStart, EPSILON);
            Assert.AreEqual(20, layout.MiddleSetFirstSlot);
            Assert.AreEqual(7, layout.SlotToDataIndex(17));
        }

        [Test]
        public void Loop_SpacingAlsoSeparatesCycles()
        {
            CyScrollerLayout layout = CreateUniform(3, 100f, 10f, 0f, 0f, true, 100f);

            // 사이클 = 300 + 3×10
            Assert.AreEqual(330f, layout.CycleExtent, EPSILON);
            Assert.AreEqual(330f, layout.GetSlotStart(3), EPSILON);
            Assert.AreEqual(layout.SetCount * 330f - 10f, layout.ContentExtent, EPSILON);
        }

        [TestCase(2, 100f, 1000f, 0f, 0f)]
        [TestCase(3, 100f, 400f, 0f, 0f)]
        [TestCase(10, 100f, 400f, 600f, 0f)]
        [TestCase(5, 248f, 1800f, 0f, 0f)]
        [TestCase(10, 100f, 400f, 0f, 900f)]
        public void Loop_KeepsOneCycleMarginBeyondRecenterWindow(int count, float size, float viewport, float before, float after)
        {
            var layout = new CyScrollerLayout();
            layout.SetDataCount(count);
            for (int i = 0; i < count; i++)
            {
                layout.SetSize(i, size);
            }

            layout.Build(0f, 0f, 0f, true, viewport, before, after);
            float cycle = layout.CycleExtent;

            // 위치는 [가운데 - 반 사이클, 가운데 + 반 사이클]에 머문다.
            // 그 끝에서도 미리보기 구간 바깥으로 한 사이클씩 콘텐츠가 남아야 한다.
            float lowest = layout.MiddleSetStart - cycle * 0.5f - before - cycle;
            float highest = layout.MiddleSetStart + cycle * 0.5f + viewport + after + cycle;
            Assert.GreaterOrEqual(lowest, -EPSILON);
            Assert.LessOrEqual(highest, layout.ContentExtent + EPSILON);
        }

        [Test]
        public void Loop_SetCountIsAlwaysOddAndAtLeastFive()
        {
            for (int viewport = 0; viewport <= 5000; viewport += 137)
            {
                int sets = CyScrollerLayout.ComputeLoopSetCount(300f, viewport, 5f, viewport * 0.3f, viewport * 0.1f);
                Assert.GreaterOrEqual(sets, 5);
                Assert.AreEqual(1, sets % 2);
            }
        }

        [Test]
        public void Loop_ZeroLengthCycleFallsBackToNonLoop()
        {
            CyScrollerLayout layout = CreateUniform(100, 0f, 0f, 0f, 0f, true, 400f);

            Assert.IsFalse(layout.IsLoop);
            Assert.AreEqual(1, layout.SetCount);
            Assert.AreEqual(100, layout.SlotCount);
        }

        [Test]
        public void RecenterCycles_MovesIntoMiddleWindow()
        {
            CyScrollerLayout layout = CreateUniform(10, 100f, 0f, 0f, 0f, true, 400f);

            // 가운데 2000, 창 [1500, 2500]
            Assert.AreEqual(0, layout.GetRecenterCycles(2000f));
            Assert.AreEqual(0, layout.GetRecenterCycles(2500f));
            Assert.AreEqual(1, layout.GetRecenterCycles(2600f));
            Assert.AreEqual(-1, layout.GetRecenterCycles(1400f));
            Assert.AreEqual(2, layout.GetRecenterCycles(4100f));

            CyScrollerLayout plain = CreateUniform(10, 100f);
            Assert.AreEqual(0, plain.GetRecenterCycles(99999f));
        }

        [Test]
        public void SetDataCount_ShrinkAndGrowKeepsWorking()
        {
            var layout = new CyScrollerLayout();
            layout.SetDataCount(100);
            layout.SetDataCount(3);
            for (int i = 0; i < 3; i++)
            {
                layout.SetSize(i, 10f);
            }

            layout.Build(0f, 0f, 0f, false, 50f);
            Assert.AreEqual(30f, layout.ContentExtent, EPSILON);

            layout.SetDataCount(500);
            for (int i = 0; i < 500; i++)
            {
                layout.SetSize(i, 1f);
            }

            layout.Build(0f, 0f, 0f, false, 50f);
            Assert.AreEqual(500f, layout.ContentExtent, EPSILON);
        }
    }
}
