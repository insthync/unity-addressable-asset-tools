using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;

namespace Insthync.AddressableAssetTools
{
    public class AddAddressableScriptableObjectDependenciesToGroupEditor : EditorWindow
    {
        private AddressableAssetSettings _settings;
        private AddressableAssetGroup _selectedGroup;
        private AddressableAssetGroup _dirtySelectedGroup;
        private List<Object> _selectedAssets = new List<Object>();
        private List<string> _dependencyPaths = new List<string>();
        private Dictionary<string, bool> _dependencySelection = new Dictionary<string, bool>();
        private Vector2 _assetsScrollPosition;
        private bool _excludeFromOtherGroups = true;
        private Vector2 _dependenciesScrollPosition;

        [MenuItem("Tools/Addressables/Add Scriptable Object Dependencies to Group By Assets")]
        public static void ShowWindow()
        {
            GetWindow<AddAddressableScriptableObjectDependenciesToGroupEditor>("Add Scriptable Object Dependencies to Group By Assets");
        }

        private void OnGUI()
        {
            GUILayout.Label("Add Scriptable Object Dependencies to Group By Assets", EditorStyles.boldLabel);

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
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    _selectedAssets.RemoveAt(i);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Add Asset"))
            {
                _selectedAssets.Add(null);
            }

            if (GUILayout.Button("Add Selected Assets (In Project Tab)"))
            {
                _selectedAssets.AddRange(Selection.objects);
            }

            if (GUILayout.Button("Clear Assets"))
            {
                _selectedAssets.Clear();
            }

            EditorGUILayout.Space();

            _excludeFromOtherGroups = EditorGUILayout.Toggle("Exclude From Other Groups", _excludeFromOtherGroups);

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

                string assetPath = AssetDatabase.GetAssetPath(asset);
                string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);

                foreach (var dependencyPath in dependencies)
                {
                    // Exclude the asset itself, source code files, and dependencies already in another group
                    if (dependencyPath == assetPath)
                        continue;

                    if (_dependencyPaths.Contains(dependencyPath))
                        continue;

                    if (_excludeFromOtherGroups && IsInAnyAddressableGroup(dependencyPath))
                        continue;

                    Object obj = AssetDatabase.LoadAssetAtPath<Object>(dependencyPath);
                    bool isScriptableObjectDependencies = obj is ScriptableObject;
                    if (!isScriptableObjectDependencies)
                        continue;

                    var depGuid = AssetDatabase.GUIDFromAssetPath(dependencyPath);
                    Debug.Log($"Found {dependencyPath} ({depGuid}) is ref by {assetPath}", asset);
                    _dependencyPaths.Add(dependencyPath);
                    _dependencySelection[dependencyPath] = true; // Default to selected
                }
            }

            // Sort dependencies by path
            _dependencyPaths.Sort();
        }

        private bool IsInAnyAddressableGroup(string dependencyPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(dependencyPath);
            AddressableAssetEntry entry = _settings.FindAssetEntry(guid);

            // Return true if the asset is in a group
            return entry != null;
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
