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
        private float m_rotationDegrees;
        private float m_stepDegrees = 90f;
        private bool m_stack;
        private bool m_stackKeyHeld;
        private bool m_eraseToggle;
        private bool m_eraseKeyHeld;

        private bool m_dragging;
        private bool m_erasing;
        private bool m_selectAdditive;
        private readonly HashSet<GameObject> m_selected = new HashSet<GameObject>();
        private Vector3Int m_startCell;
        private Vector3Int m_endCell;
        private int m_undoGroup;

        private PaletteEntry m_rolledEntry;
        private GameObject m_nextPrefab;
        private int m_nextRandomSteps;

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
                UpdateHover(level, evt);
                sceneView.Repaint();
            }
            else if(type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                UpdateHover(level, evt);

                if(m_hasHoverCell)
                {
                    BeginStroke(level, evt, controlId);
                }
            }
            else if(type == EventType.MouseDrag)
            {
                UpdateHover(level, evt);

                if(GUIUtility.hotControl == controlId)
                {
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
            else if(type == EventType.KeyUp && evt.keyCode == KeyCode.S)
            {
                m_stackKeyHeld = false;
            }
            else if(type == EventType.KeyUp && evt.keyCode == KeyCode.D)
            {
                m_eraseKeyHeld = false;
            }
            else if(type == EventType.Repaint && m_mode == GridToolMode.Select)
            {
                DrawSelectMode(level);
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

        private bool IsErasing(Event evt)
        {
            return m_eraseToggle || evt.shift;
        }

        private void UpdateHover(GridLevel level, Event evt)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            m_hasHoverCell = level.RaycastCell(ray, out m_hoverCell);
        }

        private void EnsureRolled(PaletteEntry entry)
        {
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

            if(entry.randomRotation)
            {
                Vector2Int footprint = entry.GetFootprint();

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

        private float GetEffectiveDegrees()
        {
            return Mathf.Repeat(m_rotationDegrees + m_nextRandomSteps * 90f, 360f);
        }

        private void RotateBy(GridLevel level, float delta)
        {
            if(m_mode == GridToolMode.Select && m_selected.Count > 0)
            {
                RotateSelected(level, delta);
            }
            else
            {
                m_rotationDegrees = Mathf.Repeat(m_rotationDegrees + delta, 360f);
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
            return level.GetRegionSize(entry.GetFootprint(), GridLevel.QuarterTurns(GetEffectiveDegrees()));
        }

        private void BeginStroke(GridLevel level, Event evt, int controlId)
        {
            m_startCell = m_hoverCell;
            m_endCell = m_hoverCell;

            if(m_mode == GridToolMode.Select)
            {
                m_selectAdditive = evt.shift;
                m_dragging = true;
                GUIUtility.hotControl = controlId;
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

            float degrees = GetEffectiveDegrees();
            Vector3Int size = level.GetRegionSize(entry.GetFootprint(), GridLevel.QuarterTurns(degrees));
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
            level.GetRegionLocalPose(minCell, size, degrees, entry.pivotOffset, out localPosition, out localRotation);
            localPosition.y += level.GetStackHeight(minCell, size);

            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation * prefab.transform.localRotation;
            Undo.RegisterCreatedObjectUndo(instance, "Grid Place");

            PlacedCell placed = new PlacedCell();
            placed.cell = minCell;
            placed.size = size;
            placed.instance = instance;
            placed.prefab = entry.prefab;
            placed.rotationSteps = GridLevel.QuarterTurns(degrees);
            placed.rotation = degrees;
            level.AddCell(placed);

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
            if(!m_hasHoverCell)
            {
                return;
            }

            PlacedCell piece = level.GetCell(m_hoverCell);
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
                entry.footprint = GridFootprintUtility.Compute(piece.prefab, level.cellSize);
                palette.entries.Add(entry);
                EditorUtility.SetDirty(palette);
                index = palette.entries.Count - 1;
            }

            m_rotationDegrees = piece.rotationDegrees;
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
            int half = level.halfExtent;
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

                if(u < -half || u >= half || v < -half || v >= half)
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

        private void HandleKey(GridLevel level, Event evt)
        {
            if(evt.control || evt.command || EditorGUIUtility.editingTextField)
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
            else if(evt.keyCode == KeyCode.D)
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
            level.GetRegionLocalPose(minCell, size, GetEffectiveDegrees(), entry.pivotOffset, out localPosition, out localRotation);
            localPosition.y += level.GetStackHeight(minCell, size);

            Transform prefabRoot = prefab.transform;
            Matrix4x4 rootMatrix = level.transform.localToWorldMatrix * Matrix4x4.TRS(localPosition, localRotation * prefabRoot.localRotation, prefabRoot.localScale);

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
            Handles.Label(labelPosition, FormatDegrees(GetEffectiveDegrees()), GetAngleLabelStyle());
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
            DrawToggle(ref m_eraseToggle, m_eraseToggle ? "Erase: ON (D)" : "Erase: OFF (D)", new Color(1f, 0.35f, 0.3f, 1f));
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();

            if(GUILayout.Button(new GUIContent("Rotate Left", "Rotate by -step (,)"), EditorStyles.miniButtonLeft))
            {
                RotateBy(level, -m_stepDegrees);
            }

            string angleText = "Next " + FormatDegrees(GetEffectiveDegrees());

            if(selectMode && m_selected.Count > 0)
            {
                angleText = "Selection";
            }

            GUILayout.Label(angleText, EditorStyles.miniButtonMid, GUILayout.Width(80f));

            if(GUILayout.Button(new GUIContent("Rotate Right", "Rotate by +step (.)"), EditorStyles.miniButtonRight))
            {
                RotateBy(level, m_stepDegrees);
            }

            GUILayout.EndHorizontal();

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 70f;
            m_stepDegrees = Mathf.Clamp(EditorGUILayout.FloatField("Step (deg)", m_stepDegrees), 0.1f, 360f);
            EditorGUIUtility.labelWidth = previousLabelWidth;

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


            GUILayout.EndVertical();

            if(EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
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
                DrawShortcutRow("Click", "Select the top piece in a cell");
                DrawShortcutRow("Drag", "Box select everything in the area");
                DrawShortcutRow("Shift + Click", "Add or remove from selection");
                DrawShortcutRow("Esc", "Clear selection");

                DrawShortcutHeader("Editing selection");
                DrawShortcutRow(",", "Rotate selection left by step");
                DrawShortcutRow(".", "Rotate selection right by step");
                DrawShortcutRow("Del", "Delete selected pieces");
            }
            else
            {
                DrawShortcutHeader("Placing");
                DrawShortcutRow("Click / Drag", "Place the selected prefab");
                DrawShortcutRow("Shift + Drag", "Erase while held");
                DrawShortcutRow("D", "Toggle erase mode");
                DrawShortcutRow("S", "Toggle stacking on top");
                DrawShortcutRow("I", "Pick prefab under cursor");
                DrawShortcutRow("Esc", "Cancel current drag");

                DrawShortcutHeader("Rotation");
                DrawShortcutRow(",", "Rotate left by step");
                DrawShortcutRow(".", "Rotate right by step");
            }

            DrawShortcutHeader("Camera");
            DrawShortcutRow("Alt + Drag", "Orbit (Unity default)");
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
                            detail = piece.coveredSize[level.uAxis] + " x " + piece.coveredSize[level.vAxis] + "   " + FormatDegrees(piece.rotationDegrees);
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
                    Vector2Int footprint = entry.GetFootprint();
                    detail = footprint.x + " x " + footprint.y + "   " + FormatDegrees(GetEffectiveDegrees());
                }
            }

            string cellText = "-";

            if(m_hasHoverCell)
            {
                cellText = m_hoverCell.x + ", " + m_hoverCell.y + ", " + m_hoverCell.z;
            }

            EnsurePreviewStyles();
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical();
            GUILayout.Label(header.ToUpperInvariant() + ": " + title, m_previewTitleStyle);
            GUILayout.Label(detail, m_previewTextStyle);
            GUILayout.Label("Plane " + level.plane + "   Layer " + level.layer, m_previewTextStyle);
            GUILayout.Label("Cell " + cellText, m_previewTextStyle);
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

        private void ApplySelection(GridLevel level)
        {
            if(!m_selectAdditive)
            {
                m_selected.Clear();
            }

            if(m_startCell == m_endCell)
            {
                PlacedCell piece = level.GetCell(m_startCell);

                if(piece == null)
                {
                    return;
                }

                if(m_selectAdditive && m_selected.Contains(piece.instance))
                {
                    m_selected.Remove(piece.instance);
                }
                else
                {
                    m_selected.Add(piece.instance);
                }

                return;
            }

            CollectRectangle(level, m_startCell, m_endCell, m_cellBuffer);
            HashSet<Vector3Int> area = new HashSet<Vector3Int>(m_cellBuffer);

            for(int i = 0; i < level.cells.Count; i++)
            {
                PlacedCell placed = level.cells[i];

                if(placed.instance == null)
                {
                    continue;
                }

                GridLevel.GetCoveredCells(placed.cell, placed.coveredSize, m_coveredBuffer);

                for(int c = 0; c < m_coveredBuffer.Count; c++)
                {
                    if(area.Contains(m_coveredBuffer[c]))
                    {
                        m_selected.Add(placed.instance);
                        break;
                    }
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

            foreach(GameObject instance in m_selected)
            {
                PlacedCell piece = level.FindByInstance(instance);

                if(piece == null || instance == null)
                {
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
                newPosition.y += pieceTransform.localPosition.y - oldPosition.y;
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

        private void DrawSelectMode(GridLevel level)
        {
            DrawSelectionHighlights();

            if(m_dragging)
            {
                Vector3Int minCell = Vector3Int.Min(m_startCell, m_endCell);
                Vector3Int maxCell = Vector3Int.Max(m_startCell, m_endCell);
                DrawQuad(level, minCell, maxCell, m_selectFill, m_selectOutline);
                return;
            }

            if(!m_hasHoverCell)
            {
                return;
            }

            PlacedCell piece = level.GetCell(m_hoverCell);

            if(piece != null)
            {
                DrawQuad(level, piece.cell, piece.cell + piece.coveredSize - Vector3Int.one, m_selectFill, m_selectOutline);
            }
            else
            {
                DrawQuad(level, m_hoverCell, m_hoverCell, m_freeFill, m_freeOutline);
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
