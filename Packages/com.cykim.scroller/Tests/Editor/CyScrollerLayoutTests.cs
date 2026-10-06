using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace CyKim.Scroller.Tests
{
    public class CyScrollerLayoutTests
    {
        private const float EPSILON = 0.0001f;

        private const int RANDOM_SEED = 20261001;
        private const int RANDOM_LAYOUTS = 300;
        private const int RANDOM_POSITIONS = 24;
        private const int RANDOM_BOUNDARY_SLOTS = 8;

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
        public void TrailingSlot_PicksSlotEndingAtOrAfterPosition()
        {
            // 셀 0: [0,10), 간격 [10,110), 셀 1: [110,120), 간격, 셀 2: [220,230)
            CyScrollerLayout layout = CreateUniform(3, 10f, 100f);

            Assert.AreEqual(0, layout.GetTrailingSlotAtPosition(5f));
            Assert.AreEqual(0, layout.GetTrailingSlotAtPosition(10f), "끝이 정확히 맞닿으면 그 셀");
            Assert.AreEqual(1, layout.GetTrailingSlotAtPosition(50f), "간격 안이면 간격 뒤 셀");
            Assert.AreEqual(1, layout.GetTrailingSlotAtPosition(120f));
            Assert.AreEqual(2, layout.GetTrailingSlotAtPosition(121f));
            Assert.AreEqual(0, layout.GetTrailingSlotAtPosition(-50f), "앞쪽 밖은 첫 슬롯");
            Assert.AreEqual(2, layout.GetTrailingSlotAtPosition(500f), "뒤쪽 밖은 마지막 슬롯");

            Assert.AreEqual(-1, CreateUniform(0, 10f).GetTrailingSlotAtPosition(0f));
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

        #region Incremental Edits

        private const int RANDOM_EDIT_BATCHES = 300;

        // 이 배치 수마다 데이터를 새로 채운다 (가끔 새 레이아웃으로 버퍼를 개수만큼 딱 맞춘다).
        private const int RANDOM_EDIT_RESET_INTERVAL = 25;

        private const float EDIT_PADDING_BEFORE = 3.5f;
        private const float EDIT_PADDING_AFTER = 1.5f;

        [Test]
        public void InsertRemoveMoveSizes_ShiftSizesAndRebuildStarts()
        {
            var layout = new CyScrollerLayout();
            float[] sizes = { 10f, 20f, 30f, 40f };
            layout.SetDataCount(sizes.Length);
            for (int i = 0; i < sizes.Length; i++)
            {
                layout.SetSize(i, sizes[i]);
            }

            layout.Build(5f, 2f, 3f, false, 100f);

            // [10, 20, 30, 40] → 1번 앞에 두 칸 → [10, ?, ?, 20, 30, 40]
            layout.InsertSizes(1, 2);
            Assert.AreEqual(6, layout.DataCount);
            Assert.IsTrue(layout.IsSizeUnset(1));
            Assert.IsTrue(layout.IsSizeUnset(2));
            Assert.IsFalse(layout.IsSizeUnset(3));
            Assert.AreEqual(20f, layout.GetSize(3));
            layout.SetSize(1, 1f);
            layout.SetSize(2, -7f);
            Assert.IsFalse(layout.IsSizeUnset(2), "음수 크기는 0으로 잘려 받은 크기다");

            // 5 → 0 이동: [40, 10, 1, 0, 20, 30], [2, 4) 삭제: [40, 10, 20, 30]
            layout.MoveSize(5, 0);
            layout.RemoveSizes(2, 2);
            layout.Build(5f, 2f, 3f, false, 100f);

            float[] expected = { 40f, 10f, 20f, 30f };
            Assert.AreEqual(expected.Length, layout.DataCount);
            float start = 2f;
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], layout.GetSize(i), $"size {i}");
                Assert.AreEqual(start, layout.GetSlotStart(i), EPSILON, $"start {i}");
                start += expected[i] + 5f;
            }

            Assert.AreEqual(2f + 100f + 3 * 5f + 3f, layout.ContentExtent, EPSILON);
        }

        /// <summary>
        /// 고정 시드로 크기 배열 삽입·삭제·이동(끝 삭제·끝에 붙이기 포함)을 섞은 배치를 적용하고(삽입한 자리는 배치 끝에 채움), 바뀐 자리부터 다시 더한 Build 결과가
        /// 같은 크기로 처음부터 Build한 결과와 비트 단위로 같은지 비교한다. 간격 변경·루프·빈 버퍼에서 늘어나는 경로와
        /// 개수만큼 딱 맞는 새 버퍼(첫 로드처럼)에서 늘어나는 경로도 섞는다.
        /// </summary>
        [Test]
        public void IncrementalEdits_MatchFreshBuild()
        {
            var random = new System.Random(RANDOM_SEED + 4);
            var layout = new CyScrollerLayout();

            // 기준 모델. NaN은 아직 받지 않은 크기다.
            var model = new List<float>();
            float spacing = 0f;

            for (int iteration = 0; iteration < RANDOM_EDIT_BATCHES; iteration++)
            {
                if (iteration % RANDOM_EDIT_RESET_INTERVAL == 0)
                {
                    // 새 레이아웃이면 버퍼가 개수만큼 딱 맞아 첫 배치의 삽입이 버퍼를 늘린다.
                    if (random.Next(3) != 0)
                    {
                        layout = new CyScrollerLayout();
                    }

                    int count = random.Next(0, 40);
                    model.Clear();
                    layout.SetDataCount(count);
                    for (int i = 0; i < count; i++)
                    {
                        float size = RandomEditSize(random);
                        layout.SetSize(i, size);
                        model.Add(Math.Max(0f, size));
                    }

                    layout.Build(spacing, EDIT_PADDING_BEFORE, EDIT_PADDING_AFTER, false, 200f);
                }

                int operations = random.Next(1, 7);
                for (int op = 0; op < operations; op++)
                {
                    int kind = random.Next(6);
                    if (kind == 5 && model.Count > 0)
                    {
                        // 끝 교체 (무한 스크롤에서 로딩 셀을 지우고 다음 페이지를 붙이는 경우): 끝에서 지운 뒤 그 자리에 붙인다.
                        int removed = random.Next(1, Math.Min(3, model.Count) + 1);
                        layout.RemoveSizes(model.Count - removed, removed);
                        model.RemoveRange(model.Count - removed, removed);
                        int added = random.Next(1, 9);
                        layout.InsertSizes(model.Count, added);
                        for (int j = 0; j < added; j++)
                        {
                            model.Add(float.NaN);
                        }
                    }
                    else if (kind == 0 || model.Count == 0)
                    {
                        int at = random.Next(model.Count + 1);
                        int count = random.Next(1, 6);
                        layout.InsertSizes(at, count);
                        for (int j = 0; j < count; j++)
                        {
                            model.Insert(at, float.NaN);
                        }
                    }
                    else if (kind == 1)
                    {
                        int at = random.Next(model.Count);
                        int count = random.Next(1, Math.Min(4, model.Count - at) + 1);
                        layout.RemoveSizes(at, count);
                        model.RemoveRange(at, count);
                    }
                    else if (kind == 2)
                    {
                        int from = random.Next(model.Count);
                        int to = random.Next(model.Count);
                        layout.MoveSize(from, to);
                        float moved = model[from];
                        model.RemoveAt(from);
                        model.Insert(to, moved);
                    }
                    else if (kind == 3)
                    {
                        // 끝에서 지운다 (지운 자리의 시작이 맞은 값으로 남는 경로).
                        int count = random.Next(1, Math.Min(4, model.Count) + 1);
                        layout.RemoveSizes(model.Count - count, count);
                        model.RemoveRange(model.Count - count, count);
                    }
                    else
                    {
                        // 끝에 붙인다.
                        int count = random.Next(1, 6);
                        layout.InsertSizes(model.Count, count);
                        for (int j = 0; j < count; j++)
                        {
                            model.Add(float.NaN);
                        }
                    }
                }

                string context = $"#{iteration} count={model.Count} spacing={spacing}";
                Assert.AreEqual(model.Count, layout.DataCount, context);

                // 배치 끝: 받지 않은 자리(연산을 거치며 같이 옮겨졌다)만 채운다.
                for (int i = 0; i < model.Count; i++)
                {
                    bool unset = float.IsNaN(model[i]);
                    Assert.AreEqual(unset, layout.IsSizeUnset(i), $"{context} unset {i}");
                    if (unset)
                    {
                        float size = RandomEditSize(random);
                        layout.SetSize(i, size);
                        model[i] = Math.Max(0f, size);
                    }
                    else
                    {
                        Assert.AreEqual(model[i], layout.GetSize(i), $"{context} size {i}");
                    }
                }

                if (random.Next(5) == 0)
                {
                    spacing = random.Next(0, 21) * 0.37f;
                }

                bool loop = random.Next(4) == 0;
                float viewport = random.Next(0, 801) * 0.5f;
                layout.Build(spacing, EDIT_PADDING_BEFORE, EDIT_PADDING_AFTER, loop, viewport, 10f, 20f);
                AssertMatchesFreshBuild(layout, model, spacing, loop, viewport, context);
            }
        }

        /// <summary>
        /// 끝 항목을 지운 뒤 같은 배치에서 끝에 붙여 버퍼가 늘어날 때(빈 버퍼에서 개수만큼 딱 맞게 만든 첫 로드 뒤) 접두합이 처음부터 Build한 결과와 같은지.
        /// 끝을 지운 직후에는 지운 자리의 시작도 맞은 값으로 남는다고 보므로, 버퍼를 늘릴 때 그 자리까지 옮겨야 붙인 항목이 0에서 시작하지 않는다
        /// (무한 스크롤에서 로딩 셀을 지우고 다음 페이지를 붙이는 경우).
        /// </summary>
        [TestCase(50, 1, 20, 0f)]
        [TestCase(50, 1, 20, 4f)]
        [TestCase(8, 3, 30, 2.5f)]
        [TestCase(12, 12, 13, 1f)]
        public void RemoveTailThenAppendPastCapacity_MatchesFreshBuild(int count, int removed, int appended, float spacing)
        {
            var layout = new CyScrollerLayout();
            var model = new List<float>();
            layout.SetDataCount(count);
            for (int i = 0; i < count; i++)
            {
                float size = 10f + i * 1.5f;
                layout.SetSize(i, size);
                model.Add(size);
            }

            layout.Build(spacing, EDIT_PADDING_BEFORE, EDIT_PADDING_AFTER, false, 200f);

            // 한 배치: 끝 removed개 삭제 → 그 자리에 appended개 삽입(버퍼 초과) → 삽입분 크기 → 한 번 Build
            int at = count - removed;
            layout.RemoveSizes(at, removed);
            model.RemoveRange(at, removed);
            layout.InsertSizes(at, appended);
            for (int i = 0; i < appended; i++)
            {
                float size = 7f + i * 0.75f;
                layout.SetSize(at + i, size);
                model.Insert(at + i, size);
            }

            layout.Build(spacing, EDIT_PADDING_BEFORE, EDIT_PADDING_AFTER, false, 200f);
            AssertMatchesFreshBuild(layout, model, spacing, false, 200f, $"count {count} − {removed} + {appended}");

            float expectedFirstAppended = EDIT_PADDING_BEFORE;
            for (int i = 0; i < at; i++)
            {
                expectedFirstAppended += model[i] + spacing;
            }

            // 더하는 순서가 달라 생기는 float 오차만 허용한다 (잘못되면 남은 항목 길이만큼 어긋난다).
            Assert.AreEqual(expectedFirstAppended, layout.GetSlotStart(at), 0.01f, "붙인 첫 항목은 남은 항목 뒤에서 시작한다");
        }

        /// <summary>같은 크기로 처음부터 Build한 레이아웃과 접두합·길이·루프 세트가 비트 단위로 같은지.</summary>
        private static void AssertMatchesFreshBuild(CyScrollerLayout layout, List<float> model, float spacing, bool loop, float viewport, string context)
        {
            var fresh = new CyScrollerLayout();
            fresh.SetDataCount(model.Count);
            for (int i = 0; i < model.Count; i++)
            {
                fresh.SetSize(i, model[i]);
            }

            fresh.Build(spacing, EDIT_PADDING_BEFORE, EDIT_PADDING_AFTER, loop, viewport, 10f, 20f);
            Assert.AreEqual(model.Count, layout.DataCount, context);
            Assert.AreEqual(fresh.CycleExtent, layout.CycleExtent, 0d, context);
            Assert.AreEqual(fresh.ContentExtent, layout.ContentExtent, 0d, context);
            Assert.AreEqual(fresh.IsLoop, layout.IsLoop, context);
            Assert.AreEqual(fresh.SetCount, layout.SetCount, context);
            for (int i = 0; i < model.Count; i++)
            {
                Assert.AreEqual(fresh.GetSlotStart(i), layout.GetSlotStart(i), 0d, $"{context} start {i}");
                Assert.AreEqual(fresh.GetSlotEnd(i), layout.GetSlotEnd(i), 0d, $"{context} end {i}");
            }
        }

        /// <summary>반올림이 생기는 소수 크기. 가끔 0·음수(0으로 잘림)도 낸다.</summary>
        private static float RandomEditSize(System.Random random)
        {
            int roll = random.Next(10);
            if (roll == 0)
            {
                return 0f;
            }

            if (roll == 1)
            {
                return -random.Next(1, 30);
            }

            return (float)(random.NextDouble() * 300.0);
        }

        #endregion

        #region Random Reference

        /// <summary>
        /// 고정 시드로 무작위 레이아웃(크기 0·음수 포함, 간격·패딩·루프·뷰포트·미리보기)을 만들어
        /// 슬롯 좌표와 이진 탐색 질의를 선형 탐색 기준 모델과 비교한다.
        /// </summary>
        [Test]
        public void RandomLayouts_MatchLinearReferenceModel()
        {
            var random = new System.Random(RANDOM_SEED);

            // 실제 사용처럼 인스턴스 하나를 재사용해 개수가 늘고 줄 때의 버퍼 재사용 경로도 지난다.
            var layout = new CyScrollerLayout();
            var positions = new List<double>();
            int checkedQueries = 0;

            for (int iteration = 0; iteration < RANDOM_LAYOUTS; iteration++)
            {
                // 값을 0.5 단위로 뽑아 float 합이 정확하게 한다 (경계 질의가 반올림에 흔들리지 않게).
                int count = random.Next(0, 31);
                var sizes = new float[count];
                for (int i = 0; i < count; i++)
                {
                    int roll = random.Next(10);
                    sizes[i] = roll < 3 ? 0f : roll == 3 ? -random.Next(1, 20) : random.Next(1, 401) * 0.5f;
                }

                float spacing = random.Next(3) == 0 ? 0f : random.Next(1, 41) * 0.5f;
                float paddingBefore = random.Next(3) == 0 ? 0f : random.Next(1, 81) * 0.5f;
                float paddingAfter = random.Next(3) == 0 ? 0f : random.Next(1, 81) * 0.5f;
                bool loop = random.Next(2) == 0;
                float viewport = random.Next(0, 1201) * 0.5f;
                float lookAheadBefore = random.Next(2) == 0 ? 0f : random.Next(1, 401) * 0.5f;
                float lookAheadAfter = random.Next(2) == 0 ? 0f : random.Next(1, 401) * 0.5f;

                layout.SetDataCount(count);
                for (int i = 0; i < count; i++)
                {
                    layout.SetSize(i, sizes[i]);
                }

                layout.Build(spacing, paddingBefore, paddingAfter, loop, viewport, lookAheadBefore, lookAheadAfter);

                string context = $"#{iteration} count={count} spacing={spacing} padding={paddingBefore}/{paddingAfter} " +
                                 $"loop={loop} viewport={viewport} lookAhead={lookAheadBefore}/{lookAheadAfter} sets={layout.SetCount}";

                // 루프 여부와 세트 수 규칙. 세트 수 자체는 다른 테스트가 검증하므로 기준 모델은 같은 값을 쓴다.
                double cycle = LinearReferenceLayout.GetCycleExtent(sizes, spacing);
                bool expectLoop = loop && count > 0 && cycle > 0.0;
                Assert.AreEqual(expectLoop, layout.IsLoop, context);
                if (expectLoop)
                {
                    Assert.GreaterOrEqual(layout.SetCount, 5, context);
                    Assert.AreEqual(1, layout.SetCount % 2, context);
                }
                else
                {
                    Assert.AreEqual(1, layout.SetCount, context);
                }

                var reference = new LinearReferenceLayout(sizes, spacing, paddingBefore, paddingAfter, layout.SetCount);
                Assert.AreEqual(reference.SlotCount, layout.SlotCount, context);
                Assert.AreEqual(cycle, layout.CycleExtent, EPSILON, context);
                Assert.AreEqual(reference.ContentExtent, layout.ContentExtent, EPSILON, context);
                if (expectLoop)
                {
                    int middleFirst = layout.SetCount / 2 * count;
                    Assert.AreEqual(middleFirst, layout.MiddleSetFirstSlot, context);
                    Assert.AreEqual(reference.Starts[middleFirst], layout.MiddleSetStart, EPSILON, context);
                }

                AssertSlotCoordinates(layout, reference, sizes, context);

                positions.Clear();
                for (int q = 0; q < RANDOM_POSITIONS; q++)
                {
                    positions.Add(random.Next(-160, (int)(reference.ContentExtent * 4.0) + 161) * 0.25);
                }

                // 경계 위·바로 앞뒤·셀 가운데·간격 가운데 (반열린 구간, 가까운 셀 동률 규칙)
                for (int q = 0; q < RANDOM_BOUNDARY_SLOTS && reference.SlotCount > 0; q++)
                {
                    int slot = random.Next(reference.SlotCount);
                    double start = reference.Starts[slot];
                    double end = reference.Ends[slot];
                    positions.Add(start);
                    positions.Add(end);
                    positions.Add(start - 0.25);
                    positions.Add(end + 0.25);
                    positions.Add((start + end) * 0.5);
                    if (slot + 1 < reference.SlotCount)
                    {
                        positions.Add((end + reference.Starts[slot + 1]) * 0.5);
                    }
                }

                for (int q = 0; q < positions.Count; q++)
                {
                    double position = positions[q];
                    AssertIndex("GetSlotAtPosition", position,
                        reference.GetSlotAtPosition(position), layout.GetSlotAtPosition((float)position), context);
                    AssertIndex("GetTrailingSlotAtPosition", position,
                        reference.GetTrailingSlotAtPosition(position), layout.GetTrailingSlotAtPosition((float)position), context);
                    AssertIndex("GetNearestSlot", position,
                        reference.GetNearestSlot(position), layout.GetNearestSlot((float)position), context);

                    // 길이 0·음수(빈 구간)도 섞는다.
                    double to = position + random.Next(-8, (int)((viewport + 200f) * 4f) + 1) * 0.25;
                    AssertRange(position, to, reference, layout, context);
                    checkedQueries += 4;
                }

                // 셀 경계끼리 맞닿은 구간
                for (int q = 0; q < RANDOM_BOUNDARY_SLOTS && reference.SlotCount > 0; q++)
                {
                    int a = random.Next(reference.SlotCount);
                    int b = random.Next(a, reference.SlotCount);
                    AssertRange(reference.Ends[a], reference.Starts[b], reference, layout, context);
                    AssertRange(reference.Starts[a], reference.Ends[b], reference, layout, context);
                    checkedQueries += 2;
                }
            }

            Assert.Greater(checkedQueries, RANDOM_LAYOUTS * RANDOM_POSITIONS, "질의가 충분히 돌아야 한다");
        }

        private static void AssertSlotCoordinates(CyScrollerLayout layout, LinearReferenceLayout reference, float[] sizes, string context)
        {
            for (int slot = 0; slot < reference.SlotCount; slot++)
            {
                double start = layout.GetSlotStart(slot);
                double end = layout.GetSlotEnd(slot);
                double size = layout.GetSlotSize(slot);
                int dataIndex = layout.SlotToDataIndex(slot);
                if (Math.Abs(start - reference.Starts[slot]) > EPSILON
                    || Math.Abs(end - reference.Ends[slot]) > EPSILON
                    || Math.Abs(size - Math.Max(0f, sizes[slot % sizes.Length])) > EPSILON
                    || dataIndex != slot % sizes.Length)
                {
                    Assert.Fail($"slot {slot}: start {start} / {reference.Starts[slot]}, end {end} / {reference.Ends[slot]}, " +
                                $"size {size}, data {dataIndex} (실제 / 기준) ({context})");
                }
            }
        }

        private static void AssertIndex(string query, double position, int expected, int actual, string context)
        {
            if (expected != actual)
            {
                Assert.Fail($"{query}({position}): 기준 {expected}, 실제 {actual} ({context})");
            }
        }

        private static void AssertRange(double from, double to, LinearReferenceLayout reference, CyScrollerLayout layout, string context)
        {
            reference.GetSlotRange(from, to, out int expectedFirst, out int expectedLast);
            layout.GetSlotRange((float)from, (float)to, out int first, out int last);
            if (expectedFirst != first || expectedLast != last)
            {
                Assert.Fail($"GetSlotRange({from}, {to}): 기준 [{expectedFirst}, {expectedLast}], 실제 [{first}, {last}] ({context})");
            }
        }

        /// <summary>
        /// 선형 탐색 기준 모델. 슬롯 좌표를 처음부터 차례로 더해 만들고, 모든 질의에 슬롯 전체를 앞에서부터 훑어 답한다.
        /// </summary>
        private sealed class LinearReferenceLayout
        {
            public readonly double[] Starts;
            public readonly double[] Ends;
            public readonly double ContentExtent;

            public LinearReferenceLayout(float[] sizes, float spacing, float paddingBefore, float paddingAfter, int setCount)
            {
                int slotCount = sizes.Length * setCount;
                Starts = new double[slotCount];
                Ends = new double[slotCount];

                // 사이클 사이에도 간격이 들어간다 (루프 세트를 이어 붙인 것과 같다).
                double position = paddingBefore;
                for (int slot = 0; slot < slotCount; slot++)
                {
                    double size = Math.Max(0f, sizes[slot % sizes.Length]);
                    Starts[slot] = position;
                    Ends[slot] = position + size;
                    position += size + spacing;
                }

                // 마지막 셀 뒤에는 간격 없이 뒤 패딩만 붙는다.
                ContentExtent = slotCount == 0 ? paddingBefore + paddingAfter : Ends[slotCount - 1] + paddingAfter;
            }

            public int SlotCount => Starts.Length;

            public static double GetCycleExtent(float[] sizes, float spacing)
            {
                double cycle = 0.0;
                for (int i = 0; i < sizes.Length; i++)
                {
                    cycle += Math.Max(0f, sizes[i]) + spacing;
                }

                return cycle;
            }

            /// <summary>(from, to)와 조금이라도 겹치는 슬롯. 끝이 from이거나 시작이 to인 슬롯은 겹치지 않는다. 없으면 [0, -1].</summary>
            public void GetSlotRange(double from, double to, out int first, out int last)
            {
                first = 0;
                last = -1;
                if (to <= from)
                {
                    return;
                }

                bool found = false;
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    if (Ends[slot] > from && Starts[slot] < to)
                    {
                        if (!found)
                        {
                            first = slot;
                            found = true;
                        }

                        last = slot;
                    }
                }
            }

            /// <summary>시작 ≤ position인 마지막 슬롯. 그런 슬롯이 없으면 0, 슬롯이 없으면 -1.</summary>
            public int GetSlotAtPosition(double position)
            {
                if (SlotCount == 0)
                {
                    return -1;
                }

                int found = 0;
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    if (Starts[slot] <= position)
                    {
                        found = slot;
                    }
                }

                return found;
            }

            /// <summary>끝 ≥ position인 첫 슬롯. 그런 슬롯이 없으면 마지막 슬롯, 슬롯이 없으면 -1.</summary>
            public int GetTrailingSlotAtPosition(double position)
            {
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    if (Ends[slot] >= position)
                    {
                        return slot;
                    }
                }

                return SlotCount - 1;
            }

            /// <summary>
            /// 슬롯 구간까지 거리(안이면 0)가 가장 작은 슬롯. 시작이 position 이하인 앞쪽 후보끼리는 뒤의 것,
            /// 뒤쪽 후보끼리는 앞의 것을 고르고, 앞뒤 거리가 같으면 앞쪽 후보를 고른다.
            /// </summary>
            public int GetNearestSlot(double position)
            {
                if (SlotCount == 0)
                {
                    return -1;
                }

                int before = -1;
                int after = -1;
                double beforeDistance = double.MaxValue;
                double afterDistance = double.MaxValue;
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    if (Starts[slot] <= position)
                    {
                        double distance = Math.Max(0.0, position - Ends[slot]);
                        if (distance <= beforeDistance)
                        {
                            before = slot;
                            beforeDistance = distance;
                        }
                    }
                    else
                    {
                        double distance = Starts[slot] - position;
                        if (distance < afterDistance)
                        {
                            after = slot;
                            afterDistance = distance;
                        }
                    }
                }

                if (before < 0)
                {
                    return after;
                }

                if (after < 0)
                {
                    return before;
                }

                return beforeDistance <= afterDistance ? before : after;
            }
        }

        #endregion
    }
}
