using System;
using System.Collections.Generic;
using UnityEngine;

namespace DefaceGames.GridLevelDesign
{
    public enum GridPlane
    {
        XZ,
        XY,
        YZ
    }

    [Serializable]
    public class PlacedCell
    {
        public Vector3Int cell;
        public Vector3Int size;
        public GameObject instance;
        public GameObject prefab;
        public int rotationSteps;
        public float rotation;
        public bool freeform;

        public float rotationDegrees
        {
            get
            {
                if(rotation == 0f && rotationSteps != 0)
                {
                    return rotationSteps * 90f;
                }

                return rotation;
            }
        }

        public Vector3Int coveredSize
        {
            get
            {
                return Vector3Int.Max(size, Vector3Int.one);
            }
        }
    }

    [DisallowMultipleComponent]
    public class GridLevel : MonoBehaviour
    {
        public Vector3 cellSize = Vector3.one;
        public GridPlane plane = GridPlane.XZ;
        public int layer;
        public int halfExtent = 20;
        public Color gridColor = new Color(1f, 1f, 1f, 0.35f);

        [HideInInspector]
        public List<PlacedCell> cells = new List<PlacedCell>();

        [HideInInspector]
        public GridLevelData linkedData;

        private Dictionary<Vector3Int, List<PlacedCell>> m_lookup;
        private readonly List<Vector3Int> m_coveredBuffer = new List<Vector3Int>();
        private readonly HashSet<PlacedCell> m_pieceBuffer = new HashSet<PlacedCell>();

        public int normalAxis
        {
            get
            {
                if(plane == GridPlane.XZ)
                {
                    return 1;
                }

                if(plane == GridPlane.XY)
                {
                    return 2;
                }

                return 0;
            }
        }

        public int uAxis
        {
            get
            {
                if(plane == GridPlane.YZ)
                {
                    return 2;
                }

                return 0;
            }
        }

        public int vAxis
        {
            get
            {
                if(plane == GridPlane.XZ)
                {
                    return 2;
                }

                return 1;
            }
        }

        public float layerOffset
        {
            get
            {
                return layer * cellSize[normalAxis];
            }
        }

        public Vector3 CellToLocal(Vector3Int cell)
        {
            return Vector3.Scale(cell, cellSize);
        }

        public Vector3Int GetRegionSize(Vector2Int footprint, int rotationSteps)
        {
            Vector3Int size = Vector3Int.one;
            int fu = Mathf.Max(1, footprint.x);
            int fv = Mathf.Max(1, footprint.y);

            if(rotationSteps % 2 == 1)
            {
                int swap = fu;
                fu = fv;
                fv = swap;
            }

            size[uAxis] = fu;
            size[vAxis] = fv;
            return size;
        }

        // The prefab pivot sits at the bottom centre of the covered region, and rotation turns it
        // around the region centre so a rotated piece never leaves its own cells.
        public static int QuarterTurns(float degrees)
        {
            int quarters = Mathf.RoundToInt(degrees / 90f) % 4;

            if(quarters < 0)
            {
                quarters += 4;
            }

            return quarters;
        }

        public void GetRegionLocalPose(Vector3Int minCell, Vector3Int size, float rotationDegrees, Vector3 pivotOffset, out Vector3 localPosition, out Quaternion localRotation)
        {
            Vector3 center = Vector3.Scale((Vector3)minCell + (Vector3)size * 0.5f, cellSize);
            Vector3 axis = Vector3.zero;
            axis[normalAxis] = 1f;

            localRotation = Quaternion.AngleAxis(rotationDegrees, axis);
            Vector3 baseOffset = new Vector3(0f, -cellSize.y * 0.5f, 0f) + pivotOffset;
            localPosition = center + localRotation * baseOffset;
        }

        public static void GetCoveredCells(Vector3Int minCell, Vector3Int size, List<Vector3Int> results)
        {
            results.Clear();

            for(int x = 0; x < size.x; x++)
            {
                for(int y = 0; y < size.y; y++)
                {
                    for(int z = 0; z < size.z; z++)
                    {
                        results.Add(new Vector3Int(minCell.x + x, minCell.y + y, minCell.z + z));
                    }
                }
            }
        }

        public PlacedCell GetCell(Vector3Int cell)
        {
            List<PlacedCell> stack;

            if(GetLookup().TryGetValue(cell, out stack))
            {
                for(int i = stack.Count - 1; i >= 0; i--)
                {
                    if(stack[i].instance != null)
                    {
                        return stack[i];
                    }
                }
            }

            return null;
        }

        public bool IsRegionFree(Vector3Int minCell, Vector3Int size)
        {
            GetCoveredCells(minCell, size, m_coveredBuffer);

            for(int i = 0; i < m_coveredBuffer.Count; i++)
            {
                if(GetCell(m_coveredBuffer[i]) != null)
                {
                    return false;
                }
            }

            return true;
        }

