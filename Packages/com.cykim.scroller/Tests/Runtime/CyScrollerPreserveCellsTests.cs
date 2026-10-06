using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace CyKim.Scroller.Tests
{
    /// <summary>
    /// 키 유지 리로드(<see cref="CyScroller.PreserveCellsById"/>): 위치를 지키는 리로드(FirstVisible·LastVisible·앵커 지정·ReloadDataKeepingPosition)가
    /// 항목 ID가 같은 활성 셀을 다시 바인딩하지 않고 새 인덱스로 옮기는지, 옵션을 끈 리로드와 위치·화면이 같은지,
    /// 모두 다시 바인딩해야 하는 경우(ID 없음·처음부터 리로드·기다리는 리로드가 내용 갱신을 대신 처리·델리게이트 교체·증분 변경을 대신하는 리로드·
    /// 다시 읽지 않는 재배치와 축 전환), 셀을 맞추기 전에 사용자 코드가 부른 갱신, 사용자 예외 뒤 복구, 루프 사본, 트윈, GC.
    /// </summary>
    public class CyScrollerPreserveCellsTests
    {
        // 1만 근처 위치는 RectTransform 기록 뒤 읽은 값이 0.01 넘게 어긋날 수 있다 (float 정밀도).
        private const float EPSILON = 0.01f;
        private const float POSITION_EPSILON = 0.05f;

        private const int RANDOM_SEED = 20261046;
        private const int RANDOM_STEPS = 120;

        // 항목 값(= ID). 데이터 인덱스와 겹치지 않게 띄운다.
        private const int ITEM_BASE = 1000;

        // 트윈이 끝나기를 기다리는 상한(초).
        private const float TWEEN_TIMEOUT = 3f;

        private Harness _harness;
        private Harness _twin;
        private int _nextItem;

        [TearDown]
        public void TearDown()
        {
            _harness?.Fixture.Dispose();
            _twin?.Fixture.Dispose();
            _harness = null;
            _twin = null;
            _nextItem = 0;
        }

        /// <summary>List 델리게이트와 스크롤러, 표시·회수 이벤트 기록(짝 오류 포함).</summary>
        private sealed class Harness
        {
            public ScrollerFixture Fixture;
            public ListTestDelegate Data;
            public bool Logging;
            public readonly HashSet<CyScrollerCellView> Displayed = new HashSet<CyScrollerCellView>();
            public readonly List<string> Log = new List<string>();
            public int PairingErrors;

            public CyScroller Scroller => Fixture.Scroller;
        }

        /// <summary>항목 count개(크기 size, 값 ITEM_BASE + i)를 가진 List 델리게이트로 스크롤러를 만든다. log면 표시·회수 이벤트를 적는다.</summary>
        private Harness CreateHarness(int count, float size = 100f, bool ids = true, bool preserve = true, bool loop = false, float spacing = 0f,
            RectOffset padding = null, bool reload = true, bool log = true)
        {
            var harness = new Harness
            {
                Fixture = ScrollerFixture.Create(ScrollDirection.Vertical, TestDelegate.Uniform(count, size), loop, spacing, padding, reload: false),
            };

            harness.Data = ids ? new ListIdTestDelegate() : new ListTestDelegate();
            harness.Data.Prefab = harness.Fixture.Prefab;
            for (int i = 0; i < count; i++)
            {
                harness.Data.Insert(i, ITEM_BASE + i, size);
            }

            CyScroller scroller = harness.Scroller;
            Assert.IsFalse(scroller.PreserveCellsById, "기본값은 꺼짐");
            scroller.PreserveCellsById = preserve;
            harness.Logging = log;
            if (log)
            {
                scroller.CellViewWillDisplay += (_, view) =>
                {
                    if (!view.Active || !view.IsDisplayed || !harness.Displayed.Add(view))
                    {
                        harness.PairingErrors++;
                    }

                    harness.Log.Add("will " + ((TestCellView)view).BoundData);
                };
                scroller.CellViewDidEndDisplay += (_, view) =>
                {
                    if (view.IsDisplayed || !harness.Displayed.Remove(view))
                    {
                        harness.PairingErrors++;
                    }

                    harness.Log.Add("end " + ((TestCellView)view).BoundData);
                };
                scroller.CellViewWillRecycle += view => harness.Log.Add("recycle " + ((TestCellView)view).BoundData);
            }

            scroller.Delegate = harness.Data;
            if (reload)
            {
                scroller.ReloadData();
            }

            return harness;
        }

        private int NextItem() => ITEM_BASE + 100000 + _nextItem++;

        private static TestCellView CellForItem(Harness harness, int item)
        {
            IReadOnlyList<CyScrollerCellView> cells = harness.Scroller.ActiveCellViews;
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

        #region Records

        /// <summary>리로드 전 활성 셀 하나의 상태.</summary>
        private struct CellRecord
        {
            public TestCellView View;
            public int Item;
            public int Version;
            public int DataIndex;
            public int IndexChanges;
            public int Visible;
            public int Hidden;
            public int Recycled;
            public bool Displayed;
            public float Height;
        }

        private static List<CellRecord> Record(Harness harness)
        {
            var records = new List<CellRecord>();
            IReadOnlyList<CyScrollerCellView> cells = harness.Scroller.ActiveCellViews;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                if (view == null)
                {
                    // 사용자 예외로 셀 맞추기가 멈추면 회수한 자리가 빈 채로 다음 갱신을 기다린다.
                    continue;
                }

                records.Add(new CellRecord
                {
                    View = view,
                    Item = view.BoundData,
                    Version = view.BindVersion,
                    DataIndex = view.DataIndex,
                    IndexChanges = view.DataIndexChangedCount,
                    Visible = view.BecameVisibleCount,
                    Hidden = view.BecameHiddenCount,
                    Recycled = view.RecycledCount,
                    Displayed = view.IsDisplayed,
                    Height = view.RectTransform.rect.height,
                });
            }

            return records;
        }

        /// <summary>리로드 전 활성이었고 지금도 같은 바인딩(BindVersion 그대로)으로 활성인 셀 수.</summary>
        private static int CountKept(Harness harness, List<CellRecord> before)
        {
            int kept = 0;
            for (int i = 0; i < before.Count; i++)
            {
                CellRecord record = before[i];
                if (record.View.Active && record.View.BindVersion == record.Version)
                {
                    kept++;
                }
            }

            return kept;
        }

        /// <summary>
        /// 키 유지 결과: 항목마다 min(리로드 전 활성 사본 수, 지금 활성 사본 수)개의 셀이 다시 바인딩 없이 남고(같은 항목, 회수 없음),
        /// 델리게이트 GetCellView는 나머지 활성 자리 수만큼만 불렸는지. 남은 셀은 인덱스가 바뀌었으면 OnDataIndexChanged를 한 번(이전 인덱스와 함께) 받고,
        /// 리로드 전후로 계속 보이면 표시 가상 메서드를 받지 않는다.
        /// </summary>
        private static void AssertCellsPreserved(Harness harness, List<CellRecord> before, int bindsBefore, string context)
        {
            var copiesBefore = new Dictionary<int, int>();
            for (int i = 0; i < before.Count; i++)
            {
                copiesBefore.TryGetValue(before[i].Item, out int copies);
                copiesBefore[before[i].Item] = copies + 1;
            }

            var copiesAfter = new Dictionary<int, int>();
            IReadOnlyList<CyScrollerCellView> active = harness.Scroller.ActiveCellViews;
            for (int i = 0; i < active.Count; i++)
            {
                int item = ((TestCellView)active[i]).BoundData;
                copiesAfter.TryGetValue(item, out int copies);
                copiesAfter[item] = copies + 1;
            }

            int expectedKept = 0;
            foreach (KeyValuePair<int, int> pair in copiesBefore)
            {
                if (copiesAfter.TryGetValue(pair.Key, out int after))
                {
                    expectedKept += Mathf.Min(pair.Value, after);
                }
            }

            int kept = 0;
            for (int i = 0; i < before.Count; i++)
            {
                CellRecord record = before[i];
                TestCellView view = record.View;
                if (!view.Active || view.BindVersion != record.Version)
                {
                    continue;
                }

                kept++;
                Assert.AreEqual(record.Item, view.BoundData, $"{context}: 남은 셀은 같은 항목 {record.Item}");
                Assert.AreEqual(record.Recycled, view.RecycledCount, $"{context}: 항목 {record.Item}의 셀은 회수되지 않았다");
                int expectedChanges = view.DataIndex != record.DataIndex ? 1 : 0;
                Assert.AreEqual(expectedChanges, view.DataIndexChangedCount - record.IndexChanges,
                    $"{context}: 항목 {record.Item} 인덱스 변경 알림 ({record.DataIndex} → {view.DataIndex})");
                if (expectedChanges > 0)
                {
                    Assert.AreEqual(record.DataIndex, view.LastPreviousDataIndex, $"{context}: 항목 {record.Item}이 받은 이전 인덱스");
                }

                if (record.Displayed && view.IsDisplayed)
                {
                    Assert.AreEqual(record.Visible, view.BecameVisibleCount, $"{context}: 계속 보이는 항목 {record.Item}은 표시 시작을 받지 않는다");
                    Assert.AreEqual(record.Hidden, view.BecameHiddenCount, $"{context}: 계속 보이는 항목 {record.Item}은 표시 끝을 받지 않는다");
                }
            }

            Assert.AreEqual(expectedKept, kept, $"{context}: 다시 바인딩하지 않고 남은 셀 수");
            Assert.AreEqual(active.Count - kept, harness.Data.GetCellViewCalls - bindsBefore, $"{context}: 남은 셀이 채우지 못한 자리만 바인딩한다");
        }

        /// <summary>모든 활성 셀을 다시 바인딩했는지 (리로드 전 셀이 같은 바인딩으로 남지 않고, 활성 셀 수만큼 바인딩).</summary>
        private static void AssertAllRebound(Harness harness, List<CellRecord> before, int bindsBefore, string context)
        {
            Assert.AreEqual(0, CountKept(harness, before), $"{context}: 같은 바인딩으로 남은 셀이 없다");
            Assert.AreEqual(harness.Scroller.ActiveCellViews.Count, harness.Data.GetCellViewCalls - bindsBefore, $"{context}: 활성 셀을 모두 바인딩한다");
        }

        /// <summary>
        /// 스크롤러가 델리게이트 데이터와 맞는지: 개수·크기, 활성 범위가 뷰포트 + 미리보기 구간을 정확히 덮는지, 활성 셀마다 슬롯·인덱스·바인딩 항목·ID·
        /// RectTransform 위치·크기가 레이아웃과 같은지, 표시 플래그·이벤트 짝·가상 메서드 짝이 실제 뷰포트와 맞는지(루프 사본 포함).
        /// </summary>
        private static void AssertConsistent(Harness harness, string context)
        {
            CyScroller scroller = harness.Scroller;
            ListTestDelegate data = harness.Data;
            Assert.AreEqual(data.Items.Count, scroller.NumberOfCells, $"{context}: 개수");
            for (int i = 0; i < data.Items.Count; i++)
            {
                Assert.AreEqual(data.Sizes[i], scroller.GetCellSize(i), EPSILON, $"{context}: 크기 {i}");
            }

            float position = scroller.ScrollPosition;
            float viewEnd = position + scroller.ScrollRectSize;
            scroller.Layout.GetSlotRange(position - scroller.LookAheadBefore, viewEnd + scroller.LookAheadAfter, out int first, out int last);
            IReadOnlyList<CyScrollerCellView> cells = scroller.ActiveCellViews;
            int expectedCount = first <= last ? last - first + 1 : 0;
            Assert.AreEqual(expectedCount, cells.Count, $"{context}: 활성 셀 수 (위치 {position})");

            bool hasIds = data is ListIdTestDelegate;
            int displayed = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                int slot = first + i;
                Assert.IsNotNull(view, $"{context}: 빈 자리 {slot}");
                int dataIndex = scroller.GetDataIndexForCellViewIndex(slot);
                Assert.AreEqual(slot, view.CellIndex, $"{context}: CellIndex");
                Assert.AreEqual(dataIndex, view.DataIndex, $"{context}: 슬롯 {slot} DataIndex");
                Assert.AreEqual(data.Items[dataIndex], view.BoundData, $"{context}: 슬롯 {slot}에 바인딩된 항목");
                Assert.IsTrue(view.Active && view.gameObject.activeSelf, $"{context}: 슬롯 {slot} 활성");
                Assert.AreEqual(hasIds, view.HasItemId, $"{context}: HasItemId");
                if (hasIds)
                {
                    Assert.AreEqual((long)data.Items[dataIndex], view.ItemId, $"{context}: 슬롯 {slot} ItemId");
                }

                float start = scroller.GetScrollPositionForCellViewIndex(slot);
                float end = start + scroller.GetCellSize(dataIndex);
                Assert.AreEqual(-start, view.RectTransform.offsetMax.y, POSITION_EPSILON, $"{context}: 슬롯 {slot} 위치");
                Assert.AreEqual(-end, view.RectTransform.offsetMin.y, POSITION_EPSILON, $"{context}: 슬롯 {slot} 끝 (새 크기)");

                bool overlaps = start < viewEnd && end > position;
                Assert.AreEqual(overlaps, view.IsDisplayed, $"{context}: 슬롯 {slot} IsDisplayed");
                Assert.AreEqual(overlaps ? 1 : 0, view.BecameVisibleCount - view.BecameHiddenCount, $"{context}: 슬롯 {slot} 가상 메서드 짝");
                if (harness.Logging)
                {
                    Assert.AreEqual(overlaps, harness.Displayed.Contains(view), $"{context}: 슬롯 {slot} 표시 이벤트 짝");
                }

                if (overlaps)
                {
                    displayed++;
                }
            }

            if (harness.Logging)
            {
                Assert.AreEqual(displayed, harness.Displayed.Count, $"{context}: 표시 중인 셀 수");
            }

            Assert.AreEqual(0, harness.PairingErrors, $"{context}: 표시 이벤트 짝 오류");
        }

        /// <summary>두 스크롤러의 위치·활성 범위와 슬롯마다 바인딩 항목·표시 여부·셀 위치가 같은지 (키 유지 켬/끔 결과 비교).</summary>
        private static void AssertSameScreen(Harness expected, Harness actual, string context)
        {
            Assert.AreEqual(expected.Scroller.ScrollPosition, actual.Scroller.ScrollPosition, POSITION_EPSILON, $"{context}: 스크롤 위치");
            Assert.AreEqual(expected.Scroller.StartCellViewIndex, actual.Scroller.StartCellViewIndex, $"{context}: 활성 시작");
            Assert.AreEqual(expected.Scroller.EndCellViewIndex, actual.Scroller.EndCellViewIndex, $"{context}: 활성 끝");
            IReadOnlyList<CyScrollerCellView> a = expected.Scroller.ActiveCellViews;
            IReadOnlyList<CyScrollerCellView> b = actual.Scroller.ActiveCellViews;
            Assert.AreEqual(a.Count, b.Count, $"{context}: 활성 셀 수");
            for (int i = 0; i < a.Count; i++)
            {
                var left = (TestCellView)a[i];
                var right = (TestCellView)b[i];
                Assert.AreEqual(left.BoundData, right.BoundData, $"{context}: 슬롯 {left.CellIndex} 항목");
                Assert.AreEqual(left.IsDisplayed, right.IsDisplayed, $"{context}: 슬롯 {left.CellIndex} 표시");
                Assert.AreEqual(left.RectTransform.offsetMax.y, right.RectTransform.offsetMax.y, POSITION_EPSILON, $"{context}: 슬롯 {left.CellIndex} 화면 위치");
            }
        }

        #endregion

        #region On/Off

        [Test]
        public void FirstVisibleReload_InsertAbove_ReusesCellsOnlyWhenEnabled()
        {
            _harness = CreateHarness(100);
            _twin = CreateHarness(100, preserve: false);
            Harness[] both = { _harness, _twin };
            foreach (Harness h in both)
            {
                h.Scroller.LookAheadAfter = 150f;
                h.Scroller.ScrollPosition = 1050f;   // 뷰포트 [1050, 1450]: 10~14번 표시, 15번은 미리보기
                Assert.AreEqual(6, h.Scroller.ActiveCellViews.Count);
            }

            List<CellRecord> onBefore = Record(_harness);
            List<CellRecord> offBefore = Record(_twin);
            int onBinds = _harness.Data.GetCellViewCalls;
            int offBinds = _twin.Data.GetCellViewCalls;
            _harness.Log.Clear();
            _twin.Log.Clear();

            // 앞쪽에 3개가 들어와 같은 항목의 인덱스가 3씩 밀렸다. 증분 알림 없이 위치 유지 리로드로 다시 읽는다.
            for (int i = 0; i < 3; i++)
            {
                int item = NextItem();
                _harness.Data.Insert(0, item, 100f);
                _twin.Data.Insert(0, item, 100f);
            }

            _harness.Scroller.ReloadData(ReloadAnchor.FirstVisible);
            _twin.Scroller.ReloadData(ReloadAnchor.FirstVisible);

            Assert.AreEqual(1350f, _harness.Scroller.ScrollPosition, EPSILON, "맨 앞 항목(10번 → 13번)과 오프셋 유지");
            AssertSameScreen(_twin, _harness, "on vs off");
            AssertConsistent(_harness, "on");
            AssertConsistent(_twin, "off");

            // 켬: 같은 셀을 새 인덱스로 옮기기만 한다 (바인딩·회수·표시 이벤트 없음, 인덱스 변경 알림).
            AssertCellsPreserved(_harness, onBefore, onBinds, "on");
            Assert.AreEqual(0, _harness.Data.GetCellViewCalls - onBinds, "켬: GetCellView 0회");
            CollectionAssert.IsEmpty(_harness.Log, "켬: 표시·회수 이벤트 없음");
            for (int i = 0; i < onBefore.Count; i++)
            {
                Assert.AreEqual(onBefore[i].DataIndex + 3, onBefore[i].View.DataIndex, $"켬: 항목 {onBefore[i].Item} 새 인덱스");
            }

            // 끔: 지금처럼 모두 회수하고 다시 바인딩한다 (보이던 셀은 표시 끝 → 회수, 새 셀은 표시 시작).
            AssertAllRebound(_twin, offBefore, offBinds, "off");
            Assert.AreEqual(6, _twin.Data.GetCellViewCalls - offBinds);
            Assert.AreEqual(5, CountLog(_twin, "end "), "끔: 보이던 5개 표시 끝");
            Assert.AreEqual(6, CountLog(_twin, "recycle "), "끔: 6개 회수");
            Assert.AreEqual(5, CountLog(_twin, "will "), "끔: 5개 표시 시작");
        }

        [Test]
        public void FirstVisibleReload_RemovedIdsAreRecycled_NewIdsAreBound()
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.LookAheadAfter = 150f;
            scroller.ScrollPosition = 1050f;   // 10~14번 표시, 15번 미리보기
            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;
            TestCellView removedVisible = CellForItem(_harness, ITEM_BASE + 12);
            TestCellView removedLookAhead = CellForItem(_harness, ITEM_BASE + 15);
            int visibleHidden = removedVisible.BecameHiddenCount;
            int lookAheadHidden = removedLookAhead.BecameHiddenCount;
            _harness.Log.Clear();

            // 보이던 12번과 미리보기 15번을 지우고 그 사이에 새 항목을 넣는다: 10, 11, 새 항목, 13, 14, 16
            ListTestDelegate data = _harness.Data;
            data.RemoveRange(15, 1);
            data.RemoveRange(12, 1);
            int added = NextItem();
            data.Insert(12, added, 100f);

            scroller.ReloadData(ReloadAnchor.FirstVisible);

            Assert.AreEqual(1050f, scroller.ScrollPosition, EPSILON);
            AssertConsistent(_harness, "after");
            AssertCellsPreserved(_harness, before, binds, "after");
            Assert.AreEqual(2, data.GetCellViewCalls - binds, "새 항목과 새로 미리보기 구간에 들어온 16번만 바인딩한다");
            Assert.AreEqual(visibleHidden + 1, removedVisible.BecameHiddenCount, "지운 보이던 셀은 표시 끝을 받는다");
            Assert.Less(_harness.Log.IndexOf("end " + (ITEM_BASE + 12)), _harness.Log.IndexOf("recycle " + (ITEM_BASE + 12)), "표시 끝 → 회수");
            Assert.Greater(_harness.Log.IndexOf("recycle " + (ITEM_BASE + 15)), -1, "지운 미리보기 셀도 회수한다");
            Assert.AreEqual(-1, _harness.Log.IndexOf("end " + (ITEM_BASE + 15)), "보이지 않던 셀은 표시 끝이 없다");
            Assert.Greater(_harness.Log.IndexOf("will " + added), _harness.Log.IndexOf("recycle " + (ITEM_BASE + 15)), "새 항목 표시 시작은 회수 뒤");
            Assert.AreEqual(4, _harness.Log.Count, "표시 끝 1·회수 2·새 항목 표시 시작 1만 (남은 셀은 이벤트 없음)");
            Assert.AreEqual(lookAheadHidden, removedLookAhead.BecameHiddenCount, "보이지 않던 셀은 표시 끝 가상 메서드도 없다");
        }

        [Test]
        public void WithoutItemIdProvider_OptionOn_RebindsEverythingLikeBefore()
        {
            _harness = CreateHarness(100, ids: false);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 1050f;
            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;

            _harness.Data.Insert(0, NextItem(), 100f);
            scroller.ReloadData(ReloadAnchor.FirstVisible);

            // ID가 없으면 같은 인덱스를 지키고(지금과 같다) 모두 다시 바인딩한다.
            Assert.AreEqual(1050f, scroller.ScrollPosition, EPSILON);
            AssertConsistent(_harness, "no ids");
            AssertAllRebound(_harness, before, binds, "no ids");
        }

        [Test]
        public void StartOverReloads_IgnoreOption_AndRebindEverything()
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;

            // 처음에 있는 같은 항목들을 다시 그리는 리로드여도 옵션과 무관하게 모두 다시 바인딩한다.
            System.Action[] reloads =
            {
                () => scroller.ReloadData(),
                () => scroller.ReloadData(0f),
                () => scroller.ReloadData(ReloadAnchor.Start),
                () => scroller.ReloadData(ReloadAnchor.Factor),
            };

            for (int i = 0; i < reloads.Length; i++)
            {
                List<CellRecord> before = Record(_harness);
                int binds = _harness.Data.GetCellViewCalls;
                reloads[i]();
                Assert.AreEqual(0f, scroller.ScrollPosition, EPSILON);
                AssertConsistent(_harness, $"reload {i}");
                AssertAllRebound(_harness, before, binds, $"reload {i}");
            }

            scroller.ScrollPosition = scroller.ScrollSize;
            List<CellRecord> beforeEnd = Record(_harness);
            int bindsEnd = _harness.Data.GetCellViewCalls;
            scroller.ReloadData(ReloadAnchor.End);
            Assert.AreEqual(scroller.ScrollSize, scroller.ScrollPosition, EPSILON);
            AssertAllRebound(_harness, beforeEnd, bindsEnd, "End");
        }

        [Test]
        public void KeepingPositionAndAnchorReloads_ReuseCells()
        {
            _harness = CreateHarness(100);
            _twin = CreateHarness(100, preserve: false);
            Harness[] both = { _harness, _twin };
            foreach (Harness h in both)
            {
                h.Scroller.ScrollPosition = 1050f;
            }

            // ReloadDataKeepingPosition: 앞쪽 2개 삭제 → 같은 항목이 2씩 당겨진다.
            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;
            foreach (Harness h in both)
            {
                h.Data.RemoveRange(0, 2);
                h.Scroller.ReloadDataKeepingPosition();
            }

            Assert.AreEqual(850f, _harness.Scroller.ScrollPosition, EPSILON);
            AssertSameScreen(_twin, _harness, "keeping position");
            AssertConsistent(_harness, "keeping position");
            AssertCellsPreserved(_harness, before, binds, "keeping position");
            Assert.AreEqual(0, _harness.Data.GetCellViewCalls - binds);

            // ReloadData(in anchor): 저장해 둔 앵커(맨 앞 항목 ID)로 다시 읽는다.
            CyScrollerAnchor saved = _harness.Scroller.CaptureAnchor();
            before = Record(_harness);
            binds = _harness.Data.GetCellViewCalls;
            foreach (Harness h in both)
            {
                h.Data.Insert(0, ITEM_BASE - 1, 100f);
                h.Data.Insert(0, ITEM_BASE - 2, 100f);
                h.Data.Insert(0, ITEM_BASE - 3, 100f);
                h.Scroller.ReloadData(in saved);
            }

            Assert.AreEqual(1150f, _harness.Scroller.ScrollPosition, EPSILON);
            AssertSameScreen(_twin, _harness, "anchor");
            AssertConsistent(_harness, "anchor");
            AssertCellsPreserved(_harness, before, binds, "anchor");
            Assert.AreEqual(0, _harness.Data.GetCellViewCalls - binds);
        }

        [Test]
        public void LastVisibleReload_SizeChangeAndAppend_ReusesCellsWithNewSize()
        {
            _harness = CreateHarness(100);
            _twin = CreateHarness(100, preserve: false);
            Harness[] both = { _harness, _twin };
            foreach (Harness h in both)
            {
                h.Scroller.ScrollPosition = h.Scroller.ScrollSize;   // 끝: 96~99번 표시
            }

            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;
            TestCellView resized = CellForItem(_harness, ITEM_BASE + 98);
            foreach (Harness h in both)
            {
                h.Data.Sizes[98] = 40f;   // 보이던 셀이 줄었다
                h.Data.Insert(100, ITEM_BASE + 100, 100f);
                h.Data.Insert(101, ITEM_BASE + 101, 100f);
                h.Scroller.ReloadData(ReloadAnchor.LastVisible);
            }

            // 99번 끝이 뷰포트 끝에 남는다: 끝 9940 − 400. 뒤에 붙은 항목은 그 아래다.
            Assert.AreEqual(9540f, _harness.Scroller.ScrollPosition, EPSILON);
            AssertSameScreen(_twin, _harness, "last visible");
            AssertConsistent(_harness, "last visible");
            AssertCellsPreserved(_harness, before, binds, "last visible");
            Assert.AreEqual(1, _harness.Data.GetCellViewCalls - binds, "위로 새로 보이는 95번만 바인딩한다");
            Assert.AreSame(resized, CellForItem(_harness, ITEM_BASE + 98));
            Assert.AreEqual(40f, resized.RectTransform.rect.height, EPSILON, "옮긴 셀은 새로 받은 크기로 배치한다");
            Assert.AreEqual(0, resized.DataIndexChangedCount, "인덱스가 그대로면 알리지 않는다");
        }

        #endregion

        #region Loop

        [Test]
        public void LoopMode_FirstVisibleReload_ReusesCellsWithoutDisplayEvents()
        {
            _harness = CreateHarness(12, loop: true);
            _twin = CreateHarness(12, preserve: false, loop: true);
            Harness[] both = { _harness, _twin };
            foreach (Harness h in both)
            {
                Assert.IsTrue(h.Scroller.Layout.IsLoop);
                h.Scroller.ScrollPosition = 2750f;   // 가운데 세트 3번 안 50: 3~7번 표시
            }

            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;
            _harness.Log.Clear();
            int item = NextItem();
            foreach (Harness h in both)
            {
                h.Data.Insert(0, item, 100f);
                h.Scroller.ReloadData(ReloadAnchor.FirstVisible);
            }

            Assert.AreEqual(3050f, _harness.Scroller.ScrollPosition, EPSILON, "새 사이클(1300)의 가운데 세트에서 같은 항목·오프셋");
            AssertSameScreen(_twin, _harness, "loop");
            AssertConsistent(_harness, "loop on");
            AssertConsistent(_twin, "loop off");
            AssertCellsPreserved(_harness, before, binds, "loop");
            Assert.AreEqual(0, _harness.Data.GetCellViewCalls - binds);
            CollectionAssert.IsEmpty(_harness.Log, "같은 항목이 같은 화면 자리에 남아 이벤트가 없다");
        }

        [Test]
        public void LoopMode_ShortList_MovesCopiesToNearestScreenPlace()
        {
            // 3개 × 100, 뷰포트 400: 한 화면에 같은 항목의 사본이 둘씩 보인다.
            _harness = CreateHarness(3, loop: true);
            _twin = CreateHarness(3, preserve: false, loop: true);
            Harness[] both = { _harness, _twin };
            foreach (Harness h in both)
            {
                h.Scroller.ScrollPosition = 650f;   // 슬롯 6~10: 0, 1, 2, 0, 1번
            }

            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;
            TestCellView lowerCopy = (TestCellView)_harness.Scroller.ActiveCellViews[3];   // 아래쪽 0번 사본 (화면 250)
            Assert.AreEqual(ITEM_BASE, lowerCopy.BoundData);
            int lowerVisible = lowerCopy.BecameVisibleCount;
            _harness.Log.Clear();
            int added = NextItem();
            foreach (Harness h in both)
            {
                h.Data.Insert(3, added, 100f);   // 끝에 붙인다: 사이클 400
                h.Scroller.ReloadData(ReloadAnchor.FirstVisible);
            }

            // 슬롯 8~12: 0, 1, 2, 새 항목, 0번. 위 셀 넷은 같은 화면 자리, 아래 0번 사본은 가장 가까운 사본(화면 350)으로 옮긴다.
            Assert.AreEqual(850f, _harness.Scroller.ScrollPosition, EPSILON);
            AssertSameScreen(_twin, _harness, "short loop");
            AssertConsistent(_harness, "short loop");
            AssertCellsPreserved(_harness, before, binds, "short loop");
            Assert.AreEqual(1, _harness.Data.GetCellViewCalls - binds, "새 항목만 바인딩한다");
            Assert.AreSame(lowerCopy, _harness.Scroller.ActiveCellViews[4], "아래쪽 0번 사본은 가장 가까운 0번 자리로 간다");
            Assert.AreEqual(lowerVisible, lowerCopy.BecameVisibleCount, "계속 보이므로 표시 이벤트가 없다");
            Assert.IsTrue(lowerCopy.IsDisplayed);
            Assert.AreEqual(0, lowerCopy.DataIndexChangedCount, "같은 데이터 인덱스의 다른 사본이라 인덱스 알림이 없다");

            // 사본이 줄어든 1번의 아래쪽 셀만 표시 끝 → 회수, 새 항목은 표시 시작.
            CollectionAssert.AreEqual(new[] { "end " + (ITEM_BASE + 1), "recycle " + (ITEM_BASE + 1), "will " + added }, _harness.Log);
        }

        #endregion

        #region Full Rebind Cases

        [Test]
        public void BatchWithContentUpdate_ReplacedByAnchorReload_RebindsEverything()
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 1050f;
            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;

            // 배치의 내용 갱신은 다시 읽는 요청이 대신 처리하므로 키 유지 없이 모두 다시 바인딩한다.
            scroller.BeginUpdates();
            scroller.RefreshCells(12, 1, 1);
            _harness.Data.Insert(0, NextItem(), 100f);
            scroller.ReloadData(ReloadAnchor.FirstVisible);
            scroller.EndUpdates();

            Assert.AreEqual(1150f, scroller.ScrollPosition, EPSILON);
            AssertConsistent(_harness, "content batch");
            AssertAllRebound(_harness, before, binds, "content batch");
        }

        [Test]
        public void BatchWithStructuralOpsOnly_ReplacedByAnchorReload_StillReusesCells()
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 1050f;
            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;

            // 구조 연산은 ID가 따라가므로 키 유지 리로드가 그대로 맡는다 (기록한 연산 대신 다시 읽는다).
            scroller.BeginUpdates();
            _harness.Data.Insert(0, NextItem(), 100f);
            scroller.InsertCells(0, 1);
            _harness.Data.Insert(0, NextItem(), 100f);
            scroller.ReloadData(ReloadAnchor.FirstVisible);
            scroller.EndUpdates();

            Assert.AreEqual(1250f, scroller.ScrollPosition, EPSILON);
            AssertConsistent(_harness, "structural batch");
            AssertCellsPreserved(_harness, before, binds, "structural batch");
            Assert.AreEqual(0, _harness.Data.GetCellViewCalls - binds);
        }

        [UnityTest]
        public IEnumerator DeferredAnchorReload_ReusesCells()
        {
            yield return RunDeferredReload(0);
        }

        [UnityTest]
        public IEnumerator DeferredAnchorReload_RefreshWhilePending_RebindsEverything()
        {
            yield return RunDeferredReload(1);
        }

        [UnityTest]
        public IEnumerator DeferredKeepingPosition_ReloadCellViewWhilePending_RebindsEverything()
        {
            yield return RunDeferredReload(2);
        }

        [UnityTest]
        public IEnumerator InsertCellsInsideCallback_ReplacedByAnchorReload_RebindsEverything()
        {
            yield return RunDeferredReload(3);
        }

        /// <summary>
        /// 표시 시작 콜백 안에서 앞쪽에 삽입하고 위치 유지 리로드를 요청한다(범위 갱신 뒤로 미뤄진다). kind 0은 그대로(키 유지),
        /// 1은 이어서 RefreshCells, 2는 ReloadDataKeepingPosition 뒤 ReloadCellView를 부른다(기다리는 리로드가 대신 처리하므로 모두 다시 바인딩).
        /// 3은 리로드 대신 InsertCells로 알린다(콜백 안 증분 변경을 대신하는 앵커 보존 리로드라 옵션과 무관하게 모두 다시 바인딩).
        /// </summary>
        private IEnumerator RunDeferredReload(int kind)
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            bool requested = false;
            scroller.CellViewWillDisplay += (s, view) =>
            {
                if (requested || ((TestCellView)view).BoundData != ITEM_BASE + 15)
                {
                    return;
                }

                requested = true;
                _harness.Data.Insert(0, NextItem(), 100f);
                if (kind == 3)
                {
                    s.InsertCells(0, 1);
                }
                else if (kind == 2)
                {
                    s.ReloadDataKeepingPosition();
                    s.ReloadCellView(13);
                }
                else
                {
                    s.ReloadData(ReloadAnchor.FirstVisible);
                    if (kind == 1)
                    {
                        s.RefreshCells(13, 1, 2);
                    }
                }
            };

            scroller.ScrollPosition = 1150f;   // 15번이 뷰포트로 들어온다
            Assert.IsTrue(requested);
            Assert.AreEqual(100, scroller.NumberOfCells, "범위 갱신 중에는 다시 읽지 않는다");
            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;

            yield return null;

            Assert.AreEqual(101, scroller.NumberOfCells);
            Assert.AreEqual(1250f, scroller.ScrollPosition, EPSILON, "같은 항목(11번 → 12번)을 지킨다");
            AssertConsistent(_harness, $"deferred {kind}");
            if (kind == 0)
            {
                AssertCellsPreserved(_harness, before, binds, "deferred");
                Assert.AreEqual(0, _harness.Data.GetCellViewCalls - binds);
            }
            else
            {
                AssertAllRebound(_harness, before, binds, $"deferred {kind}");
            }
        }

        [Test]
        public void DelegateReplaced_ThenAnchorReload_RebindsWithNewDelegate()
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 1050f;
            List<CellRecord> before = Record(_harness);

            // 같은 ID를 주지만 다른 셀 종류를 쓰는 델리게이트. 옛 델리게이트가 바인딩한 셀은 쓰지 않는다.
            var replacement = new ListIdTestDelegate { Prefab = _harness.Fixture.AltPrefab };
            for (int i = 0; i < _harness.Data.Items.Count; i++)
            {
                replacement.Insert(i, _harness.Data.Items[i], 100f);
            }

            scroller.Delegate = replacement;
            scroller.ReloadData(ReloadAnchor.FirstVisible);

            Assert.AreEqual(1050f, scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(0, CountKept(_harness, before), "옛 셀은 남지 않는다");
            IReadOnlyList<CyScrollerCellView> cells = scroller.ActiveCellViews;
            Assert.AreEqual(cells.Count, replacement.GetCellViewCalls, "새 델리게이트가 모두 바인딩한다");
            for (int i = 0; i < cells.Count; i++)
            {
                Assert.AreEqual(_harness.Fixture.AltPrefab.CellIdentifier, cells[i].CellIdentifier, $"{i}번째 셀 종류");
            }
        }

        [Test]
        public void DuplicateIds_KeepCellsInPlaceWhileDataIsUnchanged_AndMoveOneCellPerPlace()
        {
            _harness = CreateHarness(100, reload: false);
            CyScroller scroller = _harness.Scroller;
            ListTestDelegate data = _harness.Data;
            int duplicate = data.Items[11];
            data.Items[13] = duplicate;   // 11번과 13번이 같은 ID
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("같은 ItemId"));
            scroller.ReloadData();
            scroller.ScrollPosition = 1050f;   // 10~14번
            TestCellView first = (TestCellView)scroller.ActiveCellViews[1];
            TestCellView second = (TestCellView)scroller.ActiveCellViews[3];
            Assert.AreEqual(duplicate, first.BoundData);
            Assert.AreEqual(duplicate, second.BoundData);

            // 데이터가 그대로면 같은 ID 셀도 각자 이전 인덱스 자리에 남는다.
            List<CellRecord> before = Record(_harness);
            int binds = data.GetCellViewCalls;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("같은 ItemId"));
            scroller.ReloadData(ReloadAnchor.FirstVisible);
            AssertConsistent(_harness, "unchanged");
            Assert.AreEqual(before.Count, CountKept(_harness, before), "모든 셀이 남는다");
            Assert.AreEqual(0, data.GetCellViewCalls - binds);
            Assert.AreEqual(11, first.DataIndex);
            Assert.AreEqual(13, second.DataIndex);

            // 앞에 삽입해 인덱스가 밀리면 같은 ID는 앞 인덱스로 찾는다. 한 자리에는 셀 하나만 옮기고 나머지는 회수한 뒤 그 자리를 새로 바인딩한다.
            before = Record(_harness);
            binds = data.GetCellViewCalls;
            data.Insert(0, NextItem(), 100f);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("같은 ItemId"));
            scroller.ReloadData(ReloadAnchor.FirstVisible);
            AssertConsistent(_harness, "shifted");
            Assert.AreEqual(1150f, scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(before.Count - 1, CountKept(_harness, before), "같은 ID 셀 하나만 다시 바인딩한다");
            Assert.AreEqual(1, data.GetCellViewCalls - binds);
            Assert.AreEqual(12, first.DataIndex, "앞 셀이 앞 인덱스(12번)를 맡는다");
            Assert.AreEqual(1, second.RecycledCount - before[3].Recycled, "뒤 셀은 회수됐다");
        }

        [UnityTest]
        public IEnumerator UserExceptionDuringPreservingReload_RecoversWithFullReload()
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 1050f;
            bool thrown = false;
            scroller.CellViewWillRecycle += view =>
            {
                if (!thrown)
                {
                    thrown = true;
                    throw new System.InvalidOperationException("회수 핸들러 예외");
                }
            };

            // 보이던 12번을 지워 키 유지 리로드가 그 셀을 회수하게 한다. 핸들러 예외는 호출자에게 올라간다.
            _harness.Data.RemoveRange(12, 1);
            _harness.Data.Insert(0, NextItem(), 100f);
            Assert.Throws<System.InvalidOperationException>(() => scroller.ReloadData(ReloadAnchor.FirstVisible));
            Assert.IsTrue(thrown);
            Assert.AreEqual(1150f, scroller.ScrollPosition, EPSILON, "위치는 이미 새 배치로 맞췄다");
            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;

            // 멈춘 셀 맞추기는 다음 갱신에 모두 다시 바인딩하는 리로드로 맞춘다.
            yield return null;

            Assert.AreEqual(1150f, scroller.ScrollPosition, EPSILON);
            Assert.AreEqual(0, CountKept(_harness, before), "남은 셀 없이 모두 다시 바인딩한다");
            Assert.Greater(_harness.Data.GetCellViewCalls, binds);
            AssertConsistent(_harness, "recovered");
        }

        [Test]
        public void RefreshCellsFromTweenStopHandlerDuringReload_ReachesItemsNewCell()
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 1050f;
            scroller.JumpToDataIndex(60, 0f, 0f, false, TweenType.Linear, 1f);
            Assert.IsTrue(scroller.IsTweening);

            // 리로드가 트윈을 멈추며 부르는 알림 안의 RefreshCells는 새 인덱스다 (델리게이트는 이미 새 데이터). 셀을 맞춘 뒤 그 항목의 셀에 한 번 부른다.
            bool armed = false;
            scroller.ScrollerTweeningChanged += (s, tweening) =>
            {
                if (armed && !tweening)
                {
                    armed = false;
                    s.RefreshCells(13, 1, 4);
                }
            };

            for (int i = 0; i < 3; i++)
            {
                _harness.Data.Insert(0, NextItem(), 100f);
            }

            armed = true;
            scroller.ReloadData(ReloadAnchor.FirstVisible);

            Assert.IsFalse(scroller.IsTweening);
            AssertConsistent(_harness, "after reload");
            IReadOnlyList<CyScrollerCellView> cells = scroller.ActiveCellViews;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                bool target = view.BoundData == ITEM_BASE + 10;
                Assert.AreEqual(target ? 1 : 0, view.MaskedRefreshCount, $"항목 {view.BoundData} 내용 갱신 수");
                if (target)
                {
                    Assert.AreEqual(4, view.LastChangeMask);
                    Assert.AreEqual(13, view.DataIndex);
                }
            }
        }

        /// <summary>
        /// 다시 읽는 중(새 데이터 22번의 크기를 물을 때) 델리게이트가 새 인덱스 22번 갱신을 알린다(바로 또는 갱신만 있는 배치로).
        /// 키 유지 재배치(ReloadDataKeepingPosition)도 키 유지 리로드(FirstVisible)처럼 셀을 새 인덱스로 맞춘 뒤 그 항목(옛 20번)의 셀에 한 번 부르고,
        /// 아직 옛 인덱스 22번인 셀(옛 22번 항목, 새 24번)은 받지 않는다.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void RefreshCellsFromDelegateDuringPreservingReload_ReachesItemsNewCell(bool keepingPosition, bool batch)
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 2050f;   // 20~24번 표시
            Assert.AreEqual(ITEM_BASE + 22, ((TestCellView)scroller.ActiveCellViews[2]).BoundData);

            bool armed = false;
            _harness.Data.GetCellViewSizeHook = (s, index) =>
            {
                if (!armed || index != 22)
                {
                    return;
                }

                armed = false;
                if (batch)
                {
                    s.BeginUpdates();
                    s.RefreshCells(22, 1, 8);
                    s.EndUpdates();
                }
                else
                {
                    s.RefreshCells(22, 1, 8);
                }
            };

            // 셀마다 갱신을 받은 순간의 인덱스를 적는다 (새 인덱스로 맞춘 뒤 받았는지).
            var refreshedAt = new Dictionary<int, int>();
            IReadOnlyList<CyScrollerCellView> cells = scroller.ActiveCellViews;
            for (int i = 0; i < cells.Count; i++)
            {
                ((TestCellView)cells[i]).RefreshHook = (view, mask) => refreshedAt[view.BoundData] = view.DataIndex;
            }

            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;
            _harness.Data.Insert(0, NextItem(), 100f);
            _harness.Data.Insert(0, NextItem(), 100f);
            armed = true;
            if (keepingPosition)
            {
                scroller.ReloadDataKeepingPosition();
            }
            else
            {
                scroller.ReloadData(ReloadAnchor.FirstVisible);
            }

            string context = (keepingPosition ? "keeping position" : "first visible") + (batch ? " batch" : string.Empty);
            Assert.IsFalse(armed, $"{context}: 다시 읽는 중에 갱신을 알렸다");
            Assert.AreEqual(2250f, scroller.ScrollPosition, EPSILON, $"{context}: 같은 항목(20번 → 22번)과 오프셋");
            AssertConsistent(_harness, context);
            AssertCellsPreserved(_harness, before, binds, context);
            cells = scroller.ActiveCellViews;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                bool target = view.BoundData == ITEM_BASE + 20;
                Assert.AreEqual(target ? 1 : 0, view.MaskedRefreshCount, $"{context}: 항목 {view.BoundData} 내용 갱신 수");
                if (target)
                {
                    Assert.AreEqual(8, view.LastChangeMask, context);
                    Assert.AreEqual(22, view.DataIndex, context);
                }
            }

            CollectionAssert.AreEquivalent(new[] { ITEM_BASE + 20 }, refreshedAt.Keys, $"{context}: 새 22번 항목의 셀만 받는다");
            Assert.AreEqual(22, refreshedAt[ITEM_BASE + 20], $"{context}: 셀을 새 인덱스로 맞춘 뒤 받는다");
        }

        [UnityTest]
        public IEnumerator DelegateExceptionDuringPreservingRelayout_DropsDeferredRefreshAndRecovers()
        {
            yield return RunPreservingRelayoutException(false);
        }

        [UnityTest]
        public IEnumerator RecycleExceptionDuringPreservingRelayout_RecoversWithFullReload()
        {
            yield return RunPreservingRelayoutException(true);
        }

        /// <summary>
        /// 키 유지 재배치(ReloadDataKeepingPosition) 도중 사용자 코드 예외. inReconcile이 false면 다시 읽기를 시작한 델리게이트(개수 질의)가,
        /// true면 셀을 맞추며 지운 항목의 셀을 회수하는 핸들러가 던진다. 예외는 호출자에게 올라가고, 그 전에 델리게이트가 알린 갱신은 어느 셀에도 가지 않으며
        /// (다음 갱신에 모두 다시 바인딩하는 리로드가 대신 처리한다), 그 뒤 부른 갱신은 미뤄지지 않고 바로 간다.
        /// </summary>
        private IEnumerator RunPreservingRelayoutException(bool inReconcile)
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 1050f;   // 10~14번 표시
            bool armed = false;
            bool thrown = false;
            _harness.Data.GetNumberOfCellsHook = s =>
            {
                if (!armed)
                {
                    return;
                }

                armed = false;
                s.RefreshCells(13, 1, 2);
                if (!inReconcile)
                {
                    thrown = true;
                    throw new System.InvalidOperationException("개수 질의 예외");
                }
            };
            scroller.CellViewWillRecycle += view =>
            {
                if (inReconcile && !thrown)
                {
                    thrown = true;
                    throw new System.InvalidOperationException("회수 핸들러 예외");
                }
            };

            // 앞에 하나를 넣고 보이던 12번을 지운다 (셀을 맞출 때 그 셀을 회수한다).
            _harness.Data.RemoveRange(12, 1);
            _harness.Data.Insert(0, NextItem(), 100f);
            armed = true;
            Assert.Throws<System.InvalidOperationException>(() => scroller.ReloadDataKeepingPosition());
            Assert.IsTrue(thrown);
            Assert.IsFalse(armed);
            Assert.AreEqual(inReconcile ? 1150f : 1050f, scroller.ScrollPosition, EPSILON,
                inReconcile ? "위치는 이미 새 배치로 맞췄다" : "다시 읽기 전에 멈춰 위치는 그대로다");
            IReadOnlyList<CyScrollerCellView> cells = scroller.ActiveCellViews;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                if (view != null)
                {
                    Assert.AreEqual(0, view.MaskedRefreshCount, $"항목 {view.BoundData}: 셀을 맞추기 전에 알린 갱신은 옛 셀에 가지 않는다");
                }
            }

            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;

            yield return null;

            string context = inReconcile ? "reconcile exception" : "delegate exception";
            Assert.AreEqual(100, scroller.NumberOfCells);
            Assert.AreEqual(1150f, scroller.ScrollPosition, EPSILON, $"{context}: 같은 항목(10번 → 11번)과 오프셋");
            AssertConsistent(_harness, context);
            AssertAllRebound(_harness, before, binds, context);

            // 미루기가 풀렸다: 이제 부른 갱신은 그 항목의 셀에 바로 간다.
            scroller.RefreshCells(13, 1, 4);
            cells = scroller.ActiveCellViews;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                bool target = view.DataIndex == 13;
                Assert.AreEqual(target ? 1 : 0, view.MaskedRefreshCount, $"{context}: 항목 {view.BoundData} 내용 갱신 수");
                if (target)
                {
                    Assert.AreEqual(ITEM_BASE + 13, view.BoundData, context);
                    Assert.AreEqual(4, view.LastChangeMask, context);
                }
            }
        }

        /// <summary>키 유지로 다시 읽는 진입점.</summary>
        public enum PreservingEntry
        {
            FirstVisible,
            LastVisible,
            KeepingPosition,
        }

        private static void ReloadPreserving(CyScroller scroller, PreservingEntry entry)
        {
            switch (entry)
            {
                case PreservingEntry.FirstVisible:
                    scroller.ReloadData(ReloadAnchor.FirstVisible);
                    break;
                case PreservingEntry.LastVisible:
                    scroller.ReloadData(ReloadAnchor.LastVisible);
                    break;
                default:
                    scroller.ReloadDataKeepingPosition();
                    break;
            }
        }

        [UnityTest]
        public IEnumerator SizeExceptionDuringPreservingReloadRebuild_RecoversToScreenBeforeRebuild()
        {
            yield return RunPreservingRebuildException(PreservingEntry.FirstVisible);
        }

        [UnityTest]
        public IEnumerator SizeExceptionDuringTrailingPreservingReloadRebuild_RecoversToScreenBeforeRebuild()
        {
            yield return RunPreservingRebuildException(PreservingEntry.LastVisible);
        }

        [UnityTest]
        public IEnumerator SizeExceptionDuringPreservingRelayoutRebuild_RecoversToScreenBeforeRebuild()
        {
            yield return RunPreservingRebuildException(PreservingEntry.KeepingPosition);
        }

        /// <summary>
        /// 항목 100개를 start 위치에서 보다가 앞에 하나를 넣고, entry로 다시 읽는 도중 새 50번 크기 질의가 던지게 한다. 개수가 크기 버퍼 용량(100)을 넘게 늘어
        /// 배치가 반쯤 읽힌 채 남는다(새 버퍼라 접두합은 0, 항목 ID 배열은 옛 길이). 예외는 호출자에게 올라가고 위치는 그대로다. 멈춘 뒤 활성 셀 기록을 돌려준다.
        /// </summary>
        private List<CellRecord> FailPreservingRebuild(PreservingEntry entry, float start)
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = start;
            bool armed = false;
            _harness.Data.GetCellViewSizeHook = (s, index) =>
            {
                if (armed && index == 50)
                {
                    armed = false;
                    throw new System.InvalidOperationException("크기 질의 예외");
                }
            };

            _harness.Data.Insert(0, NextItem(), 100f);
            armed = true;
            Assert.Throws<System.InvalidOperationException>(() => ReloadPreserving(scroller, entry));
            Assert.IsFalse(armed);
            Assert.AreEqual(start, scroller.ScrollPosition, EPSILON, $"{entry}: 다시 읽다 멈춰 위치는 그대로다");
            return Record(_harness);
        }

        /// <summary>
        /// 키 유지 리로드(FirstVisible·LastVisible)·재배치(ReloadDataKeepingPosition)가 다시 읽다 멈춘다(<see cref="FailPreservingRebuild"/>).
        /// 다음 갱신의 복구 리로드는 반쯤 읽은 배치에서 화면을 읽지 않고 다시 읽기 전 화면(10번 위 50 = 뷰포트 끝 14번 끝 50 전)으로 가서 모두 다시 바인딩한다.
        /// </summary>
        private IEnumerator RunPreservingRebuildException(PreservingEntry entry)
        {
            List<CellRecord> before = FailPreservingRebuild(entry, 1050f);
            CyScroller scroller = _harness.Scroller;
            int binds = _harness.Data.GetCellViewCalls;

            yield return null;

            Assert.AreEqual(101, scroller.NumberOfCells, entry.ToString());
            Assert.AreEqual(1150f, scroller.ScrollPosition, EPSILON, $"{entry}: 다시 읽기 전 화면의 항목(10번 → 11번)과 오프셋");
            AssertConsistent(_harness, entry.ToString());
            AssertAllRebound(_harness, before, binds, entry.ToString());
        }

        /// <summary>다시 읽다 멈춘 뒤 복구 리로드 전에 코드로 옮기는 요청.</summary>
        public enum MoveBeforeRecovery
        {
            ScrollPosition,
            NormalizedScrollPosition,
            Jump,
            Snap,
            ScrollPositionInBatch,
        }

        [UnityTest]
        public IEnumerator ScrollPositionBeforeRecovery_AppliesOnRecoveredLayout()
        {
            yield return RunMoveBeforeRecovery(MoveBeforeRecovery.ScrollPosition);
        }

        [UnityTest]
        public IEnumerator NormalizedScrollPositionBeforeRecovery_AppliesOnRecoveredLayout()
        {
            yield return RunMoveBeforeRecovery(MoveBeforeRecovery.NormalizedScrollPosition);
        }

        [UnityTest]
        public IEnumerator JumpBeforeRecovery_AppliesOnRecoveredLayout()
        {
            yield return RunMoveBeforeRecovery(MoveBeforeRecovery.Jump);
        }

        [UnityTest]
        public IEnumerator SnapBeforeRecovery_AppliesOnRecoveredLayout()
        {
            yield return RunMoveBeforeRecovery(MoveBeforeRecovery.Snap);
        }

        [UnityTest]
        public IEnumerator ScrollPositionInBatchBeforeRecovery_KeepsMovedPosition()
        {
            yield return RunMoveBeforeRecovery(MoveBeforeRecovery.ScrollPositionInBatch);
        }

        /// <summary>
        /// 키 유지 리로드가 다시 읽다 멈춘 뒤(<see cref="FailPreservingRebuild"/>) 다음 프레임 전에 코드로 옮긴다. 이동 요청이 보관한 앵커(다시 읽기 전 화면)를 버려도
        /// 복구 리로드는 반쯤 읽은 배치에서 화면을 읽지 않는다. 배치 밖 요청은 복구 리로드를 먼저 처리한 뒤 다시 읽은 배치에서 위치를 정하고(정규화 위치·스냅 대상도 새 배치 기준),
        /// 배치 안 요청은 배치 끝의 복구 리로드가 그 요청이 옮긴 콘텐츠 위치를 지킨다. 셀은 모두 다시 바인딩하고, 다음 프레임에 보관한 앵커로 되돌아가지 않는다.
        /// </summary>
        private IEnumerator RunMoveBeforeRecovery(MoveBeforeRecovery move)
        {
            // 스냅은 복구한 화면(11번 위 80 = 1180)에서 가운데에 걸친 13번에 맞춰 움직이도록 오프셋 80에서 시작한다.
            List<CellRecord> before = FailPreservingRebuild(PreservingEntry.FirstVisible, move == MoveBeforeRecovery.Snap ? 1080f : 1050f);
            CyScroller scroller = _harness.Scroller;
            int snappedDataIndex = -1;
            scroller.ScrollerSnapped += (s, cellIndex, dataIndex, view) => snappedDataIndex = dataIndex;

            float expected;
            switch (move)
            {
                case MoveBeforeRecovery.ScrollPosition:
                    scroller.ScrollPosition = 500f;
                    expected = 500f;
                    break;
                case MoveBeforeRecovery.NormalizedScrollPosition:
                    // 다시 읽은 배치 기준 스크롤 거리(101 × 100 − 뷰포트 400)의 절반. 반쯤 읽은 배치(옛 콘텐츠 길이)로 계산하면 4800이다.
                    scroller.NormalizedScrollPosition = 0.5f;
                    expected = 4850f;
                    break;
                case MoveBeforeRecovery.Jump:
                    scroller.JumpToDataIndex(30, 0f, 0f, false, TweenType.Immediate, 0f);
                    expected = 3000f;
                    break;
                case MoveBeforeRecovery.Snap:
                    // 가운데(1380)에 걸친 13번의 가운데(1350)를 뷰포트 가운데에 맞춘다.
                    scroller.SnapTweenType = TweenType.Immediate;
                    scroller.Snap();
                    expected = 1150f;
                    break;
                default:
                    // 배치 안에서는 복구 리로드를 먼저 처리할 수 없다. 배치 끝의 복구 리로드가 대입한 위치를 지킨다.
                    scroller.BeginUpdates();
                    scroller.ScrollPosition = 500f;
                    scroller.EndUpdates();
                    expected = 500f;
                    break;
            }

            string context = move.ToString();
            Assert.AreEqual(101, scroller.NumberOfCells, context);
            Assert.AreEqual(expected, scroller.ScrollPosition, EPSILON, $"{context}: 다시 읽은 배치에서 정한 위치");
            AssertConsistent(_harness, context);
            Assert.AreEqual(0, CountKept(_harness, before), $"{context}: 멈춘 뒤 남은 셀은 모두 다시 바인딩했다");
            if (move == MoveBeforeRecovery.Snap)
            {
                Assert.AreEqual(13, snappedDataIndex, $"{context}: 다시 읽은 배치의 가운데 항목에 맞췄다");
            }

            int binds = _harness.Data.GetCellViewCalls;

            yield return null;

            Assert.AreEqual(expected, scroller.ScrollPosition, EPSILON, $"{context}: 다음 프레임에 다시 읽기 전 화면으로 돌아가지 않는다");
            Assert.AreEqual(binds, _harness.Data.GetCellViewCalls, $"{context}: 다음 프레임에 다시 바인딩하지 않는다");
            AssertConsistent(_harness, context + " next frame");
        }

        /// <summary>다시 읽는 중 델리게이트가 닫는 배치에 담는 것.</summary>
        public enum BatchWork
        {
            ClearRecycled,
            ReloadRequest,
            DeferredRangeUpdate,
        }

        /// <summary>
        /// 키 유지 리로드·재배치가 다시 읽는 도중(새 50번 크기를 물을 때) 델리게이트가 배치를 열고 닫는다. 배치에는 미룬 정리(<see cref="CyScroller.ClearRecycled"/>),
        /// 리로드 요청, 미룬 범위 갱신(<see cref="CyScroller.ScrollPosition"/> 대입) 중 하나가 있다. 개수가 크기 버퍼 용량을 넘게 늘어 배치는 아직 반쯤 읽힌 채다.
        /// 배치 끝은 그 배치로 범위를 맞추지 않고, 미룬 작업은 셀을 새 인덱스로 맞춘 뒤 처리한다(리로드 요청은 키 유지로 한 번 더 다시 읽는다).
        /// 위치는 다시 읽기 전 화면을 지키고 활성 셀은 다시 바인딩하지 않고 남는다.
        /// </summary>
        [Test]
        public void BatchClosedByDelegateDuringPreservingRebuild_WaitsForReconcile(
            [Values(PreservingEntry.FirstVisible, PreservingEntry.KeepingPosition)] PreservingEntry entry, [Values] BatchWork work)
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 1050f;
            scroller.ScrollPosition = 1000f;   // 10~13번 표시, 14번을 보이던 셀은 풀에 있다
            Assert.Greater(scroller.GetRecycledCellCount(), 0, "풀에 셀이 있다");

            bool armed = false;
            int countQueries = 0;
            _harness.Data.GetNumberOfCellsHook = _ => countQueries++;
            _harness.Data.GetCellViewSizeHook = (s, index) =>
            {
                if (!armed || index != 50)
                {
                    return;
                }

                armed = false;
                s.BeginUpdates();
                switch (work)
                {
                    case BatchWork.ClearRecycled:
                        s.ClearRecycled();
                        break;
                    case BatchWork.ReloadRequest:
                        s.ReloadData(ReloadAnchor.FirstVisible);
                        break;
                    default:
                        s.ScrollPosition = 0f;
                        break;
                }

                s.EndUpdates();
            };

            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;
            _harness.Data.Insert(0, NextItem(), 100f);
            armed = true;
            ReloadPreserving(scroller, entry);

            string context = $"{entry} {work}";
            Assert.IsFalse(armed, $"{context}: 다시 읽는 중에 배치를 닫았다");
            Assert.AreEqual(1100f, scroller.ScrollPosition, EPSILON, $"{context}: 같은 항목(10번 → 11번)");
            AssertConsistent(_harness, context);
            AssertCellsPreserved(_harness, before, binds, context);
            switch (work)
            {
                case BatchWork.ClearRecycled:
                    Assert.AreEqual(0, scroller.GetRecycledCellCount(), $"{context}: 미룬 정리를 처리했다");
                    break;
                case BatchWork.ReloadRequest:
                    // 다시 읽기 한 번 + 배치 끝을 기다린 리로드 요청 한 번 (배치 끝은 다시 읽을 요청이 있어 개수를 맞춰 보지 않는다).
                    Assert.AreEqual(2, countQueries, $"{context}: 리로드 요청을 셀을 맞춘 뒤 처리했다");
                    break;
            }
        }

        [Test]
        public void EndUpdatesCountMismatch_ReplacedByAnchorReload_RebindsEverything()
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 1050f;
            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;

            // 데이터는 앞에 2개 늘었는데 1개만 알렸다. 개수 불일치를 대신하는 리로드는 알리지 못한 변경까지 맞추도록 옵션과 무관하게 모두 다시 바인딩한다.
            scroller.BeginUpdates();
            _harness.Data.Insert(0, NextItem(), 100f);
            _harness.Data.Insert(0, NextItem(), 100f);
            scroller.InsertCells(0, 1);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"증분 연산을 반영한 개수\(101\)가 GetNumberOfCells\(102\)와 다릅니다"));
            scroller.EndUpdates();

            Assert.AreEqual(102, scroller.NumberOfCells);
            Assert.AreEqual(1250f, scroller.ScrollPosition, EPSILON, "FirstVisible: 같은 항목(10번 → 12번)과 오프셋");
            AssertConsistent(_harness, "count mismatch");
            AssertAllRebound(_harness, before, binds, "count mismatch");
        }

        /// <summary>
        /// 루프 모드의 삽입은 증분 대신 앵커 보존 리로드로 바뀌고, 그 리로드는 옵션과 무관하게 모두 다시 바인딩한다.
        /// withRefresh면 같은 배치에 5번 항목 갱신도 담는다. 그 갱신은 리로드가 최신 데이터로 다시 바인딩해 대신 처리한다(따로 부르지 않는다).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LoopModeBatch_ReplacedByAnchorReload_RebindsEverything(bool withRefresh)
        {
            _harness = CreateHarness(12, loop: true);
            CyScroller scroller = _harness.Scroller;
            Assert.IsTrue(scroller.Layout.IsLoop);
            scroller.ScrollPosition = 2750f;   // 가운데 세트 3번 안 50: 3~7번 표시
            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;
            TestCellView refreshedCell = CellForItem(_harness, ITEM_BASE + 5);
            Assert.IsNotNull(refreshedCell);
            int refreshedVersion = refreshedCell.BindVersion;

            scroller.BeginUpdates();
            if (withRefresh)
            {
                scroller.RefreshCells(5, 1, 4);
            }

            _harness.Data.Insert(0, NextItem(), 100f);
            scroller.InsertCells(0, 1);
            scroller.EndUpdates();

            string context = withRefresh ? "loop batch with refresh" : "loop batch";
            Assert.AreEqual(3050f, scroller.ScrollPosition, EPSILON, $"{context}: 새 사이클(1300)의 가운데 세트에서 같은 항목·오프셋");
            AssertConsistent(_harness, context);
            AssertAllRebound(_harness, before, binds, context);

            // 갱신한 항목의 옛 셀은 같은 바인딩으로 남지 않는다 (지금 그 항목을 보이는 셀은 이번 리로드에서 최신 데이터로 바인딩했고 갱신을 따로 받지 않는다).
            Assert.IsFalse(refreshedCell.Active && refreshedCell.BindVersion == refreshedVersion, $"{context}: 5번 항목의 옛 셀이 그대로 남지 않는다");
            Assert.IsNotNull(CellForItem(_harness, ITEM_BASE + 5), $"{context}: 5번 항목은 화면에 있다");
            IReadOnlyList<CyScrollerCellView> cells = scroller.ActiveCellViews;
            for (int i = 0; i < cells.Count; i++)
            {
                var view = (TestCellView)cells[i];
                Assert.AreEqual(0, view.MaskedRefreshCount, $"{context}: 항목 {view.BoundData}은 다시 바인딩해 갱신을 따로 받지 않는다");
            }
        }

        [Test]
        public void RelayoutsWithoutRequery_AndAxisSwitch_RebindEverything()
        {
            _harness = CreateHarness(100);
            CyScroller scroller = _harness.Scroller;
            scroller.ScrollPosition = 1050f;

            // 다시 읽지 않는 재배치(Spacing·Padding·Loop)는 옵션과 무관하게 모두 다시 바인딩한다.
            System.Action[] relayouts =
            {
                () => scroller.Spacing = 10f,
                () => scroller.Padding = new RectOffset(0, 0, 20, 30),
                () => scroller.Loop = true,
                () => scroller.Loop = false,
            };

            for (int i = 0; i < relayouts.Length; i++)
            {
                List<CellRecord> before = Record(_harness);
                int binds = _harness.Data.GetCellViewCalls;
                relayouts[i]();
                AssertConsistent(_harness, $"relayout {i}");
                AssertAllRebound(_harness, before, binds, $"relayout {i}");
            }

            // 축 전환은 델리게이트를 다시 읽어도 옵션과 무관하게 모두 다시 바인딩한다 (가로에서는 세로 기준 위치 검사를 건너뛴다).
            ScrollDirection[] directions = { ScrollDirection.Horizontal, ScrollDirection.Vertical };
            for (int i = 0; i < directions.Length; i++)
            {
                List<CellRecord> before = Record(_harness);
                int binds = _harness.Data.GetCellViewCalls;
                scroller.ScrollDirection = directions[i];
                AssertAllRebound(_harness, before, binds, $"axis {directions[i]}");
            }

            AssertConsistent(_harness, "back to vertical");
        }

        #endregion

        #region Tween And Random

        [UnityTest]
        public IEnumerator KeepingPosition_DuringTween_ReusesCellsAndArrivesOnce()
        {
            _harness = CreateHarness(200);
            CyScroller scroller = _harness.Scroller;
            int target = ITEM_BASE + 100;
            int completed = 0;
            scroller.JumpToDataIndex(100, 0f, 0f, false, TweenType.EaseInOutCubic, 0.4f, () => completed++);
            yield return null;
            yield return null;
            Assert.IsTrue(scroller.IsTweening);

            List<CellRecord> before = Record(_harness);
            int binds = _harness.Data.GetCellViewCalls;
            for (int i = 0; i < 5; i++)
            {
                _harness.Data.Insert(0, NextItem(), 60f);
            }

            scroller.ReloadDataKeepingPosition();
            Assert.IsTrue(scroller.IsTweening, "재배치는 트윈을 끊지 않는다");
            AssertConsistent(_harness, "mid tween");
            AssertCellsPreserved(_harness, before, binds, "mid tween");

            float timeout = TWEEN_TIMEOUT;
            while (scroller.IsTweening && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsFalse(scroller.IsTweening);
            Assert.AreEqual(1, completed, "완료 콜백 한 번");
            int index = _harness.Data.Items.IndexOf(target);
            Assert.AreEqual(105, index);
            Assert.AreEqual(scroller.GetCellStart(index), scroller.ScrollPosition, POSITION_EPSILON, "같은 항목에 도착한다");
            AssertConsistent(_harness, "arrived");
        }

        /// <summary>
        /// 고정 시드로 데이터를 몇 군데 바꾼 뒤(삽입·삭제·이동·크기, 알림 없음) 위치 유지 리로드 네 가지 중 하나로 다시 읽기를 되풀이한다.
        /// 키 유지를 켠 스크롤러와 끈 스크롤러에 똑같이 적용해 매번 위치·화면이 같은지, 켠 쪽은 남을 수 있는 셀이 모두 다시 바인딩 없이 남는지
        /// (항목마다 min(전후 활성 사본 수)개), 끈 쪽은 모두 다시 바인딩하는지, 둘 다 데이터·표시 이벤트 짝과 맞는지 본다.
        /// </summary>
        [Test]
        public void RandomReloads_MatchRebindingTwin()
        {
            RunRandomTwinReloads(false, RANDOM_SEED);
        }

        /// <summary><see cref="RandomReloads_MatchRebindingTwin"/>을 루프 모드(짧은 목록이라 같은 항목의 사본이 한 화면에 여럿)로 돌린다.</summary>
        [Test]
        public void RandomReloads_LoopMode_MatchRebindingTwin()
        {
            RunRandomTwinReloads(true, RANDOM_SEED + 1);
        }

        private static float RandomSize(System.Random random) => 30f + random.Next(0, 12) * 10f;

        private void RunRandomTwinReloads(bool loop, int seed)
        {
            var random = new System.Random(seed);
            int count = loop ? 6 : 120;
            int minCount = loop ? 3 : 20;
            int maxCount = loop ? 14 : 200;
            var padding = new RectOffset(0, 0, 12, 18);
            _harness = CreateHarness(count, 80f, loop: loop, spacing: 4f, padding: padding, reload: false);
            _twin = CreateHarness(count, 80f, preserve: false, loop: loop, spacing: 4f, padding: padding, reload: false);
            Harness[] both = { _harness, _twin };
            for (int i = 0; i < count; i++)
            {
                float size = RandomSize(random);
                _harness.Data.Sizes[i] = size;
                _twin.Data.Sizes[i] = size;
            }

            foreach (Harness h in both)
            {
                h.Scroller.LookAheadBefore = 60f;
                h.Scroller.LookAheadAfter = 90f;
                h.Scroller.ReloadData();
            }

            for (int step = 0; step < RANDOM_STEPS; step++)
            {
                if (random.Next(3) == 0)
                {
                    float position = (float)(random.NextDouble() * _harness.Scroller.ScrollSize);
                    foreach (Harness h in both)
                    {
                        h.Scroller.ScrollPosition = position;
                    }
                }

                List<CellRecord> onBefore = Record(_harness);
                List<CellRecord> offBefore = Record(_twin);
                int onBinds = _harness.Data.GetCellViewCalls;
                int offBinds = _twin.Data.GetCellViewCalls;

                int changes = random.Next(0, 4);
                for (int c = 0; c < changes; c++)
                {
                    ApplyRandomDataChange(random, both, minCount, maxCount);
                }

                int reload = random.Next(4);
                bool trailing = random.Next(2) == 0;
                foreach (Harness h in both)
                {
                    switch (reload)
                    {
                        case 0:
                            h.Scroller.ReloadData(ReloadAnchor.FirstVisible);
                            break;
                        case 1:
                            h.Scroller.ReloadData(ReloadAnchor.LastVisible);
                            break;
                        case 2:
                            h.Scroller.ReloadDataKeepingPosition();
                            break;
                        default:
                            CyScrollerAnchor anchor = h.Scroller.CaptureAnchor(trailing);
                            h.Scroller.ReloadData(in anchor);
                            break;
                    }
                }

                string context = $"step {step} (reload {reload}, changes {changes})";
                AssertConsistent(_harness, context + " on");
                AssertConsistent(_twin, context + " off");
                AssertSameScreen(_twin, _harness, context);
                AssertCellsPreserved(_harness, onBefore, onBinds, context + " on");
                AssertAllRebound(_twin, offBefore, offBinds, context + " off");
            }
        }

        /// <summary>두 스크롤러의 데이터에 같은 변경을 하나 적용한다 (스크롤러에는 알리지 않는다).</summary>
        private void ApplyRandomDataChange(System.Random random, Harness[] both, int minCount, int maxCount)
        {
            int count = both[0].Data.Items.Count;
            int kind = random.Next(4);
            if (count <= minCount)
            {
                kind = 0;
            }
            else if (count >= maxCount && kind == 0)
            {
                kind = 1;
            }

            switch (kind)
            {
                case 0:
                {
                    int at = random.Next(count + 1);
                    int added = random.Next(1, 4);
                    for (int j = 0; j < added; j++)
                    {
                        int item = NextItem();
                        float size = RandomSize(random);
                        foreach (Harness h in both)
                        {
                            h.Data.Insert(at + j, item, size);
                        }
                    }

                    break;
                }
                case 1:
                {
                    int at = random.Next(count);
                    int removed = random.Next(1, Mathf.Min(3, count - at) + 1);
                    foreach (Harness h in both)
                    {
                        h.Data.RemoveRange(at, removed);
                    }

                    break;
                }
                case 2:
                {
                    int from = random.Next(count);
                    int to = random.Next(count);
                    foreach (Harness h in both)
                    {
                        h.Data.Move(from, to);
                    }

                    break;
                }
                default:
                {
                    int at = random.Next(count);
                    float size = RandomSize(random);
                    foreach (Harness h in both)
                    {
                        h.Data.Sizes[at] = size;
                    }

                    break;
                }
            }
        }

        [Test]
        public void PreservingReloads_AfterWarmup_DoNotAllocate()
        {
            _harness = CreateHarness(400, 50f, log: false);
            CyScroller scroller = _harness.Scroller;
            scroller.LookAheadAfter = 100f;
            scroller.ScrollPosition = 5000f;

            // 데이터 목록·레이아웃·사전·작업 목록·풀 용량을 채운다.
            PreservingReloadCycle();
            PreservingReloadCycle();

            Assert.That(() => PreservingReloadCycle(), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            AssertConsistent(_harness, "after cycles");
        }

        /// <summary>앞에 삽입하고 FirstVisible, 지우고 위치 유지 리로드, 보이는 셀 크기를 바꾸고 LastVisible, 앵커 리로드. 끝나면 데이터는 처음과 같다.</summary>
        private void PreservingReloadCycle()
        {
            CyScroller scroller = _harness.Scroller;
            ListTestDelegate data = _harness.Data;
            data.Insert(0, -1, 50f);
            scroller.ReloadData(ReloadAnchor.FirstVisible);
            data.RemoveRange(0, 1);
            scroller.ReloadDataKeepingPosition();
            data.Sizes[102] = 90f;
            scroller.ReloadData(ReloadAnchor.LastVisible);
            data.Sizes[102] = 50f;
            CyScrollerAnchor anchor = scroller.CaptureAnchor();
            scroller.ReloadData(in anchor);
        }

        private static int CountLog(Harness harness, string prefix)
        {
            int count = 0;
            for (int i = 0; i < harness.Log.Count; i++)
            {
                if (harness.Log[i].StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        #endregion
    }
}
