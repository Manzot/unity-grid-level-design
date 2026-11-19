using System;
using System.Collections.Generic;
using UnityEngine;

namespace DefaceGames.GridLevelDesign
{
    [Serializable]
    public class PaletteEntry
    {
        public GameObject prefab;
        public Vector2Int footprint = Vector2Int.one;
        public Vector3 pivotOffset;
        public bool randomRotation;
        public List<GameObject> variants = new List<GameObject>();

        public Vector2Int GetFootprint()
        {
            return Vector2Int.Max(footprint, Vector2Int.one);
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
