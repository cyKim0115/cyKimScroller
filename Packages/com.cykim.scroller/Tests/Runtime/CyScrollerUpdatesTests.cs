using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace CyKim.Scroller.Tests
{
    /// <summary>
    /// 항목 값 목록과 크기 목록을 가진 테스트 델리게이트. 셀에는 항목 값을 바인딩한다(<see cref="TestCellView.BoundData"/>).
    /// 증분 변경 테스트의 기준 데이터(List 모델)다.
    /// </summary>
    internal class ListTestDelegate : ICyScrollerDelegate
    {
        public readonly List<int> Items = new List<int>();
        public readonly List<float> Sizes = new List<float>();
        public CyScrollerCellView Prefab;

        /// <summary>AltItems에 든 항목은 이 프리팹(다른 셀 식별자)으로 바인딩한다.</summary>
        public CyScrollerCellView AltPrefab;
        public readonly HashSet<int> AltItems = new HashSet<int>();

        public int GetCellViewCalls;
        public int GetCellViewSizeCalls;

        /// <summary>바인딩을 마친 뒤(반환 직전) 불린다.</summary>
        public System.Action<CyScroller, int> GetCellViewHook;

        /// <summary>크기를 돌려주기 직전에 불린다 (증분 변경을 적용하는 중 삽입분 크기를 물을 때의 사용자 코드를 흉내 낸다).</summary>
        public System.Action<CyScroller, int> GetCellViewSizeHook;

        /// <summary>개수를 돌려주기 직전에 불린다 (다시 읽기를 시작할 때의 사용자 코드를 흉내 낸다).</summary>
        public System.Action<CyScroller> GetNumberOfCellsHook;

        public int GetNumberOfCells(CyScroller scroller)
        {
            GetNumberOfCellsHook?.Invoke(scroller);
            return Items.Count;
        }

        public float GetCellViewSize(CyScroller scroller, int dataIndex)
        {
            GetCellViewSizeCalls++;
            GetCellViewSizeHook?.Invoke(scroller, dataIndex);
            return Sizes[dataIndex];
        }

        public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
        {
            GetCellViewCalls++;
            int item = Items[dataIndex];
            CyScrollerCellView prefab = AltPrefab != null && AltItems.Contains(item) ? AltPrefab : Prefab;
            var view = (TestCellView)scroller.GetCellView(prefab);
            view.BoundData = item;
            view.BoundVersion = view.BindVersion;
            view.BoundItemId = view.HasItemId ? view.ItemId : -1L;

            // 내용 갱신은 바인딩 뒤에 받은 것만 센다.
            view.MaskedRefreshCount = 0;
            view.LastChangeMask = 0;
            GetCellViewHook?.Invoke(scroller, dataIndex);
            return view;
        }

        /// <summary>item 항목을 지금 바인딩하면 고를 셀 식별자.</summary>
        public string ExpectedIdentifier(int item) => AltPrefab != null && AltItems.Contains(item) ? AltPrefab.CellIdentifier : Prefab.CellIdentifier;

        public void Insert(int index, int item, float size)
        {
            Items.Insert(index, item);
            Sizes.Insert(index, size);
        }

        public void RemoveRange(int index, int count)
        {
            Items.RemoveRange(index, count);
            Sizes.RemoveRange(index, count);
        }

        public void Move(int from, int to)
        {
            int item = Items[from];
            float size = Sizes[from];
            Items.RemoveAt(from);
            Sizes.RemoveAt(from);
            Items.Insert(to, item);
            Sizes.Insert(to, size);
        }
    }

    /// <summary>항목 값을 안정 ID로도 주는 <see cref="ListTestDelegate"/>.</summary>
    internal sealed class ListIdTestDelegate : ListTestDelegate, ICyScrollerItemIdProvider
    {
        public int GetItemIdCalls;

        public long GetItemId(CyScroller scroller, int dataIndex)
        {
            GetItemIdCalls++;
            return Items[dataIndex];
        }
    }

    /// <summary>
    /// 증분 변경: InsertCells·RemoveCells·MoveCell, BeginUpdates/EndUpdates 배치, 위치 보존, 재진입·트윈·드래그·항목 ID와
    /// 부분 갱신(RefreshCells·ReloadCellView·RefreshActiveCellViews(int)).
    /// </summary>
    public class CyScrollerUpdatesTests
    {
        private const float EPSILON = 0.01f;

        // 1만 근처 위치는 RectTransform 기록 뒤 읽은 값이 0.01 넘게 어긋날 수 있다 (float 정밀도).
        private const float POSITION_EPSILON = 0.05f;

        private const int RANDOM_SEED = 20261006;
        private const int RANDOM_STEPS = 160;

        // 항목 값. 데이터 인덱스와 겹치지 않게 띄운다.
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

        /// <summary>항목 count개(크기 size)를 가진 List 델리게이트로 로드한다.</summary>
        private ListTestDelegate Create(int count, float size = 100f, bool ids = false, bool loop = false, float spacing = 0f,
            RectOffset padding = null, bool reload = true, ScrollDirection direction = ScrollDirection.Vertical)
        {
            _fixture = ScrollerFixture.Create(direction, TestDelegate.Uniform(count, size), loop, spacing, padding, reload: false);
            _data = ids ? new ListIdTestDelegate() : new ListTestDelegate();
            _data.Prefab = _fixture.Prefab;
            for (int i = 0; i < count; i++)
            {
                _data.Insert(i, NextItem(), size);
            }

            Scroller.Delegate = _data;
            if (reload)
            {
                Scroller.ReloadData();
            }

            return _data;
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

        /// <summary>
        /// 스크롤러가 델리게이트 데이터와 맞는지: 개수·크기, 활성 범위가 뷰포트 + 미리보기 구간을 정확히 덮는지,
        /// 활성 셀마다 인덱스·바인딩된 항목·항목 ID·RectTransform 위치가 레이아웃과 같은지.
        /// </summary>
        private void AssertMatchesData(string context)
        {
            Assert.AreEqual(_data.Items.Count, Scroller.NumberOfCells, $"{context}: 개수");
            for (int i = 0; i < _data.Items.Count; i++)
            {
                Assert.AreEqual(Mathf.Max(0f, _data.Sizes[i]), Scroller.GetCellSize(i), EPSILON, $"{context}: 크기 {i}");
            }

            float position = Scroller.ScrollPosition;
            Scroller.Layout.GetSlotRange(
                position - Scroller.LookAheadBefore,
                position + Scroller.ScrollRectSize + Scroller.LookAheadAfter,
                out int first,
                out int last);

            IReadOnlyList<CyScrollerCellView> cells = Scroller.ActiveCellViews;
            int expectedCount = first <= last ? last - first + 1 : 0;
            Assert.AreEqual(expectedCount, cells.Count, $"{context}: 활성 셀 수 (위치 {position})");
            if (expectedCount > 0)
            {
                Assert.AreEqual(first, Scroller.StartCellViewIndex, $"{context}: 활성 시작");
                Assert.AreEqual(last, Scroller.EndCellViewIndex, $"{context}: 활성 끝");
            }

            bool hasIds = _data is ListIdTestDelegate;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                Assert.IsNotNull(view, $"{context}: 빈 자리 {first + i}");
                int index = first + i;
                Assert.AreEqual(index, view.DataIndex, $"{context}: DataIndex");
                Assert.AreEqual(index, view.CellIndex, $"{context}: CellIndex");
                Assert.AreEqual(_data.Items[index], view.BoundData, $"{context}: {index}번에 바인딩된 항목");
                Assert.IsTrue(view.Active && view.gameObject.activeSelf, $"{context}: {index}번 활성");
                Assert.AreEqual(hasIds, view.HasItemId, $"{context}: HasItemId");
                if (hasIds)
                {
                    Assert.AreEqual((long)_data.Items[index], view.ItemId, $"{context}: {index}번 ItemId");
                }

                float start = Scroller.GetCellStart(index);
                float end = start + Scroller.GetCellSize(index);
                if (Scroller.ScrollDirection == ScrollDirection.Vertical)
                {
                    Assert.AreEqual(-start, view.RectTransform.offsetMax.y, POSITION_EPSILON, $"{context}: {index}번 위치");
                    Assert.AreEqual(-end, view.RectTransform.offsetMin.y, POSITION_EPSILON, $"{context}: {index}번 끝");
                }
                else
                {
                    Assert.AreEqual(start, view.RectTransform.offsetMin.x, POSITION_EPSILON, $"{context}: {index}번 위치");
                    Assert.AreEqual(end, view.RectTransform.offsetMax.x, POSITION_EPSILON, $"{context}: {index}번 끝");
                }
            }
        }

        /// <summary>표시 이벤트 짝: 활성 셀마다 실제 뷰포트에 걸치는지 계산해 IsDisplayed·기록·가상 메서드 호출 수와 비교한다.</summary>
        private void AssertDisplayMatchesViewport(DisplayLog log, string context)
        {
            float viewStart = Scroller.ScrollPosition;
            float viewEnd = viewStart + Scroller.ScrollRectSize;
            int expected = 0;
            IReadOnlyList<CyScrollerCellView> cells = Scroller.ActiveCellViews;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                float start = Scroller.GetScrollPositionForCellViewIndex(view.CellIndex);
                float end = start + Scroller.GetCellSize(view.DataIndex);
                bool overlaps = start < viewEnd && end > viewStart;
                Assert.AreEqual(overlaps, view.IsDisplayed, $"{context}: {view.DataIndex}번 IsDisplayed");
                Assert.AreEqual(overlaps, log.Displayed.Contains(view), $"{context}: {view.DataIndex}번 표시 이벤트 짝");
                Assert.AreEqual(overlaps ? 1 : 0, view.BecameVisibleCount - view.BecameHiddenCount, $"{context}: {view.DataIndex}번 가상 메서드 짝");
                if (overlaps)
                {
                    expected++;
                }
            }

            Assert.AreEqual(expected, log.Displayed.Count, $"{context}: 표시 중인 셀 수");
            Assert.AreEqual(0, log.PairingErrors, $"{context}: 짝 오류");
        }

        /// <summary>표시 이벤트를 셀별로 기록한다. 같은 셀에 표시 시작이 두 번 오거나 짝 없는 표시 끝이 오면 짝 오류로 센다.</summary>
        private sealed class DisplayLog
        {
            public readonly HashSet<CyScrollerCellView> Displayed = new HashSet<CyScrollerCellView>();
            public readonly List<string> Log = new List<string>();
            public int PairingErrors;

            public DisplayLog(CyScroller scroller)
            {
                scroller.CellViewWillDisplay += (_, view) =>
                {
                    if (!view.Active || !view.IsDisplayed || !Displayed.Add(view))
                    {
                        PairingErrors++;
                    }

                    Log.Add("will " + ((TestCellView)view).BoundData);
                };
                scroller.CellViewDidEndDisplay += (_, view) =>
                {
                    if (view.IsDisplayed || !Displayed.Remove(view))
                    {
                        PairingErrors++;
                    }

                    Log.Add("end " + ((TestCellView)view).BoundData);
                };
                scroller.CellViewWillRecycle += view => Log.Add("recycle " + ((TestCellView)view).BoundData);
            }
        }

        private static float RandomSize(System.Random random) => 30f + random.Next(0, 12) * 10f;

        #region Random Model

        /// <summary>
        /// 고정 시드로 삽입·삭제·이동을 섞은 배치(가끔 배치 밖 단일 연산, 배치 중 스크롤)를 여러 번 적용하고, 매번 List 기준 모델과 비교한다:
        /// 활성 셀의 인덱스·바인딩 항목·위치, 활성 범위, 표시 이벤트 짝, 삽입분만 크기를 묻는지,
        /// 배치 전 맨 앞 항목(지워지거나 옮겨졌으면 그 자리에 온 항목) 기준 화면 위치(<see cref="ScreenAnchorModel"/>), 계속 활성인 셀을 다시 바인딩하지 않고 내용 갱신도 부르지 않는지.
        /// </summary>
        [Test]
        public void RandomBatches_MatchListModel()
        {
            RunRandomBatches(false, RANDOM_SEED);
        }

        /// <summary><see cref="RandomBatches_MatchListModel"/>을 ID 제공자 델리게이트로 돌리고 매번 ID → 인덱스 사전(삭제된 ID 포함)과 ID 조회 수도 비교한다.</summary>
        [Test]
        public void RandomBatches_WithItemIds_MatchListModel()
        {
            RunRandomBatches(true, RANDOM_SEED + 1);
        }

        /// <summary>
        /// <see cref="RandomBatches_MatchListModel"/>에 부분 갱신을 섞는다: RefreshCells(끝을 넘는 범위 포함)·RefreshActiveCellViews(int)와
        /// ReloadCellView(크기와 가끔 셀 종류를 바꾼다). 위 비교에 더해 남은 셀은 모은 changeMask로 한 번만, 새로 바인딩한 셀에는 부르지 않는지,
        /// 지웠거나 다시 받은 항목의 옛 셀은 회수 뒤 다른 항목에 다시 쓰였어도 받지 않았는지(셀별 호출 기록),
        /// 다시 받은 항목만 크기를 다시 묻고 셀을 다시 바인딩하는지, 셀 식별자가 항목에 맞는지 본다.
        /// </summary>
        [Test]
        public void RandomBatches_WithContentUpdates_MatchListModel()
        {
            RunRandomBatches(false, RANDOM_SEED + 2, true);
        }

        /// <summary><see cref="RandomBatches_WithContentUpdates_MatchListModel"/>을 ID 제공자 델리게이트로 돌린다 (다시 받은 항목만 ID를 묻는지, 사전이 맞는지).</summary>
        [Test]
        public void RandomBatches_WithContentUpdatesAndItemIds_MatchListModel()
        {
            RunRandomBatches(true, RANDOM_SEED + 3, true);
        }

        private void RunRandomBatches(bool ids, int seed, bool contentUpdates = false)
        {
            var random = new System.Random(seed);
            Create(120, 80f, ids: ids, spacing: 4f, padding: new RectOffset(0, 0, 12, 18), reload: false);
            for (int i = 0; i < _data.Sizes.Count; i++)
            {
                _data.Sizes[i] = RandomSize(random);
            }

            if (contentUpdates)
            {
                _data.AltPrefab = _fixture.AltPrefab;
            }

            Scroller.LookAheadBefore = 60f;
            Scroller.LookAheadAfter = 90f;
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            var idData = _data as ListIdTestDelegate;
            var insertedItems = new HashSet<int>();
            var removedItems = new List<int>();
            var oldItems = new List<int>();
            var recorded = new List<RecordedOp>();
            var activeBefore = new Dictionary<int, TestCellView>();
            var versionsBefore = new Dictionary<int, int>();
            var refreshCountsBefore = new Dictionary<int, int>();
            var refreshHistoryBefore = new Dictionary<int, int>();
            var reloadedItems = new HashSet<int>();
            var expectedMasks = new Dictionary<int, int>();
            var anchorModel = new ScreenAnchorModel();

            for (int step = 0; step < RANDOM_STEPS; step++)
            {
                if (random.Next(3) == 0)
                {
                    Scroller.ScrollPosition = (float)(random.NextDouble() * Scroller.ScrollSize);
                }

                oldItems.Clear();
                oldItems.AddRange(_data.Items);
                RecordActiveCells(activeBefore, versionsBefore);
                RecordRefreshCounts(refreshCountsBefore, refreshHistoryBefore);
                int sizeCalls = _data.GetCellViewSizeCalls;
                int bindCalls = _data.GetCellViewCalls;
                int idCalls = idData != null ? idData.GetItemIdCalls : 0;
                insertedItems.Clear();
                removedItems.Clear();
                recorded.Clear();
                reloadedItems.Clear();
                expectedMasks.Clear();
                bool batch = random.Next(3) != 0;
                int operations = batch ? random.Next(1, 6) : 1;

                // 단일 연산은 바로 적용되므로 지금 앵커가 적용 직전 앵커다. 배치는 배치 중 스크롤 뒤 EndUpdates 직전에 적는다.
                CyScrollerAnchor anchor = Scroller.CaptureAnchor();
                if (batch)
                {
                    Scroller.BeginUpdates();
                }

                for (int op = 0; op < operations; op++)
                {
                    int count = _data.Items.Count;
                    if (contentUpdates && random.Next(3) == 0)
                    {
                        ApplyRandomContentUpdate(random, recorded, reloadedItems, expectedMasks);
                        if (batch && random.Next(6) == 0)
                        {
                            Scroller.ScrollPosition += random.Next(-120, 121);
                        }

                        continue;
                    }

                    int kind = random.Next(count > 160 ? 2 : 3);
                    if (count < 8 || kind == 2)
                    {
                        int at = random.Next(count + 1);
                        int added = random.Next(1, 4);
                        var items = new int[added];
                        for (int j = 0; j < added; j++)
                        {
                            int item = NextItem();
                            items[j] = item;
                            insertedItems.Add(item);
                            _data.Insert(at + j, item, RandomSize(random));
                        }

                        recorded.Add(new RecordedOp { Kind = 0, A = at, B = added, Items = items });
                        Scroller.InsertCells(at, added);
                    }
                    else if (kind == 0)
                    {
                        int at = random.Next(count);
                        int removed = random.Next(1, Mathf.Min(3, count - at) + 1);
                        removedItems.AddRange(_data.Items.GetRange(at, removed));
                        _data.RemoveRange(at, removed);
                        recorded.Add(new RecordedOp { Kind = 1, A = at, B = removed });
                        Scroller.RemoveCells(at, removed);
                    }
                    else
                    {
                        int from = random.Next(count);
                        int to = random.Next(count);
                        _data.Move(from, to);
                        recorded.Add(new RecordedOp { Kind = 2, A = from, B = to });
                        Scroller.MoveCell(from, to);
                    }

                    if (batch && random.Next(6) == 0)
                    {
                        // 배치 중 스크롤: 배치 전 상태로 옮기기만 하고 셀은 배치 끝에 맞춘다.
                        Scroller.ScrollPosition += random.Next(-120, 121);
                    }
                }

                if (batch)
                {
                    anchor = Scroller.CaptureAnchor();
                    Scroller.EndUpdates();
                }

                // 크기(와 ID)를 묻는 항목: 남은 삽입분과 남은 다시 받은 항목 (같은 배치에 삽입한 뒤 다시 받았으면 한 번).
                int survived = 0;
                foreach (int item in insertedItems)
                {
                    if (_data.Items.Contains(item))
                    {
                        survived++;
                    }
                }

                foreach (int item in reloadedItems)
                {
                    if (!insertedItems.Contains(item) && _data.Items.Contains(item))
                    {
                        survived++;
                    }
                }

                // 갱신만 있는 배치는 배치 중 미룬 범위 갱신보다 먼저 활성 셀에 부른다 (그 뒤 범위를 벗어난 셀은 회수 전에 받았을 수 있다).
                bool refreshOnlyBatch = batch;
                for (int i = 0; i < recorded.Count; i++)
                {
                    refreshOnlyBatch &= recorded[i].Kind == 3;
                }

                string context = $"step {step} ({(batch ? "batch" : "single")})";
                Assert.AreEqual(survived, _data.GetCellViewSizeCalls - sizeCalls, $"{context}: 남은 삽입분·다시 받은 항목만 크기를 묻는다");
                AssertMatchesData(context);
                AssertDisplayMatchesViewport(log, context);
                AssertScreenAnchorKept(anchorModel, oldItems, recorded, in anchor, context);
                AssertNoRebinding(activeBefore, versionsBefore, bindCalls, context, reloadedItems);
                AssertRefreshCalls(activeBefore, refreshCountsBefore, refreshHistoryBefore, reloadedItems, expectedMasks, refreshOnlyBatch, context);
                if (contentUpdates)
                {
                    AssertIdentifiersMatchItems(context);
                }

                if (idData != null)
                {
                    Assert.AreEqual(survived, idData.GetItemIdCalls - idCalls, $"{context}: 남은 삽입분·다시 받은 항목만 ID를 묻는다");
                    AssertIdLookups(idData, context);
                    for (int i = 0; i < removedItems.Count; i++)
                    {
                        Assert.AreEqual(-1, Scroller.FindDataIndexForItemId(removedItems[i]), $"{context}: 지운 항목 {removedItems[i]} ID");
                    }
                }
            }
        }

        /// <summary>무작위 대조에서 적은 연산 (Kind 0 삽입·1 삭제·2 이동·3 내용 갱신·4 다시 받기). 화면 앵커 모델에 다시 적용한다.</summary>
        private struct RecordedOp
        {
            public int Kind;
            public int A;
            public int B;
            public int[] Items;
        }

        /// <summary>
        /// 무작위 부분 갱신 하나. 셋에 하나는 다시 받기(크기를 바꾸고 둘에 하나는 셀 종류도 바꾼다), 나머지는 내용 갱신이다
        /// (가끔 끝을 넘는 범위, 여덟에 하나는 RefreshActiveCellViews(int)). 그 시점 데이터로 항목마다 기대 changeMask를 OR로 모은다.
        /// </summary>
        private void ApplyRandomContentUpdate(System.Random random, List<RecordedOp> recorded, HashSet<int> reloadedItems, Dictionary<int, int> expectedMasks)
        {
            int count = _data.Items.Count;
            if (random.Next(3) == 0)
            {
                int at = PickContentIndex(random, count);
                int item = _data.Items[at];
                _data.Sizes[at] = RandomSize(random);
                if (random.Next(2) == 0 && !_data.AltItems.Remove(item))
                {
                    _data.AltItems.Add(item);
                }

                reloadedItems.Add(item);
                recorded.Add(new RecordedOp { Kind = 4, A = at, B = 1 });
                Scroller.ReloadCellView(at);
                return;
            }

            int mask = 1 << random.Next(0, 6);
            int start;
            int length;
            if (random.Next(8) == 0)
            {
                start = 0;
                length = count;
                Scroller.RefreshActiveCellViews(mask);
            }
            else
            {
                start = PickContentIndex(random, count);
                int requested = random.Next(1, 9);
                length = Mathf.Min(requested, count - start);
                Scroller.RefreshCells(start, requested, mask);
            }

            for (int i = start; i < start + length; i++)
            {
                int item = _data.Items[i];
                expectedMasks.TryGetValue(item, out int accumulated);
                expectedMasks[item] = accumulated | mask;
            }

            recorded.Add(new RecordedOp { Kind = 3, A = start, B = length });
        }

        /// <summary>
        /// 부분 갱신할 인덱스. 반은 지금 활성 범위(배치 중이면 배치 전 범위)에서 골라 남은 셀의 내용 갱신·다시 받기를 자주 거치게 하고, 나머지는 전체에서 고른다.
        /// </summary>
        private int PickContentIndex(System.Random random, int count)
        {
            int first = Scroller.StartDataIndex;
            if (first >= 0 && random.Next(2) == 0)
            {
                int span = Scroller.EndDataIndex - first + 1;
                return Mathf.Clamp(first + random.Next(span), 0, count - 1);
            }

            return random.Next(count);
        }

        /// <summary>활성 셀을 항목 값 → RefreshCellView(int) 호출 수, 항목 값 → 호출 기록(<see cref="TestCellView.RefreshHistory"/>) 길이로 적는다.</summary>
        private void RecordRefreshCounts(Dictionary<int, int> counts, Dictionary<int, int> historyLengths)
        {
            counts.Clear();
            historyLengths.Clear();
            IReadOnlyList<CyScrollerCellView> active = Scroller.ActiveCellViews;
            for (int i = 0; i < active.Count; i++)
            {
                var view = (TestCellView)active[i];
                if (view != null)
                {
                    counts[view.BoundData] = view.MaskedRefreshCount;
                    historyLengths[view.BoundData] = view.RefreshHistory.Count;
                }
            }
        }

        /// <summary>
        /// 남은 셀(이번 단계 전후로 활성이고 다시 받지 않은 항목)은 모은 changeMask가 있으면 그 값으로 한 번만 받고, 없으면 받지 않았는지.
        /// 새로 바인딩한 셀(새로 활성 범위에 들어온 항목, 삽입·다시 받은 항목)은 바인딩 뒤에 받지 않았는지.
        /// 단계 전에 활성이던 셀은 바인딩해도 지우지 않는 호출 기록으로도 본다: 지웠거나 다시 받은 항목의 옛 셀은 회수 뒤 다른 항목에 다시 쓰였어도
        /// 이번 단계에 한 번도 받지 않았고, 나머지는 받았다면 그 항목으로 모은 값을 받았는지. 활성 범위를 벗어난 셀은 받지 않는다
        /// (갱신만 있는 배치(refreshOnlyBatch)는 배치 중 미룬 범위 갱신보다 먼저 부르므로 회수 전에 한 번 받았을 수 있다).
        /// </summary>
        private void AssertRefreshCalls(Dictionary<int, TestCellView> cellsBefore, Dictionary<int, int> countsBefore, Dictionary<int, int> historyBefore,
            HashSet<int> reloaded, Dictionary<int, int> expectedMasks, bool refreshOnlyBatch, string context)
        {
            foreach (KeyValuePair<int, TestCellView> pair in cellsBefore)
            {
                int item = pair.Key;
                TestCellView cell = pair.Value;
                int from = historyBefore[item];
                int received = cell.RefreshHistory.Count - from;
                bool removed = !_data.Items.Contains(item);
                if (removed || reloaded.Contains(item))
                {
                    Assert.AreEqual(0, received, $"{context}: {(removed ? "지운" : "다시 받은")} 항목 {item}의 옛 셀은 회수 전에도 내용 갱신을 받지 않는다");
                    continue;
                }

                expectedMasks.TryGetValue(item, out int mask);
                int expected = mask != 0 ? 1 : 0;
                if (ReferenceEquals(CellForItem(item), cell))
                {
                    Assert.AreEqual(expected, received, $"{context}: 남은 항목 {item}의 셀이 받은 내용 갱신 수 (기록, 마스크 {mask})");
                }
                else
                {
                    Assert.LessOrEqual(received, refreshOnlyBatch ? expected : 0, $"{context}: 활성 범위를 벗어난 항목 {item}의 옛 셀이 받은 내용 갱신 수 (마스크 {mask})");
                }

                for (int k = from; k < cell.RefreshHistory.Count; k++)
                {
                    Assert.AreEqual(item, cell.RefreshHistory[k].Item, $"{context}: 항목 {item}의 셀은 그 항목에 바인딩된 채로만 받는다");
                    Assert.AreEqual(mask, cell.RefreshHistory[k].Mask, $"{context}: 항목 {item}이 받은 changeMask (기록)");
                }
            }

            IReadOnlyList<CyScrollerCellView> active = Scroller.ActiveCellViews;
            for (int i = 0; i < active.Count; i++)
            {
                var view = (TestCellView)active[i];
                int item = view.BoundData;
                if (!reloaded.Contains(item) && cellsBefore.ContainsKey(item))
                {
                    expectedMasks.TryGetValue(item, out int mask);
                    Assert.AreEqual(mask != 0 ? 1 : 0, view.MaskedRefreshCount - countsBefore[item], $"{context}: 남은 항목 {item} 내용 갱신 횟수 (마스크 {mask})");
                    if (mask != 0)
                    {
                        Assert.AreEqual(mask, view.LastChangeMask, $"{context}: 남은 항목 {item}이 받은 changeMask");
                    }
                }
                else
                {
                    Assert.AreEqual(0, view.MaskedRefreshCount, $"{context}: 새로 바인딩한 항목 {item}에는 내용 갱신을 부르지 않는다");
                }
            }
        }

        /// <summary>활성 셀마다 셀 식별자가 그 항목을 지금 바인딩할 때 고를 값과 같은지 (다시 받기로 셀 종류를 바꾼 항목 포함).</summary>
        private void AssertIdentifiersMatchItems(string context)
        {
            IReadOnlyList<CyScrollerCellView> active = Scroller.ActiveCellViews;
            for (int i = 0; i < active.Count; i++)
            {
                var view = (TestCellView)active[i];
                Assert.AreEqual(_data.ExpectedIdentifier(view.BoundData), view.CellIdentifier, $"{context}: 항목 {view.BoundData} 셀 식별자");
            }
        }

        /// <summary>활성 셀을 항목 값 → 셀, 항목 값 → BindVersion으로 적는다.</summary>
        private void RecordActiveCells(Dictionary<int, TestCellView> cells, Dictionary<int, int> versions)
        {
            cells.Clear();
            versions.Clear();
            IReadOnlyList<CyScrollerCellView> active = Scroller.ActiveCellViews;
            for (int i = 0; i < active.Count; i++)
            {
                var view = (TestCellView)active[i];
                if (view != null)
                {
                    cells[view.BoundData] = view;
                    versions[view.BoundData] = view.BindVersion;
                }
            }
        }

        /// <summary>
        /// 계속 활성인 항목은 같은 셀이 다시 바인딩 없이(BindVersion 그대로) 들고 있고, 델리게이트 GetCellView는 새로 활성 범위에 들어온 항목 수만큼만 불렸는지.
        /// 다시 받은 항목(reloaded)은 계속 활성이어도 다시 바인딩한다 (같은 셀을 풀에서 다시 받았으면 BindVersion이 달라진다).
        /// </summary>
        private void AssertNoRebinding(Dictionary<int, TestCellView> cellsBefore, Dictionary<int, int> versionsBefore, int bindCalls, string context,
            HashSet<int> reloaded = null)
        {
            int newlyBound = 0;
            IReadOnlyList<CyScrollerCellView> active = Scroller.ActiveCellViews;
            for (int i = 0; i < active.Count; i++)
            {
                var view = (TestCellView)active[i];
                bool wasActive = cellsBefore.TryGetValue(view.BoundData, out TestCellView previous);
                if (reloaded != null && reloaded.Contains(view.BoundData))
                {
                    if (wasActive && ReferenceEquals(previous, view))
                    {
                        Assert.AreNotEqual(versionsBefore[view.BoundData], view.BindVersion, $"{context}: 다시 받은 항목 {view.BoundData}은 다시 바인딩한다");
                    }

                    Assert.AreEqual(view.BindVersion, view.BoundVersion, $"{context}: 다시 받은 항목 {view.BoundData}은 델리게이트가 바인딩했다");
                    newlyBound++;
                }
                else if (wasActive)
                {
                    Assert.AreSame(previous, view, $"{context}: 계속 활성인 항목 {view.BoundData}은 같은 셀");
                    Assert.AreEqual(versionsBefore[view.BoundData], view.BindVersion, $"{context}: 항목 {view.BoundData} 다시 바인딩하지 않음");
                }
                else
                {
                    newlyBound++;
                }
            }

            Assert.AreEqual(newlyBound, _data.GetCellViewCalls - bindCalls, $"{context}: 새로 활성 범위에 들어온 항목(과 다시 받은 항목)만 바인딩한다");
        }

        /// <summary>
        /// 적용 직전 앵커(배치 전 인덱스)를 <see cref="ScreenAnchorModel"/>에 넣고 적은 연산을 다시 적용해 새 맨 앞 항목을 구한 뒤,
        /// 그 항목이 같은 거리로 뷰포트 시작에 왔는지(스크롤 범위로 자른 값) 본다. 뒤가 모두 지워졌으면 끝이다.
        /// </summary>
        private void AssertScreenAnchorKept(ScreenAnchorModel model, List<int> oldItems, List<RecordedOp> ops, in CyScrollerAnchor anchor, string context)
        {
            if (anchor.DataIndex < 0)
            {
                return;
            }

            model.Reset(oldItems, anchor.DataIndex);
            for (int i = 0; i < ops.Count; i++)
            {
                RecordedOp op = ops[i];
                switch (op.Kind)
                {
                    case 0:
                        model.Insert(op.A, op.Items);
                        break;
                    case 1:
                        model.Remove(op.A, op.B);
                        break;
                    case 2:
                        model.Move(op.A, op.B);
                        break;
                    default:
                        // 내용 갱신·다시 받기는 항목을 옮기지 않는다 (다시 받은 크기는 스크롤러 레이아웃이 반영한다).
                        break;
                }
            }

            model.AssertItems(_data.Items, context);
            int index = model.AnchorIndex;
            float expected = index < _data.Items.Count
                ? Mathf.Clamp(Scroller.GetCellStart(index) + anchor.Offset, 0f, Scroller.ScrollSize)
                : Scroller.ScrollSize;
            Assert.AreEqual(expected, Scroller.ScrollPosition, POSITION_EPSILON,
                $"{context}: 화면 위치 (앵커 {oldItems[anchor.DataIndex]} @{anchor.DataIndex} + {anchor.Offset} → {index}번{(model.Attached ? "" : ", 그 자리에 온 항목")})");
        }

        /// <summary>
        /// 화면 앵커 기준 모델. 배치 전 맨 앞 항목 바로 앞에 표지를 두고 연산을 목록에 그대로 적용한다.
        /// 표지는 그 항목이 제자리에 있는 동안 항목에 붙어 다니고(그 항목 자리 삽입은 표지 앞에 들어간다), 항목이 지워지거나 옮겨지면 그 자리에 남는다.
        /// 남은 표지 자리에 삽입한 항목은 표지 뒤에 들어가 자리를 채운다. 끝에 표지 앞 항목 수가 새 맨 앞 항목 인덱스다 (표지 뒤가 비었으면 개수).
        /// </summary>
        private sealed class ScreenAnchorModel
        {
            private const int MARKER = int.MinValue;
            private readonly List<int> _track = new List<int>();
            private int _anchorItem;

            /// <summary>앵커 항목이 아직 제자리에 있는지 (표지가 항목에 붙어 있는지).</summary>
            public bool Attached { get; private set; }

            public int AnchorIndex => _track.IndexOf(MARKER);

            public void Reset(List<int> items, int anchorIndex)
            {
                _track.Clear();
                _track.AddRange(items);
                _track.Insert(anchorIndex, MARKER);
                _anchorItem = items[anchorIndex];
                Attached = true;
            }

            public void Insert(int index, IReadOnlyList<int> items)
            {
                int at = ToInsertPosition(index);
                for (int i = 0; i < items.Count; i++)
                {
                    _track.Insert(at + i, items[i]);
                }
            }

            public void Remove(int index, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    int position = ToTrackPosition(index);
                    if (_track[position] == _anchorItem)
                    {
                        Attached = false;
                    }

                    _track.RemoveAt(position);
                }
            }

            public void Move(int from, int to)
            {
                int position = ToTrackPosition(from);
                int item = _track[position];
                if (item == _anchorItem)
                {
                    Attached = false;
                }

                _track.RemoveAt(position);
                _track.Insert(ToInsertPosition(to), item);
            }

            /// <summary>표지를 뺀 목록이 기준 데이터와 같은지 (모델 자체 확인).</summary>
            public void AssertItems(List<int> expected, string context)
            {
                var items = new List<int>(_track);
                items.Remove(MARKER);
                CollectionAssert.AreEqual(expected, items, $"{context}: 앵커 모델 목록");
            }

            /// <summary>표지를 뺀 index번 항목의 목록 위치.</summary>
            private int ToTrackPosition(int index) => index < AnchorIndex ? index : index + 1;

            /// <summary>표지를 뺀 index 자리에 넣을 목록 위치. 표지 자리면 붙어 있을 때 표지 앞(항목 앞), 떨어졌으면 표지 뒤(자리 채움).</summary>
            private int ToInsertPosition(int index)
            {
                int marker = AnchorIndex;
                if (index != marker)
                {
                    return index < marker ? index : index + 1;
                }

                return Attached ? marker : marker + 1;
            }
        }

        #endregion

        #region Position And Cells

        [Test]
        public void InsertAndRemoveAboveViewport_ShiftPositionAndKeepScreen()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;   // 뷰포트 [1050, 1450]: 10~14번

            var items = new List<int>();
            var offsets = new List<float>();
            var cells = new List<TestCellView>();
            for (int index = 10; index <= 14; index++)
            {
                int item = _data.Items[index];
                items.Add(item);
                offsets.Add(ScreenOffsetOfItem(item));
                cells.Add(CellForItem(item));
            }

            int calls = _data.GetCellViewCalls;

            // 2번 앞에 50짜리 3개: 맨 앞 항목(10번)이 150 밀린 만큼 위치를 옮긴다.
            for (int j = 0; j < 3; j++)
            {
                _data.Insert(2 + j, NextItem(), 50f);
            }

            Scroller.InsertCells(2, 3);
            Assert.AreEqual(1200f, Scroller.ScrollPosition, EPSILON);
            AssertSameScreen(items, offsets, cells, "insert above");
            Assert.AreEqual(calls, _data.GetCellViewCalls, "보이던 셀은 다시 바인딩하지 않는다");
            AssertMatchesData("insert above");

            // 맨 앞 4개(100 + 100 + 50 + 50) 삭제: 300 당긴다.
            _data.RemoveRange(0, 4);
            Scroller.RemoveCells(0, 4);
            Assert.AreEqual(900f, Scroller.ScrollPosition, EPSILON);
            AssertSameScreen(items, offsets, cells, "remove above");
            Assert.AreEqual(calls, _data.GetCellViewCalls);
            AssertMatchesData("remove above");
        }

        private void AssertSameScreen(List<int> items, List<float> offsets, List<TestCellView> cells, string context)
        {
            for (int i = 0; i < items.Count; i++)
            {
                Assert.AreEqual(offsets[i], ScreenOffsetOfItem(items[i]), EPSILON, $"{context}: 항목 {items[i]} 화면 위치");
                Assert.AreSame(cells[i], CellForItem(items[i]), $"{context}: 항목 {items[i]} 같은 셀");
                Assert.AreEqual(cells[i].BoundVersion, cells[i].BindVersion, $"{context}: 항목 {items[i]} 바인딩 유지");
            }
        }

        [Test]
        public void Horizontal_InsertAndMoveBeforeViewport_KeepScreen()
        {
            Create(60, 80f, spacing: 6f, padding: new RectOffset(20, 10, 0, 0), direction: ScrollDirection.Horizontal);
            Scroller.ScrollPosition = 900f;
            int top = _data.Items[Scroller.CaptureAnchor().DataIndex];
            float screen = ScreenOffsetOfItem(top);

            Scroller.BeginUpdates();
            _data.Insert(1, NextItem(), 120f);
            Scroller.InsertCells(1, 1);
            _data.Move(40, 0);
            Scroller.MoveCell(40, 0);
            Scroller.EndUpdates();

            Assert.AreEqual(900f + 120f + 6f + 80f + 6f, Scroller.ScrollPosition, EPSILON, "앞쪽 삽입(120)과 뒤에서 앞으로 옮긴 항목(80)만큼");
            Assert.AreEqual(screen, ScreenOffsetOfItem(top), EPSILON);
            AssertMatchesData("horizontal");
        }

        [Test]
        public void DisplayCallbackMovingContentDuringApply_IsRealignedAfterwards()
        {
            Create(100, reload: false);
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            Scroller.ScrollPosition = 1000f;
            bool moved = false;
            Scroller.CellViewWillDisplay += (scroller, view) =>
            {
                if (!moved)
                {
                    moved = true;
                    scroller.ScrollPosition = 5000f;
                }
            };

            // 뷰포트 안 삽입: 새 셀이 표시를 시작할 때 콜백이 콘텐츠를 옮긴다.
            _data.Insert(11, NextItem(), 100f);
            Scroller.InsertCells(11, 1);

            Assert.IsTrue(moved);
            Assert.AreEqual(5000f, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("moved during apply");
            AssertDisplayMatchesViewport(log, "moved during apply");
        }

        [Test]
        public void RemoveFirstVisibleItem_NextItemTakesItsPlace()
        {
            Create(100);
            Scroller.ScrollPosition = 1030f;   // 맨 앞 10번, 오프셋 30
            int next = _data.Items[11];
            _data.RemoveRange(10, 1);
            Scroller.RemoveCells(10, 1);

            // 그 자리의 다음 항목(11번 → 10번)이 같은 오프셋으로 맨 앞에 온다.
            Assert.AreEqual(1030f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(10, _data.Items.IndexOf(next));
            AssertMatchesData("remove first visible");

            // 맨 앞 항목부터 끝까지 지우면 새 끝으로 간다.
            Scroller.ScrollPosition = Scroller.ScrollSize;
            int top = Scroller.CaptureAnchor().DataIndex;
            int removed = _data.Items.Count - top;
            _data.RemoveRange(top, removed);
            Scroller.RemoveCells(top, removed);
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("remove tail");
        }

        [Test]
        public void MoveFirstVisibleItemBack_ScreenDoesNotFollowItem()
        {
            Create(100);
            Scroller.ScrollPosition = 1000f;   // 뷰포트 [1000, 1400]: 10~13번
            int moved = _data.Items[10];
            TestCellView movedCell = CellForItem(moved);
            int recycled = movedCell.RecycledCount;
            var items = new List<int>();
            var cells = new List<TestCellView>();
            for (int index = 11; index <= 13; index++)
            {
                items.Add(_data.Items[index]);
                cells.Add(CellForItem(_data.Items[index]));
            }

            int calls = _data.GetCellViewCalls;

            _data.Move(10, 90);
            Scroller.MoveCell(10, 90);

            // 화면은 옮긴 항목(90번)을 따라가지 않는다. 그 자리에 온 다음 항목(옛 11번)이 같은 거리(0)에 온다.
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON, "맨 앞 항목을 옮겨도 화면이 그 항목으로 튀지 않는다");
            Assert.AreEqual(90, _data.Items.IndexOf(moved));
            Assert.AreEqual(0f, ScreenOffsetOfItem(items[0]), EPSILON, "그 자리에 온 다음 항목이 맨 앞");
            for (int i = 0; i < items.Count; i++)
            {
                Assert.AreSame(cells[i], CellForItem(items[i]), $"항목 {items[i]} 같은 셀");
                Assert.AreEqual(cells[i].BoundVersion, cells[i].BindVersion, $"항목 {items[i]} 바인딩 유지");
            }

            Assert.AreEqual(recycled + 1, movedCell.RecycledCount, "활성 범위 밖으로 옮긴 항목의 셀은 회수한다");
            Assert.AreEqual(calls + 1, _data.GetCellViewCalls, "뷰포트 끝에 새로 들어온 항목(옛 14번)만 바인딩한다");
            AssertMatchesData("move first visible back");

            // 맨 앞 항목 안쪽(오프셋 30)이어도 그 자리에 온 항목이 같은 거리에 온다.
            Scroller.ScrollPosition = 2030f;   // 20번 안 30
            int next = _data.Items[21];
            _data.Move(20, 95);
            Scroller.MoveCell(20, 95);
            Assert.AreEqual(2030f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(-30f, ScreenOffsetOfItem(next), EPSILON);
            AssertMatchesData("move partially visible first item back");
        }

        [Test]
        public void MoveFirstVisibleItemToFront_ScreenDoesNotJumpToTop()
        {
            Create(100, reload: false);
            _data.Sizes[10] = 150f;
            Scroller.ReloadData();
            Scroller.ScrollPosition = 1030f;   // 10번(150) 안 30. 뷰포트 [1030, 1430]: 10~13번
            int moved = _data.Items[10];
            int next = _data.Items[11];
            TestCellView nextCell = CellForItem(next);
            int nextVersion = nextCell.BindVersion;

            // 메신저 목록처럼 보던 맨 앞 항목을 맨 위(0번)로 올린다.
            _data.Move(10, 0);
            Scroller.MoveCell(10, 0);

            // 목록 맨 위로 튀지 않는다. 그 자리에서 지우고 0번에 삽입한 것과 같아, 맨 앞 자리 앞에 들어간 150만큼 보정하고
            // 그 자리에 온 다음 항목(옛 11번)이 같은 거리(30)에 온다.
            Assert.AreEqual(0, _data.Items.IndexOf(moved));
            Assert.AreEqual(150f + 1000f + 30f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(-30f, ScreenOffsetOfItem(next), EPSILON);
            Assert.AreSame(nextCell, CellForItem(next));
            Assert.AreEqual(nextVersion, nextCell.BindVersion);
            AssertMatchesData("move first visible to front");
        }

        [Test]
        public void Batch_RemoveFirstVisibleThenInsertAtSamePlace_NewItemFillsPlace()
        {
            Create(100);
            Scroller.ScrollPosition = 1000f;   // 10~13번
            var items = new List<int>();
            var offsets = new List<float>();
            var cells = new List<TestCellView>();
            for (int index = 11; index <= 13; index++)
            {
                int item = _data.Items[index];
                items.Add(item);
                offsets.Add(ScreenOffsetOfItem(item));
                cells.Add(CellForItem(item));
            }

            int calls = _data.GetCellViewCalls;

            // 맨 앞 항목을 같은 크기의 새 항목으로 바꾼다.
            int replacement = NextItem();
            Scroller.BeginUpdates();
            _data.RemoveRange(10, 1);
            Scroller.RemoveCells(10, 1);
            _data.Insert(10, replacement, 100f);
            Scroller.InsertCells(10, 1);
            Scroller.EndUpdates();

            // 비운 자리에 삽입한 항목이 그 자리를 채운다: 위치와 남은 셀의 화면 위치가 그대로다.
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(0f, ScreenOffsetOfItem(replacement), EPSILON);
            AssertSameScreen(items, offsets, cells, "replace first visible");
            Assert.AreEqual(calls + 1, _data.GetCellViewCalls, "새 항목만 바인딩한다");
            AssertMatchesData("replace first visible");

            // 맨 앞 항목이 제자리에 있으면 그 자리 삽입은 그 항목 앞(뷰포트 위)에 들어가고 화면은 그 항목을 지킨다.
            _data.Insert(10, NextItem(), 100f);
            Scroller.InsertCells(10, 1);
            Assert.AreEqual(1100f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(0f, ScreenOffsetOfItem(replacement), EPSILON);
            AssertMatchesData("insert at first visible");
        }

        [Test]
        public void Batch_RemoveTailThenAppendPastCapacity_KeepsLayout()
        {
            Create(50);   // 첫 로드라 레이아웃 버퍼가 50개로 딱 맞다
            Scroller.ScrollPosition = 1000f;

            // 무한 스크롤: 끝의 로딩 셀을 지우고 그 자리에 다음 페이지 20개를 붙인다 (버퍼가 늘어난다).
            Scroller.BeginUpdates();
            _data.RemoveRange(49, 1);
            Scroller.RemoveCells(49, 1);
            for (int j = 0; j < 20; j++)
            {
                _data.Insert(49 + j, NextItem(), 100f);
            }

            Scroller.InsertCells(49, 20);
            Scroller.EndUpdates();

            Assert.AreEqual(69, Scroller.NumberOfCells);
            Assert.AreEqual(4900f, Scroller.GetCellStart(49), EPSILON, "붙인 첫 항목은 남은 항목 뒤에서 시작한다");
            Assert.AreEqual(6900f, Scroller.ContentSize, EPSILON);
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("after page append");

            Scroller.ScrollPosition = Scroller.ScrollSize;
            Assert.AreEqual(6500f, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("scrolled to end");
        }

        [Test]
        public void InsertInsideViewport_BindsOnlyNewItemAndKeepsOtherCells()
        {
            Create(100, reload: false);
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            Scroller.ScrollPosition = 1000f;   // 10~13번
            TestCellView first = CellForItem(_data.Items[10]);
            TestCellView second = CellForItem(_data.Items[11]);
            TestCellView third = CellForItem(_data.Items[12]);
            int pushedOut = _data.Items[13];
            TestCellView pushedOutCell = CellForItem(pushedOut);
            int recycled = pushedOutCell.RecycledCount;
            int calls = _data.GetCellViewCalls;
            log.Log.Clear();

            int added = NextItem();
            _data.Insert(11, added, 100f);
            Scroller.InsertCells(11, 1);

            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON, "맨 앞 항목 뒤 삽입은 위치를 옮기지 않는다");
            Assert.AreEqual(calls + 1, _data.GetCellViewCalls, "새 항목만 바인딩한다");
            Assert.AreSame(first, CellForItem(_data.Items[10]));
            Assert.AreEqual(0, first.DataIndexChangedCount);
            Assert.AreEqual(12, second.DataIndex);
            Assert.AreEqual(1, second.DataIndexChangedCount);
            Assert.AreEqual(11, second.LastPreviousDataIndex);
            Assert.AreEqual(13, third.DataIndex);

            // 밀려난 셀은 회수됐다가 (풀에서) 새 항목 자리에 다시 쓰였다.
            Assert.AreEqual(recycled + 1, pushedOutCell.RecycledCount, "뷰포트 밖으로 밀린 셀은 회수한다");
            Assert.IsNull(CellForItem(pushedOut));
            Assert.AreEqual(added, pushedOutCell.BoundData);
            CollectionAssert.AreEqual(new[] { "end " + pushedOut, "recycle " + pushedOut, "will " + added }, log.Log,
                "표시 끝 → 회수 → 새 셀 표시 시작. 계속 보이는 셀에는 표시 이벤트가 없다");
            AssertMatchesData("insert inside");
            AssertDisplayMatchesViewport(log, "insert inside");
        }

        [Test]
        public void RemoveVisibleCell_EndsDisplayBeforeRecycle()
        {
            Create(100, reload: false);
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            Scroller.ScrollPosition = 1000f;   // 10~13번
            int removed = _data.Items[11];
            TestCellView removedCell = CellForItem(removed);
            int version = removedCell.BindVersion;
            int recycled = removedCell.RecycledCount;
            int hidden = removedCell.BecameHiddenCount;
            int calls = _data.GetCellViewCalls;
            log.Log.Clear();

            _data.RemoveRange(11, 1);
            Scroller.RemoveCells(11, 1);

            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
            Assert.IsNull(CellForItem(removed));
            Assert.AreEqual(recycled + 1, removedCell.RecycledCount);
            Assert.AreEqual(hidden + 1, removedCell.BecameHiddenCount);

            // 회수(+1)한 셀을 풀에서 꺼내 뷰포트 끝에 새로 들어온 항목에 바인딩했다(+1).
            Assert.AreEqual(version + 2, removedCell.BindVersion);
            Assert.AreEqual(_data.Items[13], removedCell.BoundData);
            Assert.Less(log.Log.IndexOf("end " + removed), log.Log.IndexOf("recycle " + removed), "표시 끝이 회수보다 먼저");
            Assert.AreEqual(calls + 1, _data.GetCellViewCalls, "뷰포트 끝에 새로 들어온 자리만 바인딩한다");
            AssertMatchesData("remove visible");
            AssertDisplayMatchesViewport(log, "remove visible");
        }

        [Test]
        public void MoveCell_NotifiesIndexChangeWithoutRebinding()
        {
            Create(100, reload: false);
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            Scroller.ScrollPosition = 1000f;   // 10~13번
            TestCellView a = CellForItem(_data.Items[11]);
            TestCellView b = CellForItem(_data.Items[12]);
            TestCellView c = CellForItem(_data.Items[13]);
            int versionA = a.BindVersion;
            int calls = _data.GetCellViewCalls;
            log.Log.Clear();

            // [10, a, b, c] → [10, b, c, a]
            _data.Move(11, 13);
            Scroller.MoveCell(11, 13);

            Assert.AreEqual(calls, _data.GetCellViewCalls, "다시 바인딩하지 않는다");
            Assert.AreEqual(13, a.DataIndex);
            Assert.AreEqual(11, a.LastPreviousDataIndex);
            Assert.AreEqual(11, b.DataIndex);
            Assert.AreEqual(12, b.LastPreviousDataIndex);
            Assert.AreEqual(12, c.DataIndex);
            Assert.AreEqual(13, c.LastPreviousDataIndex);
            Assert.AreEqual(1, a.DataIndexChangedCount);
            Assert.AreEqual(versionA, a.BindVersion, "BindVersion 유지");
            Assert.AreEqual(-1100f, a.OffsetMaxYAtIndexChange, EPSILON, "알림을 받을 때는 아직 옛 자리다");
            Assert.AreEqual(-1300f, a.RectTransform.offsetMax.y, EPSILON, "알림 뒤 새 자리로 옮긴다");
            CollectionAssert.IsEmpty(log.Log, "계속 보이는 셀에는 표시 이벤트가 없다");
            AssertMatchesData("move inside");

            // 활성 범위 밖(50번)에서 12번으로: 그 항목만 바인딩하고 밀려난 a는 회수한다.
            int itemA = a.BoundData;
            int recycledA = a.RecycledCount;
            _data.Move(50, 12);
            Scroller.MoveCell(50, 12);
            Assert.AreEqual(calls + 1, _data.GetCellViewCalls);
            Assert.AreEqual(recycledA + 1, a.RecycledCount);
            Assert.IsNull(CellForItem(itemA));
            Assert.AreEqual(11, b.DataIndex);
            Assert.AreEqual(13, c.DataIndex);
            AssertMatchesData("move in from outside");
            AssertDisplayMatchesViewport(log, "move in from outside");
        }

        [UnityTest]
        public IEnumerator Alignment_InsertAbove_KeepsAlignedItemThroughViewportResize()
        {
            Create(100);
            int item = _data.Items[30];
            Scroller.JumpToDataIndex(30, 0.5f, 0.5f, false);   // 30번 가운데: 3050 − 200
            Assert.AreEqual(2850f, Scroller.ScrollPosition, EPSILON);

            for (int j = 0; j < 3; j++)
            {
                _data.Insert(10, NextItem(), 70f);
            }

            Scroller.InsertCells(10, 3);
            Assert.AreEqual(2850f + 210f, Scroller.ScrollPosition, EPSILON);

            // 정렬이 같은 항목으로 옮겨졌으므로 뷰포트 크기가 바뀌어도 그 항목이 가운데에 남는다.
            var scrollRect = (RectTransform)_fixture.ScrollRect.transform;
            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 300f);
            yield return null;
            int index = _data.Items.IndexOf(item);
            Assert.AreEqual(33, index);
            Assert.AreEqual(Scroller.GetCellStart(index) + 50f - 150f, Scroller.ScrollPosition, EPSILON);
        }

        [UnityTest]
        public IEnumerator Alignment_AlignedItemMoved_ReleasesAlignmentAndKeepsScreen()
        {
            Create(100);
            int moved = _data.Items[30];
            Scroller.JumpToDataIndex(30, 0.5f, 0.5f, false);   // 30번 가운데: 3050 − 200. 뷰포트 [2850, 3250]: 28~32번
            Assert.AreEqual(2850f, Scroller.ScrollPosition, EPSILON);
            int front = _data.Items[28];

            // 정렬 대상(30번)을 맨 위로 올린다. 정렬이 따라가면 화면이 목록 맨 위로 튄다.
            _data.Move(30, 0);
            Scroller.MoveCell(30, 0);

            // 맨 앞 항목(28번) 앞에 100이 들어간 만큼만 보정한다.
            Assert.AreEqual(0, _data.Items.IndexOf(moved));
            Assert.AreEqual(2950f, Scroller.ScrollPosition, EPSILON, "정렬 대상을 옮겨도 화면이 그 항목으로 튀지 않는다");
            Assert.AreEqual(-50f, ScreenOffsetOfItem(front), EPSILON);
            AssertMatchesData("move aligned item");

            // 정렬이 풀렸으므로 뷰포트 크기가 바뀌어도 옮겨진 항목으로 가지 않는다.
            var scrollRect = (RectTransform)_fixture.ScrollRect.transform;
            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 300f);
            yield return null;
            Assert.AreEqual(2950f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(-50f, ScreenOffsetOfItem(front), EPSILON);
            AssertMatchesData("viewport resize after moving aligned item");
        }

        [UnityTest]
        public IEnumerator PendingAnchor_IndexFollowsInsert()
        {
            Create(100);
            var scrollRect = (RectTransform)_fixture.ScrollRect.transform;
            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, 0f);
            yield return null;

            // 뷰포트 길이가 0이라 보관한다. 앞쪽 삽입 뒤에도 같은 항목을 가리켜야 한다 (ID 없음).
            Scroller.RestoreAnchor(new CyScrollerAnchor { DataIndex = 30, Offset = 10f });
            Assert.AreEqual(30, Scroller.CaptureAnchor().DataIndex);
            for (int j = 0; j < 4; j++)
            {
                _data.Insert(0, NextItem(), 100f);
            }

            Scroller.InsertCells(0, 4);
            Assert.AreEqual(34, Scroller.CaptureAnchor().DataIndex, "보관한 앵커의 인덱스도 옮긴다");

            scrollRect.sizeDelta = new Vector2(ScrollerFixture.VIEWPORT_WIDTH, ScrollerFixture.VIEWPORT_HEIGHT);
            yield return null;
            Assert.AreEqual(3410f, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("pending anchor");
        }

        #endregion

        #region Batches

        [Test]
        public void Batch_KeepsOldStateUntilOuterEndAndAppliesOnce()
        {
            Create(100);
            Scroller.ScrollPosition = 1000f;
            TestCellView top = CellForItem(_data.Items[10]);
            int calls = _data.GetCellViewCalls;
            int sizeCalls = _data.GetCellViewSizeCalls;

            Scroller.BeginUpdates();
            Scroller.BeginUpdates();
            _data.Insert(0, NextItem(), 100f);
            _data.Insert(1, NextItem(), 100f);
            Scroller.InsertCells(0, 2);
            _data.RemoveRange(14, 1);
            Scroller.RemoveCells(14, 1);
            Scroller.EndUpdates();

            Assert.AreEqual(100, Scroller.NumberOfCells, "바깥 EndUpdates 전에는 배치 전 상태다");
            Assert.AreEqual(10, top.DataIndex);
            Assert.AreEqual(sizeCalls, _data.GetCellViewSizeCalls, "배치 중에는 델리게이트를 부르지 않는다");

            // 배치 중 스크롤: 옮기기만 하고 범위 갱신·바인딩은 배치 끝으로 미룬다.
            Scroller.ScrollPosition = 3000f;
            Assert.AreEqual(calls, _data.GetCellViewCalls);
            Assert.AreEqual(10, Scroller.StartDataIndex, "활성 셀은 배치 전 그대로");

            Scroller.EndUpdates();
            Assert.AreEqual(101, Scroller.NumberOfCells);
            Assert.AreEqual(sizeCalls + 2, _data.GetCellViewSizeCalls, "삽입분만 크기를 묻는다");

            // 3000의 맨 앞 항목(배치 전 30번)은 2개 삽입·1개 삭제 뒤 31번이다.
            Assert.AreEqual(3100f, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("after batch");
        }

        [Test]
        public void EndUpdates_WithoutBegin_WarnsAndIsIgnored()
        {
            Create(10);
            LogAssert.Expect(LogType.Warning, new Regex("BeginUpdates 없이 EndUpdates"));
            Scroller.EndUpdates();

            Scroller.BeginUpdates();
            _data.Insert(3, NextItem(), 100f);
            Scroller.InsertCells(3, 1);
            Scroller.EndUpdates();
            AssertMatchesData("after unbalanced end");
        }

        [UnityTest]
        public IEnumerator Batch_LeftOpenAcrossFrames_WarnsOnceAndAppliesWhenClosed()
        {
            Create(100);
            Scroller.ScrollPosition = 1000f;
            int warnings = 0;
            Application.LogCallback counter = (condition, _, type) =>
            {
                if (type == LogType.Warning && condition.Contains("프레임을 넘겨 열려 있습니다"))
                {
                    warnings++;
                }
            };

            Application.logMessageReceived += counter;
            try
            {
                // 같은 프레임에 닫은 배치는 경고하지 않는다.
                Scroller.BeginUpdates();
                Scroller.EndUpdates();
                yield return null;
                yield return null;
                Assert.AreEqual(0, warnings);

                // 닫지 않은 배치: 그동안 스크롤해도 셀이 갱신되지 않으므로 프레임을 넘기면 한 번 경고한다.
                LogAssert.Expect(LogType.Warning, new Regex("BeginUpdates로 연 배치가 프레임을 넘겨 열려 있습니다"));
                Scroller.BeginUpdates();
                _data.Insert(0, NextItem(), 100f);
                Scroller.InsertCells(0, 1);
                for (int frame = 0; frame < 4; frame++)
                {
                    yield return null;
                }

                Assert.AreEqual(1, warnings, "열린 배치마다 한 번만 경고한다");
                Assert.AreEqual(100, Scroller.NumberOfCells, "닫을 때까지 배치 전 상태다");

                // 늦게라도 닫으면 그때 적용한다.
                Scroller.EndUpdates();
                Assert.AreEqual(101, Scroller.NumberOfCells);
                Assert.AreEqual(1100f, Scroller.ScrollPosition, EPSILON);
                AssertMatchesData("closed late");
                yield return null;
                yield return null;
                Assert.AreEqual(1, warnings, "닫힌 뒤에는 경고하지 않는다");
            }
            finally
            {
                Application.logMessageReceived -= counter;
            }
        }

        [Test]
        public void Batch_ClosedInFinallyAfterException_ResyncsWithData()
        {
            Create(100);
            Scroller.ScrollPosition = 1030f;

            // 데이터를 바꾼 뒤 InsertCells에 닿기 전에 예외가 났다. finally의 EndUpdates가 배치를 닫고 개수 불일치로 다시 읽는다.
            LogAssert.Expect(LogType.Warning, new Regex(@"증분 연산을 반영한 개수\(100\)가 GetNumberOfCells\(101\)와 다릅니다"));
            Assert.Throws<System.InvalidOperationException>(() =>
            {
                Scroller.BeginUpdates();
                try
                {
                    _data.Insert(0, NextItem(), 100f);
                    throw new System.InvalidOperationException("데이터 갱신 중 예외");
                }
                finally
                {
                    Scroller.EndUpdates();
                }
            });

            Assert.AreEqual(101, Scroller.NumberOfCells, "배치가 닫혀 데이터와 다시 맞췄다");
            AssertMatchesData("after exception");

            // 배치가 닫혔으므로 스크롤하면 셀이 갱신된다.
            Scroller.ScrollPosition = 5000f;
            AssertMatchesData("scroll after exception");
        }

        [Test]
        public void EndUpdates_CountMismatch_WarnsAndReloadsKeepingFirstVisible()
        {
            Create(100);
            Scroller.ScrollPosition = 1030f;
            int calls = _data.GetCellViewCalls;

            Scroller.BeginUpdates();
            _data.Insert(50, NextItem(), 100f);
            _data.Insert(50, NextItem(), 100f);
            Scroller.InsertCells(50, 1);   // 데이터는 2개 늘었는데 1개만 알린다
            LogAssert.Expect(LogType.Warning, new Regex(@"증분 연산을 반영한 개수\(101\)가 GetNumberOfCells\(102\)와 다릅니다"));
            Scroller.EndUpdates();

            Assert.AreEqual(102, Scroller.NumberOfCells);
            Assert.AreEqual(1030f, Scroller.ScrollPosition, EPSILON, "FirstVisible 리로드: 맨 앞 항목과 오프셋 유지");
            Assert.Greater(_data.GetCellViewCalls, calls, "전체 리로드는 셀을 다시 바인딩한다");
            AssertMatchesData("mismatch reload");
        }

        [Test]
        public void ReloadDuringBatch_IsDeferredAndReplacesRecordedOps()
        {
            Create(100);
            Scroller.ScrollPosition = 1000f;
            int calls = _data.GetCellViewCalls;

            Scroller.BeginUpdates();
            _data.Insert(0, NextItem(), 100f);
            Scroller.InsertCells(0, 1);
            Scroller.ReloadData(ReloadAnchor.End);
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON, "배치 중 리로드는 EndUpdates까지 미룬다");
            Assert.AreEqual(100, Scroller.NumberOfCells);
            Assert.AreEqual(calls, _data.GetCellViewCalls);
            Scroller.ClearActive();
            Assert.AreEqual(4, Scroller.ActiveCellViews.Count, "정리도 미룬다");

            Scroller.EndUpdates();
            Assert.AreEqual(101, Scroller.NumberOfCells);
            Assert.AreEqual(Scroller.ScrollSize, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("reload in batch");
        }

        [Test]
        public void LoopMode_InsertCells_ReloadsKeepingFirstVisible()
        {
            Create(10, loop: true);
            Assert.IsTrue(Scroller.Layout.IsLoop);
            Scroller.ScrollPosition = 2330f;   // 가운데 세트 3번 안 30
            CyScrollerAnchor before = Scroller.CaptureAnchor();
            Assert.AreEqual(3, before.DataIndex);
            int calls = _data.GetCellViewCalls;

            _data.Insert(7, NextItem(), 100f);
            Scroller.InsertCells(7, 1);

            Assert.AreEqual(11, Scroller.NumberOfCells);
            CyScrollerAnchor after = Scroller.CaptureAnchor();
            Assert.AreEqual(before.DataIndex, after.DataIndex, "ReloadData(FirstVisible)처럼 맨 앞 항목을 지킨다");
            Assert.AreEqual(before.Offset, after.Offset, EPSILON);
            Assert.Greater(_data.GetCellViewCalls, calls, "루프는 증분 대신 전체 리로드로 바꾼다");
            for (int i = 0; i < Scroller.ActiveCellViews.Count; i++)
            {
                var view = (TestCellView)Scroller.ActiveCellViews[i];
                Assert.AreEqual(_data.Items[view.DataIndex], view.BoundData, $"slot {view.CellIndex}");
            }
        }

        [UnityTest]
        public IEnumerator InsertCellsInsideDisplayCallback_IsDeferredToAnchorReload()
        {
            Create(100, ids: true, reload: false);
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            bool inserted = false;
            Scroller.CellViewWillDisplay += (scroller, view) =>
            {
                if (!inserted && view.DataIndex == 50)
                {
                    inserted = true;
                    _data.Insert(0, NextItem(), 100f);
                    scroller.InsertCells(0, 1);
                }
            };

            Assert.DoesNotThrow(() => Scroller.ScrollPosition = 5000f);
            Assert.IsTrue(inserted);
            Assert.AreEqual(100, Scroller.NumberOfCells, "범위 갱신 중에는 적용하지 않는다");
            Assert.AreEqual(5000f, Scroller.ScrollPosition, EPSILON);
            AssertDisplayMatchesViewport(log, "deferred");

            yield return null;

            // 범위 갱신 뒤 앵커 보존 리로드: ID로 같은 항목(50 → 51번)을 지킨다.
            Assert.AreEqual(101, Scroller.NumberOfCells);
            Assert.AreEqual(5100f, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("after deferred reload");
            AssertDisplayMatchesViewport(log, "after deferred reload");
        }

        [Test]
        public void OutOfRangeArguments_AreClamped()
        {
            Create(10);
            _data.Insert(10, NextItem(), 100f);
            Scroller.InsertCells(99, 1);       // 끝에 붙인다
            _data.Insert(0, NextItem(), 100f);
            Scroller.InsertCells(-5, 1);       // 맨 앞
            Scroller.InsertCells(3, 0);
            Scroller.InsertCells(3, -2);
            Scroller.RemoveCells(3, 0);
            Scroller.RemoveCells(50, 3);       // 남는 구간 없음
            AssertMatchesData("insert clamped");

            _data.RemoveRange(0, 2);
            Scroller.RemoveCells(-1, 3);       // [0, 2)만
            _data.Move(0, _data.Items.Count - 1);
            Scroller.MoveCell(-3, 100);        // 0 → 마지막
            Scroller.MoveCell(4, 4);
            AssertMatchesData("remove/move clamped");
        }

        #endregion

        #region Partial Refresh

        [Test]
        public void RefreshCells_PassesMaskOnlyToActiveCellsInRange()
        {
            Create(100);
            Scroller.ScrollPosition = 1000f;   // 10~13번
            TestCellView[] cells = ActiveCellsSnapshot();
            int[] versions = BindVersions(cells);
            int calls = _data.GetCellViewCalls;
            int sizeCalls = _data.GetCellViewSizeCalls;

            // [8, 12): 활성인 10·11번만 받는다. 기본 구현을 거쳐 무인자 RefreshCellView도 불린다.
            Scroller.RefreshCells(8, 4, 0b101);
            AssertMaskedRefreshes(cells, new[] { 1, 1, 0, 0 }, "range");
            Assert.AreEqual(0b101, cells[0].LastChangeMask);
            Assert.AreEqual(0b101, cells[1].LastChangeMask);
            Assert.AreEqual(1, cells[0].RefreshCount, "기본 구현은 RefreshCellView()를 부른다");
            Assert.AreEqual(0, cells[2].RefreshCount);

            // 활성 범위 밖은 부르지 않는다. 기본 마스크는 ~0이다.
            Scroller.RefreshCells(50, 10, 1);
            AssertMaskedRefreshes(cells, new[] { 1, 1, 0, 0 }, "inactive");
            Scroller.RefreshCells(13, 1);
            AssertMaskedRefreshes(cells, new[] { 1, 1, 0, 1 }, "default mask");
            Assert.AreEqual(~0, cells[3].LastChangeMask);

            // 범위 밖 인자: [0, 개수) 안쪽만 쓰고, 개수·마스크가 0 이하이면 아무것도 하지 않는다.
            Scroller.RefreshCells(-5, 17, 2);      // [0, 12)
            AssertMaskedRefreshes(cells, new[] { 2, 2, 0, 1 }, "clamped start");
            Scroller.RefreshCells(12, 1000, 4);    // [12, 100)
            AssertMaskedRefreshes(cells, new[] { 2, 2, 1, 2 }, "clamped end");
            Scroller.RefreshCells(100, 5, 8);
            Scroller.RefreshCells(10, 0, 8);
            Scroller.RefreshCells(10, -3, 8);
            Scroller.RefreshCells(10, 4, 0);
            AssertMaskedRefreshes(cells, new[] { 2, 2, 1, 2 }, "no-op arguments");

            // 다시 바인딩하지 않고 크기도 묻지 않으며 화면도 그대로다.
            Assert.AreEqual(calls, _data.GetCellViewCalls);
            Assert.AreEqual(sizeCalls, _data.GetCellViewSizeCalls);
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
            for (int i = 0; i < cells.Length; i++)
            {
                Assert.AreEqual(versions[i], cells[i].BindVersion, $"{i}번 셀 BindVersion 그대로");
            }

            AssertMatchesData("refresh");
        }

        [Test]
        public void RefreshActiveCellViews_WithMask_PassesMask_AndParameterlessKeepsBehavior()
        {
            Create(100);
            Scroller.ScrollPosition = 1000f;
            TestCellView[] cells = ActiveCellsSnapshot();

            Scroller.RefreshActiveCellViews(6);
            AssertMaskedRefreshes(cells, new[] { 1, 1, 1, 1 }, "masked");
            for (int i = 0; i < cells.Length; i++)
            {
                Assert.AreEqual(6, cells[i].LastChangeMask);
                Assert.AreEqual(1, cells[i].RefreshCount, "기본 구현을 거쳐 RefreshCellView()도 한 번");
            }

            // 무인자 버전은 지금처럼 RefreshCellView()만 부른다.
            Scroller.RefreshActiveCellViews();
            AssertMaskedRefreshes(cells, new[] { 1, 1, 1, 1 }, "parameterless");
            for (int i = 0; i < cells.Length; i++)
            {
                Assert.AreEqual(2, cells[i].RefreshCount);
            }

            Scroller.RefreshActiveCellViews(0);
            AssertMaskedRefreshes(cells, new[] { 1, 1, 1, 1 }, "zero mask");

            // 배치 안: 마스크 버전은 EndUpdates에서 남은 셀에 한 번, 무인자 버전은 지금처럼 바로 부른다.
            Scroller.BeginUpdates();
            Scroller.RefreshActiveCellViews(1);
            Scroller.RefreshActiveCellViews();
            Assert.AreEqual(3, cells[0].RefreshCount, "무인자 버전은 배치 중에도 바로 부른다");
            AssertMaskedRefreshes(cells, new[] { 1, 1, 1, 1 }, "deferred in batch");
            Scroller.RefreshActiveCellViews(2);
            Scroller.EndUpdates();
            AssertMaskedRefreshes(cells, new[] { 2, 2, 2, 2 }, "after batch");
            for (int i = 0; i < cells.Length; i++)
            {
                Assert.AreEqual(3, cells[i].LastChangeMask, "배치 안 두 번은 OR로 합쳐 한 번");
            }
        }

        [Test]
        public void Batch_RefreshCells_AccumulatesMasks_FollowsItems_AndSkipsRemovedAndNewCells()
        {
            Create(100);
            Scroller.ScrollPosition = 1000f;   // 10~13번
            int a = _data.Items[10];
            int b = _data.Items[11];
            int c = _data.Items[12];
            int d = _data.Items[13];
            TestCellView cellA = CellForItem(a);
            TestCellView cellB = CellForItem(b);
            TestCellView cellC = CellForItem(c);
            TestCellView cellD = CellForItem(d);
            int versionA = cellA.BindVersion;
            int versionB = cellB.BindVersion;
            int historyD = cellD.RefreshHistory.Count;
            int calls = _data.GetCellViewCalls;

            Scroller.BeginUpdates();
            Scroller.RefreshCells(10, 2, 1);        // a, b
            Scroller.RefreshCells(11, 1, 4);        // b
            _data.Insert(5, NextItem(), 100f);
            _data.Insert(6, NextItem(), 100f);
            Scroller.InsertCells(5, 2);             // a 12, b 13, c 14, d 15
            Scroller.RefreshCells(13, 1, 2);        // b (밀린 인덱스)
            Scroller.RefreshCells(14, 1, 8);        // c
            Scroller.RefreshCells(15, 1, 16);       // d
            _data.RemoveRange(15, 1);
            Scroller.RemoveCells(15, 1);            // d 삭제: 모은 값은 버린다
            int added = NextItem();
            _data.Insert(13, added, 100f);
            Scroller.InsertCells(13, 1);            // a 12, added 13, b 14, c 15
            Scroller.RefreshCells(13, 1, 32);       // 삽입한 항목은 새로 바인딩하므로 부르지 않는다
            Assert.AreEqual(0, cellA.MaskedRefreshCount + cellB.MaskedRefreshCount, "배치 중에는 부르지 않는다");
            Scroller.EndUpdates();

            // 위쪽 삽입 2개만큼 옮겨 a가 맨 앞에 남는다.
            Assert.AreEqual(1200f, Scroller.ScrollPosition, EPSILON);
            Assert.AreSame(cellA, CellForItem(a));
            Assert.AreSame(cellB, CellForItem(b));
            Assert.AreSame(cellC, CellForItem(c));
            Assert.AreEqual(1, cellA.MaskedRefreshCount);
            Assert.AreEqual(1, cellA.LastChangeMask);
            Assert.AreEqual(1, cellB.MaskedRefreshCount, "셀마다 한 번");
            Assert.AreEqual(1 | 4 | 2, cellB.LastChangeMask, "같은 항목에 모은 값을 OR로 합친다");
            Assert.AreEqual(1, cellC.MaskedRefreshCount);
            Assert.AreEqual(8, cellC.LastChangeMask);
            Assert.AreEqual(versionA, cellA.BindVersion, "다시 바인딩하지 않는다");
            Assert.AreEqual(versionB, cellB.BindVersion);

            TestCellView addedCell = CellForItem(added);
            Assert.IsNotNull(addedCell);
            Assert.AreEqual(0, addedCell.MaskedRefreshCount, "새로 바인딩한 셀에는 부르지 않는다");
            Assert.IsNull(CellForItem(d));

            // 회수한 d의 셀은 풀에서 곧바로 삽입 항목에 다시 쓰일 수 있고, 그러면 바인딩하며 MaskedRefreshCount가 0으로 돌아간다.
            // 그래서 바인딩해도 지우지 않는 호출 기록으로 회수 전에도 받지 않았는지 본다.
            Assert.AreEqual(historyD, cellD.RefreshHistory.Count, "지운 항목의 셀은 (회수 전에도) 내용 갱신을 받지 않는다");
            Assert.AreEqual(calls + 1, _data.GetCellViewCalls, "새 항목만 바인딩한다");
            AssertMatchesData("batch refresh");
        }

        [Test]
        public void RefreshCells_LoopMode_RefreshesEveryCopyWithoutReload()
        {
            Create(3, loop: true);   // 사이클 300, 뷰포트 400: 가운데 세트에서 0번 사본이 둘 보인다
            Assert.IsTrue(Scroller.Layout.IsLoop);
            int calls = _data.GetCellViewCalls;
            int copies = CountActiveCopies(0);
            Assert.GreaterOrEqual(copies, 2, "0번 사본이 여럿 활성이어야 한다");

            Scroller.RefreshCells(0, 1, 8);
            AssertCopiesRefreshed(0, 1, 8, "single");
            AssertCopiesRefreshed(1, 0, 0, "single, other item");

            // 갱신만 있는 배치는 루프 모드에서도 리로드하지 않고 사본마다 한 번 부른다.
            Scroller.BeginUpdates();
            Scroller.RefreshCells(0, 1, 1);
            Scroller.RefreshCells(0, 2, 2);
            Scroller.EndUpdates();
            AssertCopiesRefreshed(0, 2, 3, "batch");
            AssertCopiesRefreshed(1, 1, 2, "batch, other item");
            AssertCopiesRefreshed(2, 0, 0, "batch, untouched item");
            Assert.AreEqual(calls, _data.GetCellViewCalls, "다시 바인딩하지 않는다");

            // 다시 받기는 루프 모드에서 다른 증분 변경처럼 앵커 보존 리로드로 바뀐다.
            CyScrollerAnchor before = Scroller.CaptureAnchor();
            _data.Sizes[1] = 150f;
            Scroller.ReloadCellView(1);
            Assert.AreEqual(150f, Scroller.GetCellSize(1), EPSILON);
            Assert.Greater(_data.GetCellViewCalls, calls, "루프는 증분 대신 전체 리로드로 바꾼다");
            CyScrollerAnchor after = Scroller.CaptureAnchor();
            Assert.AreEqual(before.DataIndex, after.DataIndex, "ReloadData(FirstVisible)처럼 맨 앞 항목을 지킨다");
            Assert.AreEqual(before.Offset, after.Offset, EPSILON);
        }

        [Test]
        public void ReloadCellView_IdentifierChange_ReplacesCellAndKeepsDisplayPairs()
        {
            Create(100, reload: false);
            _data.AltPrefab = _fixture.AltPrefab;
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            Scroller.ScrollPosition = 1000f;   // 10~13번
            int item = _data.Items[11];
            TestCellView old = CellForItem(item);
            Assert.AreEqual("Test", old.CellIdentifier);
            int oldVersion = old.BindVersion;
            TestCellView[] others = { CellForItem(_data.Items[10]), CellForItem(_data.Items[12]), CellForItem(_data.Items[13]) };
            int[] otherVersions = BindVersions(others);
            int calls = _data.GetCellViewCalls;
            int sizeCalls = _data.GetCellViewSizeCalls;
            int instantiated = _fixture.InstantiatedCount;
            log.Log.Clear();

            // 셀 종류가 바뀌었다: 다른 프리팹 셀로 바꾼다.
            _data.AltItems.Add(item);
            Scroller.ReloadCellView(11);

            TestCellView replaced = CellForItem(item);
            Assert.AreNotSame(old, replaced);
            Assert.AreEqual("Alt", replaced.CellIdentifier);
            Assert.AreEqual(replaced.BindVersion, replaced.BoundVersion, "델리게이트가 새로 바인딩했다");
            Assert.IsFalse(old.IsBound, "옛 셀은 회수했다");
            Assert.AreEqual(oldVersion + 1, old.BindVersion, "회수하면 BindVersion이 는다");
            Assert.AreEqual(instantiated + 1, _fixture.InstantiatedCount);
            Assert.AreEqual(calls + 1, _data.GetCellViewCalls, "그 항목만 다시 받는다");
            Assert.AreEqual(sizeCalls + 1, _data.GetCellViewSizeCalls, "그 항목만 크기를 다시 묻는다");
            CollectionAssert.AreEqual(new[] { "end " + item, "recycle " + item, "will " + item }, log.Log,
                "표시 끝 → 회수 → 새 셀 표시 시작. 다른 셀에는 표시 이벤트가 없다");
            for (int i = 0; i < others.Length; i++)
            {
                Assert.AreSame(others[i], CellForItem(others[i].BoundData));
                Assert.AreEqual(otherVersions[i], others[i].BindVersion, "다른 셀은 다시 바인딩하지 않는다");
            }

            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("identifier change");
            AssertDisplayMatchesViewport(log, "identifier change");

            // 되돌리면 풀에 있던 옛 셀을 다시 받는다. 같은 셀이어도 다시 바인딩이라 BindVersion이 는다.
            _data.AltItems.Remove(item);
            Scroller.ReloadCellView(11);
            Assert.AreSame(old, CellForItem(item));
            Assert.AreEqual(oldVersion + 2, old.BindVersion);
            Assert.AreEqual("Test", old.CellIdentifier);
            AssertMatchesData("identifier restored");
            AssertDisplayMatchesViewport(log, "identifier restored");
        }

        [Test]
        public void ReloadCellView_SizeChanges_KeepScreen_AndInactiveItemsOnlyUpdateSize()
        {
            Create(100);
            Scroller.ScrollPosition = 1050f;   // 맨 앞 10번 안 50
            var items = new List<int>();
            var offsets = new List<float>();
            var cells = new List<TestCellView>();
            for (int index = 10; index <= 14; index++)
            {
                items.Add(_data.Items[index]);
                offsets.Add(ScreenOffsetOfItem(_data.Items[index]));
                cells.Add(CellForItem(_data.Items[index]));
            }

            int calls = _data.GetCellViewCalls;
            int sizeCalls = _data.GetCellViewSizeCalls;

            // 뷰포트 위 비활성 항목이 커졌다: 크기만 다시 묻고 그만큼 옮겨 화면을 지킨다.
            _data.Sizes[3] = 160f;
            Scroller.ReloadCellView(3);
            Assert.AreEqual(1110f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(160f, Scroller.GetCellSize(3), EPSILON);
            Assert.AreEqual(calls, _data.GetCellViewCalls, "비활성 항목은 셀을 받지 않는다");
            Assert.AreEqual(sizeCalls + 1, _data.GetCellViewSizeCalls);
            AssertSameScreen(items, offsets, cells, "reload above");
            AssertMatchesData("reload above");

            // 뷰포트 아래 비활성 항목: 위치는 그대로이고 콘텐츠 길이만 바뀐다.
            float content = Scroller.ContentSize;
            _data.Sizes[60] = 40f;
            Scroller.ReloadCellView(60);
            Assert.AreEqual(1110f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(content - 60f, Scroller.ContentSize, EPSILON);
            Assert.AreEqual(calls, _data.GetCellViewCalls);
            AssertSameScreen(items, offsets, cells, "reload below");

            // 크기가 그대로면 다시 받아도 화면은 그대로다 (보이는 항목이면 그 셀만 다시 바인딩).
            Scroller.ReloadCellView(12);
            Assert.AreEqual(1110f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(calls + 1, _data.GetCellViewCalls);
            AssertMatchesData("reload same size");

            // 맨 앞 항목 자신이 커져도 그 항목 안 거리(50)를 지킨다. 뒤 항목만 밀린다.
            _data.Sizes[10] = 300f;
            Scroller.ReloadCellView(10);
            Assert.AreEqual(1110f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(-50f, ScreenOffsetOfItem(items[0]), EPSILON);
            Assert.AreEqual(250f, ScreenOffsetOfItem(items[1]), EPSILON);
            AssertMatchesData("reload first visible");
        }

        [Test]
        public void ReloadCellView_InBatch_FollowsIndexShiftsAndSkipsRemovedItems()
        {
            Create(100, reload: false);
            _data.AltPrefab = _fixture.AltPrefab;
            Scroller.ReloadData();
            Scroller.ScrollPosition = 1000f;   // 10~13번
            int a = _data.Items[10];
            int b = _data.Items[11];
            int c = _data.Items[12];
            int d = _data.Items[13];
            TestCellView cellA = CellForItem(a);
            TestCellView cellB = CellForItem(b);
            int versionA = cellA.BindVersion;
            int versionB = cellB.BindVersion;
            int calls = _data.GetCellViewCalls;
            int sizeCalls = _data.GetCellViewSizeCalls;

            Scroller.BeginUpdates();
            _data.AltItems.Add(c);
            Scroller.ReloadCellView(12);          // c
            _data.Insert(0, NextItem(), 100f);
            Scroller.InsertCells(0, 1);           // c 13, d 14
            _data.Sizes[13] = 150f;               // EndUpdates에서 최종 인덱스로 크기를 묻는다
            Scroller.ReloadCellView(14);          // d
            _data.RemoveRange(14, 1);
            Scroller.RemoveCells(14, 1);          // d 삭제: 다시 받지 않는다
            Assert.AreEqual(sizeCalls, _data.GetCellViewSizeCalls, "배치 중에는 묻지 않는다");
            Scroller.EndUpdates();

            Assert.AreEqual(sizeCalls + 2, _data.GetCellViewSizeCalls, "삽입한 항목과 c만 묻는다");
            Assert.AreEqual(150f, Scroller.GetCellSize(13), EPSILON);
            Assert.AreEqual(1100f, Scroller.ScrollPosition, EPSILON, "위쪽 삽입만큼 옮긴다");
            TestCellView cellC = CellForItem(c);
            Assert.AreEqual("Alt", cellC.CellIdentifier, "옮겨진 c를 다시 받았다");
            Assert.AreSame(cellA, CellForItem(a));
            Assert.AreSame(cellB, CellForItem(b));
            Assert.AreEqual(versionA, cellA.BindVersion);
            Assert.AreEqual(versionB, cellB.BindVersion);
            Assert.IsNull(CellForItem(d));

            // 다시 받은 c와 뷰포트 끝에 새로 들어온 e(옛 14번)만 바인딩한다.
            Assert.AreEqual(calls + 2, _data.GetCellViewCalls);
            AssertMatchesData("reload in batch");
        }

        [Test]
        public void ReloadCellView_ItemIdChange_UpdatesLookup()
        {
            var data = (ListIdTestDelegate)Create(50, ids: true);
            int idCalls = data.GetItemIdCalls;

            // 같은 ID로 다시 받으면 사전은 그대로다.
            Scroller.ReloadCellView(7);
            Assert.AreEqual(idCalls + 1, data.GetItemIdCalls, "그 항목만 ID를 다시 묻는다");
            AssertIdLookups(data, "same id");

            // 보이는 자리(0~3번)의 항목이 다른 ID로 바뀌었다.
            int oldId = data.Items[2];
            int newId = NextItem();
            data.Items[2] = newId;
            Scroller.ReloadCellView(2);
            Assert.AreEqual(idCalls + 2, data.GetItemIdCalls);
            Assert.AreEqual(-1, Scroller.FindDataIndexForItemId(oldId));
            Assert.AreEqual(2, Scroller.FindDataIndexForItemId(newId));
            Assert.AreEqual(newId, CellForItem(newId).ItemId, "보이는 셀은 새 ID로 다시 바인딩한다");
            Assert.AreEqual(newId, CellForItem(newId).BoundItemId, "델리게이트가 바인딩하면서 읽은 ID도 새 값이다");
            AssertIdLookups(data, "changed id");

            // 뒤쪽 항목과 같은 ID가 되면 다시 받을 때처럼 경고하고 앞 인덱스가 이긴다. 다시 고치면 뒤쪽 항목이 그 ID를 되찾는다.
            int duplicate = data.Items[30];
            int original = data.Items[20];
            data.Items[20] = duplicate;
            LogAssert.Expect(LogType.Warning, new Regex("같은 ItemId"));
            Scroller.ReloadCellView(20);
            Assert.AreEqual(20, Scroller.FindDataIndexForItemId(duplicate));
            Assert.AreEqual(-1, Scroller.FindDataIndexForItemId(original));
            data.Items[20] = original;
            Scroller.ReloadCellView(20);
            Assert.AreEqual(30, Scroller.FindDataIndexForItemId(duplicate));
            AssertIdLookups(data, "duplicate restored");
            AssertMatchesData("ids");
        }

        [Test]
        public void RefreshCells_InsideCallbacks_AppliesToCurrentItemsWithoutReload()
        {
            Create(100, reload: false);
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            Scroller.ScrollPosition = 1000f;   // 10~13번
            int removed = _data.Items[11];
            int next = _data.Items[12];
            TestCellView removedCell = CellForItem(removed);
            TestCellView nextCell = CellForItem(next);

            // 증분 변경을 적용하는 중(지운 셀의 표시 끝)에 데이터 기준 인덱스로 부른다. 그때 셀은 아직 배치 전 인덱스다.
            bool calledDuringApply = false;
            Scroller.CellViewDidEndDisplay += (scroller, view) =>
            {
                if (!calledDuringApply && ((TestCellView)view).BoundData == removed)
                {
                    calledDuringApply = true;
                    scroller.RefreshCells(11, 1, 64);   // 삭제 뒤 11번 = next
                }
            };

            int calls = _data.GetCellViewCalls;
            int removedHistory = removedCell.RefreshHistory.Count;
            int nextHistory = nextCell.RefreshHistory.Count;
            _data.RemoveRange(11, 1);
            Scroller.RemoveCells(11, 1);
            Assert.IsTrue(calledDuringApply);
            Assert.AreEqual(1, nextCell.MaskedRefreshCount, "셀을 새 인덱스로 맞춘 뒤 그 항목의 셀에 부른다");
            Assert.AreEqual(64, nextCell.LastChangeMask);
            Assert.AreEqual(nextHistory + 1, nextCell.RefreshHistory.Count, "그 항목의 셀에 한 번");
            Assert.AreEqual(next, nextCell.RefreshHistory[nextHistory].Item);

            // 지운 항목의 셀은 회수된 뒤 뷰포트 끝에 새로 들어온 항목에 다시 쓰이며 MaskedRefreshCount가 0으로 돌아간다.
            // 바인딩해도 지우지 않는 호출 기록으로, 지연 적용 대신(또는 함께) 옛 인덱스의 셀에 바로 부르지 않았는지 본다.
            Assert.AreEqual(removedHistory, removedCell.RefreshHistory.Count, "지운 항목의 셀에는 (회수 전에도) 부르지 않는다");
            Assert.AreEqual(calls + 1, _data.GetCellViewCalls, "리로드하지 않는다 (뷰포트 끝에 새로 들어온 항목만)");
            AssertMatchesData("refresh during apply");
            AssertDisplayMatchesViewport(log, "refresh during apply");

            // 범위 갱신 중(표시 시작 이벤트)에는 바로 부른다.
            bool calledDuringRange = false;
            Scroller.CellViewWillDisplay += (scroller, view) =>
            {
                if (!calledDuringRange && view.DataIndex == 30)
                {
                    calledDuringRange = true;
                    scroller.RefreshCells(30, 1, 128);
                }
            };

            calls = _data.GetCellViewCalls;
            Scroller.ScrollPosition = 2950f;   // 29~33번
            Assert.IsTrue(calledDuringRange);
            TestCellView cell30 = CellForItem(_data.Items[30]);
            Assert.AreEqual(1, cell30.MaskedRefreshCount);
            Assert.AreEqual(128, cell30.LastChangeMask);
            Assert.AreEqual(calls + 5, _data.GetCellViewCalls, "리로드하지 않는다");
            AssertMatchesData("refresh during range update");
            AssertDisplayMatchesViewport(log, "refresh during range update");
        }

        /// <summary>
        /// 표시 시작 핸들러 안에서 갱신만 있는 배치를 닫아도 리로드하지 않는다. 배치 밖 RefreshCells처럼 그 셀에 모은 changeMask로 바로 한 번 부르고,
        /// 크기를 다시 묻거나 셀을 다시 바인딩하지 않는다. 리로드가 표시 시작을 다시 부르고 핸들러가 또 리로드를 미루는 반복도 생기지 않는다.
        /// </summary>
        [UnityTest]
        public IEnumerator RefreshBatch_InsideDisplayCallback_RefreshesWithoutReload()
        {
            Create(1000, reload: false);
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            int displays = 0;
            Scroller.CellViewWillDisplay += (scroller, view) =>
            {
                displays++;
                scroller.BeginUpdates();
                scroller.RefreshCells(view.DataIndex, 1, 4);
                scroller.RefreshCells(view.DataIndex, 1, 8);
                scroller.EndUpdates();
            };

            int sizeCalls = _data.GetCellViewSizeCalls;
            int calls = _data.GetCellViewCalls;
            Scroller.ScrollPosition = 50000f;   // 500~503번이 새로 보인다

            Assert.AreEqual(4, displays);
            Assert.AreEqual(calls + 4, _data.GetCellViewCalls, "새로 보이는 셀만 바인딩한다");
            TestCellView[] cells = ActiveCellsSnapshot();
            int[] versions = BindVersions(cells);
            AssertMaskedRefreshes(cells, new[] { 1, 1, 1, 1 }, "in display callback");
            for (int i = 0; i < cells.Length; i++)
            {
                Assert.AreEqual(4 | 8, cells[i].LastChangeMask, "같은 배치의 값을 OR로 합쳐 한 번");
            }

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.AreEqual(sizeCalls, _data.GetCellViewSizeCalls, "리로드하지 않는다 (크기를 다시 묻지 않는다)");
            Assert.AreEqual(calls + 4, _data.GetCellViewCalls, "다시 바인딩하지 않는다");
            Assert.AreEqual(4, displays, "표시 시작이 되풀이되지 않는다");
            for (int i = 0; i < cells.Length; i++)
            {
                Assert.AreSame(cells[i], CellForItem(cells[i].BoundData), $"항목 {cells[i].BoundData} 같은 셀");
                Assert.AreEqual(versions[i], cells[i].BindVersion, $"항목 {cells[i].BoundData} BindVersion 그대로");
            }

            AssertMaskedRefreshes(cells, new[] { 1, 1, 1, 1 }, "after frames");
            AssertMatchesData("after frames");
            AssertDisplayMatchesViewport(log, "after frames");
        }

        /// <summary>
        /// 증분 변경을 적용하는 중(삽입분 크기를 묻는 델리게이트 안)에 빈 배치를 닫고 이어서 갱신만 있는 배치를 닫아도,
        /// 적용 중인 연산 목록이 그 배치들의 기록 목록이 되거나 비워지지 않는다: 위쪽 삽입만큼 위치를 옮겨 보던 화면을 지키고,
        /// 남은 셀은 새 인덱스로 옮겨 다시 바인딩하지 않으며, 갱신은 셀을 새 인덱스로 맞춘 뒤 한 번 부른다 (리로드 없음).
        /// </summary>
        [Test]
        public void BatchesClosedInsideDelegateDuringApply_KeepAppliedOperations()
        {
            Create(100, reload: false);
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            Scroller.ScrollPosition = 1030f;   // 맨 앞 10번 안 30. 10~14번
            int top = _data.Items[10];
            int refreshed = _data.Items[12];
            TestCellView[] cells = ActiveCellsSnapshot();
            int[] versions = BindVersions(cells);
            TestCellView refreshedCell = CellForItem(refreshed);
            int history = refreshedCell.RefreshHistory.Count;
            int calls = _data.GetCellViewCalls;
            int sizeCalls = _data.GetCellViewSizeCalls;
            int hooks = 0;
            _data.GetCellViewSizeHook = (scroller, dataIndex) =>
            {
                hooks++;
                if (hooks == 1)
                {
                    // 빈 배치: 범위 밖 삭제는 연산을 남기지 않는다.
                    scroller.RemoveCells(999, 1);
                }
                else if (hooks == 2)
                {
                    // 갱신만 있는 배치. 인덱스는 데이터 기준(삽입 뒤)이다.
                    scroller.BeginUpdates();
                    scroller.RefreshCells(_data.Items.IndexOf(refreshed), 1, 256);
                    scroller.EndUpdates();
                }
            };

            // 뷰포트 위 3번 자리에 두 개 삽입: 적용하면서 삽입분 크기를 물을 때 위 핸들러가 돈다.
            _data.Insert(3, NextItem(), 100f);
            _data.Insert(4, NextItem(), 100f);
            Scroller.InsertCells(3, 2);

            Assert.AreEqual(1230f, Scroller.ScrollPosition, EPSILON, "적용 중인 삽입 연산만큼 위치를 옮긴다");
            Assert.AreEqual(-30f, ScreenOffsetOfItem(top), EPSILON, "보던 맨 앞 항목이 같은 자리");
            Assert.AreEqual(sizeCalls + 2, _data.GetCellViewSizeCalls, "삽입분만 크기를 묻는다 (리로드하지 않는다)");
            Assert.AreEqual(2, hooks);
            Assert.AreEqual(calls, _data.GetCellViewCalls, "남은 셀은 다시 바인딩하지 않는다");
            for (int i = 0; i < cells.Length; i++)
            {
                int item = cells[i].BoundData;
                Assert.AreSame(cells[i], CellForItem(item), $"항목 {item} 같은 셀");
                Assert.AreEqual(versions[i], cells[i].BindVersion, $"항목 {item} BindVersion 그대로");
                Assert.AreEqual(_data.Items.IndexOf(item), cells[i].DataIndex, $"항목 {item} 새 인덱스");
            }

            Assert.AreEqual(history + 1, refreshedCell.RefreshHistory.Count, "갱신은 셀을 새 인덱스로 맞춘 뒤 한 번");
            Assert.AreEqual(refreshed, refreshedCell.RefreshHistory[history].Item);
            Assert.AreEqual(256, refreshedCell.RefreshHistory[history].Mask);
            AssertMatchesData("after apply");
            AssertDisplayMatchesViewport(log, "after apply");
        }

        /// <summary>
        /// 갱신만 있는 배치를 적용하는 중(셀의 RefreshCellView 안)에 빈 배치를 닫아도 배치 중 스크롤로 미룬 범위 갱신을 잃지 않는다:
        /// 갱신은 배치 전 활성 셀에 먼저 부르고, 바깥 EndUpdates가 끝나면 활성 셀이 새 위치에 맞는다.
        /// </summary>
        [Test]
        public void EmptyBatchClosedWhileApplyingRefreshes_KeepsDeferredRangeUpdate()
        {
            Create(100);
            TestCellView[] cells = ActiveCellsSnapshot();   // 0~3번
            int hooks = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].RefreshHook = (view, mask) =>
                {
                    // 늘 배치로 감싸는 도우미처럼 빈 배치를 닫는다.
                    hooks++;
                    Scroller.BeginUpdates();
                    Scroller.EndUpdates();
                };
            }

            Scroller.BeginUpdates();
            Scroller.ScrollPosition = 1000f;   // 범위 갱신은 배치 끝으로 미룬다
            Scroller.RefreshCells(0, 4, 1);
            Scroller.EndUpdates();

            Assert.AreEqual(4, hooks, "갱신은 배치 전 활성 셀(0~3번)에 먼저 부른다");
            Assert.AreEqual(1000f, Scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(10, Scroller.StartDataIndex, "미룬 범위 갱신을 바깥 EndUpdates에서 처리했다");
            AssertMatchesData("after batch");
        }

        /// <summary>
        /// ReloadCellView는 [0, 개수) 밖 인덱스를 무시한다 (크기·셀을 묻지 않고 예외도 없다). 배치 안에서는 앞 연산까지 반영한 개수가 기준이라
        /// 끝을 지운 뒤 지운 자리(옛 마지막 항목 포함)는 무시하고, 같은 배치에서 삽입해 늘어난 개수까지는 받는다.
        /// </summary>
        [Test]
        public void ReloadCellView_OutOfRangeIndex_IsIgnored()
        {
            Create(10);   // 첫 로드라 레이아웃 버퍼가 10개로 딱 맞다 (10번 자리를 건드리면 예외가 난다)
            int calls = _data.GetCellViewCalls;
            int sizeCalls = _data.GetCellViewSizeCalls;

            Scroller.ReloadCellView(-1);
            Scroller.ReloadCellView(_data.Items.Count);
            Scroller.ReloadCellView(int.MaxValue);
            Scroller.ReloadCellView(int.MinValue);
            Assert.AreEqual(calls, _data.GetCellViewCalls, "범위 밖은 셀을 다시 받지 않는다");
            Assert.AreEqual(sizeCalls, _data.GetCellViewSizeCalls, "범위 밖은 크기를 묻지 않는다");
            AssertMatchesData("outside batch");

            // 배치 안: 끝 3개를 지운 뒤 개수는 7이다. 지운 자리(7번, 옛 마지막 9번)는 무시한다.
            Scroller.BeginUpdates();
            _data.RemoveRange(7, 3);
            Scroller.RemoveCells(7, 3);
            Scroller.ReloadCellView(7);
            Scroller.ReloadCellView(9);
            Scroller.ReloadCellView(-1);
            Assert.DoesNotThrow(() => Scroller.EndUpdates());
            Assert.AreEqual(7, Scroller.NumberOfCells);
            Assert.AreEqual(sizeCalls, _data.GetCellViewSizeCalls, "지운 자리는 크기를 묻지 않는다");
            Assert.AreEqual(calls, _data.GetCellViewCalls);
            AssertMatchesData("after removing tail");

            // 같은 배치에서 삽입해 늘어난 개수까지 받는다: 맨 앞 삽입 뒤 마지막 항목(7번 = 배치 전 개수)은 받고 그 너머(8번)는 무시한다.
            Scroller.BeginUpdates();
            _data.Insert(0, NextItem(), 100f);
            Scroller.InsertCells(0, 1);
            _data.Sizes[7] = 140f;
            Scroller.ReloadCellView(7);
            Scroller.ReloadCellView(8);
            Assert.DoesNotThrow(() => Scroller.EndUpdates());
            Assert.AreEqual(8, Scroller.NumberOfCells);
            Assert.AreEqual(sizeCalls + 2, _data.GetCellViewSizeCalls, "삽입한 항목과 다시 받은 마지막 항목만 묻는다");
            Assert.AreEqual(140f, Scroller.GetCellSize(7), EPSILON, "늘어난 개수를 기준으로 마지막 항목을 다시 받았다");
            AssertMatchesData("after inserting at front");
        }

        [UnityTest]
        public IEnumerator ReloadCellView_InsideDisplayCallback_IsDeferredToAnchorReload()
        {
            Create(100, reload: false);
            var log = new DisplayLog(Scroller);
            Scroller.ReloadData();
            bool reloaded = false;
            Scroller.CellViewWillDisplay += (scroller, view) =>
            {
                if (!reloaded && view.DataIndex == 50)
                {
                    reloaded = true;
                    _data.Sizes[50] = 180f;
                    scroller.ReloadCellView(50);
                }
            };

            Assert.DoesNotThrow(() => Scroller.ScrollPosition = 5000f);
            Assert.IsTrue(reloaded);
            Assert.AreEqual(100f, Scroller.GetCellSize(50), EPSILON, "범위 갱신 중에는 적용하지 않는다");
            AssertDisplayMatchesViewport(log, "deferred");

            yield return null;

            // 다른 증분 변경처럼 범위 갱신 뒤 앵커 보존 리로드(FirstVisible)로 다시 읽는다.
            Assert.AreEqual(180f, Scroller.GetCellSize(50), EPSILON);
            Assert.AreEqual(5000f, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("after deferred reload");
            AssertDisplayMatchesViewport(log, "after deferred reload");
        }

        [Test]
        public void RefreshAndReload_AfterWarmup_DoesNotAllocate()
        {
            Create(400, 50f, ids: true);
            Scroller.LookAheadAfter = 100f;
            Scroller.ScrollPosition = 5000f;

            // 연산 목록·풀·작업 목록 용량을 채운다.
            RefreshReloadCycle();
            RefreshReloadCycle();

            Assert.That(() => RefreshReloadCycle(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            AssertMatchesData("after cycles");
        }

        /// <summary>배치 밖 갱신(활성·비활성·전체), 보이는 항목·위 항목 다시 받기, 갱신을 모은 뒤 삽입·삭제로 옮기는 배치, 갱신만 있는 배치. 끝나면 데이터는 처음과 같다.</summary>
        private void RefreshReloadCycle()
        {
            Scroller.RefreshCells(100, 5, 1);
            Scroller.RefreshCells(300, 5, 2);
            Scroller.RefreshActiveCellViews(4);
            Scroller.ReloadCellView(101);
            Scroller.ReloadCellView(20);

            Scroller.BeginUpdates();
            Scroller.RefreshCells(100, 3, 8);
            _data.Insert(10, -1, 50f);
            Scroller.InsertCells(10, 1);
            Scroller.ReloadCellView(102);
            Scroller.RefreshCells(103, 2, 16);
            _data.RemoveRange(10, 1);
            Scroller.RemoveCells(10, 1);
            Scroller.EndUpdates();

            Scroller.BeginUpdates();
            Scroller.RefreshCells(99, 4, 32);
            Scroller.RefreshActiveCellViews(64);
            Scroller.EndUpdates();
        }

        /// <summary>지금 활성 셀 목록 (슬롯 순서).</summary>
        private TestCellView[] ActiveCellsSnapshot()
        {
            IReadOnlyList<CyScrollerCellView> active = Scroller.ActiveCellViews;
            var cells = new TestCellView[active.Count];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = (TestCellView)active[i];
            }

            return cells;
        }

        private static int[] BindVersions(TestCellView[] cells)
        {
            var versions = new int[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                versions[i] = cells[i].BindVersion;
            }

            return versions;
        }

        private static void AssertMaskedRefreshes(TestCellView[] cells, int[] expected, string context)
        {
            Assert.AreEqual(expected.Length, cells.Length, $"{context}: 셀 수");
            for (int i = 0; i < cells.Length; i++)
            {
                Assert.AreEqual(expected[i], cells[i].MaskedRefreshCount, $"{context}: {i}번째 셀(데이터 {cells[i].DataIndex}) 내용 갱신 횟수");
            }
        }

        private int CountActiveCopies(int dataIndex)
        {
            int copies = 0;
            IReadOnlyList<CyScrollerCellView> active = Scroller.ActiveCellViews;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].DataIndex == dataIndex)
                {
                    copies++;
                }
            }

            return copies;
        }

        /// <summary>dataIndex의 활성 사본 셀이 모두 count번 받았고 마지막 값이 mask인지 (count가 0이면 받지 않았는지).</summary>
        private void AssertCopiesRefreshed(int dataIndex, int count, int mask, string context)
        {
            IReadOnlyList<CyScrollerCellView> active = Scroller.ActiveCellViews;
            for (int i = 0; i < active.Count; i++)
            {
                var view = (TestCellView)active[i];
                if (view.DataIndex != dataIndex)
                {
                    continue;
                }

                Assert.AreEqual(count, view.MaskedRefreshCount, $"{context}: 슬롯 {view.CellIndex} 내용 갱신 횟수");
                if (count > 0)
                {
                    Assert.AreEqual(mask, view.LastChangeMask, $"{context}: 슬롯 {view.CellIndex} changeMask");
                }
            }
        }

        #endregion

        #region Tween, Drag, Ids

        // 트윈이 끝나기를 기다리는 상한(초).
        private const float TWEEN_TIMEOUT = 3f;

        [UnityTest]
        public IEnumerator Tween_InsertAboveDuringTween_ArrivesAtSameItemOnce()
        {
            Create(200);
            int target = _data.Items[100];
            int completed = 0;
            Scroller.JumpToDataIndex(100, 0f, 0f, false, TweenType.EaseInOutCubic, 0.4f, () => completed++);
            yield return null;
            yield return null;
            Assert.IsTrue(Scroller.IsTweening);

            float before = Scroller.ScrollPosition;
            for (int j = 0; j < 5; j++)
            {
                _data.Insert(0, NextItem(), 60f);
            }

            Scroller.InsertCells(0, 5);
            Assert.IsTrue(Scroller.IsTweening, "앞쪽 삽입은 트윈을 끊지 않는다");
            Assert.AreEqual(before + 300f, Scroller.ScrollPosition, EPSILON, "삽입 순간 화면은 그대로");

            float timeout = TWEEN_TIMEOUT;
            while (Scroller.IsTweening && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(1, completed);
            int index = _data.Items.IndexOf(target);
            Assert.AreEqual(105, index);
            Assert.AreEqual(Scroller.GetCellStart(index), Scroller.ScrollPosition, POSITION_EPSILON, "같은 항목에 도착한다");
            yield return null;
            Assert.AreEqual(1, completed, "완료 콜백은 한 번");
        }

        [UnityTest]
        public IEnumerator Tween_TargetRemoved_StopsWithoutCallback()
        {
            Create(200);
            int completed = 0;
            int stopped = 0;
            Scroller.ScrollerTweeningChanged += (_, tweening) =>
            {
                if (!tweening)
                {
                    stopped++;
                }
            };

            Scroller.JumpToDataIndex(100, 0f, 0f, false, TweenType.Linear, 0.5f, () => completed++);
            yield return null;
            Assert.IsTrue(Scroller.IsTweening);
            float position = Scroller.ScrollPosition;

            _data.RemoveRange(100, 1);
            Scroller.RemoveCells(100, 1);
            Assert.IsFalse(Scroller.IsTweening, "대상이 지워지면 트윈을 멈춘다");
            Assert.AreEqual(1, stopped);
            Assert.AreEqual(position, Scroller.ScrollPosition, EPSILON, "지운 항목이 뷰포트 아래라 화면은 그대로");

            yield return new WaitForSecondsRealtime(0.6f);
            Assert.AreEqual(0, completed, "완료 콜백은 부르지 않는다");
            Assert.AreEqual(position, Scroller.ScrollPosition, EPSILON);
            AssertMatchesData("tween target removed");
        }

        [UnityTest]
        public IEnumerator Tween_TargetMoved_FollowsMovedItemOnce()
        {
            Create(200);
            int target = _data.Items[100];
            int completed = 0;
            Scroller.JumpToDataIndex(100, 0f, 0f, false, TweenType.Linear, 0.5f, () => completed++);
            yield return null;
            Assert.IsTrue(Scroller.IsTweening);
            float before = Scroller.ScrollPosition;

            // 트윈 대상만 뒤로 옮긴다. 화면(맨 앞 항목)은 그대로이고 트윈은 옮겨진 항목을 따라간다.
            _data.Move(100, 150);
            Scroller.MoveCell(100, 150);
            Assert.IsTrue(Scroller.IsTweening);
            Assert.AreEqual(before, Scroller.ScrollPosition, EPSILON, "옮긴 순간 화면은 그대로");

            float timeout = TWEEN_TIMEOUT;
            while (Scroller.IsTweening && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsFalse(Scroller.IsTweening);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(150, _data.Items.IndexOf(target));
            Assert.AreEqual(Scroller.GetCellStart(150), Scroller.ScrollPosition, POSITION_EPSILON, "옮겨진 항목에 도착한다");
            AssertMatchesData("tween target moved");
        }

        [UnityTest]
        public IEnumerator Drag_InsertAboveMidDrag_KeepsContentUnderFinger()
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

            // 맨 앞 항목(1번)보다 앞에 80짜리 2개: 160 옮겨 손가락 아래 콘텐츠가 그대로다.
            int top = _data.Items[1];
            float screen = ScreenOffsetOfItem(top);
            _data.Insert(0, NextItem(), 80f);
            _data.Insert(0, NextItem(), 80f);
            Scroller.InsertCells(0, 2);
            Assert.AreEqual(310f, Scroller.ScrollPosition, 1f);
            Assert.AreEqual(screen, ScreenOffsetOfItem(top), 1f);
            Assert.IsTrue(Scroller.IsDragging, "드래그는 끊지 않는다");

            // 드래그 기준점도 옮겨졌으면 이후 50px 이동은 310 + 50이다.
            eventData.position = start + new Vector2(0f, 200f);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
            yield return null;
            Assert.AreEqual(360f, Scroller.ScrollPosition, 1f);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);
            AssertMatchesData("after drag");
        }

        [Test]
        public void ItemIds_FollowInsertRemoveMove_AndOnlyInsertedAreQueried()
        {
            var data = (ListIdTestDelegate)Create(50, ids: true);
            int idCalls = data.GetItemIdCalls;
            int formerFirst = data.Items[0];
            int a = NextItem();
            int b = NextItem();
            data.Insert(0, a, 100f);
            data.Insert(1, b, 100f);
            Scroller.InsertCells(0, 2);

            Assert.AreEqual(idCalls + 2, data.GetItemIdCalls, "삽입분만 ID를 묻는다");
            Assert.AreEqual(0, Scroller.FindDataIndexForItemId(a));
            Assert.AreEqual(1, Scroller.FindDataIndexForItemId(b));
            Assert.AreEqual(2, Scroller.FindDataIndexForItemId(formerFirst));
            AssertIdLookups(data, "insert");

            int removed = data.Items[10];
            data.RemoveRange(10, 1);
            Scroller.RemoveCells(10, 1);
            Assert.AreEqual(-1, Scroller.FindDataIndexForItemId(removed));
            data.Move(40, 3);
            Scroller.MoveCell(40, 3);
            AssertIdLookups(data, "remove/move");
            Assert.AreEqual(idCalls + 2, data.GetItemIdCalls, "삭제·이동은 ID를 묻지 않는다");
            AssertMatchesData("ids");

            // 이미 있는 ID를 앞쪽에 삽입하면 다시 받을 때처럼 경고하고 앞 인덱스가 이긴다.
            int duplicate = data.Items[20];
            data.Insert(5, duplicate, 100f);
            LogAssert.Expect(LogType.Warning, new Regex("같은 ItemId"));
            Scroller.InsertCells(5, 1);
            Assert.AreEqual(5, Scroller.FindDataIndexForItemId(duplicate));
        }

        private void AssertIdLookups(ListIdTestDelegate data, string context)
        {
            for (int i = 0; i < data.Items.Count; i++)
            {
                Assert.AreEqual(i, Scroller.FindDataIndexForItemId(data.Items[i]), $"{context}: {i}번 ID");
            }
        }

        [Test]
        public void InsertRemoveMove_AfterWarmup_DoesNotAllocate()
        {
            Create(400, 50f, ids: true);
            Scroller.LookAheadAfter = 100f;
            Scroller.ScrollPosition = 5000f;

            // 데이터 목록·연산 목록·사전·풀 용량을 채운다.
            InsertRemoveMoveCycle();
            InsertRemoveMoveCycle();

            Assert.That(() => InsertRemoveMoveCycle(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            AssertMatchesData("after cycles");
        }

        /// <summary>뷰포트 위·안·아래에 삽입했다가 지우고, 배치로 몇 번 옮긴다. 개수와 항목 집합은 처음과 같아진다.</summary>
        private void InsertRemoveMoveCycle()
        {
            for (int i = 0; i < 12; i++)
            {
                int at = i * 9;
                _data.Insert(at, -1 - i, 50f);
                Scroller.InsertCells(at, 1);
            }

            for (int i = 11; i >= 0; i--)
            {
                int at = i * 9;
                _data.RemoveRange(at, 1);
                Scroller.RemoveCells(at, 1);
            }

            Scroller.BeginUpdates();
            for (int i = 0; i < 4; i++)
            {
                _data.Move(98 + i, 104 - i);
                Scroller.MoveCell(98 + i, 104 - i);
            }

            Scroller.EndUpdates();
        }

        #endregion
    }
}
