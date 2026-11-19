using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DefaceGames.GridLevelDesign.Editor
{
    public class GridLevelWindow : EditorWindow
    {
        private const float m_tileSize = 76f;
        private const int m_maxPreviewTries = 50;

        private static readonly Color m_selectedColor = new Color(0.2f, 0.8f, 1f, 1f);
        private static readonly Color m_tileColor = new Color(1f, 1f, 1f, 0.06f);

        private readonly List<Image> m_pendingImages = new List<Image>();
        private readonly List<GameObject> m_pendingPrefabs = new List<GameObject>();

        private ObjectField m_paletteField;
        private VisualElement m_tileContainer;
        private Label m_statusLabel;
        private Button m_addFolderButton;
        private VisualElement m_detailsContainer;
        private int m_previewTries;

        [MenuItem("Window/Grid Level/Palette")]
        private static void ShowWindow()
        {
            GridLevelWindow window = GetWindow<GridLevelWindow>();
            window.titleContent = new GUIContent("Grid Palette");
            window.minSize = new Vector2(220f, 200f);
        }

        private void OnEnable()
        {
            GridPaletteState.changed += Refresh;
            Undo.undoRedoPerformed += Refresh;
        }

        private void OnDisable()
        {
            GridPaletteState.changed -= Refresh;
            Undo.undoRedoPerformed -= Refresh;
        }

        private void CreateGUI()
        {
            AssetPreview.SetPreviewTextureCacheSize(512);

            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 4f;
            root.style.paddingRight = 4f;
            root.style.paddingTop = 4f;
            root.style.paddingBottom = 4f;

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.marginBottom = 4f;

            m_paletteField = new ObjectField("Palette");
            m_paletteField.objectType = typeof(PrefabPalette);
            m_paletteField.allowSceneObjects = false;
            m_paletteField.labelElement.style.minWidth = 50f;
            m_paletteField.style.flexGrow = 1f;
            m_paletteField.RegisterValueChangedCallback(OnPaletteFieldChanged);
            header.Add(m_paletteField);

            Button newButton = new Button(CreatePalette);
            newButton.text = "New";
            header.Add(newButton);

            m_addFolderButton = new Button(AddFolder);
            m_addFolderButton.text = "Add Folder";
            header.Add(m_addFolderButton);

            root.Add(header);

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;

            m_tileContainer = new VisualElement();
            m_tileContainer.style.flexDirection = FlexDirection.Row;
            m_tileContainer.style.flexWrap = Wrap.Wrap;
            scroll.Add(m_tileContainer);
            root.Add(scroll);

            m_detailsContainer = new VisualElement();
            m_detailsContainer.style.marginTop = 4f;
            root.Add(m_detailsContainer);

            m_statusLabel = new Label();
            m_statusLabel.style.marginTop = 4f;
            m_statusLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(m_statusLabel);

            root.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            root.RegisterCallback<DragPerformEvent>(OnDragPerform);

            // Lets the Scene tool's shortcuts work while this window still has keyboard focus.
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(evt => ForwardKey(evt.target as VisualElement, EventType.KeyDown, evt.keyCode, evt.modifiers));
            root.RegisterCallback<KeyUpEvent>(evt => ForwardKey(evt.target as VisualElement, EventType.KeyUp, evt.keyCode, evt.modifiers));
            root.schedule.Execute(UpdatePendingPreviews).Every(200);

            Refresh();
        }

        private static void ForwardKey(VisualElement source, EventType type, KeyCode keyCode, EventModifiers modifiers)
        {
            GridPlacementTool tool = GridPlacementTool.activeTool;

            if(tool == null || keyCode == KeyCode.None)
            {
                return;
            }

            for(VisualElement element = source; element != null; element = element.parent)
            {
                if(element.ClassListContains("unity-base-text-field"))
                {
                    return;
                }
            }

            Event forwarded = new Event();
            forwarded.type = type;
            forwarded.keyCode = keyCode;
            forwarded.modifiers = modifiers;
            tool.HandlePanelKey(forwarded);
        }

        private void OnPaletteFieldChanged(ChangeEvent<Object> evt)
        {
            PrefabPalette picked = evt.newValue as PrefabPalette;

            if(picked != GridPaletteState.palette)
            {
                GridPaletteState.palette = picked;
            }
        }

        private void CreatePalette()
        {
            string path = EditorUtility.SaveFilePanelInProject("New Prefab Palette", "PrefabPalette", "asset", "Choose where to save the palette");

            if(string.IsNullOrEmpty(path))
            {
                return;
            }

            PrefabPalette created = CreateInstance<PrefabPalette>();
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            GridPaletteState.palette = created;
        }

        private void Refresh()
        {
            if(m_tileContainer == null)
            {
                return;
            }

            PrefabPalette palette = GridPaletteState.palette;
            m_paletteField.SetValueWithoutNotify(palette);
            m_addFolderButton.SetEnabled(palette != null);
            m_tileContainer.Clear();
            m_detailsContainer.Clear();
            m_pendingImages.Clear();
            m_pendingPrefabs.Clear();
            m_previewTries = 0;

            if(palette == null)
            {
                m_statusLabel.text = "Assign a palette or click New, then drag prefabs or folders in here.";
                return;
            }

            for(int i = 0; i < palette.entries.Count; i++)
            {
                m_tileContainer.Add(BuildTile(palette.entries[i], i));
            }

            PaletteEntry selected = GridPaletteState.selectedEntry;

            if(selected != null)
            {
                BuildDetails(palette, GridPaletteState.selectedIndex);
            }

            if(palette.entries.Count == 0)
            {
                m_statusLabel.text = "Empty palette. Drag prefabs or folders in here.";
            }
            else if(selected != null && selected.prefab != null)
            {
                m_statusLabel.text = "Selected: " + selected.prefab.name;
            }
            else
            {
                m_statusLabel.text = palette.entries.Count + " prefabs. Click one to select, right-click to remove.";
            }
        }

        private VisualElement BuildTile(PaletteEntry entry, int index)
        {
            VisualElement tile = new VisualElement();
            tile.style.width = m_tileSize;
            tile.style.marginRight = 3f;
            tile.style.marginBottom = 3f;
            tile.style.backgroundColor = m_tileColor;
            SetBorder(tile, index == GridPaletteState.selectedIndex);

            Image image = new Image();
            image.scaleMode = ScaleMode.ScaleToFit;
            image.style.width = m_tileSize - 4f;
            image.style.height = m_tileSize - 4f;
            tile.Add(image);

            Label label = new Label();
            label.style.fontSize = 10f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            tile.Add(label);

            if(entry.prefab == null)
            {
                label.text = "(missing)";
            }
            else
            {
                label.text = entry.prefab.name;
                tile.tooltip = AssetDatabase.GetAssetPath(entry.prefab);
                Texture2D preview = AssetPreview.GetAssetPreview(entry.prefab);

                if(preview != null)
                {
                    image.image = preview;
                }
                else
                {
                    image.image = AssetPreview.GetMiniThumbnail(entry.prefab);
                    m_pendingImages.Add(image);
                    m_pendingPrefabs.Add(entry.prefab);
                }
            }

            tile.RegisterCallback<MouseDownEvent>(evt =>
            {
                if(evt.button != 0)
                {
                    return;
                }

                if(GridPaletteState.selectedIndex == index)
                {
                    GridPaletteState.Select(-1);
                }
                else
                {
                    GridPaletteState.Select(index);
                }

                rootVisualElement.Focus();
                evt.StopPropagation();
            });

            tile.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Remove from palette", action => RemoveEntry(index));
            }));

            return tile;
        }

        private void BuildDetails(PrefabPalette palette, int index)
        {
            SerializedObject serializedPalette = new SerializedObject(palette);
            SerializedProperty entryProperty = serializedPalette.FindProperty("entries").GetArrayElementAtIndex(index);
            entryProperty.isExpanded = true;

            string title = "Entry";

            if(palette.entries[index].prefab != null)
            {
                title = palette.entries[index].prefab.name;
            }

            PropertyField field = new PropertyField(entryProperty, title);
            field.Bind(serializedPalette);
            m_detailsContainer.Add(field);

            PaletteEntry entry = palette.entries[index];
            string footprintText;

            if(entry.manualFootprint)
            {
                footprintText = "Footprint: " + entry.GetFootprint().x + " x " + entry.GetFootprint().y + " cells (manual)";
            }
            else
            {
                GridLevel currentLevel = GridFootprintUtility.FindLevel();
                Vector3 entryScale = entry.GetScale();

                if(entry.fitToCell && currentLevel != null)
                {
                    entryScale = Vector3.one * GridFootprintUtility.GetFitFactor(currentLevel, entry.prefab);
                }

                Vector2Int live = GridFootprintUtility.ComputeFor(currentLevel, entry.prefab, entryScale);
                footprintText = "Footprint: " + live.x + " x " + live.y + " cells (automatic, from the current grid). Tick Manual Footprint to set it yourself.";

                if(entry.fitToCell)
                {
                    footprintText += "\nFit to Cell is on: scaled " + entryScale.x.ToString("0.##") + "x to fill one cell; the Scale field is ignored.";
                }
            }

            Label footprintLabel = new Label(footprintText);
            footprintLabel.style.whiteSpace = WhiteSpace.Normal;
            footprintLabel.style.marginTop = 2f;
            footprintLabel.style.marginBottom = 2f;
            m_detailsContainer.Add(footprintLabel);

            Button autoButton = new Button(() => AutoFootprint(index));
            autoButton.text = "Fill Manual Footprint From Bounds";
            m_detailsContainer.Add(autoButton);
        }

        private void AutoFootprint(int index)
        {
            PrefabPalette palette = GridPaletteState.palette;

            if(palette == null || index < 0 || index >= palette.entries.Count)
            {
                return;
            }

            Undo.RecordObject(palette, "Auto Footprint");
            palette.entries[index].footprint = GridFootprintUtility.ComputeFor(GridFootprintUtility.FindLevel(), palette.entries[index].prefab, palette.entries[index].GetScale());
            palette.entries[index].manualFootprint = true;
            EditorUtility.SetDirty(palette);
            GridPaletteState.NotifyChanged();
        }

        private static void SetBorder(VisualElement element, bool selected)
        {
            Color color = Color.clear;

            if(selected)
            {
                color = m_selectedColor;
            }

            element.style.borderLeftWidth = 2f;
            element.style.borderRightWidth = 2f;
            element.style.borderTopWidth = 2f;
            element.style.borderBottomWidth = 2f;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        private void UpdatePendingPreviews()
        {
            if(m_pendingImages.Count == 0 || m_previewTries >= m_maxPreviewTries)
            {
                return;
            }

            m_previewTries++;

            for(int i = m_pendingImages.Count - 1; i >= 0; i--)
            {
                Texture2D preview = null;

                if(m_pendingPrefabs[i] != null)
                {
                    preview = AssetPreview.GetAssetPreview(m_pendingPrefabs[i]);
                }

                if(preview != null)
                {
                    m_pendingImages[i].image = preview;
                    m_pendingImages.RemoveAt(i);
                    m_pendingPrefabs.RemoveAt(i);
                }
            }
        }

        private void RemoveEntry(int index)
        {
            PrefabPalette palette = GridPaletteState.palette;

            if(palette == null || index < 0 || index >= palette.entries.Count)
            {
                return;
            }

            Undo.RecordObject(palette, "Remove Palette Entry");
            palette.entries.RemoveAt(index);
            EditorUtility.SetDirty(palette);

            int selected = GridPaletteState.selectedIndex;

            if(selected == index)
            {
                selected = -1;
            }
            else if(selected > index)
            {
                selected--;
            }

            GridPaletteState.Select(selected);
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if(GridPaletteState.palette != null && CollectDraggedPrefabs(null) > 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
            else
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            }
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            if(GridPaletteState.palette == null)
            {
                return;
            }

            List<GameObject> prefabs = new List<GameObject>();
            CollectDraggedPrefabs(prefabs);

            if(prefabs.Count == 0)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            AddPrefabs(prefabs);
        }

        private void AddFolder()
        {
            if(GridPaletteState.palette == null)
            {
                return;
            }

            string absolutePath = EditorUtility.OpenFolderPanel("Add Prefab Folder", Application.dataPath, string.Empty);

            if(string.IsNullOrEmpty(absolutePath))
            {
                return;
            }

            string folder = FileUtil.GetProjectRelativePath(absolutePath);

            if(string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                EditorUtility.DisplayDialog("Add Prefab Folder", "Pick a folder inside this project's Assets or Packages.", "OK");
                return;
            }

            List<GameObject> prefabs = new List<GameObject>();
            CollectFromFolder(folder, prefabs);

            if(prefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("Add Prefab Folder", "No prefabs found in " + folder + ".", "OK");
                return;
            }

            AddPrefabs(prefabs);
        }

        private static void AddPrefabs(List<GameObject> prefabs)
        {
            PrefabPalette palette = GridPaletteState.palette;
            Vector3 cellSize = GridFootprintUtility.FindCellSize();
            Undo.RecordObject(palette, "Add Palette Entries");

            for(int i = 0; i < prefabs.Count; i++)
            {
                if(!palette.Contains(prefabs[i]))
                {
                    PaletteEntry entry = new PaletteEntry();
                    entry.prefab = prefabs[i];
                    entry.footprint = GridFootprintUtility.Compute(prefabs[i], cellSize);
                    palette.entries.Add(entry);
                }
            }

            EditorUtility.SetDirty(palette);
            GridPaletteState.NotifyChanged();
        }

        private static int CollectFromFolder(string folder, List<GameObject> results)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new string[] { folder });

            if(results != null)
            {
                for(int i = 0; i < guids.Length; i++)
                {
                    GameObject found = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));

                    if(found != null && !results.Contains(found))
                    {
                        results.Add(found);
                    }
                }
            }

            return guids.Length;
        }

        private static int CollectDraggedPrefabs(List<GameObject> results)
        {
            int count = 0;
            string[] paths = DragAndDrop.paths;

            for(int i = 0; i < paths.Length; i++)
            {
                if(AssetDatabase.IsValidFolder(paths[i]))
                {
                    count += CollectFromFolder(paths[i], results);
                }
                else
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);

                    if(prefab != null)
                    {
                        count++;

                        if(results != null && !results.Contains(prefab))
                        {
                            results.Add(prefab);
                        }
                    }
                }
            }

            return count;
        }
    }
}
