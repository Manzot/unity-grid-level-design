using UnityEditor;
using UnityEngine;

namespace DefaceGames.GridLevelDesign.Editor
{
    public static class GridFootprintUtility
    {
        public static Vector2Int Compute(GameObject prefab, Vector3 cellSize)
        {
            return Compute(prefab, cellSize, Vector3.one);
        }

        public static Vector2Int Compute(GameObject prefab, Vector3 cellSize, Vector3 scale)
        {
            return Compute(prefab, cellSize, scale, 0, 2);
        }

        // Measures the prefab along the two axes the grid's plane lays out (u and v), so a vertical
        // XY grid counts width and height rather than width and depth.
        public static Vector2Int Compute(GameObject prefab, Vector3 cellSize, Vector3 scale, int uAxis, int vAxis)
        {
            Bounds total;

            if(!TryGetPlacedBounds(prefab, out total))
            {
                return Vector2Int.one;
            }

            int x = Mathf.Max(1, Mathf.CeilToInt(total.size[uAxis] * Mathf.Abs(scale[uAxis]) / cellSize[uAxis] - 0.01f));
            int y = Mathf.Max(1, Mathf.CeilToInt(total.size[vAxis] * Mathf.Abs(scale[vAxis]) / cellSize[vAxis] - 0.01f));
            return new Vector2Int(x, y);
        }

        // Bounds of the prefab's meshes as they appear when placed unrotated: the root's own rotation and
        // scale are applied, its position is not.
        public static bool TryGetPlacedBounds(GameObject prefab, out Bounds total)
        {
            total = new Bounds();

            if(prefab == null)
            {
                return false;
            }

            Transform root = prefab.transform;
            Matrix4x4 placed = Matrix4x4.TRS(Vector3.zero, root.localRotation, root.localScale) * root.worldToLocalMatrix;
            bool hasBounds = false;

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);

            for(int i = 0; i < filters.Length; i++)
            {
                if(filters[i].sharedMesh != null)
                {
                    Encapsulate(ref total, ref hasBounds, filters[i].sharedMesh.bounds, placed * filters[i].transform.localToWorldMatrix);
                }
            }

            SkinnedMeshRenderer[] skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            for(int i = 0; i < skinned.Length; i++)
            {
                if(skinned[i].sharedMesh != null)
                {
                    Encapsulate(ref total, ref hasBounds, skinned[i].sharedMesh.bounds, placed * skinned[i].transform.localToWorldMatrix);
                }
            }

            return hasBounds;
        }

        // Uniform factor that makes the prefab's larger side on the grid's plane exactly fill one cell.
        public static float GetFitFactor(GridLevel level, GameObject prefab)
        {
            Bounds bounds;

            if(level == null || !TryGetPlacedBounds(prefab, out bounds))
            {
                return 1f;
            }

            int u = level.uAxis;
            int v = level.vAxis;
            float factorU = bounds.size[u] > 0.0001f ? level.cellSize[u] / bounds.size[u] : float.MaxValue;
            float factorV = bounds.size[v] > 0.0001f ? level.cellSize[v] / bounds.size[v] : float.MaxValue;
            float factor = Mathf.Min(factorU, factorV);

            if(factor == float.MaxValue || factor <= 0f)
            {
                return 1f;
            }

            return factor;
        }

        public static Vector2Int ComputeFor(GridLevel level, GameObject prefab, Vector3 scale)
        {
            if(level == null)
            {
                return Compute(prefab, Vector3.one, scale, 0, 2);
            }

            return Compute(prefab, level.cellSize, scale, level.uAxis, level.vAxis);
        }

        public static Vector3 FindCellSize()
        {
            GridLevel level = FindLevel();

            if(level == null)
            {
                return Vector3.one;
            }

            return level.cellSize;
        }

        public static GridLevel FindLevel()
        {
            GridLevel level = null;

            if(Selection.activeGameObject != null)
            {
                level = Selection.activeGameObject.GetComponentInParent<GridLevel>();
            }

            if(level == null)
            {
                level = Object.FindAnyObjectByType<GridLevel>();
            }

            return level;
        }

        private static void Encapsulate(ref Bounds total, ref bool hasBounds, Bounds meshBounds, Matrix4x4 matrix)
        {
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

        public static string DescribeFootprint(PaletteEntry entry)
        {
            if(entry.manualFootprint)
            {
                return "Footprint: " + entry.GetFootprint().x + " x " + entry.GetFootprint().y + " cells (manual)";
            }

            GridLevel currentLevel = FindLevel();
            Vector3 entryScale = entry.GetScale();

            if(entry.fitToCell && currentLevel != null)
            {
                entryScale = Vector3.one * GetFitFactor(currentLevel, entry.prefab);
            }

            Vector2Int live = ComputeFor(currentLevel, entry.prefab, entryScale);
            string text = "Footprint: " + live.x + " x " + live.y + " cells (automatic, from the current grid). Tick Manual Footprint to set it yourself.";

            if(entry.fitToCell)
            {
                text += "\nFit to Cell is on: scaled " + entryScale.x.ToString("0.##") + "x to fill one cell; the Scale field is ignored.";
            }

            return text;
        }

        public static void FillManualFootprint(PrefabPalette palette, int index)
        {
            if(palette == null || index < 0 || index >= palette.entries.Count)
            {
                return;
            }

            Undo.RecordObject(palette, "Auto Footprint");
            palette.entries[index].footprint = ComputeFor(FindLevel(), palette.entries[index].prefab, palette.entries[index].GetScale());
            palette.entries[index].manualFootprint = true;
            EditorUtility.SetDirty(palette);
            GridPaletteState.NotifyChanged();
        }
    }
}
