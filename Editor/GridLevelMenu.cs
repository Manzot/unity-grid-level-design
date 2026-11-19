using UnityEditor;
using UnityEngine;

namespace DefaceGames.GridLevelDesign.Editor
{
    public static class GridLevelMenu
    {
        [MenuItem("GameObject/Grid Level", false, 10)]
        private static void CreateGridLevel(MenuCommand command)
        {
            GameObject levelObject = new GameObject("Grid Level");
            levelObject.AddComponent<GridLevel>();
            GameObjectUtility.SetParentAndAlign(levelObject, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(levelObject, "Create Grid Level");
            Selection.activeGameObject = levelObject;
        }
    }
}
