using UnityEditor;
using UnityEngine;

namespace DefaceGames.GridLevelDesign.Editor
{
    [CustomEditor(typeof(GridLevel))]
    public class GridLevelEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GridLevel level = (GridLevel)target;
            int pieceCount = 0;

            for(int i = 0; i < level.cells.Count; i++)
            {
                if(level.cells[i].instance != null)
                {
                    pieceCount++;
                }
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Pieces in scene", pieceCount.ToString());

            EditorGUI.BeginChangeCheck();
            GridLevelData linked = (GridLevelData)EditorGUILayout.ObjectField("Level Data", level.linkedData, typeof(GridLevelData), false);

            if(EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(level, "Link Grid Level Data");
                level.linkedData = linked;
                EditorUtility.SetDirty(level);
            }

            EditorGUILayout.BeginHorizontal();

            if(GUILayout.Button("Save As..."))
            {
                GridLevelIO.SaveAs(level);
                GUIUtility.ExitGUI();
            }

            GUI.enabled = level.linkedData != null;

            if(GUILayout.Button(new GUIContent("Save", "Overwrite the linked Level Data asset")))
            {
                GridLevelIO.SaveToAsset(level, level.linkedData);
            }

            if(GUILayout.Button(new GUIContent("Load", "Replace the scene pieces with the linked Level Data")))
            {
                GridLevelIO.LoadFromAsset(level, level.linkedData);
                GUIUtility.ExitGUI();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if(GUILayout.Button("Export JSON..."))
            {
                GridLevelIO.ExportJson(level);
                GUIUtility.ExitGUI();
            }

            if(GUILayout.Button("Import JSON..."))
            {
                GridLevelIO.ImportJson(level);
                GUIUtility.ExitGUI();
            }

            GUI.enabled = pieceCount > 0;

            if(GUILayout.Button("Clear"))
            {
                GridLevelIO.Clear(level, true);
                GUIUtility.ExitGUI();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }
    }
}
