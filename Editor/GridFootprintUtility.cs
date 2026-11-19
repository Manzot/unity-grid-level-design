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
            if(prefab == null)
            {
                return Vector2Int.one;
            }

            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
            Bounds total = new Bounds();
            bool hasBounds = false;

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);

            for(int i = 0; i < filters.Length; i++)
            {
                if(filters[i].sharedMesh != null)
                {
                    Encapsulate(ref total, ref hasBounds, filters[i].sharedMesh.bounds, rootInverse * filters[i].transform.localToWorldMatrix);
                }
            }

            SkinnedMeshRenderer[] skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            for(int i = 0; i < skinned.Length; i++)
            {
                if(skinned[i].sharedMesh != null)
                {
                    Encapsulate(ref total, ref hasBounds, skinned[i].sharedMesh.bounds, rootInverse * skinned[i].transform.localToWorldMatrix);
                }
            }

            if(!hasBounds)
            {
                return Vector2Int.one;
            }

            int x = Mathf.Max(1, Mathf.CeilToInt(total.size.x * Mathf.Abs(scale.x) / cellSize.x - 0.01f));
            int y = Mathf.Max(1, Mathf.CeilToInt(total.size.z * Mathf.Abs(scale.z) / cellSize.z - 0.01f));
            return new Vector2Int(x, y);
        }

        public static Vector3 FindCellSize()
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

            if(level == null)
            {
                return Vector3.one;
            }

            return level.cellSize;
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
    }
}
