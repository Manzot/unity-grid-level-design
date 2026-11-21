using System;
using System.Collections.Generic;
using UnityEngine;

namespace DefaceGames.GridLevelDesign
{
    [Serializable]
    public class PaletteEntry
    {
        public GameObject prefab;
        public bool manualFootprint;
        public Vector2Int footprint = Vector2Int.one;
        public Vector3 pivotOffset;
        public Vector3 scale = Vector3.one;
        public bool fitToCell;
        public bool randomRotation;
        public Vector3 randomPosition;
        public List<GameObject> variants = new List<GameObject>();

        public Vector2Int GetFootprint()
        {
            return Vector2Int.Max(footprint, Vector2Int.one);
        }

        // Entries saved before the scale field existed load it as zero, which would hide the piece.
        public Vector3 GetScale()
        {
            if(Mathf.Abs(scale.x) < 0.0001f || Mathf.Abs(scale.y) < 0.0001f || Mathf.Abs(scale.z) < 0.0001f)
            {
                return Vector3.one;
            }

            return scale;
        }
    }

    [CreateAssetMenu(fileName = "PrefabPalette", menuName = "Grid Level/Prefab Palette")]
    public class PrefabPalette : ScriptableObject
    {
        public List<PaletteEntry> entries = new List<PaletteEntry>();

        public bool Contains(GameObject prefab)
        {
            return IndexOf(prefab) >= 0;
        }

        public int IndexOf(GameObject prefab)
        {
            for(int i = 0; i < entries.Count; i++)
            {
                if(entries[i].prefab == prefab)
                {
                    return i;
                }
            }

            for(int i = 0; i < entries.Count; i++)
            {
                if(entries[i].variants != null && entries[i].variants.Contains(prefab))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
