using UnityEditor;
using UnityEngine;

namespace CyKim.Scroller.Editor
{
    /// <summary>기본 인스펙터 + 플레이 중 가상화 상태와 디버그 버튼.</summary>
    [CustomEditor(typeof(CyScroller))]
    public class CyScrollerEditor : UnityEditor.Editor
    {
        private int _jumpDataIndex;
        private float _jumpTweenTime = 0.3f;

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (!Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "content의 LayoutGroup·ContentSizeFitter는 실행 시 비활성화됩니다. 셀 크기는 델리게이트의 GetCellViewSize가 정합니다.",
                    MessageType.Info);
                return;
            }

            var scroller = (CyScroller)target;
            CyScrollerLayout layout = scroller.Layout;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Cells (data)", scroller.NumberOfCells);
                EditorGUILayout.IntField("Slots", scroller.NumberOfCellSlots);
                EditorGUILayout.IntField("Loop Sets", layout.SetCount);
                EditorGUILayout.Vector2IntField("Active Slots", new Vector2Int(scroller.StartCellViewIndex, scroller.EndCellViewIndex));
                EditorGUILayout.Vector2IntField("Active Data", new Vector2Int(scroller.StartDataIndex, scroller.EndDataIndex));
                EditorGUILayout.Vector2IntField("Displayed Slots", new Vector2Int(scroller.DisplayedFirstSlot, scroller.DisplayedLastSlot));
                EditorGUILayout.IntField("Active Views", scroller.ActiveCellViews.Count);
                EditorGUILayout.IntField("Recycled Views", scroller.GetRecycledCellCount());
                EditorGUILayout.FloatField("Scroll Position", scroller.ScrollPosition);
                EditorGUILayout.FloatField("Scroll Size", scroller.ScrollSize);
                EditorGUILayout.FloatField("Normalized", scroller.NormalizedScrollPosition);
                EditorGUILayout.FloatField("Linear Velocity", scroller.LinearVelocity);
                EditorGUILayout.Toggle("Scrolling", scroller.IsScrolling);
                EditorGUILayout.Toggle("Tweening", scroller.IsTweening);
                EditorGUILayout.Toggle("Settled", scroller.IsSettled);
                EditorGUILayout.Toggle("Fast Scrolling", scroller.IsFastScrolling);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reload Data"))
                {
                    scroller.ReloadData(scroller.NormalizedScrollPosition);
                }

                if (GUILayout.Button("Refresh Active"))
                {
                    scroller.RefreshActiveCellViews();
                }

                if (GUILayout.Button("Snap"))
                {
                    scroller.Snap();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _jumpDataIndex = EditorGUILayout.IntField("Jump To", _jumpDataIndex);
                _jumpTweenTime = EditorGUILayout.FloatField(_jumpTweenTime, GUILayout.Width(40f));
                if (GUILayout.Button("Go", GUILayout.Width(40f)))
                {
                    scroller.JumpToDataIndex(_jumpDataIndex, 0.5f, 0.5f, true, TweenType.EaseInOutCubic, _jumpTweenTime);
                }
            }
        }
    }
}
