using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CyKim.Scroller.Tests
{
    /// <summary>테스트 델리게이트. 크기 배열과 프리팹 두 종류(식별자 테스트용)를 쓴다.</summary>
    internal sealed class TestDelegate : ICyScrollerDelegate
    {
        public float[] Sizes;
        public CyScrollerCellView Prefab;
        public CyScrollerCellView AltPrefab;
        public int AltEvery;
        public int GetCellViewCalls;

        /// <summary>바인딩을 마친 뒤(반환 직전) 불린다. 델리게이트 안의 사용자 코드를 흉내 낸다.</summary>
        public Action<CyScroller, int> GetCellViewHook;

        public int GetNumberOfCells(CyScroller scroller) => Sizes.Length;

        public float GetCellViewSize(CyScroller scroller, int dataIndex) => Sizes[dataIndex];

        public CyScrollerCellView GetCellView(CyScroller scroller, int dataIndex, int cellIndex)
        {
            GetCellViewCalls++;
            CyScrollerCellView prefab = AltPrefab != null && AltEvery > 0 && dataIndex % AltEvery == 0 ? AltPrefab : Prefab;
            var view = (TestCellView)scroller.GetCellView(prefab);
            view.BoundData = dataIndex;
            view.BoundVersion = view.BindVersion;
            GetCellViewHook?.Invoke(scroller, dataIndex);
            return view;
        }

        public static float[] Uniform(int count, float size)
        {
            var sizes = new float[count];
            for (int i = 0; i < count; i++)
            {
                sizes[i] = size;
            }

            return sizes;
        }
    }

    /// <summary>
    /// Canvas → ScrollRect(고정 크기) → Viewport → Content 계층과 셀 템플릿을 코드로 만든다.
    /// </summary>
    internal sealed class ScrollerFixture : IDisposable
    {
        public const float VIEWPORT_WIDTH = 300f;
        public const float VIEWPORT_HEIGHT = 400f;

        public GameObject Root;
        public ScrollRect ScrollRect;
        public RectTransform Content;
        public CyScroller Scroller;
        public TestDelegate Delegate;
        public TestCellView Prefab;
        public TestCellView AltPrefab;
        public int InstantiatedCount;

        public static ScrollerFixture Create(
            ScrollDirection direction,
            float[] sizes,
            bool loop = false,
            float spacing = 0f,
            RectOffset padding = null,
            bool reload = true)
        {
            var fixture = new ScrollerFixture();
            fixture.Build(direction, sizes, loop, spacing, padding, reload);
            return fixture;
        }

        private void Build(ScrollDirection direction, float[] sizes, bool loop, float spacing, RectOffset padding, bool reload)
        {
            Root = new GameObject("ScrollerFixture", typeof(RectTransform));
            Root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            var scrollRectTransform = (RectTransform)scrollGo.transform;
            scrollRectTransform.SetParent(Root.transform, false);
            scrollRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            scrollRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRectTransform.sizeDelta = new Vector2(VIEWPORT_WIDTH, VIEWPORT_HEIGHT);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewport = (RectTransform)viewportGo.transform;
            viewport.SetParent(scrollRectTransform, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            Content = (RectTransform)contentGo.transform;
            Content.SetParent(viewport, false);

            ScrollRect = scrollGo.AddComponent<ScrollRect>();
            ScrollRect.viewport = viewport;
            ScrollRect.content = Content;
            ScrollRect.movementType = ScrollRect.MovementType.Elastic;

            Prefab = CreateTemplate("CellTemplate", "Test");
            AltPrefab = CreateTemplate("AltCellTemplate", "Alt");

            Delegate = new TestDelegate { Sizes = sizes, Prefab = Prefab };

            Scroller = scrollGo.AddComponent<CyScroller>();
            Scroller.ScrollDirection = direction;
            Scroller.Spacing = spacing;
            Scroller.Padding = padding ?? new RectOffset();
            Scroller.Loop = loop;
            Scroller.CellViewInstantiated += (_, __) => InstantiatedCount++;
            Scroller.Delegate = Delegate;

            if (reload)
            {
                Scroller.ReloadData();
            }
        }

        private TestCellView CreateTemplate(string name, string identifier)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(Root.transform, false);
            var view = go.AddComponent<TestCellView>();
            view.CellIdentifier = identifier;
            return view;
        }

        /// <summary>스크롤 축 스크롤바를 만들어 ScrollRect에 연결한다.</summary>
        public Scrollbar AddScrollbar()
        {
            var go = new GameObject("Scrollbar", typeof(RectTransform));
            go.transform.SetParent(ScrollRect.transform, false);
            var handle = (RectTransform)new GameObject("Handle", typeof(RectTransform)).transform;
            handle.SetParent(go.transform, false);

            var scrollbar = go.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            if (Scroller.ScrollDirection == ScrollDirection.Vertical)
            {
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                ScrollRect.verticalScrollbar = scrollbar;
                ScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            }
            else
            {
                ScrollRect.horizontalScrollbar = scrollbar;
                ScrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            }

            return scrollbar;
        }

        public void Dispose()
        {
            if (Root != null)
            {
                Object.DestroyImmediate(Root);
            }
        }
    }
}
