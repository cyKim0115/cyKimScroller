using UnityEngine;
using UnityEngine.UI;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 빈 씬의 GameObject에 붙이고 Play하면 샘플 UI 전체를 만든다.
    /// 왼쪽: 높이가 다른 500개 세로 목록 / 오른쪽: 점프 버튼 / 아래: 순환·스냅 캐러셀.
    /// </summary>
    public class BasicSample : MonoBehaviour
    {
        [SerializeField, Min(0)] private int _listItemCount = 500;
        [SerializeField, Min(1)] private int _carouselCardCount = 12;

        private BasicListController _list;
        private BasicCarouselController _carousel;
        private Text _status;

        private void Start()
        {
            SampleUiFactory.EnsureEventSystem();

            Canvas canvas = SampleUiFactory.CreateCanvas("CyScroller Sample Canvas");
            var root = (RectTransform)canvas.transform;

            BuildList(root);
            BuildCarousel(root);
            BuildButtons(root);
        }

        private void BuildList(RectTransform root)
        {
            CyScroller scroller = SampleUiFactory.CreateScroller("Vertical List", root, ScrollDirection.Vertical, new Color(0.93f, 0.94f, 0.96f));
            SampleUiFactory.Place((RectTransform)scroller.transform, new Vector2(0.03f, 0.32f), new Vector2(0.5f, 0.97f));
            scroller.Spacing = 6f;
            scroller.Padding = new RectOffset(12, 12, 12, 12);
            scroller.LookAheadAfter = 120f;

            BasicCellView template = SampleUiFactory.CreateCellTemplate("ListItem", root, 30);
            _list = gameObject.AddComponent<BasicListController>();
            _list.Setup(scroller, template, _listItemCount);

            scroller.ScrollerScrolled += OnListScrolled;
        }

        private void BuildCarousel(RectTransform root)
        {
            CyScroller scroller = SampleUiFactory.CreateScroller("Loop Carousel", root, ScrollDirection.Horizontal, new Color(0.16f, 0.17f, 0.21f));
            SampleUiFactory.Place((RectTransform)scroller.transform, new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.28f));
            scroller.Spacing = 20f;

            BasicCellView template = SampleUiFactory.CreateCellTemplate("Card", root, 36);
            _carousel = gameObject.AddComponent<BasicCarouselController>();
            _carousel.Setup(scroller, template, _carouselCardCount, 260f);
        }

        private void BuildButtons(RectTransform root)
        {
            RectTransform panel = SampleUiFactory.CreateRect("Buttons", root);
            SampleUiFactory.Place(panel, new Vector2(0.55f, 0.32f), new Vector2(0.97f, 0.97f));

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            AddButton(panel, "List: Top", () => _list.JumpTo(0));
            AddButton(panel, "List: Item #250 (center)", () => _list.JumpTo(250));
            AddButton(panel, "List: Bottom", () => _list.JumpTo(_list.ItemCount - 1));
            AddButton(panel, "List: Random", () => _list.JumpTo(Random.Range(0, _list.ItemCount)));
            AddButton(panel, "Carousel: Previous", () => _carousel.Previous());
            AddButton(panel, "Carousel: Next", () => _carousel.Next());

            _status = SampleUiFactory.CreateText("Status", panel, string.Empty, 26, TextAnchor.UpperLeft);
            _status.color = new Color(0.85f, 0.87f, 0.9f);
            _status.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
        }

        private static void AddButton(RectTransform panel, string label, UnityEngine.Events.UnityAction onClick)
        {
            Button button = SampleUiFactory.CreateButton(label, panel, onClick);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
        }

        private void OnListScrolled(CyScroller scroller, Vector2 normalizedPosition, float scrollPosition)
        {
            // 샘플 표시용. 실제 핫패스에서는 문자열을 매 프레임 만들지 않는다.
            _status.text = $"List visible data: {scroller.StartDataIndex} ~ {scroller.EndDataIndex}\n" +
                           $"Active views: {scroller.ActiveCellViews.Count}, recycled: {scroller.GetRecycledCellCount()}";
        }
    }
}
