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
        /// 셀 템플릿. 비활성 GameObject로 만들어 두고 Instantiate 원본으로만 쓴다.
        /// </summary>
        public static BasicCellView CreateCellTemplate(string identifier, RectTransform parent, int fontSize)
        {
            RectTransform root = CreateRect(identifier + "Template", parent);
            root.gameObject.SetActive(false);

            RectTransform visual = CreateRect("Visual", root);
            Stretch(visual, 4f);
            var background = visual.gameObject.AddComponent<Image>();

            Text label = CreateText("Label", visual, string.Empty, fontSize, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 8f);
            label.raycastTarget = false;

            var view = root.gameObject.AddComponent<BasicCellView>();
            view.CellIdentifier = identifier;
            view.Setup(visual, background, label);
            return view;
        }

        public static Button CreateButton(string label, RectTransform parent, UnityAction onClick)
        {
            RectTransform root = CreateRect(label, parent);
            var image = root.gameObject.AddComponent<Image>();
            image.color = new Color(0.22f, 0.24f, 0.3f);

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Text", root, label, 28, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 0f);
            text.color = Color.white;
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
            text.color = new Color(0.1f, 0.1f, 0.12f);
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
    }
}
