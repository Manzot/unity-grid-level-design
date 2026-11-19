using UnityEditor;
using UnityEngine;

namespace DefaceGames.GridLevelDesign.Editor
{
    public static class GridLevelGizmo
    {
        private static Vector3[] m_linePoints = new Vector3[0];
        private static readonly Vector3[] m_axisPoints = new Vector3[4];
        private static int m_levelCount = -1;

        [InitializeOnLoadMethod]
        private static void WatchHierarchy()
        {
            EditorApplication.hierarchyChanged += ResetLevelCount;
        }

        private static void ResetLevelCount()
        {
            m_levelCount = -1;
        }

        // A scene with several grids only shows the one being worked on: the selected grid, or the grid
        // that owns the selected piece. A lone grid is always shown.
        private static bool ShouldDraw(GridLevel level)
        {
            Transform active = Selection.activeTransform;

            if(Selection.Contains(level.gameObject) || (active != null && active.IsChildOf(level.transform)))
            {
                return true;
            }

            if(m_levelCount < 0)
            {
                m_levelCount = Object.FindObjectsByType<GridLevel>(FindObjectsInactive.Exclude).Length;
            }

            return m_levelCount <= 1;
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawGrid(GridLevel level, GizmoType gizmoType)
        {
            if(!ShouldDraw(level))
            {
                return;
            }

            int n = level.normalAxis;
            int u = level.uAxis;
            int v = level.vAxis;
            int min = level.gridMin;
            int max = level.gridMax;
            int lineCount = (level.gridSize + 1) * 2;

            if(m_linePoints.Length != lineCount * 2)
            {
                m_linePoints = new Vector3[lineCount * 2];
            }

            float offset = level.layerOffset;
            float uSize = level.cellSize[u];
            float vSize = level.cellSize[v];
            float uMin = min * uSize;
            float vMin = min * vSize;
            float uMax = max * uSize;
            float vMax = max * vSize;
            int index = 0;

            for(int i = min; i <= max; i++)
            {
                m_linePoints[index++] = MakePoint(n, u, v, offset, i * uSize, vMin);
                m_linePoints[index++] = MakePoint(n, u, v, offset, i * uSize, vMax);
                m_linePoints[index++] = MakePoint(n, u, v, offset, uMin, i * vSize);
                m_linePoints[index++] = MakePoint(n, u, v, offset, uMax, i * vSize);
            }

            m_axisPoints[0] = MakePoint(n, u, v, offset, 0f, vMin);
            m_axisPoints[1] = MakePoint(n, u, v, offset, 0f, vMax);
            m_axisPoints[2] = MakePoint(n, u, v, offset, uMin, 0f);
            m_axisPoints[3] = MakePoint(n, u, v, offset, uMax, 0f);

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = level.transform.localToWorldMatrix;
            Gizmos.color = level.gridColor;
            Gizmos.DrawLineList(m_linePoints);

            Color axisColor = level.gridColor;
            axisColor.a = 1f;
            Gizmos.color = axisColor;
            Gizmos.DrawLineList(m_axisPoints);

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private static Vector3 MakePoint(int n, int u, int v, float offset, float uValue, float vValue)
        {
            Vector3 point = Vector3.zero;
            point[n] = offset;
            point[u] = uValue;
            point[v] = vValue;
            return point;
        }
    }
}
