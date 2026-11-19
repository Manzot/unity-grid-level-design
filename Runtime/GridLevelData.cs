using System;
using System.Collections.Generic;
using UnityEngine;

namespace DefaceGames.GridLevelDesign
{
    [Serializable]
    public class LevelPieceData
    {
        public GameObject prefab;
        public GameObject paletteKey;
        public string prefabGuid;
        public Vector3Int cell;
        public Vector3Int size;
        public float rotation;
        public bool freeform;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
    }

    [CreateAssetMenu(fileName = "GridLevelData", menuName = "Grid Level/Level Data")]
    public class GridLevelData : ScriptableObject
    {
        public Vector3 cellSize = Vector3.one;
        public GridPlane plane = GridPlane.XZ;
        public int halfExtent = 20;
        public List<LevelPieceData> pieces = new List<LevelPieceData>();
    }
}
