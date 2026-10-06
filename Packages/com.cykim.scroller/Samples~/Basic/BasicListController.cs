using System.Collections.Generic;
using UnityEngine;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 높이가 제각각인 세로 목록. 항목은 List로 두고 번호(항목 ID)를 셀에 보여 준다. 라벨·색·높이는 항목을 만들 때 한 번만 만들고
    /// 바인딩 때는 대입만 한다 (스크롤 중 GC 없음). 삽입·삭제·이동은 증분 변경(InsertCells·RemoveCells·MoveCell)으로 알려 보던 화면을 지킨다.
    /// </summary>
    public class BasicListController : MonoBehaviour, ICyScrollerDelegate, ICyScrollerItemIdProvider
    {
        [SerializeField] private CyScroller _scroller;
        [SerializeField] private BasicCellView _cellPrefab;
        [SerializeField, Min(0)] private int _itemCount = 500;

        private readonly List<Item> _items = new List<Item>();
        private int _nextId;

        public CyScroller Scroller => _scroller;
        public int ItemCount => _items.Count;

        /// <summary>마지막 편집 설명 (샘플 상태 줄용). 편집 메서드를 부를 때만 바뀐다.</summary>
        public string LastEdit { get; private set; } = "edit buttons keep the view in place";

        private struct Item
        {
            public int Id;
            public float Height;
            public string Label;
            public Color Color;
        }

        public void Setup(CyScroller scroller, BasicCellView cellPrefab, int itemCount)
        {
            _scroller = scroller;
            _cellPrefab = cellPrefab;
            _itemCount = itemCount;
        }

        private void Start()
        {
            BuildData();
            _scroller.Delegate = this;
        }

        private void BuildData()
        {
            _items.Clear();
            _nextId = 0;
            for (int i = 0; i < _itemCount; i++)
            {
                _items.Add(CreateItem(_nextId++));
            }
        }

        private static Item CreateItem(int id)
        {
            // 60~180 사이 높이를 번호로 결정적으로 섞는다.
            float height = 60f + (id * 37 % 5) * 30f;
            return new Item
            {
                Id = id,
                Height = height,
                Label = $"Item #{id}  (height {height:0})",
                Color = Color.HSVToRGB(id * 0.013f % 1f, 0.35f, 0.95f),
            };
        }

        public void JumpTo(int dataIndex)
        {
            _scroller.JumpToDataIndex(dataIndex, 0.5f, 0.5f, true, TweenType.EaseInOutCubic, 0.4f);
        }

        /// <summary>번호(항목 ID)로 점프한다. 앞에 항목이 들어오거나 빠져도 같은 항목으로 가고, 그 항목이 지워졌으면 같은 인덱스로 간다.</summary>
        public void JumpToItem(int id)
        {
            int index = _scroller.FindDataIndexForItemId(id);
            JumpTo(index >= 0 ? index : Mathf.Min(id, _items.Count - 1));
        }

        /// <summary>맨 위에 새 항목 count개를 넣는다. 보던 항목은 화면에서 그대로다 (넣은 높이만큼 스크롤 위치를 옮긴다).</summary>
        public void InsertAtTop(int count)
        {
            if (count <= 0)
            {
                return;
            }

            int firstId = _nextId;
            for (int i = 0; i < count; i++)
            {
                _items.Insert(i, CreateItem(_nextId++));
            }

            // 데이터를 먼저 바꾼 뒤 알린다.
            _scroller.InsertCells(0, count);
            LastEdit = count == 1 ? $"inserted #{firstId} at top" : $"inserted #{firstId} ~ #{_nextId - 1} at top";
        }

        /// <summary>
        /// 뷰포트 안에서 시작하는 첫 항목을 지운다 (위가 잘려 조금만 보이는 항목은 건너뛴다). 위쪽 화면은 그대로이고 아래 항목이 빈자리를 채운다.
        /// 맨 위 항목이 뷰포트 시작에 딱 맞으면 그 항목이 지워지고 다음 항목이 같은 자리로 온다.
        /// </summary>
        public void RemoveFirstVisible()
        {
            int index = FindDisplayedIndex(false);
            if (index < 0)
            {
                LastEdit = "nothing to remove";
                return;
            }

            int id = _items[index].Id;
            _items.RemoveAt(index);
            _scroller.RemoveCells(index, 1);
            LastEdit = $"removed #{id}";
        }

        /// <summary>보이는 항목 가운데(뷰포트 가운데쯤) 하나를 맨 위로 옮긴다. 보던 화면은 움직이지 않고 그 항목만 빠진다 (위로 스크롤하면 맨 위에 있다).</summary>
        public void MoveVisibleToTop()
        {
            int index = FindDisplayedIndex(true);
            if (index < 0)
            {
                LastEdit = "nothing to move";
                return;
            }

            Item item = _items[index];
            if (index == 0)
            {
                LastEdit = $"#{item.Id} is already at top";
                return;
            }

            _items.RemoveAt(index);
            _items.Insert(0, item);
            _scroller.MoveCell(index, 0);
            LastEdit = $"moved #{item.Id} to top";
        }

        /// <summary>
        /// 실제 뷰포트에 보이는 셀 중 뷰포트 안에서 시작하는 첫 셀(없으면 맨 앞 셀), middle이면 가운데 셀의 데이터 인덱스. 없으면 -1.
        /// 보이는 셀은 활성 목록에 이어져 있다.
        /// </summary>
        private int FindDisplayedIndex(bool middle)
        {
            IReadOnlyList<CyScrollerCellView> cells = _scroller.ActiveCellViews;
            float viewStart = _scroller.ScrollPosition;
            int first = -1;
            int firstStarting = -1;
            int count = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                CyScrollerCellView view = cells[i];
                if (view == null || !view.IsDisplayed)
                {
                    continue;
                }

                if (first < 0)
                {
                    first = i;
                }

                if (firstStarting < 0 && _scroller.GetCellStart(view.DataIndex) >= viewStart - 0.5f)
                {
                    firstStarting = i;
                }

                count++;
            }

            if (first < 0)
            {
                return -1;
            }

            int picked = middle ? first + count / 2 : (firstStarting >= 0 ? firstStarting : first);
            return cells[picked] != null ? cells[picked].DataIndex : -1;
        }

        public int GetNumberOfCells(CyScroller scroller) => _items.Count;

        public float GetCellViewSize(CyScroller scroller, int dataIndex) => _items[dataIndex].Height;

        public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
        {
            var view = (BasicCellView)scroller.GetCellView(_cellPrefab);
            Item item = _items[dataIndex];
            view.SetData(item.Label, item.Color);
            return view;
        }

        /// <summary>항목 번호가 안정 ID다. 앞에 항목이 들어오거나 빠져도 같은 항목은 같은 값이다.</summary>
        public long GetItemId(CyScroller scroller, int dataIndex) => _items[dataIndex].Id;
    }
}
