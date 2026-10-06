using UnityEngine;
using UnityEngine.UI;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 빈 씬의 GameObject에 붙이고 Play하면 샘플 UI 전체를 만든다.
    /// 왼쪽: 높이가 다른 500개 세로 목록 / 가운데: 점프·편집(증분 변경) 버튼과 목록 상태 / 오른쪽: 휠 피커 / 아래: 순환·스냅 캐러셀.
    /// 1920×1080 기준 Canvas에 비율 앵커로 배치하므로 16:9 해상도(1280×720 등)에서는 같은 배치로 보인다.
    /// </summary>
    public class BasicSample : MonoBehaviour
    {
        private const float CAROUSEL_CARD_WIDTH = 260f;

        // 가운데 버튼 열: 버튼 10개와 4줄 상태가 1920×1080 기준 본문 높이(약 577) 안에 들어가게 한다.
        private const float BUTTON_HEIGHT = 40f;
        private const float BUTTON_SPACING = 6f;
        private const float STATUS_MIN_HEIGHT = 96f;

        [SerializeField, Min(0)] private int _listItemCount = 500;
        [SerializeField, Min(1)] private int _carouselCardCount = 12;
        [SerializeField, Min(1)] private int _wheelValueCount = 60;

        private BasicListController _list;
        private BasicCarouselController _carousel;
        private BasicWheelPickerController _wheel;
        private Text _listStatus;
        private Text _carouselStatus;
        private Text _wheelValue;
        private bool _listStatusDirty = true;

        private void Start()
        {
            SampleUiFactory.EnsureEventSystem();

            Canvas canvas = SampleUiFactory.CreateCanvas("CyScroller Sample Canvas");
            var root = (RectTransform)canvas.transform;
            SampleUiFactory.CreateBackground(root);

            BuildHeader(root);
            BuildList(root);
            BuildWheel(root);
            BuildCarousel(root);
            BuildControls(root);
        }

        private void OnDestroy()
        {
            if (_list != null && _list.Scroller != null)
            {
                CyScroller scroller = _list.Scroller;
                scroller.CellViewWillDisplay -= OnListDisplayChanged;
                scroller.CellViewDidEndDisplay -= OnListDisplayChanged;
                scroller.CellViewVisibilityChanged -= OnListCellActivityChanged;
            }

            if (_carousel != null)
            {
                _carousel.CardCentered -= OnCardCentered;
            }

            if (_wheel != null)
            {
                _wheel.ValueSelected -= OnWheelValueSelected;
            }
        }

        private void LateUpdate()
        {
            // 샘플 표시용. 문자열은 셀이 보이거나 사라지거나 활성화·회수된 프레임에만 만든다 (위치만 바뀐 스크롤 프레임에는 만들지 않는다).
            if (_listStatusDirty && _list != null)
            {
                _listStatusDirty = false;
                UpdateListStatus();
            }
        }

        private void BuildHeader(RectTransform root)
        {
            RectTransform header = SampleUiFactory.CreateRect("Header", root);
            SampleUiFactory.Place(header, new Vector2(0.025f, 0.925f), new Vector2(0.975f, 0.985f));

            Text title = SampleUiFactory.CreateText("Title", header, "CyKim Scroller", 42, TextAnchor.MiddleLeft);
            SampleUiFactory.Stretch(title.rectTransform, 0f);
            title.fontStyle = FontStyle.Bold;
            title.color = SampleUiFactory.TextColor;
            title.raycastTarget = false;

            Text subtitle = SampleUiFactory.CreateText("Subtitle", header,
                "Basic sample  ·  virtualized uGUI ScrollRect with cell recycling", 22, TextAnchor.MiddleRight);
            SampleUiFactory.Stretch(subtitle.rectTransform, 0f);
            subtitle.color = SampleUiFactory.MutedTextColor;
            subtitle.raycastTarget = false;
        }

        private void BuildList(RectTransform root)
        {
            RectTransform body = SampleUiFactory.CreateSection("List Section", root,
                new Vector2(0.025f, 0.33f), new Vector2(0.405f, 0.905f), "VERTICAL LIST  ·  500 items, variable heights");

            CyScroller scroller = SampleUiFactory.CreateScroller("Vertical List", body, ScrollDirection.Vertical, SampleUiFactory.PanelColor);
            SampleUiFactory.Stretch((RectTransform)scroller.transform, 0f);
            scroller.Spacing = 8f;
            scroller.Padding = new RectOffset(14, 14, 14, 14);
            scroller.LookAheadAfter = 120f;

            BasicCellView template = SampleUiFactory.CreateListItemTemplate("ListItem", root);
            _list = gameObject.AddComponent<BasicListController>();
            _list.Setup(scroller, template, _listItemCount);

            // 상태 줄 값은 이 이벤트들에서만 바뀐다: 실제로 보이는 셀(lookAhead 제외)은 표시 이벤트,
            // 활성·풀 개수는 활성화·회수 이벤트. 스크롤 이벤트는 위치만 바뀐 프레임에도 오므로 쓰지 않는다.
            scroller.CellViewWillDisplay += OnListDisplayChanged;
            scroller.CellViewDidEndDisplay += OnListDisplayChanged;
            scroller.CellViewVisibilityChanged += OnListCellActivityChanged;
        }

        private void BuildControls(RectTransform root)
        {
            RectTransform body = SampleUiFactory.CreateSection("Controls Section", root,
                new Vector2(0.425f, 0.33f), new Vector2(0.625f, 0.905f), "JUMP  ·  EDIT keeps the view");

            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = BUTTON_SPACING;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            // 점프: 트윈으로 이동한다. #250은 항목 번호(ID)라 앞에 항목이 들어와도 같은 항목으로 간다.
            AddButton(body, "List: Top", () => _list.JumpTo(0));
            AddButton(body, "List: Item #250 (center)", () => _list.JumpToItem(250));
            AddButton(body, "List: Bottom", () => _list.JumpTo(_list.ItemCount - 1));
            AddButton(body, "List: Random", () => _list.JumpTo(Random.Range(0, _list.ItemCount)));

            // 편집: 증분 변경(InsertCells·RemoveCells·MoveCell)으로 알려 남은 셀을 다시 바인딩하지 않고 보던 화면을 지킨다.
            AddButton(body, "List: Insert 3 at top", () => EditList(0));
            AddButton(body, "List: Remove first visible", () => EditList(1));
            AddButton(body, "List: Move visible to top", () => EditList(2));

            AddButton(body, "Carousel: Previous", () => _carousel.Previous());
            AddButton(body, "Carousel: Next", () => _carousel.Next());
            AddButton(body, "Wheel: Spin to random", () => _wheel.SpinToRandom());

            _listStatus = SampleUiFactory.CreateText("List Status", body, string.Empty, 20, TextAnchor.LowerLeft);
            _listStatus.color = SampleUiFactory.MutedTextColor;
            _listStatus.raycastTarget = false;
            var statusLayout = _listStatus.gameObject.AddComponent<LayoutElement>();
            statusLayout.minHeight = STATUS_MIN_HEIGHT;
            statusLayout.flexibleHeight = 1f;
        }

        private void EditList(int edit)
        {
            switch (edit)
            {
                case 0:
                    _list.InsertAtTop(3);
                    break;
                case 1:
                    _list.RemoveFirstVisible();
                    break;
                default:
                    _list.MoveVisibleToTop();
                    break;
            }

            // 화면 밖 삽입처럼 셀 이벤트가 없는 편집도 상태 줄(개수·인덱스·마지막 편집)을 다시 그린다.
            _listStatusDirty = true;
        }

        private void BuildWheel(RectTransform root)
        {
            RectTransform body = SampleUiFactory.CreateSection("Wheel Section", root,
                new Vector2(0.645f, 0.33f), new Vector2(0.975f, 0.905f), "WHEEL PICKER  ·  loop + center snap + position hook");

            CyScroller scroller = SampleUiFactory.CreateScroller("Wheel Picker", body, ScrollDirection.Vertical, SampleUiFactory.PanelColor);
            SampleUiFactory.Place((RectTransform)scroller.transform, new Vector2(0f, 0.06f), new Vector2(0.56f, 0.94f));
            scroller.ScrollRect.scrollSensitivity = BasicWheelPickerController.ROW_HEIGHT;
            SampleUiFactory.CreateCenterHighlight(scroller, BasicWheelPickerController.ROW_HEIGHT, "min");

            BasicWheelRowView template = SampleUiFactory.CreateWheelRowTemplate("WheelRow", root);
            _wheel = gameObject.AddComponent<BasicWheelPickerController>();
            _wheel.Setup(scroller, template, _wheelValueCount, 30);
            _wheel.ValueSelected += OnWheelValueSelected;

            // 오른쪽: 스냅된 값
            RectTransform info = SampleUiFactory.CreateRect("Selected Value", body);
            SampleUiFactory.Place(info, new Vector2(0.6f, 0.06f), new Vector2(1f, 0.94f));

            Text label = SampleUiFactory.CreateText("Label", info, "SELECTED", 22, TextAnchor.LowerCenter);
            SampleUiFactory.Place(label.rectTransform, new Vector2(0f, 0.62f), new Vector2(1f, 0.72f));
            label.color = SampleUiFactory.MutedTextColor;
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;

            _wheelValue = SampleUiFactory.CreateText("Value", info, "--", 120, TextAnchor.MiddleCenter);
            SampleUiFactory.Place(_wheelValue.rectTransform, new Vector2(0f, 0.36f), new Vector2(1f, 0.62f));
            _wheelValue.fontStyle = FontStyle.Bold;
            _wheelValue.color = SampleUiFactory.AccentColor;
            _wheelValue.raycastTarget = false;

            Text hint = SampleUiFactory.CreateText("Hint", info, "minutes\n\ndrag or scroll,\nit snaps to center", 20, TextAnchor.UpperCenter);
            SampleUiFactory.Place(hint.rectTransform, new Vector2(0f, 0.04f), new Vector2(1f, 0.34f));
            hint.color = SampleUiFactory.MutedTextColor;
            hint.raycastTarget = false;
        }

        private void BuildCarousel(RectTransform root)
        {
            RectTransform body = SampleUiFactory.CreateSection("Carousel Section", root,
                new Vector2(0.025f, 0.035f), new Vector2(0.975f, 0.3f), "CAROUSEL  ·  loop + center snap + position hook");
            _carouselStatus = SampleUiFactory.CreateCaptionStatus(body);

            CyScroller scroller = SampleUiFactory.CreateScroller("Loop Carousel", body, ScrollDirection.Horizontal, SampleUiFactory.PanelColor);
            SampleUiFactory.Stretch((RectTransform)scroller.transform, 0f);
            scroller.Spacing = 20f;
            scroller.Padding = new RectOffset(0, 0, 18, 18);

            BasicCarouselCardView template = SampleUiFactory.CreateCardTemplate("Card", root);
            _carousel = gameObject.AddComponent<BasicCarouselController>();
            _carousel.Setup(scroller, template, _carouselCardCount, CAROUSEL_CARD_WIDTH);
            _carousel.CardCentered += OnCardCentered;
        }

        private static void AddButton(RectTransform panel, string label, UnityEngine.Events.UnityAction onClick)
        {
            Button button = SampleUiFactory.CreateButton(label, panel, onClick);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = BUTTON_HEIGHT;
        }

        private void OnListDisplayChanged(CyScroller scroller, CyScrollerCellView cellView)
        {
            _listStatusDirty = true;
        }

        private void OnListCellActivityChanged(CyScrollerCellView cellView)
        {
            _listStatusDirty = true;
        }

        private void UpdateListStatus()
        {
            CyScroller scroller = _list.Scroller;

            // 실제 뷰포트에 걸친 셀만 센다. 미리보기(lookAhead) 구간 셀은 활성이지만 보이지 않는다.
            int displayed = 0;
            int firstDisplayed = -1;
            int lastDisplayed = -1;
            for (int i = 0; i < scroller.ActiveCellViews.Count; i++)
            {
                CyScrollerCellView view = scroller.ActiveCellViews[i];
                if (view == null || !view.IsDisplayed)
                {
                    continue;
                }

                displayed++;
                if (firstDisplayed < 0)
                {
                    firstDisplayed = view.DataIndex;
                }

                lastDisplayed = view.DataIndex;
            }

            // 인덱스는 데이터 인덱스다 (셀 라벨의 번호는 항목 ID). 위에 삽입하면 같은 항목이 보이는 채로 인덱스만 밀린다.
            _listStatus.text =
                $"List  displayed index {firstDisplayed} ~ {lastDisplayed}  ({displayed} cells)\n" +
                $"{_list.ItemCount} items  ·  active views {scroller.ActiveCellViews.Count}  ·  pooled {scroller.GetRecycledCellCount()}\n" +
                "lookahead: 120px below the viewport\n" +
                _list.LastEdit;
        }

        private void OnCardCentered(int card)
        {
            _carouselStatus.text = $"centered: Card {card}";
        }

        private void OnWheelValueSelected(int value)
        {
            _wheelValue.text = value.ToString("00");
        }
    }
}
