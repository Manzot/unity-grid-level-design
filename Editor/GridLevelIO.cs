using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DefaceGames.GridLevelDesign.Editor
{
    public static class GridLevelIO
    {
        [Serializable]
        private class JsonPiece
        {
            public string prefabGuid;
            public string prefabPath;
            public string paletteGuid;
            public Vector3 cell;
            public Vector3 size;
            public float rotation;
            public Vector3 position;
            public Quaternion localRotation;
            public Vector3 scale;
        }

        [Serializable]
        private class JsonLevel
        {
            public int version = 1;
            public Vector3 cellSize;
            public string plane;
            public int halfExtent;
            public List<JsonPiece> pieces = new List<JsonPiece>();
        }

        public static List<LevelPieceData> Capture(GridLevel level)
        {
            List<LevelPieceData> pieces = new List<LevelPieceData>();

            for(int i = 0; i < level.cells.Count; i++)
            {
                PlacedCell placed = level.cells[i];

                if(placed.instance == null)
                {
                    continue;
                }

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(placed.instance);

                if(source == null)
                {
                    source = placed.prefab;
                }

                if(source == null)
                {
                    continue;
                }

                Transform pieceTransform = placed.instance.transform;
                LevelPieceData piece = new LevelPieceData();
                piece.prefab = source;
                piece.paletteKey = placed.prefab;
                piece.prefabGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                piece.cell = placed.cell;
                piece.size = placed.coveredSize;
                piece.rotation = placed.rotationDegrees;
                piece.localPosition = pieceTransform.localPosition;
                piece.localRotation = pieceTransform.localRotation;
                piece.localScale = pieceTransform.localScale;
                pieces.Add(piece);
            }

            return pieces;
        }

        public static void SaveToAsset(GridLevel level, GridLevelData data)
        {
            Undo.RecordObject(data, "Save Grid Level");
            data.cellSize = level.cellSize;
            data.plane = level.plane;
            data.halfExtent = level.halfExtent;
            data.pieces = Capture(level);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            Debug.Log("Grid Level: saved " + data.pieces.Count + " pieces to " + AssetDatabase.GetAssetPath(data), data);
        }

        public static bool SaveAs(GridLevel level)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Grid Level", level.gameObject.name, "asset", "Choose where to save the level");

            if(string.IsNullOrEmpty(path))
            {
                return false;
            }

            GridLevelData data = AssetDatabase.LoadAssetAtPath<GridLevelData>(path);

            if(data == null)
            {
                data = ScriptableObject.CreateInstance<GridLevelData>();
                AssetDatabase.CreateAsset(data, path);
            }

            SaveToAsset(level, data);
            Undo.RecordObject(level, "Link Grid Level Data");
            level.linkedData = data;
            EditorUtility.SetDirty(level);
            return true;
        }

        public static void LoadFromAsset(GridLevel level, GridLevelData data)
        {
            if(!ConfirmReplace(level))
            {
                return;
            }

            Apply(level, data.cellSize, data.plane, data.halfExtent, data.pieces, "Load Grid Level");
        }

        public static void Clear(GridLevel level, bool ask)
        {
            if(ask && !ConfirmReplace(level))
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Clear Grid Level");
            int group = Undo.GetCurrentGroup();
            ClearInternal(level);
            Undo.CollapseUndoOperations(group);
        }

        public static void ExportJson(GridLevel level)
        {
            string path = EditorUtility.SaveFilePanel("Export Grid Level JSON", Application.dataPath, level.gameObject.name, "json");

            if(string.IsNullOrEmpty(path))
            {
                return;
            }

            List<LevelPieceData> pieces = Capture(level);
            JsonLevel json = new JsonLevel();
            json.cellSize = level.cellSize;
            json.plane = level.plane.ToString();
            json.halfExtent = level.halfExtent;

            for(int i = 0; i < pieces.Count; i++)
            {
                LevelPieceData piece = pieces[i];
                JsonPiece entry = new JsonPiece();
                entry.prefabGuid = piece.prefabGuid;
                entry.prefabPath = AssetDatabase.GetAssetPath(piece.prefab);
                entry.paletteGuid = piece.paletteKey != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(piece.paletteKey)) : string.Empty;
                entry.cell = piece.cell;
                entry.size = piece.size;
                entry.rotation = piece.rotation;
                entry.position = piece.localPosition;
                entry.localRotation = piece.localRotation;
                entry.scale = piece.localScale;
                json.pieces.Add(entry);
            }

            File.WriteAllText(path, JsonUtility.ToJson(json, true));
            AssetDatabase.Refresh();
            Debug.Log("Grid Level: exported " + json.pieces.Count + " pieces to " + path);
        }

        public static void ImportJson(GridLevel level)
        {
            string path = EditorUtility.OpenFilePanel("Import Grid Level JSON", Application.dataPath, "json");

            if(string.IsNullOrEmpty(path))
            {
                return;
            }

            JsonLevel json = JsonUtility.FromJson<JsonLevel>(File.ReadAllText(path));

            if(json == null || json.pieces == null)
            {
                EditorUtility.DisplayDialog("Import Grid Level JSON", "The file is not a Grid Level export.", "OK");
                return;
            }

            if(!ConfirmReplace(level))
            {
                return;
            }

            GridPlane plane = level.plane;

            if(!string.IsNullOrEmpty(json.plane))
            {
                Enum.TryParse(json.plane, out plane);
            }

            List<LevelPieceData> pieces = new List<LevelPieceData>();

            for(int i = 0; i < json.pieces.Count; i++)
            {
                JsonPiece entry = json.pieces[i];
                LevelPieceData piece = new LevelPieceData();
                piece.prefabGuid = entry.prefabGuid;
                piece.prefab = LoadByGuid(entry.prefabGuid);

                if(piece.prefab == null && !string.IsNullOrEmpty(entry.prefabPath))
                {
                    piece.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabPath);
                }

                piece.paletteKey = LoadByGuid(entry.paletteGuid);
                piece.cell = Vector3Int.RoundToInt(entry.cell);
                piece.size = Vector3Int.RoundToInt(entry.size);
                piece.rotation = entry.rotation;
                piece.localPosition = entry.position;
                piece.localRotation = entry.localRotation;
                piece.localScale = entry.scale;
                pieces.Add(piece);
            }

            Apply(level, json.cellSize, plane, json.halfExtent, pieces, "Import Grid Level");
        }

        private static GameObject LoadByGuid(string guid)
        {
            if(string.IsNullOrEmpty(guid))
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);

            if(string.IsNullOrEmpty(path))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static bool ConfirmReplace(GridLevel level)
        {
            int count = 0;

            for(int i = 0; i < level.cells.Count; i++)
            {
                if(level.cells[i].instance != null)
                {
                    count++;
                }
            }

            if(count == 0)
            {
                return true;
            }

            return EditorUtility.DisplayDialog("Grid Level", "This will remove the " + count + " pieces currently in " + level.gameObject.name + ". You can undo it.", "Continue", "Cancel");
        }

        private static void ClearInternal(GridLevel level)
        {
            Undo.RecordObject(level, "Clear Grid Level");
            List<GameObject> instances = new List<GameObject>();

            for(int i = 0; i < level.cells.Count; i++)
            {
                if(level.cells[i].instance != null)
                {
                    instances.Add(level.cells[i].instance);
                }
            }

            level.cells.Clear();
            level.InvalidateLookup();

            for(int i = 0; i < instances.Count; i++)
            {
                Undo.DestroyObjectImmediate(instances[i]);
            }

            EditorUtility.SetDirty(level);
        }

        private static void Apply(GridLevel level, Vector3 cellSize, GridPlane plane, int halfExtent, List<LevelPieceData> pieces, string undoName)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            int group = Undo.GetCurrentGroup();

            ClearInternal(level);

            Undo.RecordObject(level, undoName);
            level.cellSize = Vector3.Max(cellSize, Vector3.one * 0.01f);
            level.plane = plane;
            level.halfExtent = Mathf.Max(1, halfExtent);

            List<string> missing = new List<string>();
            int created = 0;

            for(int i = 0; i < pieces.Count; i++)
            {
                LevelPieceData piece = pieces[i];
                GameObject prefab = piece.prefab;

                if(prefab == null)
                {
                    prefab = LoadByGuid(piece.prefabGuid);
                }

                if(prefab == null)
                {
                    missing.Add(string.IsNullOrEmpty(piece.prefabGuid) ? "(no reference)" : piece.prefabGuid);
                    continue;
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, level.transform) as GameObject;

                if(instance == null)
                {
                    missing.Add(prefab.name);
                    continue;
                }

                instance.transform.localPosition = piece.localPosition;
                instance.transform.localRotation = piece.localRotation;
                instance.transform.localScale = piece.localScale;
                Undo.RegisterCreatedObjectUndo(instance, undoName);

                PlacedCell placed = new PlacedCell();
                placed.cell = piece.cell;
                placed.size = Vector3Int.Max(piece.size, Vector3Int.one);
                placed.instance = instance;
                placed.prefab = piece.paletteKey != null ? piece.paletteKey : prefab;
                placed.rotation = piece.rotation;
                placed.rotationSteps = GridLevel.QuarterTurns(piece.rotation);
                level.AddCell(placed);
                created++;
            }

            EditorUtility.SetDirty(level);
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();

            if(missing.Count > 0)
            {
                Debug.LogWarning("Grid Level: " + missing.Count + " pieces were skipped because their prefab is missing: " + string.Join(", ", missing), level);
            }

            Debug.Log("Grid Level: loaded " + created + " pieces into " + level.gameObject.name, level);
        }
    }
}
