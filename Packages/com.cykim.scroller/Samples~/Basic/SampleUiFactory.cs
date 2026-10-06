using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CyKim.Scroller.Samples.Basic
{
    /// <summary>
    /// 샘플이 씬·프리팹 없이 돌도록 UI를 코드로 만든다. 실제 프로젝트에서는 프리팹과 인스펙터를 쓴다.
    /// 색은 세이지·차콜 라이트 테마다. 면은 Resources의 흰색 9-slice 임시 스프라이트에 색을 곱해(tint) 그리고,
    /// 스프라이트가 없으면 단색으로 그린다 (휠 하이라이트는 옅은 띠와 위·아래 선).
    /// </summary>
    public static class SampleUiFactory
    {
        /// <summary>버튼 모양. 주 버튼은 강조색 면에 흰 글자, 보조 버튼은 보조 면에 본문 글자.</summary>
        public enum ButtonStyle
        {
            Secondary,
            Primary,
        }

        /// <summary>화면 배경.</summary>
        public static readonly Color BackgroundColor = new Color32(0xFC, 0xFD, 0xFC, 0xFF);

        /// <summary>보조 면. 스크롤러 패널과 보조 버튼 면.</summary>
        public static readonly Color SurfaceColor = new Color32(0xF0, 0xF3, 0xF0, 0xFF);

        /// <summary>본문 글자. 제목·셀·휠 행 글자.</summary>
        public static readonly Color TextColor = new Color32(0x2C, 0x33, 0x33, 0xFF);

        /// <summary>흐린 글자. 부제·캡션·상태 줄.</summary>
        public static readonly Color MutedTextColor = new Color32(0x51, 0x5B, 0x56, 0xFF);

        /// <summary>강조 세이지. 강조 글자, 주 버튼 면, 휠 하이라이트.</summary>
        public static readonly Color AccentColor = new Color32(0x56, 0x7E, 0x65, 0xFF);

        /// <summary>강조색 면 위 글자.</summary>
        public static readonly Color OnAccentTextColor = Color.white;

        private const float CAPTION_HEIGHT = 34f;
        private const float CAPTION_GAP = 10f;

        // Resources 경로 "CyKimScrollerBasic/<이름>". 같은 이름의 Sprite로 바꾸면 그 모양으로 그린다.
        private const string SPRITE_FOLDER = "CyKimScrollerBasic/";
        private const string PANEL_SPRITE = "Panel";
        private const string CARD_SPRITE = "Card";
        private const string BUTTON_SPRITE = "Button";
        private const string PILL_SPRITE = "Pill";

        // Panel 스프라이트 안쪽 테두리 두께. 셀이 테두리를 덮지 않게 뷰포트 마스크를 이만큼 안으로 줄인다 (레이아웃은 그대로).
        // cyKimScroller 저장소 tools/generate_sample_textures.py(패키지 밖)의 PANEL_RING과 같은 값이다. 한쪽을 바꾸면 다른 쪽도 바꾼다.
        private const float PANEL_EDGE = 2f;

        // Card·Button 스프라이트 아래 립(눌림 입체감) 두께. 글자를 립 위 면의 가운데에 둔다.
        // 생성 스크립트의 CARD_LIP·BUTTON_LIP과 같은 값이다.
        private const float CARD_LIP = 4f;
        private const float BUTTON_LIP = 5f;

        // 버튼 상태 색. 면 색(Image.color) 위에 곱한다.
        private const float BUTTON_HIGHLIGHT = 0.95f;
        private const float BUTTON_PRESSED = 0.85f;

        // 휠 하이라이트 강조색 알파. Pill 면 알파(0.35)와 곱한 실효 알파로 선택 영역처럼 옅은 강조색 띠를 그린다.
        // Linear 색 공간(URP 기본)은 선형 값으로 섞어 같은 알파가 Gamma보다 옅게 보이므로 더 높게 잡는다.
        // 두 색 공간 모두 sRGB로 섞은 기준 실효 알파 약 0.21~0.24이고, 띠 위 강조색 단위 글자 대비는 굵은 큰 글자 기준 3 이상이다 (Linear 약 3.2, Gamma 약 3.1).
        // 스프라이트가 없으면 단색 띠라 더 옅게 그리고 위·아래 선을 더한다.
        private const float HIGHLIGHT_ALPHA_LINEAR = 0.85f;
        private const float HIGHLIGHT_ALPHA_GAMMA = 0.7f;
        private const float FALLBACK_HIGHLIGHT_ALPHA = 0.12f;
        private const float FALLBACK_LINE_ALPHA = 0.7f;

        // 목록 셀 면에 곱할 밝은 세이지 계열 톤. 흰색에 세이지(#A6B8A6)를 섞은 톤을 기본으로, 강조·슬레이트를 옅게 섞은 톤을 사이에 둔다.
        // 밝은 톤과 조금 진한 톤을 번갈아 둬 이웃 톤끼리 구분되고, 진한 톤도 목록이 무겁지 않게 세이지 절반 안팎으로 둔다.
        // 어느 톤에서도 본문 글자 대비가 4.5 이상이다 (가장 진한 톤에서 약 8.9).
        private static readonly Color[] _sageTones =
        {
            new Color32(0xFA, 0xFB, 0xFA, 0xFF), // 흰색 + 세이지 6%
            new Color32(0xDA, 0xE2, 0xDC, 0xFF), // 흰색 + 세이지 22%, 강조 12%
            new Color32(0xE8, 0xEC, 0xE8, 0xFF), // 흰색 + 세이지 21%, 슬레이트 3%
            new Color32(0xCF, 0xD9, 0xCF, 0xFF), // 흰색 + 세이지 54%
            new Color32(0xDF, 0xE5, 0xDF, 0xFF), // 흰색 + 세이지 36%
            new Color32(0xD7, 0xDE, 0xD8, 0xFF), // 흰색 + 세이지 35%, 슬레이트 7%
        };

        // 캐러셀 카드 면 톤. 패널(#F0F3F0)보다 확실히 어두운 세이지 계열 4가지를 섞지 않고 차례로 돌려 쓴다 (패널 대비 1.14 이상, 이웃 톤 대비 1.08 이상).
        // 카드가 패널에 묻히지 않고 이웃 카드끼리 구분되며, 위치 훅으로 흐려진 양옆 카드는 패널 쪽으로 밝아진다.
        // 첫 톤을 가장 진하게 둬 처음 가운데에 오는 Card 0이 도드라진다.
        private static readonly Color[] _cardTones =
        {
            new Color32(0xBF, 0xCC, 0xBF, 0xFF),
            new Color32(0xCA, 0xD3, 0xCB, 0xFF),
            new Color32(0xDF, 0xE5, 0xDF, 0xFF),
            new Color32(0xCE, 0xD8, 0xD0, 0xFF),
        };

        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private static bool _missingSpriteWarned;
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

        /// <summary>
        /// 목록 셀 면에 곱할 밝은 세이지 계열 톤. <paramref name="index"/>마다 다음 톤으로 넘어가고 톤 수만큼 돌면 처음으로 돌아온다.
        /// 이웃 번호끼리 구분된다. 항목을 만들 때 미리 계산해 둔다.
        /// </summary>
        public static Color GetSageTone(int index)
        {
            int count = _sageTones.Length;
            return _sageTones[(index % count + count) % count];
        }

        /// <summary>
        /// 캐러셀 카드 면 톤. <paramref name="index"/>마다 다음 톤으로 넘어가고 4장마다 처음으로 돌아온다.
        /// 이웃 번호끼리 구분되고 어느 톤도 패널 색과 겹치지 않는다. 항목을 만들 때 미리 계산해 둔다.
        /// </summary>
        public static Color GetCardTone(int index)
        {
            int count = _cardTones.Length;
            return _cardTones[(index % count + count) % count];
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

        /// <summary>
        /// Viewport(RectMask2D) → Content 구조의 ScrollRect에 CyScroller를 붙여 만든다.
        /// 배경은 Panel 스프라이트에 <paramref name="tint"/>를 곱해 그리고, 드래그를 받는다.
        /// </summary>
        public static CyScroller CreateScroller(string name, RectTransform parent, ScrollDirection direction, Color tint)
        {
            RectTransform root = CreateRect(name, parent);
            var image = root.gameObject.AddComponent<Image>();
            bool hasPanel = ApplySprite(image, PANEL_SPRITE, tint);

            RectTransform viewport = CreateRect("Viewport", root);
            Stretch(viewport, 0f);
            var mask = viewport.gameObject.AddComponent<RectMask2D>();
            if (hasPanel)
            {
                // 잘라 내는 영역만 줄인다. 뷰포트 크기(스크롤러 계산)는 그대로다.
                mask.padding = new Vector4(PANEL_EDGE, PANEL_EDGE, PANEL_EDGE, PANEL_EDGE);
            }

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
        /// 배경은 Card 스프라이트이고 색은 바인딩 때 항목 색을 곱한다.
        /// </summary>
        public static BasicCellView CreateListItemTemplate(string identifier, RectTransform parent)
        {
            RectTransform root = CreateTemplateRoot(identifier, parent);

            RectTransform visual = CreateRect("Visual", root);
            Stretch(visual, 0f);
            var background = visual.gameObject.AddComponent<Image>();
            bool hasCard = ApplySprite(background, CARD_SPRITE, Color.white);

            Text label = CreateText("Label", visual, string.Empty, 26, TextAnchor.MiddleLeft);
            Stretch(label.rectTransform, 0f);
            label.rectTransform.offsetMin = new Vector2(22f, hasCard ? CARD_LIP : 0f);
            label.color = TextColor;
            label.raycastTarget = false;

            var view = root.gameObject.AddComponent<BasicCellView>();
            view.CellIdentifier = identifier;
            view.Setup(visual, background, label);
            return view;
        }

        /// <summary>
        /// 캐러셀 카드 템플릿. 배경은 Card 스프라이트이고 색은 바인딩 때 카드 색을 곱한다.
        /// 위치 훅이 Visual 자식의 스케일과 CanvasGroup 투명도를 바꾼다.
        /// </summary>
        public static BasicCarouselCardView CreateCardTemplate(string identifier, RectTransform parent)
        {
            RectTransform root = CreateTemplateRoot(identifier, parent);

            RectTransform visual = CreateRect("Visual", root);
            Stretch(visual, 0f);
            var background = visual.gameObject.AddComponent<Image>();
            bool hasCard = ApplySprite(background, CARD_SPRITE, Color.white);
            var group = visual.gameObject.AddComponent<CanvasGroup>();

            Text label = CreateText("Label", visual, string.Empty, 40, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 8f);
            label.rectTransform.offsetMin = new Vector2(8f, 8f + (hasCard ? CARD_LIP : 0f));
            label.fontStyle = FontStyle.Bold;
            label.color = TextColor;
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
        /// 뷰포트 가운데 행을 가리키는 캡슐 하이라이트와 단위 글자. 스크롤러 루트의 첫 자식으로 뷰포트 뒤에 깔려 행 글자가 위에 그려지고,
        /// 레이캐스트는 막지 않는다. Pill 스프라이트(반투명 면 + 테두리)에 강조색을 곱하고, 스프라이트가 없으면 옅은 띠와 위·아래 선으로 그린다.
        /// </summary>
        public static void CreateCenterHighlight(CyScroller scroller, float rowHeight, string unit)
        {
            RectTransform bar = CreateRect("Center Highlight", (RectTransform)scroller.transform);
            bar.SetAsFirstSibling();
            bar.anchorMin = new Vector2(0f, 0.5f);
            bar.anchorMax = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(-24f, rowHeight);
            var fill = bar.gameObject.AddComponent<Image>();
            fill.raycastTarget = false;
            float highlightAlpha = QualitySettings.activeColorSpace == ColorSpace.Linear ? HIGHLIGHT_ALPHA_LINEAR : HIGHLIGHT_ALPHA_GAMMA;
            if (!ApplySprite(fill, PILL_SPRITE, WithAlpha(AccentColor, highlightAlpha)))
            {
                fill.color = WithAlpha(AccentColor, FALLBACK_HIGHLIGHT_ALPHA);
                CreateLine("Top Line", bar, 1f);
                CreateLine("Bottom Line", bar, 0f);
            }

            Text unitText = CreateText("Unit", bar, unit, 24, TextAnchor.MiddleRight);
            Stretch(unitText.rectTransform, 0f);
            unitText.rectTransform.offsetMax = new Vector2(-18f, 0f);
            unitText.fontStyle = FontStyle.Bold;
            unitText.color = AccentColor;
            unitText.raycastTarget = false;
        }

        /// <summary>
        /// 버튼. 면은 Button 스프라이트에 스타일 색을 곱하고, 상태(하이라이트·눌림) 색은 그 위에 곱한다.
        /// GameObject 이름과 글자는 <paramref name="label"/> 그대로다.
        /// </summary>
        public static Button CreateButton(string label, RectTransform parent, UnityAction onClick, ButtonStyle style = ButtonStyle.Secondary)
        {
            bool primary = style == ButtonStyle.Primary;

            RectTransform root = CreateRect(label, parent);
            var image = root.gameObject.AddComponent<Image>();
            bool hasSprite = ApplySprite(image, BUTTON_SPRITE, primary ? AccentColor : SurfaceColor);

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(BUTTON_HIGHLIGHT, BUTTON_HIGHLIGHT, BUTTON_HIGHLIGHT, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(BUTTON_PRESSED, BUTTON_PRESSED, BUTTON_PRESSED, 1f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Text", root, label, 24, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 0f);
            text.rectTransform.offsetMin = new Vector2(0f, hasSprite ? BUTTON_LIP : 0f);
            text.color = primary ? OnAccentTextColor : TextColor;
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
            text.color = TextColor;

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

        // 도메인 리로드를 끈 Enter Play Mode에서도 Play마다 스프라이트를 다시 찾고, 없으면 경고를 다시 한 번 남긴다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _sprites.Clear();
            _missingSpriteWarned = false;
        }

        /// <summary>
        /// 이미지를 9-slice 스프라이트 × <paramref name="tint"/>로 그린다. 스프라이트가 없으면 같은 색 단색으로 그린다.
        /// 스프라이트를 입혔으면 true.
        /// </summary>
        private static bool ApplySprite(Image image, string spriteName, Color tint)
        {
            Sprite sprite = LoadSprite(spriteName);
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = tint;
            return sprite != null;
        }

        /// <summary>
        /// Resources에서 샘플 스프라이트를 찾는다. 찾은 스프라이트는 한 번만 로드해 두고,
        /// 없으면 null(호출부가 단색으로 그린다)을 돌려주며 경고는 처음 한 번만 남긴다.
        /// </summary>
        private static Sprite LoadSprite(string spriteName)
        {
            if (_sprites.TryGetValue(spriteName, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Sprite sprite = Resources.Load<Sprite>(SPRITE_FOLDER + spriteName);
            if (sprite != null)
            {
                _sprites[spriteName] = sprite;
                return sprite;
            }

            if (!_missingSpriteWarned)
            {
                _missingSpriteWarned = true;
                Debug.LogWarning($"[SampleUiFactory] 샘플 스프라이트가 없어 단색으로 그립니다: Resources/{SPRITE_FOLDER}{spriteName} (다른 샘플 스프라이트가 없어도 다시 알리지 않습니다)");
            }

            return null;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
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
            image.color = WithAlpha(AccentColor, FALLBACK_LINE_ALPHA);
            image.raycastTarget = false;
        }
    }
}
