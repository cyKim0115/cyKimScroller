using CyKim.Scroller;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CyKim.Scroller.Dev
{
    /// <summary>
    /// 개발 검증용. 셀 수만 개를 자동으로 왕복 스크롤하며 프레임당 GC 할당·활성 셀 수를 HUD에 띄운다.
    /// HUD의 프레임 할당에는 에디터 자체와 uGUI Text 메시 재생성 몫이 섞인다.
    /// 스크롤러+바인딩만의 할당은 2026-10-01 측정에서 4000단계 0 B (GC.GetAllocatedBytesForCurrentThread).
    /// 에디터가 백그라운드면 플레이가 멈추므로 Application.runInBackground를 켠다.
    /// </summary>
    public class DevStressHarness : MonoBehaviour, ICyScrollerDelegate
    {
        private const float HUD_INTERVAL = 0.5f;

        [SerializeField, Min(1)] private int _cellCount = 100000;
        [SerializeField] private bool _loop;
        [SerializeField] private bool _autoScroll = true;
        [SerializeField, Min(0f)] private float _autoScrollSpeed = 4000f;
        [SerializeField, Min(0f)] private float _lookAhead = 200f;

        private CyScroller _scroller;
        private DevStressCellView _template;
        private Text _hud;
        private float[] _sizes;
        private string[] _labels;
        private Color[] _colors;

        private ProfilerRecorder _gcRecorder;
        private float _hudTimer;
        private bool _hudUpdatedLastFrame;
        private long _maxScrollFrameAlloc;
        private int _framesSinceHud;
        private float _direction = 1f;

        private void OnEnable()
        {
            _gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        private void OnDisable()
        {
            _gcRecorder.Dispose();
        }

        private void Start()
        {
            Application.runInBackground = true;
            BuildData();
            BuildUi();
            _scroller.Loop = _loop;
            _scroller.LookAheadBefore = _lookAhead;
            _scroller.LookAheadAfter = _lookAhead;
            _scroller.Delegate = this;
        }

        private void Update()
        {
            if (_autoScroll && _scroller.NumberOfCells > 0 && !_scroller.IsDragging)
            {
                float next = _scroller.ScrollPosition + _direction * _autoScrollSpeed * Time.unscaledDeltaTime;
                if (!_scroller.Loop && (next <= 0f || next >= _scroller.ScrollSize))
                {
                    _direction = -_direction;
                }

                _scroller.ScrollPosition = next;
            }

            // HUD 갱신이 만든 문자열 할당은 다음 프레임 기록에 잡히므로 그 프레임은 제외한다.
            if (_gcRecorder.Valid && !_hudUpdatedLastFrame)
            {
                long alloc = _gcRecorder.LastValue;
                if (alloc > _maxScrollFrameAlloc)
                {
                    _maxScrollFrameAlloc = alloc;
                }
            }

            _hudUpdatedLastFrame = false;
            _framesSinceHud++;
            _hudTimer += Time.unscaledDeltaTime;
            if (_hudTimer >= HUD_INTERVAL)
            {
                UpdateHud();
                _hudUpdatedLastFrame = true;
            }
        }

        private void UpdateHud()
        {
            float fps = _framesSinceHud / _hudTimer;
            _hud.text =
                $"cells {_scroller.NumberOfCells:N0}  loop {_scroller.Loop}  fps {fps:0}\n" +
                $"active {_scroller.ActiveCellViews.Count}  recycled {_scroller.GetRecycledCellCount()}  " +
                $"range {_scroller.StartDataIndex}~{_scroller.EndDataIndex}\n" +
                $"max GC/frame (HUD 제외, 최근 {HUD_INTERVAL}s) {_maxScrollFrameAlloc} B";
            _hudTimer = 0f;
            _framesSinceHud = 0;
            _maxScrollFrameAlloc = 0;
        }

        private void BuildData()
        {
            _sizes = new float[_cellCount];
            _labels = new string[_cellCount];
            _colors = new Color[_cellCount];
            for (int i = 0; i < _cellCount; i++)
            {
                _sizes[i] = 48f + (i * 7919 % 4) * 24f;
                _labels[i] = "#" + i;
                _colors[i] = (i & 1) == 0 ? new Color(0.92f, 0.93f, 0.96f) : new Color(0.84f, 0.87f, 0.93f);
            }
        }

        private void BuildUi()
        {
            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                eventSystem.AddComponent<StandaloneInputModule>();
#endif
            }

            var canvasGo = new GameObject("Dev Canvas", typeof(RectTransform));
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var canvas = (RectTransform)canvasGo.transform;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            RectTransform scrollRoot = CreateRect("Stress Scroller", canvas);
            Place(scrollRoot, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.85f));
            scrollRoot.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.21f, 0.25f);

            RectTransform viewport = CreateRect("Viewport", scrollRoot);
            Place(viewport, Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = CreateRect("Content", viewport);

            var scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            _scroller = scrollRoot.gameObject.AddComponent<CyScroller>();
            _scroller.Spacing = 2f;

            RectTransform templateRoot = CreateRect("CellTemplate", canvas);
            templateRoot.gameObject.SetActive(false);
            var background = templateRoot.gameObject.AddComponent<Image>();
            background.raycastTarget = true;
            Text label = CreateText(templateRoot, font, 30, TextAnchor.MiddleLeft);
            Place(label.rectTransform, Vector2.zero, Vector2.one);
            label.rectTransform.offsetMin = new Vector2(24f, 0f);
            _template = templateRoot.gameObject.AddComponent<DevStressCellView>();
            _template.CellIdentifier = "Stress";
            _template.Setup(background, label);

            RectTransform hudRect = CreateRect("HUD", canvas);
            Place(hudRect, new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.98f));
            hudRect.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
            _hud = CreateText(hudRect, font, 32, TextAnchor.UpperLeft);
            Place(_hud.rectTransform, Vector2.zero, Vector2.one);
            _hud.rectTransform.offsetMin = new Vector2(16f, 8f);
            _hud.rectTransform.offsetMax = new Vector2(-16f, -8f);
            _hud.color = Color.white;
        }

        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Text CreateText(RectTransform parent, Font font, int size, TextAnchor anchor)
        {
            RectTransform rect = CreateRect("Text", parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = new Color(0.1f, 0.1f, 0.12f);
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public int GetNumberOfCells(CyScroller scroller) => _cellCount;

        public float GetCellViewSize(CyScroller scroller, int dataIndex) => _sizes[dataIndex];

        public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
        {
            var view = (DevStressCellView)scroller.GetCellView(_template);
            view.SetData(_labels[dataIndex], _colors[dataIndex]);
            return view;
        }
    }
}
