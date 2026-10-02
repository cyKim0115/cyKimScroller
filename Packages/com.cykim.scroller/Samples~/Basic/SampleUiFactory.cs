using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 샘플이 씬·프리팹 없이 돌도록 UI를 코드로 만든다. 실제 프로젝트에서는 프리팹과 인스펙터를 쓴다.
    /// </summary>
    public static class SampleUiFactory
    {
        public static readonly Color BackgroundColor = new Color(0.071f, 0.078f, 0.102f);
        public static readonly Color PanelColor = new Color(0.118f, 0.129f, 0.165f);
        public static readonly Color ButtonColor = new Color(0.180f, 0.200f, 0.259f);
        public static readonly Color AccentColor = new Color(0.475f, 0.639f, 1f);
        public static readonly Color TextColor = new Color(0.925f, 0.937f, 0.965f);
        public static readonly Color MutedTextColor = new Color(0.580f, 0.620f, 0.710f);
        public static readonly Color DarkTextColor = new Color(0.078f, 0.090f, 0.118f);

        private const float CAPTION_HEIGHT = 34f;
        private const float CAPTION_GAP = 10f;

        private static Font _font;

        private static Font DefaultFont
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return _font;
            }
        }

        public static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        /// <summary>화면 전체 배경. 레이캐스트는 받지 않는다.</summary>
        public static void CreateBackground(RectTransform parent)
        {
            RectTransform rect = CreateRect("Background", parent);
            Stretch(rect, 0f);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = BackgroundColor;
            image.raycastTarget = false;
        }

        /// <summary>
        /// 위쪽 캡션 줄과 그 아래 본문 영역. 부모 기준 비율 영역(0~1)에 놓고 본문 RectTransform을 돌려준다.
        /// </summary>
        public static RectTransform CreateSection(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, string caption)
        {
            RectTransform section = CreateRect(name, parent);
            Place(section, anchorMin, anchorMax);

            Text title = CreateText("Caption", section, caption, 22, TextAnchor.MiddleLeft);
            title.color = MutedTextColor;
            title.fontStyle = FontStyle.Bold;
            title.raycastTarget = false;
            PlaceTop(title.rectTransform, CAPTION_HEIGHT);

            RectTransform body = CreateRect("Body", section);
            Stretch(body, 0f);
            body.offsetMax = new Vector2(0f, -(CAPTION_HEIGHT + CAPTION_GAP));
            return body;
        }

        /// <summary>섹션 캡션 줄 오른쪽에 붙는 상태 텍스트. <paramref name="body"/>는 <see cref="CreateSection"/>이 돌려준 본문.</summary>
        public static Text CreateCaptionStatus(RectTransform body)
        {
            Text status = CreateText("Caption Status", (RectTransform)body.parent, string.Empty, 22, TextAnchor.MiddleRight);
            status.color = AccentColor;
            status.raycastTarget = false;
            PlaceTop(status.rectTransform, CAPTION_HEIGHT);
            return status;
        }

        /// <summary>Viewport(RectMask2D) → Content 구조의 ScrollRect에 CyScroller를 붙여 만든다.</summary>
        public static CyScroller CreateScroller(string name, RectTransform parent, ScrollDirection direction, Color background)
        {
            RectTransform root = CreateRect(name, parent);
            var image = root.gameObject.AddComponent<Image>();
            image.color = background;

            RectTransform viewport = CreateRect("Viewport", root);
            Stretch(viewport, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateRect("Content", viewport);

            var scrollRect = root.gameObject.AddComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.scrollSensitivity = 30f;

            // ScrollRect를 먼저 구성해야 CyScroller.Awake가 content를 찾는다.
            var scroller = root.gameObject.AddComponent<CyScroller>();
            scroller.ScrollDirection = direction;
            return scroller;
        }

        /// <summary>
        /// 목록 셀 템플릿. 비활성 GameObject로 만들어 두고 Instantiate 원본으로만 쓴다.
        /// </summary>
        public static BasicCellView CreateListItemTemplate(string identifier, RectTransform parent)
        {
            RectTransform root = CreateTemplateRoot(identifier, parent);

            RectTransform visual = CreateRect("Visual", root);
            Stretch(visual, 0f);
            var background = visual.gameObject.AddComponent<Image>();

            Text label = CreateText("Label", visual, string.Empty, 26, TextAnchor.MiddleLeft);
            Stretch(label.rectTransform, 0f);
            label.rectTransform.offsetMin = new Vector2(22f, 0f);
            label.color = DarkTextColor;
            label.raycastTarget = false;

            var view = root.gameObject.AddComponent<BasicCellView>();
            view.CellIdentifier = identifier;
            view.Setup(visual, background, label);
            return view;
        }

        /// <summary>캐러셀 카드 템플릿. 위치 훅이 Visual 자식의 스케일과 CanvasGroup 투명도를 바꾼다.</summary>
        public static BasicCarouselCardView CreateCardTemplate(string identifier, RectTransform parent)
        {
            RectTransform root = CreateTemplateRoot(identifier, parent);

            RectTransform visual = CreateRect("Visual", root);
            Stretch(visual, 0f);
            var background = visual.gameObject.AddComponent<Image>();
            var group = visual.gameObject.AddComponent<CanvasGroup>();

            Text label = CreateText("Label", visual, string.Empty, 40, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 8f);
            label.fontStyle = FontStyle.Bold;
            label.color = DarkTextColor;
            label.raycastTarget = false;

            var view = root.gameObject.AddComponent<BasicCarouselCardView>();
            view.CellIdentifier = identifier;
            view.Setup(visual, background, label);
            view.SetupCard(group);
            return view;
        }

        /// <summary>휠 피커 행 템플릿. 행은 레이캐스트를 받지 않고 드래그는 스크롤러 배경이 받는다.</summary>
        public static BasicWheelRowView CreateWheelRowTemplate(string identifier, RectTransform parent)
        {
            RectTransform root = CreateTemplateRoot(identifier, parent);

            RectTransform visual = CreateRect("Visual", root);
            Stretch(visual, 0f);
            var group = visual.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            Text label = CreateText("Label", visual, string.Empty, 40, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 0f);
            label.fontStyle = FontStyle.Bold;
            label.color = TextColor;
            label.raycastTarget = false;

            var view = root.gameObject.AddComponent<BasicWheelRowView>();
            view.CellIdentifier = identifier;
            view.Setup(visual, group, label);
            return view;
        }

        /// <summary>
        /// 뷰포트 가운데 행을 가리키는 반투명 띠와 단위 글자. 스크롤러 루트의 마지막 자식으로 뷰포트 위에 겹치고 레이캐스트는 막지 않는다.
        /// </summary>
        public static void CreateCenterHighlight(CyScroller scroller, float rowHeight, string unit)
        {
            RectTransform bar = CreateRect("Center Highlight", (RectTransform)scroller.transform);
            bar.anchorMin = new Vector2(0f, 0.5f);
            bar.anchorMax = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(-24f, rowHeight);
            var fill = bar.gameObject.AddComponent<Image>();
            fill.color = new Color(1f, 1f, 1f, 0.06f);
            fill.raycastTarget = false;

            CreateLine("Top Line", bar, 1f);
            CreateLine("Bottom Line", bar, 0f);

            Text unitText = CreateText("Unit", bar, unit, 24, TextAnchor.MiddleRight);
            Stretch(unitText.rectTransform, 0f);
            unitText.rectTransform.offsetMax = new Vector2(-18f, 0f);
            unitText.color = AccentColor;
            unitText.raycastTarget = false;
        }

        public static Button CreateButton(string label, RectTransform parent, UnityAction onClick)
        {
            RectTransform root = CreateRect(label, parent);
            var image = root.gameObject.AddComponent<Image>();
            image.color = ButtonColor;

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Text", root, label, 24, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 0f);
            text.color = TextColor;
            text.raycastTarget = false;
            return button;
        }

        public static Text CreateText(string name, RectTransform parent, string value, int fontSize, TextAnchor alignment)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = DefaultFont;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = DarkTextColor;

            // 영역이 글자 높이보다 조금 작아도 잘려 사라지지 않게 한다.
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static RectTransform CreateRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>부모 기준 비율 영역(0~1)에 맞춘다.</summary>
        public static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>부모 위쪽에 높이 <paramref name="height"/>인 띠로 붙인다.</summary>
        public static void PlaceTop(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -height);
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform CreateTemplateRoot(string identifier, RectTransform parent)
        {
            RectTransform root = CreateRect(identifier + "Template", parent);
            root.gameObject.SetActive(false);
            return root;
        }

        private static void CreateLine(string name, RectTransform parent, float anchorY)
        {
            RectTransform line = CreateRect(name, parent);
            line.anchorMin = new Vector2(0f, anchorY);
            line.anchorMax = new Vector2(1f, anchorY);
            line.sizeDelta = new Vector2(0f, 3f);
            var image = line.gameObject.AddComponent<Image>();
            image.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.7f);
            image.raycastTarget = false;
        }
    }
}
