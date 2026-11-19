using System;
using UnityEditor;
using UnityEngine;

namespace DefaceGames.GridLevelDesign.Editor
{
    public static class GridPaletteState
    {
        private const string m_paletteKey = "DefaceGames.GridLevel.PaletteGuid";

        private static PrefabPalette m_palette;
        private static bool m_loaded;
        private static int m_selectedIndex = -1;

        public static event Action changed;

        public static PrefabPalette palette
        {
            get
            {
                if(!m_loaded)
                {
                    m_loaded = true;
                    string guid = EditorPrefs.GetString(m_paletteKey, string.Empty);
                    if(!string.IsNullOrEmpty(guid))
                    {
                        m_palette = AssetDatabase.LoadAssetAtPath<PrefabPalette>(AssetDatabase.GUIDToAssetPath(guid));
                    }
                }

                return m_palette;
            }
            set
            {
                m_loaded = true;
                m_palette = value;
                m_selectedIndex = -1;

                string guid = string.Empty;

                if(value != null)
                {
                    guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value));
                }

                EditorPrefs.SetString(m_paletteKey, guid);
                NotifyChanged();
            }
        }

        public static int selectedIndex
        {
            get
            {
                return m_selectedIndex;
            }
        }

        public static PaletteEntry selectedEntry
        {
            get
            {
                PrefabPalette current = palette;

                if(current == null || m_selectedIndex < 0 || m_selectedIndex >= current.entries.Count)
                {
                    return null;
                }

                return current.entries[m_selectedIndex];
            }
        }

        public static void Select(int index)
        {
            m_selectedIndex = index;
            NotifyChanged();
        }

        public static void NotifyChanged()
        {
            if(changed != null)
            {
                changed.Invoke();
            }
        }
    }
}
