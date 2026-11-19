using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
    public class GridLevelData : ScriptableObject, ISerializationCallbackReceiver
    {
        public Vector3 cellSize = Vector3.one;
        public GridPlane plane = GridPlane.XZ;
        public int gridSize = 40;
        public List<LevelPieceData> pieces = new List<LevelPieceData>();

        [SerializeField, HideInInspector, FormerlySerializedAs("halfExtent")]
        private int m_legacyHalfExtent;

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            if(m_legacyHalfExtent > 0)
            {
                gridSize = m_legacyHalfExtent * 2;
                m_legacyHalfExtent = 0;
            }
        }
    }
}
