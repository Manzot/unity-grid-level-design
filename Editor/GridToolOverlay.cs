using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace DefaceGames.GridLevelDesign.Editor
{
    [Overlay(typeof(SceneView), "Grid Level Tool", true)]
    public class GridToolOverlay : Overlay, ITransientOverlay
    {
        private Vector2 m_scroll;
        private IMGUIContainer m_container;

        public bool visible
        {
            get
            {
                return GridPlacementTool.activeTool != null;
            }
        }

        public override void OnCreated()
        {
            minSize = new Vector2(240f, 160f);
            maxSize = new Vector2(1200f, 1400f);
        }

        public override VisualElement CreatePanelContent()
        {
            IMGUIContainer container = new IMGUIContainer(DrawGUI);
            m_container = container;
            container.style.flexGrow = 1f;
            container.style.alignSelf = Align.Stretch;
            container.style.width = Length.Percent(100f);
            container.style.height = Length.Percent(100f);
            container.style.minWidth = 240f;
            container.style.minHeight = 160f;
            container.RegisterCallback<AttachToPanelEvent>(OnAttached);

            // Keeps the cell readout and preview live while the mouse moves in the Scene view.
            container.schedule.Execute(container.MarkDirtyRepaint).Every(100);
            return container;
        }

        // The overlay's own wrapper elements size to their content by default, so they are told to
        // stretch with the resized overlay; otherwise the IMGUI area never grows.
        private static void OnAttached(AttachToPanelEvent evt)
        {
            VisualElement element = evt.target as VisualElement;
            int depth = 0;

            while(element != null && element.parent != null && depth < 4)
            {
                element = element.parent;
                element.style.flexGrow = 1f;
                element.style.alignSelf = Align.Stretch;
                depth++;
            }
        }

        public static bool IsPanelElement(Focusable element)
        {
            IMGUIContainer container = element as IMGUIContainer;
            return container != null && container.onGUIHandler != null && container.onGUIHandler.Target is GridToolOverlay;
        }

        private void DrawGUI()
        {
            GridPlacementTool tool = GridPlacementTool.activeTool;

            if(tool == null)
            {
                return;
            }

            m_scroll = GUILayout.BeginScrollView(m_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            float width = 300f;

            if(m_container != null && m_container.contentRect.width > 0f)
            {
                width = m_container.contentRect.width;
            }

            // Reserve room for the vertical scroll bar so content never forces a horizontal one.
            tool.DrawPanelGUI(Mathf.Max(200f, width - 18f));
            GUILayout.EndScrollView();

            Event evt = Event.current;

            bool typing = EditorGUIUtility.editingTextField && GUIUtility.keyboardControl != 0;

            if((evt.type == EventType.KeyDown || evt.type == EventType.KeyUp) && !typing)
            {
                tool.HandlePanelKey(evt);
            }
        }
    }
}
