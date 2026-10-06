using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace CyKim.Scroller.Tests
{
    /// <summary>
    /// 셀 크기 변경(ResizeCellView·RequestResize): 바로 바꾸기와 애니메이션, 화면 기준(Auto·Start·End), 다시 바인딩하지 않기,
    /// 트윈·드래그·증분 변경·리로드와 겹칠 때, 루프 모드, 할당.
    /// </summary>
    public class CyScrollerResizeTests
    {
        private const float EPSILON = 0.01f;
        private const float POSITION_EPSILON = 0.05f;
        private const int ITEM_BASE = 1000;

        private ScrollerFixture _fixture;
        private ListTestDelegate _data;
        private int _nextItem;

        [TearDown]
        public void TearDown()
        {
            _fixture?.Dispose();
            _fixture = null;
            _data = null;
            _nextItem = 0;
        }

        private CyScroller Scroller => _fixture.Scroller;

        private void Create(int count, float size = 100f, bool loop = false)
        {
            _fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(count, size), loop, reload: false);
            _data = new ListTestDelegate { Prefab = _fixture.Prefab };
            for (int i = 0; i < count; i++)
            {
                _data.Insert(i, NextItem(), size);
            }

            Scroller.Delegate = _data;
            Scroller.ReloadData();
        }

        private int NextItem() => ITEM_BASE + _nextItem++;

        private TestCellView CellForItem(int item)
        {
            IReadOnlyList<CyScrollerCellView> cells = Scroller.ActiveCellViews;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                if (view != null && view.BoundData == item)
                {
                    return view;
                }
            }

            return null;
        }

        /// <summary>항목의 화면 위치 (셀 시작 − 뷰포트 시작).</summary>
        private float ScreenOffsetOfItem(int item)
        {
            int index = _data.Items.IndexOf(item);
            return Scroller.GetCellStart(index) - Scroller.ScrollPosition;
        }

        /// <summary>크기가 델리게이트와 같고, 활성 셀이 레이아웃과 맞는지.</summary>
        private void AssertMatchesData(string context)
        {
            Assert.AreEqual(_data.Items.Count, Scroller.NumberOfCells, $"{context}: 개수");
            for (int i = 0; i < _data.Items.Count; i++)
            {
                Assert.AreEqual(_data.Sizes[i], Scroller.GetCellSize(i), EPSILON, $"{context}: 크기 {i}");
            }

            AssertCellsMatchLayout(context);
        }

        /// <summary>
        /// 활성 범위가 뷰포트 + 미리보기 구간을 정확히 덮고, 활성 셀마다 인덱스·바인딩 항목·RectTransform 위치와 크기가 지금 레이아웃과 같은지 (루프면 슬롯 기준).
        /// </summary>
        private void AssertCellsMatchLayout(string context)
        {
            float position = Scroller.ScrollPosition;
            Scroller.Layout.GetSlotRange(
                position - Scroller.LookAheadBefore,
                position + Scroller.ScrollRectSize + Scroller.LookAheadAfter,
                out int first,
                out int last);

            IReadOnlyList<CyScrollerCellView> cells = Scroller.ActiveCellViews;
            int expectedCount = first <= last ? last - first + 1 : 0;
            Assert.AreEqual(expectedCount, cells.Count, $"{context}: 활성 셀 수 (위치 {position})");
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                int slot = first + i;
                int index = Scroller.GetDataIndexForCellViewIndex(slot);
                Assert.IsNotNull(view, $"{context}: 빈 자리 {slot}");
                Assert.AreEqual(slot, view.CellIndex, $"{context}: CellIndex");
                Assert.AreEqual(index, view.DataIndex, $"{context}: DataIndex");
                Assert.AreEqual(_data.Items[index], view.BoundData, $"{context}: {index}번에 바인딩된 항목");

                float start = Scroller.GetScrollPositionForCellViewIndex(slot);
                float end = start + Scroller.GetCellSize(index);
                Assert.AreEqual(-start, view.RectTransform.offsetMax.y, POSITION_EPSILON, $"{context}: {slot}번 위치");
                Assert.AreEqual(-end, view.RectTransform.offsetMin.y, POSITION_EPSILON, $"{context}: {slot}번 끝");
            }
        }

        private void Step(float deltaTime, int times = 1)
        {
            for (int i = 0; i < times; i++)
            {
                Scroller.UpdateResizeAnimations(deltaTime);
            }
        }

        #region Immediate

        [Test]
        public void Immediate_Auto_KeepsScreen_AndDoesNotRebind()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;   // 맨 앞 10번 안 50
            int reused = 0;
            Scroller.CellViewReused += (_, __) => reused++;

            var items = new List<int>();
            var cells = new List<TestCellView>();
            var versions = new List<int>();
            var offsets = new List<float>();
            for (int index = 10; index <= 13; index++)
            {
                int item = _data.Items[index];
                items.Add(item);
                cells.Add(CellForItem(item));
                versions.Add(CellForItem(item).BindVersion);
                offsets.Add(ScreenOffsetOfItem(item));
            }

            int calls = _data.GetCellViewCalls;
            int sizeCalls = _data.GetCellViewSizeCalls;

            // 뷰포트 위 항목이 커졌다: 그만큼 옮겨 화면을 지킨다.
            _data.Sizes[3] = 160f;
            Scroller.ResizeCellView(3);
            Assert.AreEqual(1110f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(sizeCalls + 1, _data.GetCellViewSizeCalls, "그 항목 크기만 묻는다");
            for (int i = 0; i < items.Count; i++)
            {
                Assert.AreEqual(offsets[i], ScreenOffsetOfItem(items[i]), EPSILON, $"{items[i]} 화면 위치");
            }

            AssertMatchesData("above");

            // 보이는 항목(맨 앞 뒤)이 커졌다: 위치는 그대로이고 그 셀이 아래로 늘어난다.
            _data.Sizes[12] = 200f;
            Scroller.ResizeCellView(12);
            Assert.AreEqual(1110f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(offsets[2], ScreenOffsetOfItem(items[2]), EPSILON);
            Assert.AreEqual(200f, cells[2].RectTransform.rect.height, POSITION_EPSILON, "셀 높이");
            AssertMatchesData("visible");

            // 맨 앞 항목 자신: 그 항목 안 거리(50)를 지킨다.
            _data.Sizes[10] = 300f;
            Scroller.ResizeCellView(10);
            Assert.AreEqual(1110f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(-50f, ScreenOffsetOfItem(items[0]), EPSILON);
            AssertMatchesData("first visible");

            // 남은 셀은 같은 뷰이고 다시 바인딩하지 않았다.
            Assert.AreEqual(calls, _data.GetCellViewCalls, "새로 활성 범위에 들어온 자리가 없으면 셀을 받지 않는다");
            Assert.AreEqual(0, reused);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreSame(cells[i], CellForItem(items[i]));
                Assert.AreEqual(versions[i], cells[i].BindVersion, $"{items[i]} BindVersion");
                Assert.AreEqual(0, cells[i].DataIndexChangedCount);
            }
        }

        [Test]
        public void Immediate_StartAndEndAnchors_KeepThatEdge_AndClampToRange()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;
            int top = _data.Items[10];

            // Start: 뷰포트 위 3번의 시작을 지킨다 → 위치 그대로, 보이던 항목이 아래로 밀린다.
            _data.Sizes[3] = 160f;
            Scroller.ResizeCellView(3, anchor: ResizeAnchor.Start);
            Assert.AreEqual(1050f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(10f, ScreenOffsetOfItem(top), EPSILON);
            AssertMatchesData("start above");

            // End: 3번의 끝을 지킨다 → 줄어든 만큼 위치가 당겨지고 보던 화면은 그대로다.
            _data.Sizes[3] = 100f;
            Scroller.ResizeCellView(3, anchor: ResizeAnchor.End);
            Assert.AreEqual(990f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(10f, ScreenOffsetOfItem(top), EPSILON);
            AssertMatchesData("end above");

            // End: 보이는 12번이 위로 늘어난다 (끝이 화면 같은 자리).
            int item12 = _data.Items[12];
            float endOnScreen = Scroller.GetCellStart(12) + 100f - Scroller.ScrollPosition;
            _data.Sizes[12] = 200f;
            Scroller.ResizeCellView(12, anchor: ResizeAnchor.End);
            Assert.AreEqual(1090f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(endOnScreen, Scroller.GetCellStart(12) + 200f - Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(endOnScreen - 200f, ScreenOffsetOfItem(item12), EPSILON);
            AssertMatchesData("end visible");

            // Start: 보이는 11번의 시작을 지킨다.
            float startOnScreen = ScreenOffsetOfItem(_data.Items[11]);
            _data.Sizes[11] = 50f;
            Scroller.ResizeCellView(11, anchor: ResizeAnchor.Start);
            Assert.AreEqual(1090f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(startOnScreen, ScreenOffsetOfItem(_data.Items[11]), EPSILON);
            AssertMatchesData("start visible");

            // 끝에서 줄어들면 결과는 스크롤 범위로 잘린다.
            Scroller.ScrollPosition = Scroller.ScrollSize;
            _data.Sizes[98] = 20f;
            Scroller.ResizeCellView(98, anchor: ResizeAnchor.Start);
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("clamped");
        }

        [Test]
        public void Immediate_InBatch_FollowsSequentialIndices_AndSkipsRemovedItems()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;
            int target = _data.Items[12];
            TestCellView cell = CellForItem(target);
            int version = cell.BindVersion;

            // 삽입 뒤 기준 인덱스(13)로 요청하고, 뒤따른 삭제가 다시 12로 옮긴다.
            Scroller.BeginUpdates();
            _data.Insert(0, NextItem(), 100f);
            Scroller.InsertCells(0, 1);
            _data.Sizes[13] = 250f;
            Scroller.ResizeCellView(13);
            _data.RemoveRange(0, 1);
            Scroller.RemoveCells(0, 1);
            Scroller.EndUpdates();

            Assert.AreEqual(250f, Scroller.GetCellSize(12), EPSILON);
            Assert.AreSame(cell, CellForItem(target));
            Assert.AreEqual(version, cell.BindVersion);
            AssertMatchesData("sequential");

            // 같은 배치에서 지워진 항목의 크기 변경은 아무것도 하지 않는다 (크기를 묻지 않는다).
            int sizeCalls = _data.GetCellViewSizeCalls;
            Scroller.BeginUpdates();
            _data.Sizes[5] = 300f;
            Scroller.ResizeCellView(5);
            _data.RemoveRange(5, 1);
            Scroller.RemoveCells(5, 1);
            Scroller.EndUpdates();
            Assert.AreEqual(sizeCalls, _data.GetCellViewSizeCalls, "지워진 항목 크기는 묻지 않는다");
            AssertMatchesData("removed in batch");
        }

        [Test]
        public void Immediate_EdgeAnchorInBatch_UsesPreBatchEdge_AndFallsBackWhenRemoved()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;
            int item11 = _data.Items[11];

            // 앞 연산(이동)으로 인덱스가 바뀐 항목(옛 11번 → 12번)의 시작 가장자리를 지킨다. 맨 앞 항목 기준(Auto)이면 위치는 1100이다.
            Scroller.BeginUpdates();
            _data.Move(40, 5);
            Scroller.MoveCell(40, 5);
            _data.Sizes[12] = 160f;
            Scroller.ResizeCellView(12, anchor: ResizeAnchor.Start);
            Scroller.EndUpdates();
            Assert.AreEqual(1150f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(50f, ScreenOffsetOfItem(item11), EPSILON);
            AssertMatchesData("start after move");

            // 가장자리를 지킬 항목이 같은 배치에서 지워지면 맨 앞 항목 기준으로 지킨다.
            int top = _data.Items[11];
            float topOffset = ScreenOffsetOfItem(top);
            Scroller.BeginUpdates();
            _data.Sizes[3] = 160f;
            Scroller.ResizeCellView(3, anchor: ResizeAnchor.End);
            _data.RemoveRange(3, 1);
            Scroller.RemoveCells(3, 1);
            Scroller.EndUpdates();
            Assert.AreEqual(topOffset, ScreenOffsetOfItem(top), EPSILON);
            AssertMatchesData("edge item removed");
        }

        [Test]
        public void Immediate_AlignmentKeptAfterJump_WinsOverAnchor()
        {
            Create(100);
            Scroller.JumpToDataIndex(50, 0.5f, 0.5f);
            Assert.AreEqual(4850f, Scroller.ScrollPosition, EPSILON);

            // 점프 정렬이 유지되는 중이면 앵커보다 정렬이 먼저다: 50번 가운데가 계속 뷰포트 가운데에 있다.
            _data.Sizes[50] = 300f;
            Scroller.ResizeCellView(50, anchor: ResizeAnchor.Start);
            Assert.AreEqual(4950f, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("aligned");
        }

        [Test]
        public void RequestResize_FromCell_UsesItsDataIndex()
        {
            Create(100);
            int item = _data.Items[2];
            TestCellView cell = CellForItem(item);
            int version = cell.BindVersion;

            _data.Sizes[2] = 250f;
            cell.RequestResize();
            Assert.AreEqual(250f, Scroller.GetCellSize(2), EPSILON);
            Assert.AreSame(cell, CellForItem(item));
            Assert.AreEqual(version, cell.BindVersion);
            AssertMatchesData("request");

            // 바인딩되지 않은 셀(템플릿)은 아무것도 하지 않는다.
            Assert.DoesNotThrow(() => _fixture.Prefab.RequestResize());
        }

        #endregion

        #region Animated

        [Test]
        public void Animated_InterpolatesSize_AndEndsExactlyOnTarget()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;
            int item = _data.Items[12];
            TestCellView cell = CellForItem(item);
            int version = cell.BindVersion;
            int calls = _data.GetCellViewCalls;

            _data.Sizes[12] = 200f;
            Scroller.ResizeCellView(12, 1f, TweenType.Linear);
            Assert.IsTrue(Scroller.IsResizing);
            Assert.AreEqual(100f, Scroller.GetCellSize(12), EPSILON, "요청 순간에는 그대로");

            Step(0.25f);
            Assert.AreEqual(125f, Scroller.GetCellSize(12), EPSILON);
            Assert.IsTrue(Scroller.Layout.HasDeferredSizes, "중간 걸음은 접두합을 다시 더하지 않는다");
            AssertCellsMatchLayout("quarter");
            Step(0.25f);
            Assert.AreEqual(150f, Scroller.GetCellSize(12), EPSILON);
            Assert.AreEqual(1050f, Scroller.ScrollPosition, EPSILON, "Auto: 맨 앞 항목 뒤는 위치 그대로");
            AssertCellsMatchLayout("half");

            Step(0.6f);
            Assert.IsFalse(Scroller.IsResizing, "끝나면 진행 목록이 빈다");
            Assert.IsFalse(Scroller.Layout.HasDeferredSizes, "마지막 걸음에 접는다");
            Assert.AreEqual(200f, Scroller.GetCellSize(12), EPSILON, "끝은 정확히 목표");
            AssertMatchesData("done");
            Assert.AreSame(cell, CellForItem(item));
            Assert.AreEqual(version, cell.BindVersion, "다시 바인딩하지 않는다");
            Assert.AreEqual(calls, _data.GetCellViewCalls);
        }

        [Test]
        public void Animated_EndAnchor_MovesPositionEachStep()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;
            int item = _data.Items[11];
            float end = Scroller.GetCellStart(11) + 100f - Scroller.ScrollPosition;

            _data.Sizes[11] = 50f;
            Scroller.ResizeCellView(11, 1f, TweenType.Linear, ResizeAnchor.End);
            Step(0.5f);
            Assert.AreEqual(75f, Scroller.GetCellSize(11), EPSILON);
            Assert.AreEqual(1025f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(end, ScreenOffsetOfItem(item) + 75f, EPSILON, "끝 가장자리가 화면 같은 자리");
            AssertCellsMatchLayout("half");

            Step(0.5f);
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(end, ScreenOffsetOfItem(item) + 50f, EPSILON);
            AssertMatchesData("done");
        }

        [Test]
        public void Animated_NewRequestContinuesFromCurrentSize_AndFollowsInsertions()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;

            _data.Sizes[12] = 200f;
            Scroller.ResizeCellView(12, 1f, TweenType.Linear);
            Step(0.5f);
            Assert.AreEqual(150f, Scroller.GetCellSize(12), EPSILON);

            // 같은 항목에 새 요청: 지금 크기(150)에서 새 목표(100)로.
            _data.Sizes[12] = 100f;
            Scroller.ResizeCellView(12, 1f, TweenType.Linear);
            Step(0.5f);
            Assert.AreEqual(125f, Scroller.GetCellSize(12), EPSILON);
            Step(0.5f);
            Assert.IsFalse(Scroller.IsResizing);
            AssertMatchesData("retarget");

            // 앞에 삽입하면 애니메이션이 같은 항목을 따라간다.
            int item = _data.Items[12];
            _data.Sizes[12] = 200f;
            Scroller.ResizeCellView(12, 1f, TweenType.Linear);
            Step(0.5f);
            _data.Insert(0, NextItem(), 100f);
            Scroller.InsertCells(0, 1);
            Assert.AreEqual(13, _data.Items.IndexOf(item));
            Assert.AreEqual(150f, Scroller.GetCellSize(13), EPSILON);
            Step(0.5f);
            Assert.IsFalse(Scroller.IsResizing);
            AssertMatchesData("followed insert");
        }

        [Test]
        public void Animated_DroppedByRemoveReloadAndImmediateResize()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;

            // 지워지면 버린다.
            _data.Sizes[13] = 50f;
            Scroller.ResizeCellView(13, 1f, TweenType.Linear);
            Step(0.5f);
            _data.RemoveRange(13, 1);
            Scroller.RemoveCells(13, 1);
            Assert.IsFalse(Scroller.IsResizing);
            AssertMatchesData("removed");

            // ReloadCellView는 그 크기를 다시 받는다.
            _data.Sizes[20] = 300f;
            Scroller.ResizeCellView(20, 1f, TweenType.Linear);
            Step(0.5f);
            Scroller.ReloadCellView(20);
            Assert.IsFalse(Scroller.IsResizing);
            AssertMatchesData("reload cell");

            // 바로 바꾸는 요청이 진행 중인 애니메이션을 대신한다.
            _data.Sizes[15] = 40f;
            Scroller.ResizeCellView(15, 1f, TweenType.Linear);
            Step(0.5f);
            Scroller.ResizeCellView(15);
            Assert.IsFalse(Scroller.IsResizing);
            AssertMatchesData("immediate");

            // 크기를 다시 읽는 리로드는 모두 버린다.
            _data.Sizes[16] = 250f;
            _data.Sizes[30] = 250f;
            Scroller.ResizeCellView(16, 1f, TweenType.Linear);
            Scroller.ResizeCellView(30, 1f, TweenType.Linear);
            Step(0.5f);
            Scroller.ReloadDataKeepingPosition();
            Assert.IsFalse(Scroller.IsResizing);
            AssertMatchesData("reload keeping position");
        }

        [Test]
        public void Animated_InBatch_StartsAtFinalIndex_AndWaitsWhileBatchIsOpen()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;
            int item = _data.Items[30];

            Scroller.BeginUpdates();
            _data.Sizes[30] = 200f;
            Scroller.ResizeCellView(30, 1f, TweenType.Linear);
            _data.RemoveRange(0, 1);
            Scroller.RemoveCells(0, 1);
            Scroller.EndUpdates();

            Assert.IsTrue(Scroller.IsResizing);
            Assert.AreEqual(29, _data.Items.IndexOf(item));

            // 배치가 열려 있는 동안에는 진행하지 않는다.
            Scroller.BeginUpdates();
            Step(0.5f);
            Assert.AreEqual(100f, Scroller.GetCellSize(29), EPSILON);
            Scroller.EndUpdates();

            Step(0.5f);
            Assert.AreEqual(150f, Scroller.GetCellSize(29), EPSILON);
            Step(0.5f);
            Assert.AreEqual(200f, Scroller.GetCellSize(29), EPSILON);
            AssertMatchesData("batched animation");
        }

        [Test]
        public void Animated_WhileTweening_ArrivesAtTargetAndCompletesOnce()
        {
            Create(200);
            int completed = 0;
            Scroller.JumpToDataIndex(150, tweenType: TweenType.Linear, tweenTime: 1f, jumpComplete: () => completed++);

            // 목표보다 앞의 항목이 트윈 도중 커진다.
            _data.Sizes[20] = 400f;
            Scroller.ResizeCellView(20, 0.5f, TweenType.EaseInOutCubic);
            for (int i = 0; i < 30; i++)
            {
                Scroller.UpdateResizeAnimations(0.05f);
                Scroller.UpdateTween(0.05f);
            }

            Assert.IsFalse(Scroller.IsResizing);
            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(1, completed, "완료 콜백 한 번");
            Assert.AreEqual(Scroller.GetCellStart(150), Scroller.ScrollPosition, POSITION_EPSILON, "새 배치의 목표에 도착");
            AssertMatchesData("tween");
        }

        [Test]
        public void Loop_AppliesImmediately()
        {
            Create(10, loop: true);
            Assert.IsTrue(Scroller.Layout.IsLoop);
            CyScrollerAnchor before = Scroller.CaptureAnchor();

            // 맨 앞 항목 뒤의 항목: 위치를 지키는 재배치라 맨 앞 항목과 그 안 거리가 그대로다.
            int changed = (before.DataIndex + 3) % 10;
            _data.Sizes[changed] = 150f;
            Scroller.ResizeCellView(changed, 1f, TweenType.Linear);
            Assert.IsFalse(Scroller.IsResizing, "루프는 애니메이션 없이 바로 바꾼다");
            Assert.AreEqual(150f, Scroller.GetCellSize(changed), EPSILON);
            CyScrollerAnchor after = Scroller.CaptureAnchor();
            Assert.AreEqual(before.DataIndex, after.DataIndex);
            Assert.AreEqual(before.Offset, after.Offset, EPSILON);
            AssertCellsMatchLayout("loop animated request");

            int other = (before.DataIndex + 4) % 10;
            _data.Sizes[other] = 60f;
            Scroller.ResizeCellView(other);
            Assert.AreEqual(60f, Scroller.GetCellSize(other), EPSILON);
            AssertCellsMatchLayout("loop immediate");
        }

        [Test]
        public void Loop_TurnedOnMidAnimation_FinishesAtTarget()
        {
            Create(20);
            _data.Sizes[5] = 180f;
            Scroller.ResizeCellView(5, 1f, TweenType.Linear);
            Step(0.5f);

            Scroller.Loop = true;
            Step(0.1f);
            Assert.IsFalse(Scroller.IsResizing);
            Assert.AreEqual(180f, Scroller.GetCellSize(5), EPSILON);
            AssertCellsMatchLayout("loop on");
        }

        [UnityTest]
        public IEnumerator Animated_MidDrag_KeepsContentUnderFinger()
        {
            Create(100);
            _fixture.ScrollRect.inertia = false;
            yield return null;

            GameObject target = _fixture.ScrollRect.gameObject;
            var start = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var eventData = new PointerEventData(null) { button = PointerEventData.InputButton.Left, position = start };
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);
            eventData.position = start + new Vector2(0f, 150f);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
            yield return null;
            Assert.AreEqual(150f, Scroller.ScrollPosition, 1f);

            // 맨 앞 항목(1번)보다 앞의 0번이 0.2초 동안 100 커진다. 손가락 아래 콘텐츠는 그대로다.
            int top = _data.Items[1];
            float screen = ScreenOffsetOfItem(top);
            _data.Sizes[0] = 200f;
            Scroller.ResizeCellView(0, 0.2f, TweenType.Linear);
            float timeout = 2f;
            while (Scroller.IsResizing && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
                Assert.AreEqual(screen, ScreenOffsetOfItem(top), 1f, "애니메이션 중 화면 그대로");
            }

            Assert.IsFalse(Scroller.IsResizing);
            Assert.AreEqual(250f, Scroller.ScrollPosition, 1f);
            Assert.IsTrue(Scroller.IsDragging, "드래그는 끊지 않는다");

            // 드래그 기준점도 옮겨졌으면 이후 50px 이동은 250 + 50이다.
            eventData.position = start + new Vector2(0f, 200f);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
            yield return null;
            Assert.AreEqual(300f, Scroller.ScrollPosition, 1f);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);
            AssertMatchesData("after drag");
        }

        #endregion

        [Test]
        public void ResizeAnimation_AfterWarmup_DoesNotAllocate()
        {
            Create(400, 50f);
            Scroller.LookAheadAfter = 100f;
            Scroller.ScrollPosition = 5000f;

            // 연산 목록·애니메이션 목록·풀 용량을 채운다.
            ResizeCycle();
            ResizeCycle();

            Assert.That(() => ResizeCycle(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            AssertMatchesData("after cycles");
        }

        /// <summary>보이는 항목·위 항목을 애니메이션으로 키웠다 줄이고(Auto·End), 바로 바꾸기도 한다. 끝나면 크기는 처음과 같다.</summary>
        private void ResizeCycle()
        {
            _data.Sizes[101] = 120f;
            _data.Sizes[20] = 90f;
            Scroller.ResizeCellView(101, 0.2f, TweenType.EaseOutCubic);
            Scroller.ResizeCellView(20, 0.2f, TweenType.Linear, ResizeAnchor.End);
            Step(0.05f, 5);

            _data.Sizes[101] = 50f;
            _data.Sizes[20] = 50f;
            Scroller.ResizeCellView(101, 0.2f, TweenType.Linear, ResizeAnchor.Start);
            Scroller.ResizeCellView(20);
            Step(0.05f, 5);
        }
    }
}
