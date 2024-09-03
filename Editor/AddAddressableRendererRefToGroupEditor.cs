using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
using UnityEngine.UI;

namespace Insthync.AddressableAssetTools
{
    public class AddAddressableRendererRefToGroupEditor : EditorWindow
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _selectedGroup;
        private AddressableAssetGroup _dirtySelectedGroup;
        private List<Object> _selectedAssets = new List<Object>();
        private List<string> _dependencyPaths = new List<string>();
        private Dictionary<string, bool> _dependencySelection = new Dictionary<string, bool>();
        private Vector2 _assetsScrollPosition;
        private Vector2 _dependenciesScrollPosition;

        [MenuItem("Tools/Addressables/Add Renderer Ref to Group")]
        public static void ShowWindow()
        {
            GetWindow<AddAddressableRendererRefToGroupEditor>("Add Renderer Ref to Group");
        }

        private void OnGUI()
        {
            GUILayout.Label("Add Renderer Ref to Group", EditorStyles.boldLabel);

            _settings = AddressableAssetSettingsDefaultObject.Settings;
            if (_settings == null)
            {
                EditorGUILayout.HelpBox("Addressable Asset Settings not found!", MessageType.Error);
                return;
            }
            _selectedGroup = (AddressableAssetGroup)EditorGUILayout.ObjectField("Target Group", _selectedGroup, typeof(AddressableAssetGroup), false);
            if (_dirtySelectedGroup != _selectedGroup)
            {
                _dirtySelectedGroup = _selectedGroup;
                _selectedAssets.Clear();
                if (_selectedGroup != null)
                {
                    var entries = _selectedGroup.entries;
                    foreach (var entry in entries)
                    {
                        _selectedAssets.Add(entry.TargetAsset);
                    }
                }
            }
            EditorGUILayout.Space();

            GUILayout.Label("Selected Assets:", EditorStyles.boldLabel);

            // Scrollable list of selected assets
            _assetsScrollPosition = EditorGUILayout.BeginScrollView(_assetsScrollPosition, GUILayout.Height(150));
            for (int i = 0; i < _selectedAssets.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _selectedAssets[i] = EditorGUILayout.ObjectField(_selectedAssets[i], typeof(Object), false);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            if (GUILayout.Button("Find Dependencies of Selected Assets"))
            {
                FindDependencies();
            }

            if (_dependencyPaths.Count > 0)
            {
                GUILayout.Label("Select Dependencies to Add:", EditorStyles.boldLabel);

                // Begin Scroll View for Dependencies
                _dependenciesScrollPosition = EditorGUILayout.BeginScrollView(_dependenciesScrollPosition, GUILayout.Height(300));

                EditorGUILayout.BeginVertical("box");
                foreach (var dependencyPath in _dependencyPaths)
                {
                    _dependencySelection[dependencyPath] = EditorGUILayout.ToggleLeft(dependencyPath, _dependencySelection[dependencyPath]);
                }
                EditorGUILayout.EndVertical();

                // End Scroll View
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();

                // Select/Deselect All Buttons
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select All"))
                {
                    SetAllDependenciesSelection(true);
                }
                if (GUILayout.Button("Deselect All"))
                {
                    SetAllDependenciesSelection(false);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                if (GUILayout.Button("Add Selected Dependencies to Group"))
                {
                    AddSelectedDependencies();
                }
            }
        }

        private void FindDependencies()
        {
            _dependencyPaths.Clear();
            _dependencySelection.Clear();

            foreach (var asset in _selectedAssets)
            {
                if (asset == null) continue;
                Component comp = asset as Component;
                GameObject gameObject = asset as GameObject;

                List<MeshFilter> meshFilters = new List<MeshFilter>();
                List<MeshRenderer> meshRenderers = new List<MeshRenderer>();
                List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
                List<SpriteRenderer> spriteRenderers = new List<SpriteRenderer>();
                List<Image> images = new List<Image>();
                List<RawImage> rawImages = new List<RawImage>();
                if (comp)
                {
                    meshFilters.AddRange(comp.GetComponentsInChildren<MeshFilter>(true));
                    meshRenderers.AddRange(comp.GetComponentsInChildren<MeshRenderer>(true));
                    skinnedMeshRenderers.AddRange(comp.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                    spriteRenderers.AddRange(comp.GetComponentsInChildren<SpriteRenderer>(true));
                    images.AddRange(comp.GetComponentsInChildren<Image>(true));
                    rawImages.AddRange(comp.GetComponentsInChildren<RawImage>(true));
                }
                if (gameObject)
                {
                    meshFilters.AddRange(gameObject.GetComponentsInChildren<MeshFilter>(true));
                    meshRenderers.AddRange(gameObject.GetComponentsInChildren<MeshRenderer>(true));
                    skinnedMeshRenderers.AddRange(gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                    spriteRenderers.AddRange(gameObject.GetComponentsInChildren<SpriteRenderer>(true));
                    images.AddRange(gameObject.GetComponentsInChildren<Image>(true));
                    rawImages.AddRange(gameObject.GetComponentsInChildren<RawImage>(true));
                }

                foreach (var meshFilter in meshFilters)
                {
                    AddToDependency(meshFilter.sharedMesh);
                }

                foreach (var meshRenderer in meshRenderers)
                {
                    AddMaterialToDependency(meshRenderer.sharedMaterials);
                }

                foreach (var meshRenderer in meshRenderers)
                {
                    AddMaterialToDependency(meshRenderer.sharedMaterials);
                }

                foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
                {
                    AddMaterialToDependency(skinnedMeshRenderer.sharedMaterials);
                    AddToDependency(skinnedMeshRenderer.sharedMesh);
                }

                foreach (var spriteRenderer in spriteRenderers)
                {
                    AddToDependency(spriteRenderer.sprite);
                }

                foreach (var image in images)
                {
                    AddToDependency(image.sprite);
                }

                foreach (var rawImage in rawImages)
                {
                    AddToDependency(rawImage.texture);
                }
            }

            // Sort dependencies by path
            _dependencyPaths.Sort();
        }

        private void AddToDependency(Object asset)
        {
            if (!asset)
                return;
            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (!_dependencyPaths.Contains(assetPath))
            {
                _dependencyPaths.Add(assetPath);
                _dependencySelection[assetPath] = true; // Default to selected
            }
        }

        private void AddToDependency(IList<Object> assets)
        {
            foreach (var asset in assets)
            {
                AddToDependency(asset);
            }
        }

        private void AddMaterialToDependency(Material asset)
        {
            if (!asset)
                return;
            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (!_dependencyPaths.Contains(assetPath))
            {
                _dependencyPaths.Add(assetPath);
                _dependencySelection[assetPath] = true; // Default to selected
            }
            string[] names = asset.GetTexturePropertyNames();
            foreach (string name in names)
            {
                AddToDependency(asset.GetTexture(name));
            }
        }

        private void AddMaterialToDependency(IList<Material> assets)
        {
            foreach (var asset in assets)
            {
                AddMaterialToDependency(asset);
            }
        }

        private bool IsInOtherAddressableGroup(string dependencyPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(dependencyPath);
            AddressableAssetEntry entry = _settings.FindAssetEntry(guid);

            // Return true if the asset is in a group and it's not the selected group
            return entry != null && entry.parentGroup != _selectedGroup;
        }

        private void AddSelectedDependencies()
        {
            if (_selectedGroup == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a target Addressable Group.", "OK");
                return;
            }

            foreach (var dependencyPath in _dependencyPaths)
            {
                if (_dependencySelection[dependencyPath])
                {
                    AddressableAssetEntry dependencyEntry = _settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(dependencyPath));
                    if (dependencyEntry == null)
                    {
                        _settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(dependencyPath), _selectedGroup, false, false);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Done", "Selected dependencies have been added to the group.", "OK");
        }

        private void SetAllDependenciesSelection(bool isSelected)
        {
            foreach (var key in _dependencyPaths)
            {
                _dependencySelection[key] = isSelected;
            }
        }
    }
}
