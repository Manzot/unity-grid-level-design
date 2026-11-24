using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace DefaceGames.GridLevelDesign.Editor
{
    public enum GridToolMode
    {
        Paint,
        Rectangle,
        Line,
        Fill,
        Select
    }

    [EditorTool("Grid Level Tool", typeof(GridLevel))]
    public class GridPlacementTool : EditorTool
    {
        private const int m_maxCellsPerAction = 10000;

        private static readonly int m_controlHash = "DefaceGames.GridPlacementTool".GetHashCode();
        private const float m_panelWidth = 220f;
        private float m_previewSize = 64f;

        private static readonly Color m_previewBackground = new Color(0f, 0f, 0f, 0.25f);

        private static readonly GUIContent[] m_modeContents =
        {
            new GUIContent("Paint", "Click or drag to place (B)"),
            new GUIContent("Rect", "Drag a rectangle (M)"),
            new GUIContent("Line", "Drag a straight line (L)"),
            new GUIContent("Fill", "Flood fill the connected area (K)"),
            new GUIContent("Select", "Select placed pieces (U)")
        };
        private static readonly Color m_selectFill = new Color(1f, 0.85f, 0.2f, 0.12f);
        private static readonly Color m_selectOutline = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color m_selectTint = new Color(1f, 0.8f, 0.1f, 0.45f);
        private static readonly Color m_arrowColor = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color m_freeFill = new Color(0.2f, 0.8f, 1f, 0.15f);
        private static readonly Color m_freeOutline = new Color(0.2f, 0.8f, 1f, 0.9f);
        private static readonly Color m_blockedFill = new Color(1f, 0.25f, 0.2f, 0.2f);
        private static readonly Color m_blockedOutline = new Color(1f, 0.25f, 0.2f, 0.95f);
        private static readonly Color m_ghostFree = new Color(0.3f, 0.85f, 1f, 0.45f);
        private static readonly Color m_ghostBlocked = new Color(1f, 0.3f, 0.25f, 0.45f);

        private readonly Vector3[] m_quadCorners = new Vector3[4];
        private readonly List<Mesh> m_ghostMeshes = new List<Mesh>();
        private readonly List<Matrix4x4> m_ghostMatrices = new List<Matrix4x4>();
        private readonly List<Vector3Int> m_cellBuffer = new List<Vector3Int>();
        private readonly List<Vector3Int> m_minBuffer = new List<Vector3Int>();
        private readonly List<Vector3Int> m_coveredBuffer = new List<Vector3Int>();
        private readonly HashSet<Vector3Int> m_strokeCells = new HashSet<Vector3Int>();

        private static GUIStyle m_angleLabelStyle;
        private static Texture2D m_angleLabelBackground;
        private bool m_showShortcuts;
        private static GUIStyle m_previewTitleStyle;
        private static GUIStyle m_previewTextStyle;

        private static GridPlacementTool m_activeTool;

        // The static reference is lost on script reload while Unity keeps the tool active, so it is recovered here.
        public static GridPlacementTool activeTool
        {
            get
            {
                if(m_activeTool == null && ToolManager.activeToolType == typeof(GridPlacementTool))
                {
                    GridPlacementTool[] tools = Resources.FindObjectsOfTypeAll<GridPlacementTool>();

                    if(tools.Length > 0)
                    {
                        m_activeTool = tools[0];
                    }
                }

                if(m_activeTool != null && ToolManager.activeToolType != typeof(GridPlacementTool))
                {
                    m_activeTool = null;
                }

                return m_activeTool;
            }
        }

        private GameObject m_ghostPrefab;
        private Material m_ghostMaterial;
        private Material m_highlightMaterial;
        private readonly Vector3[] m_boxCorners = new Vector3[8];
        private readonly Vector3[] m_edgePoints = new Vector3[2];
        private GridToolMode m_mode;
        private bool m_hasHoverCell;
        private Vector3Int m_hoverCell;
        private UnityEngine.UIElements.IMGUIContainer m_sceneContainer;
        private SceneView m_sceneContainerOwner;
        private Quaternion m_rotation = Quaternion.identity;
        private GameObject m_footprintPrefab;
        private Vector3 m_footprintCell;
        private Vector3 m_footprintScale;
        private int m_footprintPlane = -1;
        private Vector2Int m_footprintValue = Vector2Int.one;
        private int m_rotationAxis = -1;

        private static readonly GUIContent[] m_axisContents =
        {
            new GUIContent("X", "Rotate around the grid's X axis (/ cycles the axis)"),
            new GUIContent("Y", "Rotate around the grid's Y axis (/ cycles the axis)"),
            new GUIContent("Z", "Rotate around the grid's Z axis (/ cycles the axis)")
        };
        private float m_stepDegrees = 90f;
        private float m_scaleMultiplier = 1f;
        private float m_scaleStep = 0.1f;
        private bool m_stack;
        private bool m_stackKeyHeld;
        private bool m_eraseToggle;
        private bool m_eraseKeyHeld;
        private bool m_snapToGrid = true;
        private bool m_snapKeyHeld;

        private const float m_snapPixelRadius = 20f;
        private const float m_centreSnapPixelRadius = 28f;
        private const float m_snapDotSize = 0.03f;
        private const float m_centreSnapDotSize = 0.045f;
        private const float m_activeSnapDotSize = 0.06f;
        private static readonly Color m_sideSnapColor = new Color(0.3f, 0.9f, 1f, 0.9f);
        private static readonly bool[] m_facePositive = { true, true, false, true, false, false };
        private readonly List<SnapPoint> m_snapPoints = new List<SnapPoint>();
        private readonly List<SnapPoint> m_candidatePoints = new List<SnapPoint>();
        private readonly List<Vector3> m_vertexBuffer = new List<Vector3>();
        private readonly List<Vector3> m_meshVertices = new List<Vector3>();
        private Vector2 m_mousePosition;
        private bool m_hasSnapPoint;
        private Vector3 m_snapPoint;
        private int m_snapFace;
        private int m_activeSnapIndex = -1;
        private GameObject m_snapPiece;
        [System.NonSerialized] private GameObject m_facePiece;
        private Matrix4x4 m_faceMatrix;
        private Vector3 m_topFaceMin;
        private Vector3 m_topFaceMax;
        private readonly Vector3[] m_sideCentres = new Vector3[5];
        [System.NonSerialized] private GameObject m_anchorPrefab;
        private Quaternion m_anchorRotation;
        private Vector3 m_anchorScale;
        private bool m_anchorBoundsMode;
        private bool m_faceBoundsMode;
        private GridPlane m_facePlane;
        private GridPlane m_anchorPlane;
        private bool m_snapToBounds = true;
        private bool m_centreKeyHeld;
        private readonly Vector3[] m_anchorValues = new Vector3[6];
        private float m_fineSnap;
        private bool m_hasFreePoint;
        private Vector3 m_freePoint;
        private GameObject m_hoverPiece;
        private Vector2 m_selectStartMouse;
        private Vector2 m_selectEndMouse;

        private bool m_dragging;
        private bool m_erasing;
        private bool m_selectAdditive;
        private readonly HashSet<GameObject> m_selected = new HashSet<GameObject>();
        private GameObject m_lastPiece;
        private Vector3Int m_startCell;
        private Vector3Int m_endCell;
        private int m_undoGroup;

        private PaletteEntry m_rolledEntry;
        private GameObject m_nextPrefab;
        private int m_nextRandomSteps;
        private bool m_rightMouseHeld;
        private Vector3 m_nextRandomOffset;

        public override void OnActivated()
        {
            m_ghostPrefab = null;
            m_rolledEntry = null;
            m_dragging = false;
            m_stackKeyHeld = false;
            m_eraseKeyHeld = false;
            m_activeTool = this;
            GridPaletteState.changed += SceneView.RepaintAll;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public override void OnWillBeDeactivated()
        {
            GridPaletteState.changed -= SceneView.RepaintAll;
            Undo.undoRedoPerformed -= OnUndoRedo;
            m_dragging = false;
            m_selected.Clear();

            if(m_activeTool == this)
            {
                m_activeTool = null;
            }

            if(m_ghostMaterial != null)
            {
                DestroyImmediate(m_ghostMaterial);
                m_ghostMaterial = null;
            }

            if(m_highlightMaterial != null)
            {
                DestroyImmediate(m_highlightMaterial);
                m_highlightMaterial = null;
            }
        }

        private void OnUndoRedo()
        {
            GridLevel level = target as GridLevel;

            if(level != null)
            {
                level.InvalidateLookup();
            }

            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            SceneView sceneView = window as SceneView;
            GridLevel level = target as GridLevel;

            if(sceneView == null || level == null)
            {
                return;
            }

            sceneView.wantsMouseMove = true;
            int controlId = GUIUtility.GetControlID(m_controlHash, FocusType.Passive);
            Event evt = Event.current;
            EventType type = evt.GetTypeForControl(controlId);

            if(evt.rawType == EventType.MouseDown && evt.button == 1)
            {
                m_rightMouseHeld = true;
            }
            else if(evt.rawType == EventType.MouseUp && evt.button == 1)
            {
                m_rightMouseHeld = false;
            }

            if((type == EventType.ValidateCommand || type == EventType.ExecuteCommand) && IsFrameCommand(evt))
            {
                HandleFrameCommand(sceneView, level, evt, type == EventType.ExecuteCommand);
                return;
            }

            if((type == EventType.ValidateCommand || type == EventType.ExecuteCommand) && IsDeleteCommand(evt))
            {
                // Without this, Unity's Delete would remove the selected Grid Level object itself.
                if(m_mode == GridToolMode.Select && m_selected.Count > 0)
                {
                    if(type == EventType.ExecuteCommand)
                    {
                        DeleteSelected(level);
                    }

                    evt.Use();
                }

                return;
            }

            if(type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
            }
            else if(type == EventType.MouseMove)
            {
                EnsureSceneKeyboardFocus(sceneView);
                UpdateHover(level, evt);
                sceneView.Repaint();
            }
            else if(type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                UpdateHover(level, evt);

                if(m_hasHoverCell || m_mode == GridToolMode.Select || IsFreeform())
                {
                    BeginStroke(level, evt, controlId);
                }
            }
            else if(type == EventType.MouseDrag)
            {
                UpdateHover(level, evt);

                if(GUIUtility.hotControl == controlId)
                {
                    if(m_mode == GridToolMode.Select)
                    {
                        m_selectEndMouse = evt.mousePosition;
                    }

                    ContinueStroke(level);
                    evt.Use();
                }

                sceneView.Repaint();
            }
            else if(type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                EndStroke(level);
                GUIUtility.hotControl = 0;
                evt.Use();
            }
            else if(type == EventType.KeyDown)
            {
                HandleKey(level, evt);
            }
            else if(type == EventType.KeyUp)
            {
                HandleKeyUp(evt);
            }
            else if(type == EventType.Repaint && m_mode == GridToolMode.Select)
            {
                DrawSelectMode(level);
            }
            else if(type == EventType.Repaint && IsFreeform())
            {
                DrawSelectionHighlights();
                DrawFreeHover(level, m_dragging ? m_erasing : IsErasing(evt));
            }
            else if(type == EventType.Repaint)
            {
                DrawSelectionHighlights();

                if(m_dragging && m_mode != GridToolMode.Paint)
                {
                    DrawStrokePreview(level);
                }
                else if(m_dragging)
                {
                    DrawHover(level, m_erasing);
                }
                else
                {
                    DrawHover(level, IsErasing(evt));
                }
            }
        }

        private void HandleKeyUp(Event evt)
        {
            if(evt.keyCode == KeyCode.S)
            {
                m_stackKeyHeld = false;
            }
            else if(evt.keyCode == KeyCode.X)
            {
                m_eraseKeyHeld = false;
            }
            else if(evt.keyCode == KeyCode.N)
            {
                m_snapKeyHeld = false;
            }
            else if(evt.keyCode == KeyCode.C)
            {
                m_centreKeyHeld = false;
            }
        }

        // Called by the overlay panel when it holds keyboard focus, so shortcuts keep working after a
        // click on one of its buttons. Pick (I) is left out because it needs the Scene view's mouse position.
        public void HandlePanelKey(Event evt)
        {
            GridLevel level = target as GridLevel;

            if(level == null || evt.keyCode == KeyCode.I)
            {
                return;
            }

            if(evt.type == EventType.KeyDown)
            {
                HandleKey(level, evt);
                SceneView.RepaintAll();
            }
            else if(evt.type == EventType.KeyUp)
            {
                HandleKeyUp(evt);
            }
        }

        // Shortcuts only reach the tool while the Scene view's own drawing area has keyboard focus.
        // Clicking the palette window or the overlay panel moves focus away, so it is taken back as
        // soon as the mouse is over the Scene view again.
        private void EnsureSceneKeyboardFocus(SceneView sceneView)
        {
            if(EditorWindow.focusedWindow != sceneView)
            {
                // Typing in an unrelated window (the Inspector, say) is left alone; the palette window is ours.
                if(EditorGUIUtility.editingTextField && !(EditorWindow.focusedWindow is GridLevelWindow))
                {
                    return;
                }

                sceneView.Focus();
            }

            if(m_sceneContainer == null || m_sceneContainerOwner != sceneView)
            {
                UnityEngine.UIElements.VisualElement root = sceneView.rootVisualElement;

                while(root.parent != null)
                {
                    root = root.parent;
                }

                m_sceneContainer = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.IMGUIContainer>(root, "unity-scene-view-camera-rect");
                m_sceneContainerOwner = sceneView;
            }

            if(m_sceneContainer == null || m_sceneContainer.panel == null)
            {
                return;
            }

            UnityEngine.UIElements.Focusable focused = m_sceneContainer.panel.focusController.focusedElement;

            if(focused != m_sceneContainer)
            {
                // A text box in some other Scene view panel keeps its focus; anything else gives it up.
                if(IsOtherTextField(focused))
                {
                    return;
                }

                m_sceneContainer.Focus();
                m_stackKeyHeld = false;
                m_eraseKeyHeld = false;
                m_snapKeyHeld = false;
                m_centreKeyHeld = false;
            }

            EditorGUIUtility.editingTextField = false;
        }

        private static bool IsOtherTextField(UnityEngine.UIElements.Focusable focused)
        {
            UnityEngine.UIElements.VisualElement element = focused as UnityEngine.UIElements.VisualElement;

            while(element != null)
            {
                if(element.ClassListContains("unity-base-text-field"))
                {
                    return true;
                }

                element = element.parent;
            }

            return false;
        }

        private bool IsErasing(Event evt)
        {
            return m_eraseToggle || evt.shift;
        }

        private bool IsFreeform()
        {
            return !m_snapToGrid && m_mode == GridToolMode.Paint;
        }

        private void UpdateHover(GridLevel level, Event evt)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            m_hasHoverCell = level.RaycastCell(ray, out m_hoverCell);
            m_hoverPiece = null;
            m_hasFreePoint = false;

            if(m_mode == GridToolMode.Select || IsFreeform())
            {
                m_hoverPiece = PickPiece(level, evt.mousePosition);
            }

            if(IsFreeform())
            {
                m_hasFreePoint = FindFreePoint(level, ray, out m_freePoint);
            }

            m_mousePosition = evt.mousePosition;
            UpdateSnapPoints(level);
        }

        // Faces follow the grid plane: 0 is the top (+normal), 1 to 4 the +U, -U, +V and -V sides, 5 the bottom.
        private struct SnapPoint
        {
            public Vector3 position;
            public int face;
            public bool corner;
            public bool centre;

            public SnapPoint(Vector3 position, int face, bool corner, bool centre = false)
            {
                this.position = position;
                this.face = face;
                this.corner = corner;
                this.centre = centre;
            }
        }

        // Snap points sit on the real faces of the piece under the cursor: nine on the top face, one at the
        // centre of each side facing the camera and one under the bottom. The last piece stays the target while the cursor is still
        // within reach of one of its points, so corners out past a round mesh can be reached.
        private void UpdateSnapPoints(GridLevel level)
        {
            m_snapPoints.Clear();
            m_hasSnapPoint = false;
            m_activeSnapIndex = -1;
            m_snapFace = 0;

            if(!IsFreeform())
            {
                m_snapPiece = null;
                return;
            }

            if(m_snapPiece != null && level.FindByInstance(m_snapPiece) == null)
            {
                m_snapPiece = null;
            }

            GameObject target = m_hoverPiece;
            float bestDistance = float.MaxValue;

            if(target != null)
            {
                BuildSnapPoints(level, target, m_snapPoints);
                m_activeSnapIndex = FindNearestSnap(level, m_snapPoints, out bestDistance);
            }

            if(m_snapPiece != null && m_snapPiece != target)
            {
                BuildSnapPoints(level, m_snapPiece, m_candidatePoints);
                float stickyDistance;
                int stickyIndex = FindNearestSnap(level, m_candidatePoints, out stickyDistance);

                if(stickyIndex >= 0 && (m_activeSnapIndex < 0 || stickyDistance < bestDistance))
                {
                    target = m_snapPiece;
                    m_snapPoints.Clear();
                    m_snapPoints.AddRange(m_candidatePoints);
                    m_activeSnapIndex = stickyIndex;
                }
            }

            m_snapPiece = target;

            if(m_activeSnapIndex >= 0)
            {
                m_hasSnapPoint = true;
                m_snapPoint = m_snapPoints[m_activeSnapIndex].position;
                m_snapFace = m_snapPoints[m_activeSnapIndex].face;
            }
        }

        private void BuildSnapPoints(GridLevel level, GameObject piece, List<SnapPoint> output)
        {
            output.Clear();

            if(!GetPieceFaces(level, piece))
            {
                return;
            }

            int u = level.uAxis;
            int v = level.vAxis;
            Vector3 min = m_topFaceMin;
            Vector3 max = m_topFaceMax;
            float top = max[level.normalAxis];
            float midU = (min[u] + max[u]) * 0.5f;
            float midV = (min[v] + max[v]) * 0.5f;
            output.Add(new SnapPoint(GetPlanePoint(level, midU, midV, top), 0, false, true));
            output.Add(new SnapPoint(GetPlanePoint(level, min[u], min[v], top), 0, true));
            output.Add(new SnapPoint(GetPlanePoint(level, max[u], min[v], top), 0, true));
            output.Add(new SnapPoint(GetPlanePoint(level, min[u], max[v], top), 0, true));
            output.Add(new SnapPoint(GetPlanePoint(level, max[u], max[v], top), 0, true));
            output.Add(new SnapPoint(GetPlanePoint(level, midU, min[v], top), 0, false));
            output.Add(new SnapPoint(GetPlanePoint(level, midU, max[v], top), 0, false));
            output.Add(new SnapPoint(GetPlanePoint(level, min[u], midV, top), 0, false));
            output.Add(new SnapPoint(GetPlanePoint(level, max[u], midV, top), 0, false));

            Camera camera = Camera.current;

            if(camera == null && SceneView.lastActiveSceneView != null)
            {
                camera = SceneView.lastActiveSceneView.camera;
            }

            for(int i = 0; i < m_sideCentres.Length; i++)
            {
                int face = i + 1;

                // A side is hidden only when it is turned away and the opposite side is the one in view.
                // Sides seen edge-on both stay.
                if(camera != null && face != 5 && GetFacing(level, camera, face) < 0f && GetFacing(level, camera, GetOppositeFace(face)) > 0.1f)
                {
                    continue;
                }

                output.Add(new SnapPoint(m_sideCentres[i], face, false, true));
            }
        }

        // Above zero when the side faces the camera, below when it is turned away.
        private float GetFacing(GridLevel level, Camera camera, int face)
        {
            Vector3 normal = Vector3.zero;
            normal[GetFaceAxis(level, face)] = m_facePositive[face] ? 1f : -1f;
            Vector3 world = level.transform.TransformPoint(m_sideCentres[face - 1]);
            Vector3 toCamera = camera.orthographic ? -camera.transform.forward : (camera.transform.position - world).normalized;
            return Vector3.Dot(level.transform.TransformDirection(normal).normalized, toCamera);
        }

        private int FindNearestSnap(GridLevel level, List<SnapPoint> points, out float bestDistance)
        {
            int best = -1;
            bestDistance = float.MaxValue;

            for(int i = 0; i < points.Count; i++)
            {
                Vector2 screen = HandleUtility.WorldToGUIPoint(level.transform.TransformPoint(points[i].position));
                float distance = Vector2.Distance(screen, m_mousePosition);
                float radius = points[i].centre ? m_centreSnapPixelRadius : m_snapPixelRadius;

                if(distance <= radius && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        private void CollectMeshVertices(Mesh mesh, Matrix4x4 matrix, List<Vector3> output)
        {
            m_meshVertices.Clear();
            mesh.GetVertices(m_meshVertices);

            for(int i = 0; i < m_meshVertices.Count; i++)
            {
                output.Add(matrix.MultiplyPoint3x4(m_meshVertices[i]));
            }
        }

        // The flat face at one end of a set of points along an axis: every point within a small tolerance of
        // the extreme value. When that face is little more than a point (a rounded end), the whole extent of
        // the points is used instead.
        private static void GetFlatFace(List<Vector3> points, int axis, bool positive, out Vector3 min, out Vector3 max)
        {
            Vector3 allMin = points[0];
            Vector3 allMax = points[0];

            for(int i = 1; i < points.Count; i++)
            {
                allMin = Vector3.Min(allMin, points[i]);
                allMax = Vector3.Max(allMax, points[i]);
            }

            int b = (axis + 1) % 3;
            int c = (axis + 2) % 3;
            float tolerance = Mathf.Max(0.001f, (allMax[axis] - allMin[axis]) * 0.02f);
            float extreme = positive ? allMax[axis] : allMin[axis];
            min = Vector3.one * float.MaxValue;
            max = Vector3.one * float.MinValue;
            min[axis] = extreme;
            max[axis] = extreme;

            for(int i = 0; i < points.Count; i++)
            {
                if(Mathf.Abs(points[i][axis] - extreme) > tolerance)
                {
                    continue;
                }

                min[b] = Mathf.Min(min[b], points[i][b]);
                min[c] = Mathf.Min(min[c], points[i][c]);
                max[b] = Mathf.Max(max[b], points[i][b]);
                max[c] = Mathf.Max(max[c], points[i][c]);
            }

            bool tinyB = max[b] - min[b] < (allMax[b] - allMin[b]) * 0.1f;
            bool tinyC = max[c] - min[c] < (allMax[c] - allMin[c]) * 0.1f;

            if(tinyB && tinyC)
            {
                min[b] = allMin[b];
                min[c] = allMin[c];
                max[b] = allMax[b];
                max[c] = allMax[c];
            }
        }

        private bool GetPieceFaces(GridLevel level, GameObject piece)
        {
            Matrix4x4 matrix = level.transform.worldToLocalMatrix * piece.transform.localToWorldMatrix;

            if(piece == m_facePiece && matrix == m_faceMatrix && m_faceBoundsMode == m_snapToBounds && m_facePlane == level.plane)
            {
                return true;
            }

            m_vertexBuffer.Clear();
            Matrix4x4 toLevel = level.transform.worldToLocalMatrix;
            MeshFilter[] filters = piece.GetComponentsInChildren<MeshFilter>();

            for(int i = 0; i < filters.Length; i++)
            {
                if(filters[i].sharedMesh != null)
                {
                    CollectMeshVertices(filters[i].sharedMesh, toLevel * filters[i].transform.localToWorldMatrix, m_vertexBuffer);
                }
            }

            SkinnedMeshRenderer[] skinned = piece.GetComponentsInChildren<SkinnedMeshRenderer>();

            for(int i = 0; i < skinned.Length; i++)
            {
                if(skinned[i].sharedMesh != null)
                {
                    CollectMeshVertices(skinned[i].sharedMesh, toLevel * skinned[i].transform.localToWorldMatrix, m_vertexBuffer);
                }
            }

            if(m_vertexBuffer.Count == 0)
            {
                Bounds bounds;

                if(!TryGetLevelBounds(level, piece, out bounds))
                {
                    m_facePiece = null;
                    return false;
                }

                SetFacesFromBounds(level, bounds);
            }
            else if(m_snapToBounds)
            {
                SetFacesFromBounds(level, GetPointBounds(m_vertexBuffer));
            }
            else
            {
                GetFlatFace(m_vertexBuffer, level.normalAxis, true, out m_topFaceMin, out m_topFaceMax);

                for(int i = 0; i < m_sideCentres.Length; i++)
                {
                    Vector3 min;
                    Vector3 max;
                    GetFlatFace(m_vertexBuffer, GetFaceAxis(level, i + 1), m_facePositive[i + 1], out min, out max);
                    m_sideCentres[i] = (min + max) * 0.5f;
                }
            }

            m_facePiece = piece;
            m_faceMatrix = matrix;
            m_faceBoundsMode = m_snapToBounds;
            m_facePlane = level.plane;
            return true;
        }

        private void SetFacesFromBounds(GridLevel level, Bounds bounds)
        {
            m_topFaceMin = bounds.min;
            m_topFaceMin[level.normalAxis] = bounds.max[level.normalAxis];
            m_topFaceMax = bounds.max;

            for(int i = 0; i < m_sideCentres.Length; i++)
            {
                m_sideCentres[i] = GetBoundsFaceCentre(level, bounds, i + 1);
            }
        }

        private Vector3 GetBoundsFaceCentre(GridLevel level, Bounds bounds, int face)
        {
            int axis = GetFaceAxis(level, face);
            Vector3 centre = bounds.center;
            centre[axis] = m_facePositive[face] ? bounds.max[axis] : bounds.min[axis];
            return centre;
        }

        private static Bounds GetPointBounds(List<Vector3> points)
        {
            Bounds bounds = new Bounds(points[0], Vector3.zero);

            for(int i = 1; i < points.Count; i++)
            {
                bounds.Encapsulate(points[i]);
            }

            return bounds;
        }

        private static int GetFaceAxis(GridLevel level, int face)
        {
            if(face == 0 || face == 5)
            {
                return level.normalAxis;
            }

            return face <= 2 ? level.uAxis : level.vAxis;
        }

        private static Vector3 GetPlanePoint(GridLevel level, float u, float v, float n)
        {
            Vector3 point = Vector3.zero;
            point[level.uAxis] = u;
            point[level.vAxis] = v;
            point[level.normalAxis] = n;
            return point;
        }

        private static int GetOppositeFace(int face)
        {
            if(face == 0 || face == 5)
            {
                return 5 - face;
            }

            return face % 2 == 1 ? face + 1 : face - 1;
        }

        // Centres of the ghost's real faces relative to its pivot, numbered like the snap faces (the bottom
        // face is an elbow's opening, say).
        private Vector3 GetGhostAnchor(GridLevel level, PaletteEntry entry, Quaternion rotation, int face)
        {
            GameObject prefab = m_nextPrefab;
            Quaternion rootRotation = rotation * prefab.transform.localRotation;
            Vector3 scale = GetPlacementScale(entry, prefab);

            if(prefab == m_anchorPrefab && rootRotation == m_anchorRotation && scale == m_anchorScale && m_anchorBoundsMode == m_snapToBounds && m_anchorPlane == level.plane)
            {
                return m_anchorValues[face];
            }

            EnsureGhostCache(prefab);
            Matrix4x4 root = Matrix4x4.TRS(Vector3.zero, rootRotation, scale);
            m_vertexBuffer.Clear();

            for(int i = 0; i < m_ghostMeshes.Count; i++)
            {
                CollectMeshVertices(m_ghostMeshes[i], root * m_ghostMatrices[i], m_vertexBuffer);
            }

            for(int i = 0; i < m_anchorValues.Length; i++)
            {
                m_anchorValues[i] = Vector3.zero;

                if(m_vertexBuffer.Count == 0)
                {
                    continue;
                }

                if(m_snapToBounds)
                {
                    m_anchorValues[i] = GetBoundsFaceCentre(level, GetPointBounds(m_vertexBuffer), i);
                    continue;
                }

                Vector3 min;
                Vector3 max;
                GetFlatFace(m_vertexBuffer, GetFaceAxis(level, i), m_facePositive[i], out min, out max);
                m_anchorValues[i] = (min + max) * 0.5f;
            }

            m_anchorPrefab = prefab;
            m_anchorRotation = rootRotation;
            m_anchorScale = scale;
            m_anchorBoundsMode = m_snapToBounds;
            m_anchorPlane = level.plane;
            return m_anchorValues[face];
        }

        private static GameObject GetPieceRoot(GridLevel level, GameObject picked)
        {
            GameObject current = picked;

            while(current != null && current.transform.parent != level.transform)
            {
                if(current.transform.parent == null)
                {
                    return null;
                }

                current = current.transform.parent.gameObject;
            }

            if(current == null || level.FindByInstance(current) == null)
            {
                return null;
            }

            return current;
        }

        private static GameObject PickPiece(GridLevel level, Vector2 mousePosition)
        {
            return GetPieceRoot(level, HandleUtility.PickGameObject(mousePosition, false));
        }

        private static Vector3 NormalVector(GridLevel level)
        {
            Vector3 axis = Vector3.zero;
            axis[level.normalAxis] = 1f;
            return axis;
        }

        // With Stack on, the point lands on the nearest placed piece under the cursor (colliders first,
        // render bounds for pieces without one); otherwise on the current layer's plane.
        private bool FindFreePoint(GridLevel level, Ray ray, out Vector3 localPoint)
        {
            bool found = false;
            float best = float.MaxValue;
            Vector3 worldPoint = Vector3.zero;

            if(m_stack)
            {
                Physics.SyncTransforms();
                RaycastHit[] hits = Physics.RaycastAll(ray, 100000f, ~0, QueryTriggerInteraction.Ignore);

                for(int i = 0; i < hits.Length; i++)
                {
                    if(hits[i].distance < best && hits[i].collider.transform.IsChildOf(level.transform))
                    {
                        best = hits[i].distance;
                        worldPoint = hits[i].point;
                        found = true;
                    }
                }

                if(!found)
                {
                    for(int i = 0; i < level.cells.Count; i++)
                    {
                        if(level.cells[i].instance == null)
                        {
                            continue;
                        }

                        Renderer[] renderers = level.cells[i].instance.GetComponentsInChildren<Renderer>();

                        for(int r = 0; r < renderers.Length; r++)
                        {
                            float distance;

                            if(renderers[r].bounds.IntersectRay(ray, out distance) && distance < best)
                            {
                                best = distance;
                                worldPoint = ray.GetPoint(distance);
                                found = true;
                            }
                        }
                    }
                }
            }

            if(found)
            {
                localPoint = level.transform.InverseTransformPoint(worldPoint);

                localPoint[level.normalAxis] = level.layerOffset;
            }
            else if(!level.RaycastPlane(ray, out localPoint))
            {
                return false;
            }

            if(m_fineSnap > 0f)
            {
                int u = level.uAxis;
                int v = level.vAxis;
                localPoint[u] = Mathf.Round(localPoint[u] / m_fineSnap) * m_fineSnap;
                localPoint[v] = Mathf.Round(localPoint[v] / m_fineSnap) * m_fineSnap;
            }

            return true;
        }

        private static Bounds ShrinkBounds(Bounds bounds)
        {
            Vector3 size = bounds.size;
            size.x = Mathf.Max(0f, size.x - 0.02f);
            size.y = Mathf.Max(0f, size.y - 0.02f);
            size.z = Mathf.Max(0f, size.z - 0.02f);
            bounds.size = size;
            return bounds;
        }

        private void PlaceFree(GridLevel level, PaletteEntry entry)
        {
            EnsureRolled(entry);

            Vector3 freePosition;
            Quaternion localRotation;
            GetFreePose(level, entry, out freePosition, out localRotation);
            float degrees;
            TryGetYaw(level, localRotation, out degrees);
            GameObject prefab = m_nextPrefab;
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, level.transform) as GameObject;

            if(instance == null)
            {
                return;
            }

            instance.transform.localPosition = freePosition;
            instance.transform.localRotation = localRotation * prefab.transform.localRotation;
            instance.transform.localScale = GetPlacementScale(entry, prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Grid Place Free");

            PlacedCell placed = new PlacedCell();
            placed.cell = Vector3Int.FloorToInt(new Vector3(m_freePoint.x / level.cellSize.x, m_freePoint.y / level.cellSize.y, m_freePoint.z / level.cellSize.z));
            placed.size = Vector3Int.one;
            placed.instance = instance;
            placed.prefab = entry.prefab;
            placed.rotation = degrees;
            placed.rotationSteps = GridLevel.QuarterTurns(degrees);
            placed.freeform = true;

            Undo.RecordObject(level, "Grid Place Free");
            level.AddCell(placed);
            m_lastPiece = instance;
            EditorUtility.SetDirty(level);
            Roll(entry);
        }

        private void EraseObject(GridLevel level, GameObject instance)
        {
            PlacedCell piece = level.FindByInstance(instance);

            if(piece == null)
            {
                return;
            }

            Undo.RecordObject(level, "Grid Erase");
            level.RemovePiece(piece);
            Undo.DestroyObjectImmediate(instance);
            m_hoverPiece = null;
            EditorUtility.SetDirty(level);
        }

        private void EnsureRolled(PaletteEntry entry)
        {
            if(entry != m_rolledEntry)
            {
                m_scaleMultiplier = 1f;
            }

            if(entry != m_rolledEntry || m_nextPrefab == null)
            {
                Roll(entry);
            }
        }

        private void Roll(PaletteEntry entry)
        {
            m_rolledEntry = entry;
            m_nextPrefab = null;
            m_nextRandomSteps = 0;
            m_nextRandomOffset = Vector3.zero;

            if(entry == null || entry.prefab == null)
            {
                return;
            }

            m_nextPrefab = entry.prefab;
            int variantCount = 0;

            if(entry.variants != null)
            {
                for(int i = 0; i < entry.variants.Count; i++)
                {
                    if(entry.variants[i] != null)
                    {
                        variantCount++;
                    }
                }
            }

            int pick = Random.Range(0, variantCount + 1);

            if(pick > 0)
            {
                for(int i = 0; i < entry.variants.Count; i++)
                {
                    if(entry.variants[i] == null)
                    {
                        continue;
                    }

                    pick--;

                    if(pick == 0)
                    {
                        m_nextPrefab = entry.variants[i];
                        break;
                    }
                }
            }

            if(entry.randomPosition != Vector3.zero)
            {
                m_nextRandomOffset = new Vector3(Random.Range(0f, entry.randomPosition.x), Random.Range(0f, entry.randomPosition.y), Random.Range(0f, entry.randomPosition.z));
            }

            if(entry.randomRotation)
            {
                Vector2Int footprint = GetEntryFootprint(target as GridLevel, entry);

                if(footprint.x == footprint.y)
                {
                    m_nextRandomSteps = Random.Range(0, 4);
                }
                else
                {
                    m_nextRandomSteps = Random.Range(0, 2) * 2;
                }
            }
        }

        // Automatic footprints follow the current grid's cell size and plane, the entry's Scale and the
        // scale tool. Manual footprints use the cells typed into the palette.
        private Vector2Int GetEntryFootprint(GridLevel level, PaletteEntry entry)
        {
            if(entry == null || entry.prefab == null)
            {
                return Vector2Int.one;
            }

            if(entry.manualFootprint || level == null)
            {
                return entry.GetFootprint();
            }

            Vector3 scale = GetEntryScaleVector(level, entry) * m_scaleMultiplier;

            if(m_footprintPrefab != entry.prefab || m_footprintCell != level.cellSize || m_footprintScale != scale || m_footprintPlane != (int)level.plane)
            {
                m_footprintPrefab = entry.prefab;
                m_footprintCell = level.cellSize;
                m_footprintScale = scale;
                m_footprintPlane = (int)level.plane;
                m_footprintValue = GridFootprintUtility.ComputeFor(level, entry.prefab, scale);
            }

            return m_footprintValue;
        }

        private int GetRotationAxis(GridLevel level)
        {
            if(m_rotationAxis < 0 || m_rotationAxis > 2)
            {
                return level.normalAxis;
            }

            return m_rotationAxis;
        }

        private static Vector3 AxisVector(int axis)
        {
            Vector3 vector = Vector3.zero;
            vector[axis] = 1f;
            return vector;
        }

        private Quaternion GetEffectiveRotation(GridLevel level)
        {
            return Quaternion.AngleAxis(m_nextRandomSteps * 90f, NormalVector(level)) * m_rotation;
        }

        // True when the rotation only turns around the grid's up axis, which is the case the grid
        // footprint and cell-centred placement were designed for.
        private static bool TryGetYaw(GridLevel level, Quaternion rotation, out float degrees)
        {
            degrees = 0f;
            Vector3 normal = NormalVector(level);

            if(Vector3.Angle(rotation * normal, normal) > 0.05f)
            {
                return false;
            }

            Vector3 reference = AxisVector(level.uAxis);
            float angle = Vector3.SignedAngle(reference, rotation * reference, normal);
            degrees = Mathf.Repeat(Mathf.Round(angle * 100f) / 100f, 360f);
            return true;
        }

        private static string FormatRotation(GridLevel level, Quaternion rotation)
        {
            float yaw;

            if(TryGetYaw(level, rotation, out yaw))
            {
                return FormatDegrees(yaw);
            }

            Vector3 euler = rotation.eulerAngles;
            return "X" + (Mathf.RoundToInt(euler.x) % 360) + " Y" + (Mathf.RoundToInt(euler.y) % 360) + " Z" + (Mathf.RoundToInt(euler.z) % 360);
        }

        private static Quaternion GetPieceRotation(GameObject instance)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            Quaternion baseRotation = source != null ? source.transform.localRotation : Quaternion.identity;
            return instance.transform.localRotation * Quaternion.Inverse(baseRotation);
        }

        private void EnsureGhostCache(GameObject prefab)
        {
            if(prefab != null && m_ghostPrefab != prefab)
            {
                RebuildGhostCache(prefab);
            }
        }

        // Bounds of the prefab's meshes after the given root rotation and scale, relative to its pivot.
        private Bounds GetGhostLocalBounds(GameObject prefab, Quaternion rootRotation, Vector3 scale)
        {
            Bounds total = new Bounds(Vector3.zero, Vector3.zero);

            if(prefab == null)
            {
                return total;
            }

            EnsureGhostCache(prefab);
            Matrix4x4 root = Matrix4x4.TRS(Vector3.zero, rootRotation, scale);
            bool hasBounds = false;

            for(int i = 0; i < m_ghostMeshes.Count; i++)
            {
                Matrix4x4 matrix = root * m_ghostMatrices[i];
                Bounds meshBounds = m_ghostMeshes[i].bounds;

                for(int corner = 0; corner < 8; corner++)
                {
                    Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = matrix.MultiplyPoint3x4(meshBounds.center + Vector3.Scale(meshBounds.extents, sign));

                    if(hasBounds)
                    {
                        total.Encapsulate(point);
                    }
                    else
                    {
                        total = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                }
            }

            return total;
        }

        private Bounds GetNextPieceBounds(PaletteEntry entry, Quaternion rotation)
        {
            return GetGhostLocalBounds(m_nextPrefab, rotation * m_nextPrefab.transform.localRotation, GetPlacementScale(entry, m_nextPrefab));
        }

        // Upright pieces keep the palette footprint and cell-centred pose. Tilted pieces take their
        // footprint from their rotated shape and are re-seated so their lowest point rests on the cell floor.
        private void GetGridPose(GridLevel level, PaletteEntry entry, Vector3Int minCell, Vector3Int size, out Vector3 localPosition, out Quaternion localRotation)
        {
            Quaternion rotation = GetEffectiveRotation(level);
            float stackHeight = level.GetStackHeight(minCell, size);
            float yaw;

            if(m_nextPrefab == null || TryGetYaw(level, rotation, out yaw))
            {
                TryGetYaw(level, rotation, out yaw);
                level.GetRegionLocalPose(minCell, size, yaw, entry.pivotOffset, out localPosition, out localRotation);
                localPosition.y += stackHeight;
                localPosition += m_nextRandomOffset;
                return;
            }

            Bounds bounds = GetNextPieceBounds(entry, rotation);
            Vector3 center = Vector3.Scale((Vector3)minCell + (Vector3)size * 0.5f, level.cellSize);
            localRotation = rotation;
            localPosition = new Vector3(center.x - bounds.center.x, minCell.y * level.cellSize.y - bounds.min.y + stackHeight, center.z - bounds.center.z);
            localPosition += m_nextRandomOffset;
        }

        private void GetFreePose(GridLevel level, PaletteEntry entry, out Vector3 localPosition, out Quaternion localRotation)
        {
            localRotation = GetEffectiveRotation(level);
            float yaw;

            if(m_nextPrefab == null || TryGetYaw(level, localRotation, out yaw))
            {
                localPosition = m_freePoint + localRotation * entry.pivotOffset;
            }
            else
            {
                Bounds tilted = GetNextPieceBounds(entry, localRotation);
                Vector3 seat = tilted.center;
                seat[level.normalAxis] = tilted.min[level.normalAxis];
                localPosition = m_freePoint - seat;
            }

            if(m_nextPrefab == null)
            {
                return;
            }

            // A snapped ghost sits exactly on the face it snapped to. An unsnapped ghost drops onto the
            // highest piece below it with Stack on, and stays at the layer's height with Stack off.
            if(m_hasSnapPoint)
            {
                localPosition = m_snapPoint - GetGhostAnchor(level, entry, localRotation, GetOppositeFace(m_snapFace));
                return;
            }

            if(!m_stack)
            {
                return;
            }

            Bounds ghost = GetNextPieceBounds(entry, localRotation);
            ghost.center += localPosition;
            localPosition[level.normalAxis] += GetDropLift(level, ghost);
        }

        // How far the ghost must rise to rest on the highest placed piece under its area. Pieces that only
        // touch its sides do not count.
        private static float GetDropLift(GridLevel level, Bounds ghost)
        {
            int n = level.normalAxis;
            int u = level.uAxis;
            int v = level.vAxis;
            float top = ghost.min[n];
            Bounds shrunk = ShrinkBounds(ghost);

            for(int i = 0; i < level.cells.Count; i++)
            {
                GameObject instance = level.cells[i].instance;
                Bounds bounds;

                if(instance == null || !TryGetLevelBounds(level, instance, out bounds))
                {
                    continue;
                }

                Bounds other = ShrinkBounds(bounds);
                bool overlapsU = other.min[u] < shrunk.max[u] && other.max[u] > shrunk.min[u];
                bool overlapsV = other.min[v] < shrunk.max[v] && other.max[v] > shrunk.min[v];

                if(overlapsU && overlapsV && bounds.max[n] > top)
                {
                    top = bounds.max[n];
                }
            }

            return top - ghost.min[n];
        }

        // With Fit to Cell on, the entry's Scale field is replaced by the uniform factor that makes the
        // prefab fill one cell of the current grid.
        private Vector3 GetEntryScaleVector(GridLevel level, PaletteEntry entry)
        {
            if(entry == null)
            {
                return Vector3.one;
            }

            if(entry.fitToCell && level != null && entry.prefab != null)
            {
                return Vector3.one * GridFootprintUtility.GetFitFactor(level, entry.prefab);
            }

            return entry.GetScale();
        }

        private Vector3 GetPlacementScale(PaletteEntry entry, GameObject prefab)
        {
            Vector3 entryScale = GetEntryScaleVector(target as GridLevel, entry);
            return Vector3.Scale(prefab.transform.localScale, entryScale) * m_scaleMultiplier;
        }

        private static string FormatScale(float factor)
        {
            return factor.ToString("0.##") + "x";
        }

        private float GetEntryFactor(PaletteEntry entry)
        {
            if(entry == null)
            {
                return 1f;
            }

            Vector3 scale = GetEntryScaleVector(target as GridLevel, entry);
            return Mathf.Max(0.0001f, (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f);
        }

        private void FitToCell(GridLevel level)
        {
            if(m_mode == GridToolMode.Select && m_selected.Count > 0)
            {
                FitSelectedToCell(level);
                return;
            }

            PaletteEntry entry = GridPaletteState.selectedEntry;

            if(entry == null || entry.prefab == null)
            {
                return;
            }

            m_scaleMultiplier = GridFootprintUtility.GetFitFactor(level, entry.prefab) / GetEntryFactor(entry);
        }

        // Scales each selected piece so its larger side on the grid's plane fills one cell. Grid pieces are
        // then nudged so their new size lines up with whole cells; the base stays at the same height.
        private void FitSelectedToCell(GridLevel level)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Grid Fit Selection");
            int group = Undo.GetCurrentGroup();
            Undo.RecordObject(level, "Grid Fit Selection");
            int u = level.uAxis;
            int v = level.vAxis;

            foreach(GameObject instance in m_selected)
            {
                PlacedCell piece = level.FindByInstance(instance);
                Bounds before;

                if(piece == null || instance == null || !TryGetLevelBounds(level, instance, out before))
                {
                    continue;
                }

                float factorU = before.size[u] > 0.0001f ? level.cellSize[u] / before.size[u] : float.MaxValue;
                float factorV = before.size[v] > 0.0001f ? level.cellSize[v] / before.size[v] : float.MaxValue;
                float factor = Mathf.Min(factorU, factorV);

                if(factor == float.MaxValue || factor <= 0f)
                {
                    continue;
                }

                Transform pieceTransform = instance.transform;
                Undo.RecordObject(pieceTransform, "Grid Fit Selection");
                pieceTransform.localScale = pieceTransform.localScale * factor;

                Bounds after;

                if(piece.freeform || !TryGetLevelBounds(level, instance, out after))
                {
                    continue;
                }

                Vector3 shift = Vector3.zero;
                Vector3Int minCell = piece.cell;
                Vector3Int size = Vector3Int.one;
                int[] axes = { u, v };

                for(int i = 0; i < axes.Length; i++)
                {
                    int axis = axes[i];
                    float cell = level.cellSize[axis];
                    int count = Mathf.Max(1, Mathf.CeilToInt(after.size[axis] / cell - 0.01f));
                    int first = Mathf.RoundToInt(after.center[axis] / cell - count * 0.5f);
                    shift[axis] = (first + count * 0.5f) * cell - after.center[axis];
                    minCell[axis] = first;
                    size[axis] = count;
                }

                pieceTransform.localPosition += shift;
                piece.cell = minCell;
                piece.size = size;
            }

            level.InvalidateLookup();
            EditorUtility.SetDirty(level);
            Undo.CollapseUndoOperations(group);
        }

        private float GetDisplayedScale()
        {
            return GetEntryFactor(GridPaletteState.selectedEntry) * m_scaleMultiplier;
        }

        private void ScaleBy(GridLevel level, float delta)
        {
            if(m_mode == GridToolMode.Select && m_selected.Count > 0)
            {
                ScaleSelected(level, delta);
            }
            else
            {
                float entryFactor = GetEntryFactor(GridPaletteState.selectedEntry);
                float displayed = Mathf.Max(0.05f, entryFactor * m_scaleMultiplier + delta);
                m_scaleMultiplier = displayed / entryFactor;
            }
        }

        // Scales each selected piece around its own pivot, keeping its proportions; the step is added to
        // its current size factor relative to its source prefab.
        private void ScaleSelected(GridLevel level, float delta)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Grid Scale Selection");
            int group = Undo.GetCurrentGroup();

            foreach(GameObject instance in m_selected)
            {
                if(instance == null)
                {
                    continue;
                }

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
                Vector3 baseScale = source != null ? source.transform.localScale : Vector3.one;
                float baseLength = Mathf.Max(0.0001f, baseScale.magnitude);
                float factor = instance.transform.localScale.magnitude / baseLength;
                float newFactor = Mathf.Max(0.05f, factor + delta);

                Undo.RecordObject(instance.transform, "Grid Scale Selection");
                instance.transform.localScale = instance.transform.localScale * (newFactor / Mathf.Max(0.0001f, factor));
            }

            Undo.CollapseUndoOperations(group);
        }

        private void RotateBy(GridLevel level, float delta)
        {
            if(m_mode == GridToolMode.Select && m_selected.Count > 0)
            {
                RotateSelected(level, delta);
            }
            else
            {
                m_rotation = Quaternion.AngleAxis(delta, AxisVector(GetRotationAxis(level))) * m_rotation;
                m_rotation.Normalize();
            }
        }

        private static Vector3Int GetCenterOffset(GridLevel level, Vector3Int size)
        {
            Vector3Int offset = Vector3Int.zero;
            offset[level.uAxis] = (size[level.uAxis] - 1) / 2;
            offset[level.vAxis] = (size[level.vAxis] - 1) / 2;
            return offset;
        }

        private Vector3Int GetPlacementSize(GridLevel level, PaletteEntry entry)
        {
            EnsureRolled(entry);
            Quaternion rotation = GetEffectiveRotation(level);
            float yaw;

            if(m_nextPrefab == null || TryGetYaw(level, rotation, out yaw))
            {
                TryGetYaw(level, rotation, out yaw);
                return level.GetRegionSize(GetEntryFootprint(level, entry), GridLevel.QuarterTurns(yaw));
            }

            Bounds bounds = GetNextPieceBounds(entry, rotation);
            Vector3Int size = Vector3Int.one;
            int u = level.uAxis;
            int v = level.vAxis;
            size[u] = Mathf.Max(1, Mathf.CeilToInt(bounds.size[u] / level.cellSize[u] - 0.01f));
            size[v] = Mathf.Max(1, Mathf.CeilToInt(bounds.size[v] / level.cellSize[v] - 0.01f));
            return size;
        }

        private void BeginStroke(GridLevel level, Event evt, int controlId)
        {
            m_startCell = m_hoverCell;
            m_endCell = m_hoverCell;

            if(m_mode == GridToolMode.Select)
            {
                m_selectAdditive = evt.shift;
                m_selectStartMouse = evt.mousePosition;
                m_selectEndMouse = evt.mousePosition;
                m_dragging = true;
                GUIUtility.hotControl = controlId;
                evt.Use();
                return;
            }

            if(IsFreeform())
            {
                m_erasing = IsErasing(evt);
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(m_erasing ? "Grid Erase" : "Grid Place Free");
                m_undoGroup = Undo.GetCurrentGroup();

                if(m_erasing)
                {
                    EraseObject(level, m_hoverPiece);
                    m_dragging = true;
                    GUIUtility.hotControl = controlId;
                }
                else
                {
                    PaletteEntry entry = GridPaletteState.selectedEntry;

                    if(entry != null && entry.prefab != null && m_hasFreePoint)
                    {
                        PlaceFree(level, entry);
                    }

                    Undo.CollapseUndoOperations(m_undoGroup);
                }

                evt.Use();
                return;
            }

            m_erasing = IsErasing(evt);
            m_strokeCells.Clear();

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(m_erasing ? "Grid Erase" : "Grid Place");
            m_undoGroup = Undo.GetCurrentGroup();

            if(m_mode == GridToolMode.Fill)
            {
                CollectFill(level, m_hoverCell, m_erasing, m_cellBuffer);

                if(m_erasing)
                {
                    ApplyErase(level, m_cellBuffer);
                }
                else
                {
                    ApplyFillPlace(level, m_cellBuffer);
                }

                Undo.CollapseUndoOperations(m_undoGroup);
                evt.Use();
                return;
            }

            m_dragging = true;
            GUIUtility.hotControl = controlId;

            if(m_mode == GridToolMode.Paint)
            {
                m_cellBuffer.Clear();
                m_cellBuffer.Add(m_hoverCell);
                ApplyStrokeCells(level, m_cellBuffer);
            }

            evt.Use();
        }

        private void ContinueStroke(GridLevel level)
        {
            if(m_dragging && m_erasing && IsFreeform())
            {
                EraseObject(level, m_hoverPiece);
                return;
            }

            if(!m_dragging || !m_hasHoverCell || m_hoverCell == m_endCell)
            {
                return;
            }

            if(m_mode == GridToolMode.Paint)
            {
                CollectLine(level, m_endCell, m_hoverCell, m_cellBuffer);
                ApplyStrokeCells(level, m_cellBuffer);
            }

            m_endCell = m_hoverCell;
        }

        private void EndStroke(GridLevel level)
        {
            if(!m_dragging)
            {
                return;
            }

            m_dragging = false;

            if(m_mode == GridToolMode.Select)
            {
                ApplySelection(level);
                return;
            }

            if(IsFreeform())
            {
                Undo.CollapseUndoOperations(m_undoGroup);
                return;
            }

            if(m_mode == GridToolMode.Rectangle)
            {
                if(m_erasing)
                {
                    CollectRectangle(level, m_startCell, m_endCell, m_cellBuffer);
                    ApplyErase(level, m_cellBuffer);
                }
                else
                {
                    PaletteEntry entry = GridPaletteState.selectedEntry;

                    if(entry != null && entry.prefab != null)
                    {
                        CollectRectAnchors(level, m_startCell, m_endCell, GetPlacementSize(level, entry), m_minBuffer);
                        ApplyPlace(level, m_minBuffer, null);
                    }
                }
            }
            else if(m_mode == GridToolMode.Line)
            {
                CollectLine(level, m_startCell, m_endCell, m_cellBuffer);
                ApplyStrokeCells(level, m_cellBuffer);
            }

            Undo.CollapseUndoOperations(m_undoGroup);
        }

        private void ApplyStrokeCells(GridLevel level, List<Vector3Int> cells)
        {
            if(m_erasing)
            {
                ApplyErase(level, cells);
                return;
            }

            PaletteEntry entry = GridPaletteState.selectedEntry;

            if(entry == null || entry.prefab == null)
            {
                return;
            }

            Vector3Int offset = GetCenterOffset(level, GetPlacementSize(level, entry));
            m_minBuffer.Clear();

            for(int i = 0; i < cells.Count; i++)
            {
                m_minBuffer.Add(cells[i] - offset);
            }

            ApplyPlace(level, m_minBuffer, null);
        }

        private void ApplyFillPlace(GridLevel level, List<Vector3Int> cells)
        {
            if(cells.Count == 0)
            {
                return;
            }

            HashSet<Vector3Int> allowed = new HashSet<Vector3Int>(cells);
            int u = level.uAxis;
            int v = level.vAxis;

            m_minBuffer.Clear();
            m_minBuffer.AddRange(cells);
            m_minBuffer.Sort((a, b) =>
            {
                if(a[v] != b[v])
                {
                    return a[v].CompareTo(b[v]);
                }

                return a[u].CompareTo(b[u]);
            });

            ApplyPlace(level, m_minBuffer, allowed);
        }

        private void ApplyPlace(GridLevel level, List<Vector3Int> minCells, HashSet<Vector3Int> allowed)
        {
            PaletteEntry entry = GridPaletteState.selectedEntry;

            if(entry == null || entry.prefab == null || minCells.Count == 0)
            {
                return;
            }

            Undo.RecordObject(level, "Grid Place");

            for(int i = 0; i < minCells.Count; i++)
            {
                PlaceAt(level, minCells[i], entry, allowed);
            }

            EditorUtility.SetDirty(level);
        }

        private void PlaceAt(GridLevel level, Vector3Int minCell, PaletteEntry entry, HashSet<Vector3Int> allowed)
        {
            EnsureRolled(entry);

            Vector3Int size = GetPlacementSize(level, entry);
            float degrees;
            TryGetYaw(level, GetEffectiveRotation(level), out degrees);
            GridLevel.GetCoveredCells(minCell, size, m_coveredBuffer);

            for(int i = 0; i < m_coveredBuffer.Count; i++)
            {
                if(m_strokeCells.Contains(m_coveredBuffer[i]))
                {
                    return;
                }

                if(allowed != null && !allowed.Contains(m_coveredBuffer[i]))
                {
                    return;
                }
            }

            if(!m_stack && !level.IsRegionFree(minCell, size))
            {
                return;
            }

            GameObject prefab = m_nextPrefab;
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, level.transform) as GameObject;

            if(instance == null)
            {
                return;
            }

            Vector3 localPosition;
            Quaternion localRotation;
            GetGridPose(level, entry, minCell, size, out localPosition, out localRotation);

            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation * prefab.transform.localRotation;
            instance.transform.localScale = GetPlacementScale(entry, prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Grid Place");

            PlacedCell placed = new PlacedCell();
            placed.cell = minCell;
            placed.size = size;
            placed.instance = instance;
            placed.prefab = entry.prefab;
            placed.rotationSteps = GridLevel.QuarterTurns(degrees);
            placed.rotation = degrees;
            level.AddCell(placed);
            m_lastPiece = instance;

            GridLevel.GetCoveredCells(minCell, size, m_coveredBuffer);

            for(int i = 0; i < m_coveredBuffer.Count; i++)
            {
                m_strokeCells.Add(m_coveredBuffer[i]);
            }

            Roll(entry);
        }

        private void ApplyErase(GridLevel level, List<Vector3Int> cells)
        {
            if(cells.Count == 0)
            {
                return;
            }

            Undo.RecordObject(level, "Grid Erase");

            for(int i = 0; i < cells.Count; i++)
            {
                if(m_strokeCells.Contains(cells[i]))
                {
                    continue;
                }

                PlacedCell piece = level.GetCell(cells[i]);

                if(piece == null)
                {
                    m_strokeCells.Add(cells[i]);
                    continue;
                }

                GridLevel.GetCoveredCells(piece.cell, piece.coveredSize, m_coveredBuffer);

                for(int c = 0; c < m_coveredBuffer.Count; c++)
                {
                    m_strokeCells.Add(m_coveredBuffer[c]);
                }

                GameObject instance = piece.instance;
                level.RemovePiece(piece);
                Undo.DestroyObjectImmediate(instance);
            }

            EditorUtility.SetDirty(level);
        }

        private void PickUnderCursor(GridLevel level)
        {
            PlacedCell piece = level.FindByInstance(PickPiece(level, Event.current.mousePosition));

            if(piece == null && m_hasHoverCell)
            {
                piece = level.GetCell(m_hoverCell);
            }

            PrefabPalette palette = GridPaletteState.palette;

            if(piece == null || piece.prefab == null || palette == null)
            {
                return;
            }

            int index = palette.IndexOf(piece.prefab);

            if(index < 0)
            {
                Undo.RecordObject(palette, "Pick Prefab");
                PaletteEntry entry = new PaletteEntry();
                entry.prefab = piece.prefab;
                entry.footprint = GridFootprintUtility.ComputeFor(level, piece.prefab, Vector3.one);
                palette.entries.Add(entry);
                EditorUtility.SetDirty(palette);
                index = palette.entries.Count - 1;
            }

            if(piece.instance != null)
            {
                m_rotation = GetPieceRotation(piece.instance);
                m_lastPiece = piece.instance;
            }

            m_eraseToggle = false;

            if(m_mode == GridToolMode.Select)
            {
                m_mode = GridToolMode.Paint;
            }

            GridPaletteState.Select(index);
        }

        private static Vector3Int MakeCell(GridLevel level, int u, int v, int n)
        {
            Vector3Int cell = Vector3Int.zero;
            cell[level.uAxis] = u;
            cell[level.vAxis] = v;
            cell[level.normalAxis] = n;
            return cell;
        }

        private static void CollectRectangle(GridLevel level, Vector3Int from, Vector3Int to, List<Vector3Int> results)
        {
            results.Clear();

            int uAxis = level.uAxis;
            int vAxis = level.vAxis;
            int n = from[level.normalAxis];
            int uMin = Mathf.Min(from[uAxis], to[uAxis]);
            int uMax = Mathf.Max(from[uAxis], to[uAxis]);
            int vMin = Mathf.Min(from[vAxis], to[vAxis]);
            int vMax = Mathf.Max(from[vAxis], to[vAxis]);

            if((uMax - uMin + 1) * (vMax - vMin + 1) > m_maxCellsPerAction)
            {
                Debug.LogWarning("Grid Level: rectangle is larger than " + m_maxCellsPerAction + " cells, nothing was changed.");
                return;
            }

            for(int u = uMin; u <= uMax; u++)
            {
                for(int v = vMin; v <= vMax; v++)
                {
                    results.Add(MakeCell(level, u, v, n));
                }
            }
        }

        // Tiles the dragged rectangle with whole footprints, always placing at least one piece.
        private static void CollectRectAnchors(GridLevel level, Vector3Int from, Vector3Int to, Vector3Int size, List<Vector3Int> results)
        {
            results.Clear();

            int uAxis = level.uAxis;
            int vAxis = level.vAxis;
            int n = from[level.normalAxis];
            int uMin = Mathf.Min(from[uAxis], to[uAxis]);
            int uMax = Mathf.Max(from[uAxis], to[uAxis]);
            int vMin = Mathf.Min(from[vAxis], to[vAxis]);
            int vMax = Mathf.Max(from[vAxis], to[vAxis]);
            int su = size[uAxis];
            int sv = size[vAxis];
            int countU = Mathf.Max(1, (uMax - uMin + 1) / su);
            int countV = Mathf.Max(1, (vMax - vMin + 1) / sv);

            if(countU * countV > m_maxCellsPerAction)
            {
                Debug.LogWarning("Grid Level: rectangle is larger than " + m_maxCellsPerAction + " pieces, nothing was changed.");
                return;
            }

            for(int i = 0; i < countU; i++)
            {
                for(int j = 0; j < countV; j++)
                {
                    results.Add(MakeCell(level, uMin + i * su, vMin + j * sv, n));
                }
            }
        }

        private static void CollectLine(GridLevel level, Vector3Int from, Vector3Int to, List<Vector3Int> results)
        {
            results.Clear();

            int n = from[level.normalAxis];
            int u = from[level.uAxis];
            int v = from[level.vAxis];
            int uEnd = to[level.uAxis];
            int vEnd = to[level.vAxis];
            int du = Mathf.Abs(uEnd - u);
            int dv = Mathf.Abs(vEnd - v);
            int uStep = u < uEnd ? 1 : -1;
            int vStep = v < vEnd ? 1 : -1;
            int error = du - dv;

            while(results.Count < m_maxCellsPerAction)
            {
                results.Add(MakeCell(level, u, v, n));

                if(u == uEnd && v == vEnd)
                {
                    break;
                }

                int doubled = error * 2;

                if(doubled > -dv)
                {
                    error -= dv;
                    u += uStep;
                }

                if(doubled < du)
                {
                    error += du;
                    v += vStep;
                }
            }
        }

        // Placing floods the connected empty cells inside the drawn grid extent.
        // Erasing floods the connected cells that hold the same prefab as the clicked cell.
        private static void CollectFill(GridLevel level, Vector3Int origin, bool erase, List<Vector3Int> results)
        {
            results.Clear();

            int uAxis = level.uAxis;
            int vAxis = level.vAxis;
            int n = origin[level.normalAxis];
            int min = level.gridMin;
            int max = level.gridMax;
            PlacedCell originCell = level.GetCell(origin);
            GameObject matchPrefab = null;

            if(erase)
            {
                if(originCell == null)
                {
                    return;
                }

                matchPrefab = originCell.prefab;
            }
            else if(originCell != null)
            {
                return;
            }

            HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
            Queue<Vector3Int> queue = new Queue<Vector3Int>();
            queue.Enqueue(origin);
            visited.Add(origin);

            while(queue.Count > 0 && results.Count < m_maxCellsPerAction)
            {
                Vector3Int cell = queue.Dequeue();
                int u = cell[uAxis];
                int v = cell[vAxis];

                if(u < min || u >= max || v < min || v >= max)
                {
                    continue;
                }

                PlacedCell placed = level.GetCell(cell);

                if(erase)
                {
                    if(placed == null || placed.prefab != matchPrefab)
                    {
                        continue;
                    }
                }
                else if(placed != null)
                {
                    continue;
                }

                results.Add(cell);
                TryEnqueue(MakeCell(level, u + 1, v, n), visited, queue);
                TryEnqueue(MakeCell(level, u - 1, v, n), visited, queue);
                TryEnqueue(MakeCell(level, u, v + 1, n), visited, queue);
                TryEnqueue(MakeCell(level, u, v - 1, n), visited, queue);
            }
        }

        private static void TryEnqueue(Vector3Int cell, HashSet<Vector3Int> visited, Queue<Vector3Int> queue)
        {
            if(visited.Add(cell))
            {
                queue.Enqueue(cell);
            }
        }

        private bool IsFlying()
        {
            if(m_rightMouseHeld && !Tools.viewToolActive && Event.current != null && Event.current.type == EventType.MouseMove)
            {
                m_rightMouseHeld = false;
            }

            return m_rightMouseHeld || (Tools.viewToolActive && Tools.viewTool == ViewTool.FPS);
        }

        private void HandleKey(GridLevel level, Event evt)
        {
            if(evt.control || evt.command || IsFlying())
            {
                return;
            }

            if(evt.keyCode == KeyCode.S)
            {
                if(!m_stackKeyHeld)
                {
                    m_stackKeyHeld = true;
                    m_stack = !m_stack;
                }
            }
            else if(evt.keyCode == KeyCode.X)
            {
                if(!m_eraseKeyHeld)
                {
                    m_eraseKeyHeld = true;
                    m_eraseToggle = !m_eraseToggle;
                }
            }
            else if(evt.keyCode == KeyCode.I)
            {
                PickUnderCursor(level);
            }
            else if(evt.keyCode == KeyCode.N)
            {
                if(!m_snapKeyHeld)
                {
                    m_snapKeyHeld = true;
                    m_snapToGrid = !m_snapToGrid;

                    if(!m_snapToGrid)
                    {
                        m_mode = GridToolMode.Paint;
                    }
                }
            }
            else if(evt.keyCode == KeyCode.C)
            {
                if(!m_centreKeyHeld)
                {
                    m_centreKeyHeld = true;
                    m_snapToBounds = !m_snapToBounds;
                }
            }
            else if(evt.keyCode == KeyCode.RightBracket)
            {
                Undo.RecordObject(level, "Grid Layer Up");
                level.layer++;
                m_hasHoverCell = false;
                EditorUtility.SetDirty(level);
            }
            else if(evt.keyCode == KeyCode.LeftBracket)
            {
                Undo.RecordObject(level, "Grid Layer Down");
                level.layer--;
                m_hasHoverCell = false;
                EditorUtility.SetDirty(level);
            }
            else if(evt.keyCode == KeyCode.G)
            {
                Undo.RecordObject(level, "Cycle Grid Plane");
                level.plane = (GridPlane)(((int)level.plane + 1) % 3);
                m_hasHoverCell = false;
                EditorUtility.SetDirty(level);
            }
            else if(evt.keyCode == KeyCode.Period)
            {
                RotateBy(level, m_stepDegrees);
            }
            else if(evt.keyCode == KeyCode.Equals || evt.keyCode == KeyCode.KeypadPlus)
            {
                ScaleBy(level, m_scaleStep);
            }
            else if(evt.keyCode == KeyCode.Minus || evt.keyCode == KeyCode.KeypadMinus)
            {
                ScaleBy(level, -m_scaleStep);
            }
            else if(evt.keyCode == KeyCode.Slash)
            {
                m_rotationAxis = (GetRotationAxis(level) + 1) % 3;
            }
            else if(evt.keyCode == KeyCode.Comma)
            {
                RotateBy(level, -m_stepDegrees);
            }
            else if(evt.keyCode == KeyCode.U)
            {
                m_mode = GridToolMode.Select;
            }
            else if((evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) && m_mode == GridToolMode.Select && m_selected.Count > 0)
            {
                DeleteSelected(level);
            }
            else if(evt.keyCode == KeyCode.Escape && !m_dragging && m_selected.Count > 0)
            {
                m_selected.Clear();
            }
            else if(evt.keyCode == KeyCode.B)
            {
                m_mode = GridToolMode.Paint;
            }
            else if(evt.keyCode == KeyCode.M)
            {
                m_mode = GridToolMode.Rectangle;
            }
            else if(evt.keyCode == KeyCode.L)
            {
                m_mode = GridToolMode.Line;
            }
            else if(evt.keyCode == KeyCode.K)
            {
                m_mode = GridToolMode.Fill;
            }
            else if(evt.keyCode == KeyCode.Escape && m_dragging)
            {
                if(m_mode == GridToolMode.Paint)
                {
                    Undo.CollapseUndoOperations(m_undoGroup);
                }

                m_dragging = false;
                GUIUtility.hotControl = 0;
            }
            else
            {
                return;
            }

            evt.Use();
        }

        private void DrawQuad(GridLevel level, Vector3Int minCell, Vector3Int maxCell, Color fill, Color outline)
        {
            int u = level.uAxis;
            int v = level.vAxis;
            Vector3 min = level.CellToLocal(minCell);
            Vector3 max = level.CellToLocal(maxCell);
            max[u] += level.cellSize[u];
            max[v] += level.cellSize[v];

            m_quadCorners[0] = min;
            m_quadCorners[1] = min;
            m_quadCorners[1][u] = max[u];
            m_quadCorners[2] = min;
            m_quadCorners[2][u] = max[u];
            m_quadCorners[2][v] = max[v];
            m_quadCorners[3] = min;
            m_quadCorners[3][v] = max[v];

            Matrix4x4 previousMatrix = Handles.matrix;
            Handles.matrix = level.transform.localToWorldMatrix;
            Handles.DrawSolidRectangleWithOutline(m_quadCorners, fill, outline);
            Handles.matrix = previousMatrix;
        }

        // Wireframe of the whole cell volume, depth included, so the ghost can be seen sitting inside it.
        // On vertical grids the flat outline lies on the back face while pieces sit mid-depth.
        private void DrawCellVolume(GridLevel level, Vector3Int minCell, Vector3Int size, Color color)
        {
            Vector3 center = Vector3.Scale((Vector3)minCell + (Vector3)size * 0.5f, level.cellSize);
            Vector3 extents = Vector3.Scale((Vector3)size, level.cellSize) * 0.5f;

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            Handles.matrix = level.transform.localToWorldMatrix;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = new Color(color.r, color.g, color.b, 0.7f);
            DrawThickBox(center, extents, 2f);
            Handles.zTest = previousZTest;
            Handles.color = previousColor;
            Handles.matrix = previousMatrix;
        }

        private void DrawStrokePreview(GridLevel level)
        {
            Color fill = m_erasing ? m_blockedFill : m_freeFill;
            Color outline = m_erasing ? m_blockedOutline : m_freeOutline;

            if(m_mode == GridToolMode.Rectangle)
            {
                Vector3Int minCell = Vector3Int.Min(m_startCell, m_endCell);
                Vector3Int maxCell = Vector3Int.Max(m_startCell, m_endCell);
                DrawQuad(level, minCell, maxCell, fill, outline);
            }
            else
            {
                CollectLine(level, m_startCell, m_endCell, m_cellBuffer);

                for(int i = 0; i < m_cellBuffer.Count; i++)
                {
                    DrawQuad(level, m_cellBuffer[i], m_cellBuffer[i], fill, outline);
                }
            }
        }

        private void DrawHover(GridLevel level, bool erasing)
        {
            if(!m_hasHoverCell)
            {
                return;
            }

            if(erasing)
            {
                PlacedCell piece = level.GetCell(m_hoverCell);

                if(piece != null)
                {
                    DrawQuad(level, piece.cell, piece.cell + piece.coveredSize - Vector3Int.one, m_blockedFill, m_blockedOutline);
                }
                else
                {
                    DrawQuad(level, m_hoverCell, m_hoverCell, m_freeFill, m_freeOutline);
                }

                return;
            }

            PaletteEntry entry = GridPaletteState.selectedEntry;

            if(entry == null || entry.prefab == null)
            {
                DrawQuad(level, m_hoverCell, m_hoverCell, m_freeFill, m_freeOutline);
                return;
            }

            Vector3Int size = GetPlacementSize(level, entry);
            Vector3Int minCell = m_hoverCell - GetCenterOffset(level, size);
            bool blocked = !m_stack && !level.IsRegionFree(minCell, size);

            DrawQuad(level, minCell, minCell + size - Vector3Int.one, blocked ? m_blockedFill : m_freeFill, blocked ? m_blockedOutline : m_freeOutline);
            DrawCellVolume(level, minCell, size, blocked ? m_blockedOutline : m_freeOutline);
            DrawGhost(level, minCell, size, entry, blocked);
        }

        private void DrawGhost(GridLevel level, Vector3Int minCell, Vector3Int size, PaletteEntry entry, bool blocked)
        {
            GameObject prefab = m_nextPrefab;

            if(prefab == null)
            {
                return;
            }

            if(m_ghostPrefab != prefab)
            {
                RebuildGhostCache(prefab);
            }

            if(m_ghostMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");

                if(shader == null)
                {
                    return;
                }

                m_ghostMaterial = new Material(shader);
                m_ghostMaterial.hideFlags = HideFlags.HideAndDontSave;
                m_ghostMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m_ghostMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m_ghostMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
                m_ghostMaterial.SetInt("_ZWrite", 0);
                m_ghostMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            }

            Vector3 localPosition;
            Quaternion localRotation;
            GetGridPose(level, entry, minCell, size, out localPosition, out localRotation);
            DrawGhostPose(level, localPosition, localRotation, blocked);
        }

        private void DrawGhostPose(GridLevel level, Vector3 localPosition, Quaternion localRotation, bool blocked)
        {
            GameObject prefab = m_nextPrefab;

            if(prefab == null || m_ghostMaterial == null)
            {
                return;
            }

            Transform prefabRoot = prefab.transform;
            Matrix4x4 rootMatrix = level.transform.localToWorldMatrix * Matrix4x4.TRS(localPosition, localRotation * prefabRoot.localRotation, GetPlacementScale(GridPaletteState.selectedEntry, prefab));

            m_ghostMaterial.SetColor("_Color", blocked ? m_ghostBlocked : m_ghostFree);
            m_ghostMaterial.SetPass(0);

            for(int i = 0; i < m_ghostMeshes.Count; i++)
            {
                Matrix4x4 matrix = rootMatrix * m_ghostMatrices[i];

                for(int sub = 0; sub < m_ghostMeshes[i].subMeshCount; sub++)
                {
                    Graphics.DrawMeshNow(m_ghostMeshes[i], matrix, sub);
                }
            }

            DrawFacingArrow(level, localPosition, localRotation);

            Vector3 labelPosition = level.transform.TransformPoint(localPosition + Vector3.up * level.cellSize.y * 1.15f);
            string ghostLabel = FormatRotation(level, GetEffectiveRotation(level));

            if(!Mathf.Approximately(GetDisplayedScale(), 1f))
            {
                ghostLabel += "   " + FormatScale(GetDisplayedScale());
            }

            Handles.Label(labelPosition, ghostLabel, GetAngleLabelStyle());
        }

        private void DrawFreeHover(GridLevel level, bool erasing)
        {
            if(erasing)
            {
                if(m_hoverPiece != null)
                {
                    DrawPieceBox(m_hoverPiece, m_blockedOutline);
                }

                return;
            }

            if(!m_hasFreePoint)
            {
                return;
            }

            PaletteEntry entry = GridPaletteState.selectedEntry;
            bool hasEntry = entry != null && entry.prefab != null;
            bool blocked = false;

            float discSize = Mathf.Min(level.cellSize[level.uAxis], level.cellSize[level.vAxis]) * 0.3f;
            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            Handles.matrix = level.transform.localToWorldMatrix;
            Handles.color = blocked ? m_blockedOutline : m_freeOutline;
            Handles.DrawWireDisc(m_freePoint, NormalVector(level), discSize);
            Handles.color = previousColor;
            Handles.matrix = previousMatrix;

            if(!hasEntry)
            {
                return;
            }

            EnsureRolled(entry);
            EnsureGhostCache(m_nextPrefab);

            Vector3 freePosition;
            Quaternion localRotation;
            GetFreePose(level, entry, out freePosition, out localRotation);
            EnsureGhostMaterial();
            DrawGhostPose(level, freePosition, localRotation, blocked);
            DrawSnapPoints(level);
        }

        private void DrawSnapPoints(GridLevel level)
        {
            if(m_snapPoints.Count == 0)
            {
                return;
            }

            Color previousColor = Handles.color;
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            for(int i = 0; i < m_snapPoints.Count; i++)
            {
                Vector3 world = level.transform.TransformPoint(m_snapPoints[i].position);
                bool active = i == m_activeSnapIndex;

                if(active)
                {
                    Handles.color = m_arrowColor;
                }
                else if(m_snapPoints[i].face > 0)
                {
                    Handles.color = m_sideSnapColor;
                }
                else
                {
                    Handles.color = new Color(1f, 1f, 1f, 0.85f);
                }

                float dotSize = m_snapPoints[i].centre ? m_centreSnapDotSize : m_snapDotSize;

                if(active)
                {
                    dotSize = m_activeSnapDotSize;
                }

                Handles.DotHandleCap(0, world, Quaternion.identity, HandleUtility.GetHandleSize(world) * dotSize, EventType.Repaint);
            }

            Handles.zTest = previousZTest;
            Handles.color = previousColor;
        }

        private void EnsureGhostMaterial()
        {
            if(m_ghostMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");

            if(shader == null)
            {
                return;
            }

            m_ghostMaterial = new Material(shader);
            m_ghostMaterial.hideFlags = HideFlags.HideAndDontSave;
            m_ghostMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m_ghostMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m_ghostMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
            m_ghostMaterial.SetInt("_ZWrite", 0);
            m_ghostMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        }

        private static bool TryGetBounds(GameObject instance, out Bounds bounds)
        {
            bounds = new Bounds(instance.transform.position, Vector3.zero);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();

            if(renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;

            for(int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private void DrawPieceBox(GameObject instance, Color color)
        {
            Bounds bounds;

            if(!TryGetBounds(instance, out bounds))
            {
                return;
            }

            Color previousColor = Handles.color;
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            Handles.color = color;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            DrawThickBox(bounds.center, bounds.extents * 1.02f, 3f);
            Handles.zTest = previousZTest;
            Handles.color = previousColor;
        }

        private static GUIStyle GetAngleLabelStyle()
        {
            if(m_angleLabelStyle == null || m_angleLabelBackground == null)
            {
                m_angleLabelBackground = new Texture2D(1, 1);
                m_angleLabelBackground.hideFlags = HideFlags.HideAndDontSave;
                m_angleLabelBackground.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.1f, 0.85f));
                m_angleLabelBackground.Apply();

                m_angleLabelStyle = new GUIStyle(EditorStyles.boldLabel);
                m_angleLabelStyle.normal.background = m_angleLabelBackground;
                m_angleLabelStyle.normal.textColor = Color.white;
                m_angleLabelStyle.padding = new RectOffset(6, 6, 2, 2);
                m_angleLabelStyle.alignment = TextAnchor.MiddleCenter;
            }

            return m_angleLabelStyle;
        }

        private static string FormatDegrees(float degrees)
        {
            return degrees.ToString("0.#") + "\u00B0";
        }

        // Points along the piece's forward (+Z) so the current rotation is readable at a glance.
        private void DrawFacingArrow(GridLevel level, Vector3 localPosition, Quaternion localRotation)
        {
            float length = Mathf.Min(level.cellSize[level.uAxis], level.cellSize[level.vAxis]) * 0.8f;
            Vector3 direction = localRotation * Vector3.forward;
            Vector3 start = localPosition + Vector3.up * 0.02f - direction * length * 0.5f;

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            Handles.matrix = level.transform.localToWorldMatrix;
            Handles.color = m_arrowColor;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.ArrowHandleCap(0, start, Quaternion.LookRotation(direction, Vector3.up), length, EventType.Repaint);
            Handles.zTest = previousZTest;
            Handles.color = previousColor;
            Handles.matrix = previousMatrix;
        }

        private void RebuildGhostCache(GameObject prefab)
        {
            m_ghostPrefab = prefab;
            m_ghostMeshes.Clear();
            m_ghostMatrices.Clear();

            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);

            for(int i = 0; i < filters.Length; i++)
            {
                if(filters[i].sharedMesh != null)
                {
                    m_ghostMeshes.Add(filters[i].sharedMesh);
                    m_ghostMatrices.Add(rootInverse * filters[i].transform.localToWorldMatrix);
                }
            }

            SkinnedMeshRenderer[] skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            for(int i = 0; i < skinned.Length; i++)
            {
                if(skinned[i].sharedMesh != null)
                {
                    m_ghostMeshes.Add(skinned[i].sharedMesh);
                    m_ghostMatrices.Add(rootInverse * skinned[i].transform.localToWorldMatrix);
                }
            }
        }

        private void DrawToggle(ref bool value, string label, Color onColor)
        {
            Color previousBackground = GUI.backgroundColor;

            if(value)
            {
                GUI.backgroundColor = onColor;
            }

            value = GUILayout.Toggle(value, label, EditorStyles.miniButton);
            GUI.backgroundColor = previousBackground;
        }

        public void DrawPanelGUI(float availableWidth)
        {
            m_previewSize = Mathf.Clamp(availableWidth * 0.18f, 64f, 128f);

            GridLevel level = target as GridLevel;

            if(level == null)
            {
                return;
            }

            bool selectMode = m_mode == GridToolMode.Select;
            m_selected.RemoveWhere(IsMissing);

            EditorGUI.BeginChangeCheck();
            GUILayout.BeginVertical(GUILayout.Width(Mathf.Max(m_panelWidth, availableWidth)));

            GUILayout.BeginHorizontal();
            m_mode = (GridToolMode)GUILayout.Toolbar((int)m_mode, m_modeContents);
            m_showShortcuts = GUILayout.Toggle(m_showShortcuts, new GUIContent("Shortcuts", "Show or hide the shortcut list"), EditorStyles.miniButton, GUILayout.Width(72f));
            GUILayout.EndHorizontal();
            selectMode = m_mode == GridToolMode.Select;

            if(m_showShortcuts)
            {
                DrawShortcuts(selectMode, Mathf.Max(m_panelWidth, availableWidth));
            }

            GUILayout.BeginHorizontal();
            DrawToggle(ref m_stack, m_stack ? "Stack: ON (S)" : "Stack: OFF (S)", new Color(0.3f, 0.85f, 1f, 1f));
            DrawToggle(ref m_eraseToggle, m_eraseToggle ? "Erase: ON (X)" : "Erase: OFF (X)", new Color(1f, 0.35f, 0.3f, 1f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool snapHasNoEffect = !m_snapToGrid && m_mode != GridToolMode.Paint;
            bool wasSnapping = m_snapToGrid;
            string snapLabel = m_snapToGrid ? "Snap to Grid: ON (N)" : "Snap to Grid: OFF (N)";

            if(snapHasNoEffect)
            {
                snapLabel = "Snap to Grid: OFF (Paint only)";
                GUI.enabled = false;
            }

            DrawToggle(ref m_snapToGrid, snapLabel, new Color(0.45f, 0.9f, 0.45f, 1f));
            GUI.enabled = true;

            if(wasSnapping && !m_snapToGrid)
            {
                m_mode = GridToolMode.Paint;
            }

            if(!m_snapToGrid)
            {
                float previousSnapLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 60f;
                m_fineSnap = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Fine snap", "0 = fully free; otherwise rounds the position to this increment"), m_fineSnap, GUILayout.Width(120f)));
                EditorGUIUtility.labelWidth = previousSnapLabelWidth;
            }

            GUILayout.EndHorizontal();

            if(!m_snapToGrid)
            {
                GUILayout.BeginHorizontal();
                DrawToggle(ref m_snapToBounds, m_snapToBounds ? "Snap Centres: Bounds (C)" : "Snap Centres: Faces (C)", new Color(1f, 0.8f, 0.3f, 1f));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();

            if(GUILayout.Button(new GUIContent("Rotate Left", "Rotate by -step (,)"), EditorStyles.miniButtonLeft))
            {
                RotateBy(level, -m_stepDegrees);
            }

            string angleText = "Next " + FormatRotation(level, GetEffectiveRotation(level));

            if(selectMode && m_selected.Count > 0)
            {
                angleText = "Selection";
            }

            GUILayout.Label(angleText, EditorStyles.miniButtonMid, GUILayout.Width(112f));

            if(GUILayout.Button(new GUIContent("Rotate Right", "Rotate by +step (.)"), EditorStyles.miniButtonRight))
            {
                RotateBy(level, m_stepDegrees);
            }

            GUILayout.EndHorizontal();

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 70f;
            m_stepDegrees = Mathf.Clamp(EditorGUILayout.FloatField("Step (deg)", m_stepDegrees), 0.1f, 360f);
            EditorGUIUtility.labelWidth = previousLabelWidth;

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Axis", "Which grid axis Rotate Left / Right turns around (/ cycles)"), GUILayout.Width(66f));
            int currentAxis = GetRotationAxis(level);
            int pickedAxis = GUILayout.Toolbar(currentAxis, m_axisContents, EditorStyles.miniButton);

            if(pickedAxis != currentAxis)
            {
                m_rotationAxis = pickedAxis;
            }

            if(GUILayout.Button(new GUIContent("Reset", "Clear the next piece's rotation"), EditorStyles.miniButton, GUILayout.Width(50f)))
            {
                m_rotation = Quaternion.identity;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();

            if(GUILayout.Button(new GUIContent("Scale Down", "Shrink by step (-)"), EditorStyles.miniButtonLeft))
            {
                ScaleBy(level, -m_scaleStep);
            }

            string scaleText = "Next " + FormatScale(GetDisplayedScale());

            if(selectMode && m_selected.Count > 0)
            {
                scaleText = "Selection";
            }

            GUILayout.Label(scaleText, EditorStyles.miniButtonMid, GUILayout.Width(80f));

            if(GUILayout.Button(new GUIContent("Scale Up", "Grow by step (=)"), EditorStyles.miniButtonRight))
            {
                ScaleBy(level, m_scaleStep);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUIUtility.labelWidth = 70f;
            m_scaleStep = Mathf.Clamp(EditorGUILayout.FloatField("Step (x)", m_scaleStep), 0.01f, 10f);
            EditorGUIUtility.labelWidth = previousLabelWidth;

            if(GUILayout.Button(new GUIContent("Fit", "Scale so the larger side fills one grid cell (the selection, in Select mode)"), EditorStyles.miniButtonLeft, GUILayout.Width(40f)))
            {
                FitToCell(level);
            }

            if(GUILayout.Button(new GUIContent("Reset", "Return to the selected prefab's own scale"), EditorStyles.miniButtonRight, GUILayout.Width(50f)))
            {
                m_scaleMultiplier = 1f;
            }

            GUILayout.EndHorizontal();

            if(selectMode)
            {
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Selected: " + m_selected.Count, GUILayout.Width(90f));
                GUI.enabled = m_selected.Count > 0;

                if(GUILayout.Button("Delete (Del)", EditorStyles.miniButtonLeft))
                {
                    DeleteSelected(level);
                }

                if(GUILayout.Button("Clear (Esc)", EditorStyles.miniButtonRight))
                {
                    m_selected.Clear();
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4f);
            DrawInfoAndPreview(level);
            DrawPaletteSection(Mathf.Max(m_panelWidth, availableWidth));


            GUILayout.EndVertical();

            if(EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
        }

        private const string m_showPaletteKey = "DefaceGames.GridLevel.ShowPanelPalette";
        private const float m_paletteTileSize = 64f;
        private const float m_paletteTileSpacing = 3f;
        private const float m_paletteLabelHeight = 14f;
        private const int m_paletteVisibleRows = 3;

        private static readonly Color m_paletteTileColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color m_paletteSelectedColor = new Color(0.2f, 0.8f, 1f, 1f);

        private static GUIStyle m_paletteLabelStyle;
        private static bool m_previewCacheSized;
        private Vector2 m_paletteScroll;
        private const string m_showPrefabSettingsKey = "DefaceGames.GridLevel.ShowPanelPrefabSettings";
        private SerializedObject m_paletteObject;

        private void DrawPaletteSection(float width)
        {
            bool show = EditorPrefs.GetBool(m_showPaletteKey, false);

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            bool newShow = GUILayout.Toggle(show, new GUIContent("Palette", "Show or hide the prefab palette"), EditorStyles.miniButton);

            if(GUILayout.Button(new GUIContent("Open Window", "Open the palette window to add, remove or set up prefabs"), EditorStyles.miniButton, GUILayout.Width(90f)))
            {
                EditorApplication.ExecuteMenuItem("Window/Grid Level/Palette");
            }

            GUILayout.EndHorizontal();

            if(newShow != show)
            {
                EditorPrefs.SetBool(m_showPaletteKey, newShow);
            }

            if(!newShow)
            {
                return;
            }

            if(!m_previewCacheSized)
            {
                m_previewCacheSized = true;
                AssetPreview.SetPreviewTextureCacheSize(512);
            }

            PrefabPalette current = GridPaletteState.palette;
            EditorGUI.BeginChangeCheck();
            PrefabPalette picked = (PrefabPalette)EditorGUILayout.ObjectField(current, typeof(PrefabPalette), false);

            if(EditorGUI.EndChangeCheck())
            {
                GridPaletteState.palette = picked;
                current = picked;
                m_paletteScroll = Vector2.zero;
            }

            if(current == null)
            {
                EditorGUILayout.HelpBox("Assign a palette, or create one in the palette window.", MessageType.None);
                return;
            }

            int count = current.entries.Count;

            if(count == 0)
            {
                EditorGUILayout.HelpBox("Empty palette. Add prefabs in the palette window.", MessageType.None);
                return;
            }

            if(m_paletteLabelStyle == null)
            {
                m_paletteLabelStyle = new GUIStyle(EditorStyles.miniLabel);
                m_paletteLabelStyle.alignment = TextAnchor.MiddleCenter;
                m_paletteLabelStyle.clipping = TextClipping.Clip;
                m_paletteLabelStyle.fontSize = 9;
            }

            float innerWidth = Mathf.Max(m_paletteTileSize, width - 16f);
            float cellWidth = m_paletteTileSize + m_paletteTileSpacing;
            float cellHeight = m_paletteTileSize + m_paletteLabelHeight + m_paletteTileSpacing;
            int columns = Mathf.Max(1, Mathf.FloorToInt((innerWidth + m_paletteTileSpacing) / cellWidth));
            int rows = Mathf.CeilToInt(count / (float)columns);
            float areaHeight = Mathf.Min(rows, m_paletteVisibleRows) * cellHeight + 2f;

            m_paletteScroll = GUILayout.BeginScrollView(m_paletteScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(areaHeight));
            Rect content = GUILayoutUtility.GetRect(innerWidth, rows * cellHeight);

            for(int i = 0; i < count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                Rect tile = new Rect(content.x + column * cellWidth, content.y + row * cellHeight, m_paletteTileSize, m_paletteTileSize + m_paletteLabelHeight);
                DrawPaletteTile(tile, current.entries[i], i);
            }

            GUILayout.EndScrollView();
            DrawPrefabSettings(current);
        }

        private void DrawPrefabSettings(PrefabPalette palette)
        {
            int index = GridPaletteState.selectedIndex;

            if(index < 0 || index >= palette.entries.Count)
            {
                return;
            }

            bool show = EditorPrefs.GetBool(m_showPrefabSettingsKey, false);
            GUILayout.Space(4f);
            bool newShow = GUILayout.Toggle(show, new GUIContent("Prefab Settings", "Show or hide the selected prefab's palette settings"), EditorStyles.miniButton);

            if(newShow != show)
            {
                EditorPrefs.SetBool(m_showPrefabSettingsKey, newShow);
            }

            if(!newShow)
            {
                return;
            }

            if(m_paletteObject == null || m_paletteObject.targetObject != palette)
            {
                m_paletteObject = new SerializedObject(palette);
            }

            m_paletteObject.Update();
            SerializedProperty entries = m_paletteObject.FindProperty("entries");

            if(entries == null || index >= entries.arraySize)
            {
                return;
            }

            SerializedProperty entryProperty = entries.GetArrayElementAtIndex(index);
            SerializedProperty child = entryProperty.Copy();
            SerializedProperty end = entryProperty.GetEndProperty();
            bool enterChildren = true;

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 110f;

            while(child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                EditorGUILayout.PropertyField(child, true);
                enterChildren = false;
            }

            EditorGUIUtility.labelWidth = previousLabelWidth;

            if(m_paletteObject.ApplyModifiedProperties())
            {
                GridPaletteState.NotifyChanged();
            }

            PaletteEntry entry = palette.entries[index];
            GUILayout.Label(GridFootprintUtility.DescribeFootprint(entry), EditorStyles.wordWrappedMiniLabel);

            if(GUILayout.Button("Fill Manual Footprint From Bounds", EditorStyles.miniButton))
            {
                GridFootprintUtility.FillManualFootprint(palette, index);
                m_paletteObject.Update();
            }
        }

        private void DrawPaletteTile(Rect rect, PaletteEntry entry, int index)
        {
            bool selected = index == GridPaletteState.selectedIndex;
            EditorGUI.DrawRect(rect, m_paletteTileColor);

            if(selected)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), m_paletteSelectedColor);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), m_paletteSelectedColor);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), m_paletteSelectedColor);
                EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), m_paletteSelectedColor);
            }

            Rect imageRect = new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, m_paletteTileSize - 6f);
            Rect labelRect = new Rect(rect.x + 2f, rect.y + m_paletteTileSize - 2f, rect.width - 4f, m_paletteLabelHeight);
            string tooltip = string.Empty;

            if(entry == null || entry.prefab == null)
            {
                GUI.Label(labelRect, "(missing)", m_paletteLabelStyle);
            }
            else
            {
                Texture2D preview = AssetPreview.GetAssetPreview(entry.prefab);

                if(preview == null)
                {
                    preview = AssetPreview.GetMiniThumbnail(entry.prefab);
                }

                if(preview != null)
                {
                    GUI.DrawTexture(imageRect, preview, ScaleMode.ScaleToFit);
                }

                GUI.Label(labelRect, entry.prefab.name, m_paletteLabelStyle);
                tooltip = entry.prefab.name + "\n" + AssetDatabase.GetAssetPath(entry.prefab);
            }

            GUI.Label(rect, new GUIContent(string.Empty, tooltip), GUIStyle.none);

            Event evt = Event.current;

            if(evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                if(selected)
                {
                    GridPaletteState.Select(-1);
                }
                else
                {
                    GridPaletteState.Select(index);
                }

                GUIUtility.keyboardControl = 0;
                EditorGUIUtility.editingTextField = false;
                evt.Use();
            }
        }

        private static GUIStyle m_keyStyle;
        private float m_shortcutKeyWidth = 96f;
        private static GUIStyle m_shortcutHeaderStyle;

        private void DrawShortcuts(bool selectMode, float width)
        {
            bool twoColumns = width >= 420f;
            float columnWidth = twoColumns ? (width - 24f) * 0.5f : width - 12f;
            m_shortcutKeyWidth = Mathf.Clamp(columnWidth * 0.35f, 56f, 96f);

            if(m_keyStyle == null)
            {
                m_keyStyle = new GUIStyle(EditorStyles.miniButton);
                m_keyStyle.alignment = TextAnchor.MiddleCenter;
                m_keyStyle.fontStyle = FontStyle.Bold;
                m_keyStyle.margin = new RectOffset(2, 6, 1, 1);
            }

            if(m_shortcutHeaderStyle == null)
            {
                m_shortcutHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                m_shortcutHeaderStyle.margin = new RectOffset(2, 2, 6, 1);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if(twoColumns)
            {
                GUILayout.BeginHorizontal();
            }

            GUILayout.BeginVertical(GUILayout.Width(columnWidth));

            if(selectMode)
            {
                DrawShortcutHeader("Selecting");
                DrawShortcutRow("Click", "Select the piece under the cursor");
                DrawShortcutRow("Drag", "Box select pieces on screen");
                DrawShortcutRow("Shift + Click", "Add or remove from selection");
                DrawShortcutRow("Esc", "Clear selection");

                DrawShortcutHeader("Editing selection");
                DrawShortcutRow(",", "Rotate selection left by step");
                DrawShortcutRow(".", "Rotate selection right by step");
                DrawShortcutRow("/", "Cycle rotation axis X / Y / Z");
                DrawShortcutRow("- / =", "Scale selection down / up");
                DrawShortcutRow("Del", "Delete selected pieces");
            }
            else
            {
                DrawShortcutHeader("Placing");
                DrawShortcutRow("Click / Drag", "Place the selected prefab");
                DrawShortcutRow("Shift + Drag", "Erase while held");
                DrawShortcutRow("X", "Toggle erase mode");
                DrawShortcutRow("S", "Toggle stacking on top");
                DrawShortcutRow("N", "Toggle snap to grid (freeform)");
                DrawShortcutRow("C", "Snap centres: bounds / faces");
                DrawShortcutRow("I", "Pick prefab under cursor");
                DrawShortcutRow("Esc", "Cancel current drag");

                DrawShortcutHeader("Rotation and scale");
                DrawShortcutRow(",", "Rotate left by step");
                DrawShortcutRow(".", "Rotate right by step");
                DrawShortcutRow("/", "Cycle rotation axis X / Y / Z");
                DrawShortcutRow("-", "Scale down by step");
                DrawShortcutRow("=", "Scale up by step");
            }

            DrawShortcutHeader("Camera");
            DrawShortcutRow("Alt + Drag", "Orbit (Unity default)");
            DrawShortcutRow("F", "Focus last piece or selection");
            DrawShortcutRow("Shift + F", "Focus whole grid");
            GUILayout.EndVertical();

            if(twoColumns)
            {
                GUILayout.Space(8f);
            }

            GUILayout.BeginVertical(GUILayout.Width(columnWidth));

            DrawShortcutHeader("Grid");
            DrawShortcutRow("[", "Layer down");
            DrawShortcutRow("]", "Layer up");
            DrawShortcutRow("G", "Cycle plane XZ / XY / YZ");

            DrawShortcutHeader("Modes");
            DrawShortcutRow("B", "Paint");
            DrawShortcutRow("M", "Rectangle");
            DrawShortcutRow("L", "Line");
            DrawShortcutRow("K", "Fill");
            DrawShortcutRow("U", "Select");
            GUILayout.EndVertical();

            if(twoColumns)
            {
                GUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawShortcutHeader(string title)
        {
            GUILayout.Label(title.ToUpperInvariant(), m_shortcutHeaderStyle);
        }

        private void DrawShortcutRow(string key, string action)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, m_keyStyle, GUILayout.Width(m_shortcutKeyWidth));
            GUILayout.Label(action, EditorStyles.label);
            GUILayout.EndHorizontal();
        }

        private void DrawInfoAndPreview(GridLevel level)
        {
            GameObject shown = null;
            string header;
            string title;
            string detail = string.Empty;

            if(m_mode == GridToolMode.Select)
            {
                header = "Selected";
                GameObject first = null;

                foreach(GameObject instance in m_selected)
                {
                    first = instance;
                    break;
                }

                if(first == null)
                {
                    title = "Nothing selected";
                }
                else
                {
                    PlacedCell piece = level.FindByInstance(first);
                    shown = PrefabUtility.GetCorrespondingObjectFromSource(first);

                    if(shown == null && piece != null)
                    {
                        shown = piece.prefab;
                    }

                    if(m_selected.Count == 1)
                    {
                        title = first.name;

                        if(piece != null)
                        {
                            detail = piece.coveredSize[level.uAxis] + " x " + piece.coveredSize[level.vAxis] + "   " + FormatRotation(level, GetPieceRotation(first));
                        }
                    }
                    else
                    {
                        title = m_selected.Count + " pieces";
                        detail = "first: " + first.name;
                    }
                }
            }
            else
            {
                header = IsErasing(Event.current) ? "Erasing" : "Drawing";
                PaletteEntry entry = GridPaletteState.selectedEntry;

                if(entry == null || entry.prefab == null)
                {
                    title = "No prefab selected";
                    detail = "Pick one in the palette";
                }
                else
                {
                    EnsureRolled(entry);
                    shown = m_nextPrefab;
                    title = m_nextPrefab != null ? m_nextPrefab.name : entry.prefab.name;
                    Vector2Int footprint = GetEntryFootprint(level, entry);
                    detail = footprint.x + " x " + footprint.y + "   " + FormatRotation(level, GetEffectiveRotation(level));

                    if(!Mathf.Approximately(GetDisplayedScale(), 1f))
                    {
                        detail += "   " + FormatScale(GetDisplayedScale());
                    }
                }
            }

            string cellText = "-";

            if(m_hasHoverCell)
            {
                cellText = m_hoverCell.x + ", " + m_hoverCell.y + ", " + m_hoverCell.z;
            }

            EnsurePreviewStyles();
            GUILayout.BeginHorizontal();

            string placementTag = string.Empty;

            if(m_mode != GridToolMode.Select)
            {
                placementTag = IsFreeform() ? " (FREEFORM)" : " (GRID)";
            }

            GUILayout.BeginVertical();
            GUILayout.Label(header.ToUpperInvariant() + placementTag + ": " + title, m_previewTitleStyle);
            GUILayout.Label(detail, m_previewTextStyle);
            GUILayout.Label("Plane " + level.plane + "   Layer " + level.layer, m_previewTextStyle);

            if(IsFreeform())
            {
                string positionText = m_hasFreePoint ? m_freePoint.x.ToString("0.##") + ", " + m_freePoint.y.ToString("0.##") + ", " + m_freePoint.z.ToString("0.##") : "-";
                GUILayout.Label("Pos " + positionText, m_previewTextStyle);
            }
            else
            {
                GUILayout.Label("Cell " + cellText, m_previewTextStyle);
            }

            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            Rect imageRect = GUILayoutUtility.GetRect(m_previewSize, m_previewSize, GUILayout.Width(m_previewSize), GUILayout.Height(m_previewSize));

            if(Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(imageRect, m_previewBackground);

                if(shown != null)
                {
                    Texture2D preview = AssetPreview.GetAssetPreview(shown);

                    if(preview == null)
                    {
                        preview = AssetPreview.GetMiniThumbnail(shown);

                        if(AssetPreview.IsLoadingAssetPreviews())
                        {
                            SceneView.RepaintAll();
                        }
                    }

                    if(preview != null)
                    {
                        GUI.DrawTexture(imageRect, preview, ScaleMode.ScaleToFit);
                    }
                }
            }

            GUILayout.EndHorizontal();
        }

        private static void EnsurePreviewStyles()
        {
            if(m_previewTitleStyle == null)
            {
                m_previewTitleStyle = new GUIStyle(EditorStyles.boldLabel);
                m_previewTitleStyle.clipping = TextClipping.Clip;
            }

            if(m_previewTextStyle == null)
            {
                m_previewTextStyle = new GUIStyle(EditorStyles.miniLabel);
                m_previewTextStyle.clipping = TextClipping.Clip;
            }
        }

        private static bool IsMissing(GameObject instance)
        {
            return instance == null;
        }

        private static bool IsDeleteCommand(Event evt)
        {
            return evt.commandName == "SoftDelete" || evt.commandName == "Delete";
        }

        private static bool IsFrameCommand(Event evt)
        {
            return evt.commandName == "FrameSelected" || evt.commandName == "FrameSelectedWithLock";
        }

        // F frames the pieces being worked on rather than the whole Grid Level object, which is what Unity
        // has selected while the tool is active. Shift+F still frames the whole grid.
        private void HandleFrameCommand(SceneView sceneView, GridLevel level, Event evt, bool execute)
        {
            if(evt.commandName == "FrameSelectedWithLock")
            {
                if(execute)
                {
                    sceneView.FrameSelected(false);
                }

                evt.Use();
                return;
            }

            Bounds bounds;

            if(!TryGetFocusBounds(level, out bounds))
            {
                return;
            }

            if(execute)
            {
                sceneView.Frame(bounds, false);
            }

            evt.Use();
        }

        private bool TryGetFocusBounds(GridLevel level, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            if(m_mode == GridToolMode.Select)
            {
                foreach(GameObject instance in m_selected)
                {
                    Bounds pieceBounds;

                    if(instance == null || !TryGetBounds(instance, out pieceBounds))
                    {
                        continue;
                    }

                    if(hasBounds)
                    {
                        bounds.Encapsulate(pieceBounds);
                    }
                    else
                    {
                        bounds = pieceBounds;
                        hasBounds = true;
                    }
                }
            }

            if(!hasBounds && m_lastPiece != null && level.FindByInstance(m_lastPiece) != null)
            {
                hasBounds = TryGetBounds(m_lastPiece, out bounds);
            }

            return hasBounds;
        }

        private void ApplySelection(GridLevel level)
        {
            if(!m_selectAdditive)
            {
                m_selected.Clear();
            }

            if((m_selectEndMouse - m_selectStartMouse).sqrMagnitude < 16f)
            {
                GameObject piece = PickPiece(level, m_selectStartMouse);

                if(piece == null)
                {
                    return;
                }

                if(m_selectAdditive && m_selected.Contains(piece))
                {
                    m_selected.Remove(piece);
                }
                else
                {
                    m_selected.Add(piece);
                    m_lastPiece = piece;
                }

                return;
            }

            Rect area = Rect.MinMaxRect(
                Mathf.Min(m_selectStartMouse.x, m_selectEndMouse.x),
                Mathf.Min(m_selectStartMouse.y, m_selectEndMouse.y),
                Mathf.Max(m_selectStartMouse.x, m_selectEndMouse.x),
                Mathf.Max(m_selectStartMouse.y, m_selectEndMouse.y));

            for(int i = 0; i < level.cells.Count; i++)
            {
                GameObject instance = level.cells[i].instance;

                if(instance == null)
                {
                    continue;
                }

                Bounds bounds;
                TryGetBounds(instance, out bounds);

                if(area.Contains(HandleUtility.WorldToGUIPoint(bounds.center)))
                {
                    m_selected.Add(instance);
                    m_lastPiece = instance;
                }
            }
        }

        private void DeleteSelected(GridLevel level)
        {
            if(m_selected.Count == 0)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Grid Delete Selection");
            int group = Undo.GetCurrentGroup();
            Undo.RecordObject(level, "Grid Delete Selection");

            List<GameObject> targets = new List<GameObject>(m_selected);

            for(int i = 0; i < targets.Count; i++)
            {
                PlacedCell piece = level.FindByInstance(targets[i]);

                if(piece != null)
                {
                    level.RemovePiece(piece);
                }

                if(targets[i] != null)
                {
                    Undo.DestroyObjectImmediate(targets[i]);
                }
            }

            m_selected.Clear();
            EditorUtility.SetDirty(level);
            Undo.CollapseUndoOperations(group);
        }

        // Each piece turns around its own centre; multi-cell pieces swap their footprint on quarter turns.
        private void RotateSelected(GridLevel level, float delta)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Grid Rotate Selection");
            int group = Undo.GetCurrentGroup();
            Undo.RecordObject(level, "Grid Rotate Selection");

            PrefabPalette palette = GridPaletteState.palette;
            int u = level.uAxis;
            int v = level.vAxis;
            int axisIndex = GetRotationAxis(level);
            bool aroundNormal = axisIndex == level.normalAxis;

            foreach(GameObject instance in m_selected)
            {
                PlacedCell piece = level.FindByInstance(instance);

                if(piece == null || instance == null)
                {
                    continue;
                }

                float currentYaw;
                bool upright = TryGetYaw(level, GetPieceRotation(instance), out currentYaw);

                if(!aroundNormal || !upright)
                {
                    TurnInPlace(level, piece, instance, AxisVector(axisIndex), delta);
                    continue;
                }

                if(piece.freeform)
                {
                    Undo.RecordObject(instance.transform, "Grid Rotate Selection");
                    instance.transform.localRotation = Quaternion.AngleAxis(delta, NormalVector(level)) * instance.transform.localRotation;
                    piece.rotation = Mathf.Repeat(currentYaw + delta, 360f);
                    piece.rotationSteps = GridLevel.QuarterTurns(piece.rotation);
                    continue;
                }

                Vector3 pivotOffset = Vector3.zero;

                if(palette != null)
                {
                    int index = palette.IndexOf(piece.prefab);

                    if(index >= 0)
                    {
                        pivotOffset = palette.entries[index].pivotOffset;
                    }
                }

                float oldDegrees = piece.rotationDegrees;
                float newDegrees = Mathf.Repeat(oldDegrees + delta, 360f);
                Vector3Int oldSize = piece.coveredSize;

                Vector2Int footprint = new Vector2Int(oldSize[u], oldSize[v]);

                if(GridLevel.QuarterTurns(oldDegrees) % 2 == 1)
                {
                    footprint = new Vector2Int(oldSize[v], oldSize[u]);
                }

                Vector3Int newSize = level.GetRegionSize(footprint, GridLevel.QuarterTurns(newDegrees));
                Vector3Int newMin = piece.cell;

                for(int axis = 0; axis < 3; axis++)
                {
                    newMin[axis] = Mathf.FloorToInt((2 * piece.cell[axis] + oldSize[axis] - newSize[axis]) / 2f);
                }

                Vector3 oldPosition;
                Quaternion oldRotation;
                level.GetRegionLocalPose(piece.cell, oldSize, oldDegrees, pivotOffset, out oldPosition, out oldRotation);

                Vector3 newPosition;
                Quaternion newRotation;
                level.GetRegionLocalPose(newMin, newSize, newDegrees, pivotOffset, out newPosition, out newRotation);

                Transform pieceTransform = instance.transform;
                Undo.RecordObject(pieceTransform, "Grid Rotate Selection");
                newPosition += pieceTransform.localPosition - oldPosition;
                pieceTransform.localPosition = newPosition;
                pieceTransform.localRotation = newRotation * (Quaternion.Inverse(oldRotation) * pieceTransform.localRotation);

                piece.cell = newMin;
                piece.size = newSize;
                piece.rotation = newDegrees;
                piece.rotationSteps = GridLevel.QuarterTurns(newDegrees);
            }

            level.InvalidateLookup();
            EditorUtility.SetDirty(level);
            Undo.CollapseUndoOperations(group);
        }

        private static bool TryGetLevelBounds(GridLevel level, GameObject instance, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            Matrix4x4 toLocal = level.transform.worldToLocalMatrix;
            bool hasBounds = false;

            for(int i = 0; i < renderers.Length; i++)
            {
                Bounds world = renderers[i].bounds;

                for(int corner = 0; corner < 8; corner++)
                {
                    Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = toLocal.MultiplyPoint3x4(world.center + Vector3.Scale(world.extents, sign));

                    if(hasBounds)
                    {
                        bounds.Encapsulate(point);
                    }
                    else
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                }
            }

            return hasBounds;
        }

        // Used for tilting: the piece turns around the centre of its bounds, is re-seated so its lowest
        // point stays at the same height, and a grid piece's cells are re-read from its new bounds.
        private void TurnInPlace(GridLevel level, PlacedCell piece, GameObject instance, Vector3 localAxis, float delta)
        {
            Transform pieceTransform = instance.transform;
            Undo.RecordObject(pieceTransform, "Grid Rotate Selection");

            Bounds before;
            bool hasBounds = TryGetLevelBounds(level, instance, out before);
            Vector3 pivot = hasBounds ? level.transform.TransformPoint(before.center) : pieceTransform.position;
            pieceTransform.RotateAround(pivot, level.transform.TransformDirection(localAxis), delta);

            Bounds after;

            if(hasBounds && TryGetLevelBounds(level, instance, out after))
            {
                float lift = before.min.y - after.min.y;
                pieceTransform.localPosition += Vector3.up * lift;
                after.center += Vector3.up * lift;

                if(!piece.freeform)
                {
                    int n = level.normalAxis;
                    Vector3Int minCell = piece.cell;
                    Vector3Int size = Vector3Int.one;

                    Vector3 shift = Vector3.zero;

                    for(int axis = 0; axis < 3; axis++)
                    {
                        if(axis == n)
                        {
                            continue;
                        }

                        float cell = level.cellSize[axis];

                        if(axis == 1)
                        {
                            // Height is already seated, so the covered cells are read straight from the bounds.
                            int low = Mathf.FloorToInt((after.min[axis] + 0.01f) / cell);
                            int high = Mathf.Max(low, Mathf.FloorToInt((after.max[axis] - 0.01f) / cell));
                            minCell[axis] = low;
                            size[axis] = high - low + 1;
                            continue;
                        }

                        // Sideways, the piece is nudged so its new shape lines up with whole cells again.
                        int count = Mathf.Max(1, Mathf.CeilToInt(after.size[axis] / cell - 0.01f));
                        int first = Mathf.RoundToInt(after.center[axis] / cell - count * 0.5f);
                        shift[axis] = (first + count * 0.5f) * cell - after.center[axis];
                        minCell[axis] = first;
                        size[axis] = count;
                    }

                    pieceTransform.localPosition += shift;
                    piece.cell = minCell;
                    piece.size = size;
                }
            }

            float yaw;
            TryGetYaw(level, GetPieceRotation(instance), out yaw);
            piece.rotation = yaw;
            piece.rotationSteps = GridLevel.QuarterTurns(yaw);
        }

        private void DrawSelectMode(GridLevel level)
        {
            DrawSelectionHighlights();

            if(m_dragging)
            {
                Rect area = Rect.MinMaxRect(
                    Mathf.Min(m_selectStartMouse.x, m_selectEndMouse.x),
                    Mathf.Min(m_selectStartMouse.y, m_selectEndMouse.y),
                    Mathf.Max(m_selectStartMouse.x, m_selectEndMouse.x),
                    Mathf.Max(m_selectStartMouse.y, m_selectEndMouse.y));

                Handles.BeginGUI();
                EditorGUI.DrawRect(area, m_selectFill);
                EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, 1f), m_selectOutline);
                EditorGUI.DrawRect(new Rect(area.x, area.yMax - 1f, area.width, 1f), m_selectOutline);
                EditorGUI.DrawRect(new Rect(area.x, area.y, 1f, area.height), m_selectOutline);
                EditorGUI.DrawRect(new Rect(area.xMax - 1f, area.y, 1f, area.height), m_selectOutline);
                Handles.EndGUI();
                return;
            }

            if(m_hoverPiece != null && !m_selected.Contains(m_hoverPiece))
            {
                DrawPieceBox(m_hoverPiece, new Color(m_selectOutline.r, m_selectOutline.g, m_selectOutline.b, 0.55f));
            }
        }

        private void DrawSelectionHighlights()
        {
            if(m_selected.Count == 0)
            {
                return;
            }

            if(m_highlightMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");

                if(shader == null)
                {
                    return;
                }

                m_highlightMaterial = new Material(shader);
                m_highlightMaterial.hideFlags = HideFlags.HideAndDontSave;
                m_highlightMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m_highlightMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m_highlightMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
                m_highlightMaterial.SetInt("_ZWrite", 0);
                m_highlightMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                m_highlightMaterial.SetFloat("_ZBias", -2f);
            }

            m_highlightMaterial.SetColor("_Color", m_selectTint);

            foreach(GameObject instance in m_selected)
            {
                if(instance == null)
                {
                    continue;
                }

                m_highlightMaterial.SetPass(0);
                MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>();

                for(int i = 0; i < filters.Length; i++)
                {
                    Mesh mesh = filters[i].sharedMesh;

                    if(mesh == null)
                    {
                        continue;
                    }

                    for(int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        Graphics.DrawMeshNow(mesh, filters[i].transform.localToWorldMatrix, sub);
                    }
                }
            }

            Color previousColor = Handles.color;
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            Handles.color = m_selectOutline;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            foreach(GameObject instance in m_selected)
            {
                if(instance == null)
                {
                    continue;
                }

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();

                if(renderers.Length == 0)
                {
                    continue;
                }

                Bounds bounds = renderers[0].bounds;

                for(int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                DrawThickBox(bounds.center, bounds.extents * 1.02f, 4f);
            }

            Handles.zTest = previousZTest;
            Handles.color = previousColor;
        }

        private void DrawThickBox(Vector3 center, Vector3 extents, float width)
        {
            for(int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                m_boxCorners[corner] = center + Vector3.Scale(extents, sign);
            }

            for(int a = 0; a < 8; a++)
            {
                for(int bit = 1; bit < 8; bit <<= 1)
                {
                    if((a & bit) != 0)
                    {
                        continue;
                    }

                    m_edgePoints[0] = m_boxCorners[a];
                    m_edgePoints[1] = m_boxCorners[a | bit];
                    Handles.DrawAAPolyLine(width, m_edgePoints);
                }
            }
        }
    }
}
