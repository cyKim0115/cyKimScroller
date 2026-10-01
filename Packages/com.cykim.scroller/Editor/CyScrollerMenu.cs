using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CyKim.Scroller.Editor
{
    /// <summary>
    /// GameObject 메뉴에서 CyScroller가 붙은 Scroll View를 만든다.
    /// Canvas·EventSystem·입력 모듈 생성은 Unity 기본 Scroll View 메뉴에 맡긴다.
    /// </summary>
    public static class CyScrollerMenu
    {
        // Unity 6.3부터 uGUI 메뉴 이름이 "UI"에서 "UI (Canvas)"로 바뀌었다. 없는 메뉴를 실행하면 콘솔 오류가 나므로 컴파일 시점에 고른다.
#if UNITY_6000_3_OR_NEWER
        private const string UI_MENU = "GameObject/UI (Canvas)/";
#else
        private const string UI_MENU = "GameObject/UI/";
#endif
        private const string MENU_ROOT = UI_MENU + "CyKim Scroller/";
        private const string SCROLL_VIEW_MENU = UI_MENU + "Scroll View";
        private const int MENU_PRIORITY = 2062;

        [MenuItem(MENU_ROOT + "Vertical Scroller", false, MENU_PRIORITY)]
        public static void CreateVerticalScroller(MenuCommand command)
        {
            CreateScroller(command, ScrollDirection.Vertical);
        }

        [MenuItem(MENU_ROOT + "Horizontal Scroller", false, MENU_PRIORITY + 1)]
        public static void CreateHorizontalScroller(MenuCommand command)
        {
            CreateScroller(command, ScrollDirection.Horizontal);
        }

        /// <summary>CyScroller가 붙은 Scroll View를 만들어 반환한다. 실패하면 null.</summary>
        public static CyScroller CreateScroller(MenuCommand command, ScrollDirection direction)
        {
            GameObject parent = command != null ? command.context as GameObject : null;
            if (parent != null)
            {
                Selection.activeGameObject = parent;
            }

            if (!EditorApplication.ExecuteMenuItem(SCROLL_VIEW_MENU))
            {
                Debug.LogError($"[CyScrollerMenu] Unity 기본 Scroll View 메뉴({SCROLL_VIEW_MENU})를 찾지 못했습니다.");
                return null;
            }

            GameObject scrollView = Selection.activeGameObject;
            if (scrollView == null || !scrollView.TryGetComponent(out ScrollRect scrollRect))
            {
                Debug.LogError("[CyScrollerMenu] 생성된 Scroll View를 찾지 못했습니다.");
                return null;
            }

            KeepUnderClickedParent(scrollView, parent);

            // 이름은 바꾸지 않는다. Unity가 새 오브젝트에 이름 편집을 걸어 두면 편집 종료 때 원래 이름으로 되돌아간다.
            Undo.RecordObject(scrollRect, "Create CyScroller");
            bool vertical = direction == ScrollDirection.Vertical;
            scrollRect.vertical = vertical;
            scrollRect.horizontal = !vertical;

            // 쓰지 않는 축의 스크롤바는 지운다.
            Scrollbar unused = vertical ? scrollRect.horizontalScrollbar : scrollRect.verticalScrollbar;
            if (vertical)
            {
                scrollRect.horizontalScrollbar = null;
            }
            else
            {
                scrollRect.verticalScrollbar = null;
            }

            if (unused != null)
            {
                Undo.DestroyObjectImmediate(unused.gameObject);
            }

            ReplaceStencilMask(scrollRect.viewport);

            CyScroller scroller = Undo.AddComponent<CyScroller>(scrollView);
            var serialized = new SerializedObject(scroller);
            serialized.FindProperty("_scrollDirection").enumValueIndex = (int)direction;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = scrollView;
            return scroller;
        }

        /// <summary>
        /// 기본 메뉴는 메뉴 컨텍스트 없이 실행되면 우클릭한 오브젝트가 아니라 그 Canvas 바로 아래에 만든다.
        /// 우클릭한 오브젝트가 Canvas 안에 있으면 그 아래로 옮긴다. Canvas 밖이면 기본 메뉴의 배치를 그대로 둔다.
        /// </summary>
        private static void KeepUnderClickedParent(GameObject scrollView, GameObject parent)
        {
            if (parent == null || scrollView.transform.parent == parent.transform)
            {
                return;
            }

            if (parent.GetComponentInParent<Canvas>(true) == null)
            {
                return;
            }

            Undo.SetTransformParent(scrollView.transform, parent.transform, "Create CyScroller");
            var rect = (RectTransform)scrollView.transform;
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            scrollView.layer = parent.layer;
        }

        /// <summary>
        /// 기본 Scroll View의 Viewport는 스텐실 Mask + Image를 쓴다. 사각 영역에는 드로우콜이 적은 RectMask2D로 바꾼다.
        /// 레이캐스트는 Scroll View 루트 Image가 받는다.
        /// </summary>
        private static void ReplaceStencilMask(RectTransform viewport)
        {
            if (viewport == null || !viewport.TryGetComponent(out Mask mask))
            {
                return;
            }

            Undo.DestroyObjectImmediate(mask);
            if (viewport.TryGetComponent(out Image image))
            {
                Undo.DestroyObjectImmediate(image);
            }

            Undo.AddComponent<RectMask2D>(viewport.gameObject);
        }
    }
}