        public PlacedCell FindByInstance(GameObject instance)
        {
            if(instance == null)
            {
                return null;
            }

            for(int i = 0; i < cells.Count; i++)
            {
                if(cells[i].instance == instance)
                {
                    return cells[i];
                }
            }

            return null;
        }

        public void AddCell(PlacedCell placed)
        {
            cells.Add(placed);
            m_lookup = null;
        }

        public void RemovePiece(PlacedCell piece)
        {
            cells.Remove(piece);
            cells.RemoveAll(IsStale);
            m_lookup = null;
        }

        private static bool IsStale(PlacedCell placed)
        {
            return placed.instance == null;
        }

        // Height of everything already in the region, measured from the cell base along local Y,
        // so the next piece can rest on top of it.
        public float GetStackHeight(Vector3Int minCell, Vector3Int size)
        {
            Dictionary<Vector3Int, List<PlacedCell>> lookup = GetLookup();
            GetCoveredCells(minCell, size, m_coveredBuffer);
            m_pieceBuffer.Clear();

            for(int i = 0; i < m_coveredBuffer.Count; i++)
            {
                List<PlacedCell> stack;

                if(lookup.TryGetValue(m_coveredBuffer[i], out stack))
                {
                    for(int s = 0; s < stack.Count; s++)
                    {
                        if(stack[s].instance != null)
                        {
                            m_pieceBuffer.Add(stack[s]);
                        }
                    }
                }
            }

            float baseY = minCell.y * cellSize.y;
            float top = baseY;
            Matrix4x4 toLocal = transform.worldToLocalMatrix;

            foreach(PlacedCell piece in m_pieceBuffer)
            {
                Renderer[] renderers = piece.instance.GetComponentsInChildren<Renderer>();

                for(int r = 0; r < renderers.Length; r++)
                {
                    Bounds bounds = renderers[r].bounds;

                    for(int corner = 0; corner < 8; corner++)
                    {
                        Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                        Vector3 point = bounds.center + Vector3.Scale(bounds.extents, sign);
                        top = Mathf.Max(top, toLocal.MultiplyPoint3x4(point).y);
                    }
                }
            }

            return top - baseY;
        }

        public void InvalidateLookup()
        {
            m_lookup = null;
        }

        private Dictionary<Vector3Int, List<PlacedCell>> GetLookup()
        {
            if(m_lookup == null)
            {
                m_lookup = new Dictionary<Vector3Int, List<PlacedCell>>();
                List<Vector3Int> covered = new List<Vector3Int>();

                for(int i = 0; i < cells.Count; i++)
                {
                    // Freeform pieces sit off the grid, so they never occupy or block cells.
                    if(cells[i].freeform)
                    {
                        continue;
                    }

                    GetCoveredCells(cells[i].cell, cells[i].coveredSize, covered);

                    for(int c = 0; c < covered.Count; c++)
                    {
                        List<PlacedCell> stack;

                        if(!m_lookup.TryGetValue(covered[c], out stack))
                        {
                            stack = new List<PlacedCell>();
                            m_lookup[covered[c]] = stack;
                        }

                        stack.Add(cells[i]);
                    }
                }
            }

            return m_lookup;
        }

        public bool RaycastPlane(Ray worldRay, out Vector3 localPoint)
        {
            localPoint = Vector3.zero;
            int n = normalAxis;
            Vector3 origin = transform.InverseTransformPoint(worldRay.origin);
            Vector3 direction = transform.InverseTransformVector(worldRay.direction);

            if(Mathf.Abs(direction[n]) < 0.000001f)
            {
                return false;
            }

            float t = (layerOffset - origin[n]) / direction[n];

            if(t < 0f)
            {
                return false;
            }

            localPoint = origin + direction * t;
            return true;
        }

        public bool RaycastCell(Ray worldRay, out Vector3Int cell)
        {
            cell = Vector3Int.zero;

            int n = normalAxis;
            int u = uAxis;
            int v = vAxis;

            Vector3 origin = transform.InverseTransformPoint(worldRay.origin);
            Vector3 direction = transform.InverseTransformVector(worldRay.direction);

            if(Mathf.Abs(direction[n]) < 0.000001f)
            {
                return false;
            }

            float t = (layerOffset - origin[n]) / direction[n];

            if(t < 0f)
            {
                return false;
            }

            Vector3 hit = origin + direction * t;

            cell[u] = Mathf.FloorToInt(hit[u] / cellSize[u]);
            cell[v] = Mathf.FloorToInt(hit[v] / cellSize[v]);
            cell[n] = layer;
            return true;
        }

        private void OnValidate()
        {
            cellSize = Vector3.Max(cellSize, Vector3.one * 0.01f);
            halfExtent = Mathf.Max(1, halfExtent);
            InvalidateLookup();
        }
    }
}
