#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
#endif

#if UNITY_EDITOR

namespace NestedSO.SOEditor
{
    public class NestedSOCollectionWindow : EditorWindow
    {
        private SerializedObject _targetSO;
        private string _propertyPath;
        private SerializedProperty _listProperty;

        // State
        private SearchField _searchField;
        private string _searchString = "";
        private List<SearchMatch> _filteredItems = new List<SearchMatch>();
        private List<UnityEngine.Object> _breadcrumbs = new List<UnityEngine.Object>();
        private Dictionary<UnityEngine.Object, Editor> _editorCache = new Dictionary<UnityEngine.Object, Editor>();
        
        // Mass Edit
        private string _massEditPropertyPath;
        private bool _massEditExpanded = true;
        
        // Scroll State
        private Vector2 _scrollPosition;

        private class SearchMatch
        {
            public ScriptableObject Item;
            public List<string> MatchDetails = new List<string>();
        }

        public static void Open(SerializedProperty listProperty)
        {
            NestedSOCollectionWindow win = GetWindow<NestedSOCollectionWindow>("Collection Editor");
            win._targetSO = listProperty.serializedObject;
            win._propertyPath = listProperty.propertyPath;
            win._breadcrumbs.Clear(); // Start at root
            win._searchString = "";
            win.Show();
        }

        public static void OpenItem(SerializedProperty listProperty, ScriptableObject item)
        {
            NestedSOCollectionWindow win = GetWindow<NestedSOCollectionWindow>("Collection Editor");
            win._targetSO = listProperty.serializedObject;
            win._propertyPath = listProperty.propertyPath;
            win._breadcrumbs.Clear();
            if (item != null) win._breadcrumbs.Add(item);
            win._searchString = "";
            win.Show();
        }

        private void OnEnable()
        {
            _searchField = new SearchField();
        }

        private void OnGUI()
        {
            // 1. Recover Property (Required because SerializedProperty is not valid across frames)
            if (_targetSO == null || _targetSO.targetObject == null)
            {
                EditorGUILayout.HelpBox("Target object is missing.", MessageType.Warning);
                return;
            }
            _targetSO.Update();
            _listProperty = _targetSO.FindProperty(_propertyPath);
            if (_listProperty == null)
            {
                EditorGUILayout.HelpBox($"Could not find property: {_propertyPath}", MessageType.Error);
                return;
            }

            // 2. Toolbar
            DrawToolbar();
            
            // 3. Main Content
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            if (!string.IsNullOrEmpty(_searchString))
            {
                DrawSearchResults();
            }
            else if (_breadcrumbs.Count > 0)
            {
                DrawDeepDive();
            }
            else
            {
                DrawRootList();
            }
            
            EditorGUILayout.EndScrollView();
            
            // Apply changes back to the original object
            _targetSO.ApplyModifiedProperties();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            // Breadcrumbs
            if (GUILayout.Button("Root", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                _breadcrumbs.Clear();
                _searchString = "";
                GUI.FocusControl(null);
            }

            foreach (var item in _breadcrumbs)
            {
                GUILayout.Label(" > ", EditorStyles.miniLabel, GUILayout.Width(15));
                if (item != null)
                {
                    if (GUILayout.Button(item.name, EditorStyles.toolbarButton, GUILayout.MaxWidth(150)))
                    {
                        // Navigate back to this item (remove items after it)
                        int index = _breadcrumbs.IndexOf(item);
                        _breadcrumbs.RemoveRange(index + 1, _breadcrumbs.Count - (index + 1));
                    }
                }
            }

            GUILayout.FlexibleSpace();

            // Search
            string newSearch = _searchField.OnToolbarGUI(_searchString, GUILayout.Width(250));
            if (newSearch != _searchString)
            {
                _searchString = newSearch;
                PerformSearch();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRootList()
        {
            EditorGUILayout.LabelField($"Collection: {_listProperty.displayName}", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Just list the items with Open buttons. Creation/Reordering is done in the Inspector.
            if (_listProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Collection is empty. Add items in the Inspector.", MessageType.Info);
                return;
            }

            for (int i = 0; i < _listProperty.arraySize; i++)
            {
                SerializedProperty el = _listProperty.GetArrayElementAtIndex(i);
                ScriptableObject item = el.objectReferenceValue as ScriptableObject;

                if (item == null) continue;

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(item.name, EditorStyles.boldLabel);
                if (GUILayout.Button("Edit", GUILayout.Width(60)))
                {
                    _breadcrumbs.Add(item);
                    GUI.FocusControl(null);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawDeepDive()
        {
            UnityEngine.Object item = _breadcrumbs.LastOrDefault();
            if (item == null)
            {
                _breadcrumbs.RemoveAt(_breadcrumbs.Count - 1);
                return;
            }

            if (!_editorCache.TryGetValue(item, out Editor editor))
            {
                _editorCache[item] = editor = Editor.CreateEditor(item);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Editing: {item.name}", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Name Field
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField("Name", item.name);
            if (EditorGUI.EndChangeCheck())
            {
                item.name = newName;
                EditorUtility.SetDirty(item);
            }

            EditorGUILayout.Space();
            
            // Draw Inner Inspector
            if (editor != null)
            {
                try
                {
                    editor.OnInspectorGUI();
                }
                catch { } // Catch layout errors from inner editors
            }
            
            EditorGUILayout.EndVertical();
        }

        private void DrawSearchResults()
        {
            if (_filteredItems.Count > 0)
            {
                DrawMassEdit();
                EditorGUILayout.Space();
                
                foreach (var match in _filteredItems)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(match.Item.name, EditorStyles.boldLabel);
                    if (GUILayout.Button("Edit", GUILayout.Width(60)))
                    {
                        _breadcrumbs.Clear();
                        _breadcrumbs.Add(match.Item);
                        _searchString = "";
                        GUI.FocusControl(null);
                    }
                    EditorGUILayout.EndHorizontal();

                    if (match.MatchDetails.Count > 0)
                    {
                        EditorGUI.indentLevel++;
                        GUIStyle richStyle = new GUIStyle(EditorStyles.miniLabel) { richText = true };
                        foreach (var detail in match.MatchDetails)
                        {
                            EditorGUILayout.LabelField(detail, richStyle);
                        }
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndVertical();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No results found.", MessageType.Info);
            }
        }

        private void DrawMassEdit()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _massEditExpanded = EditorGUILayout.Foldout(_massEditExpanded, $"Mass Edit ({_filteredItems.Count} items)", true);
            
            if (_massEditExpanded)
            {
                EditorGUILayout.BeginHorizontal();
                
                // 1. Property Selection
                SerializedObject rep = new SerializedObject(_filteredItems[0].Item);
                List<string> props = new List<string>();
                SerializedProperty iter = rep.GetIterator();
                if (iter.NextVisible(true))
                {
                    while (iter.NextVisible(false))
                    {
                        if (iter.name != "m_Script") props.Add(iter.name);
                    }
                }

                int idx = props.IndexOf(_massEditPropertyPath);
                int newIdx = EditorGUILayout.Popup(idx, props.ToArray());
                if (newIdx != idx && newIdx >= 0) _massEditPropertyPath = props[newIdx];

                // 2. Value Editing
                if (!string.IsNullOrEmpty(_massEditPropertyPath))
                {
                    SerializedProperty p = rep.FindProperty(_massEditPropertyPath);
                    if (p != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(p, GUIContent.none, true);
                        if (EditorGUI.EndChangeCheck()) rep.ApplyModifiedProperties();
                        
                        if (GUILayout.Button("Apply All", GUILayout.Width(80)))
                        {
                            ApplyMassEdit(p);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void PerformSearch()
        {
            _filteredItems.Clear();
            string lower = _searchString.ToLowerInvariant();

            // Iterate through the list property
            for (int i = 0; i < _listProperty.arraySize; i++)
            {
                ScriptableObject item = _listProperty.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableObject;
                if (item == null) continue;

                List<string> matches = new List<string>();
                bool nameMatch = item.name.ToLowerInvariant().Contains(lower);
                if (item.GetType().Name.ToLowerInvariant().Contains(lower)) 
                    matches.Add($"Type: <color=#FFFF00>{item.GetType().Name}</color>");

                SerializedObject so = new SerializedObject(item);
                SerializedProperty iterator = so.GetIterator();
                if (iterator.NextVisible(true))
                {
                    while (iterator.NextVisible(false))
                    {
                        if (iterator.name == "m_Script") continue;
                        string val = "";
                        // Simple value checks
                        switch (iterator.propertyType)
                        {
                            case SerializedPropertyType.String: val = iterator.stringValue; break;
                            case SerializedPropertyType.Integer: val = iterator.intValue.ToString(); break;
                            case SerializedPropertyType.Float: val = iterator.floatValue.ToString(); break;
                            case SerializedPropertyType.Boolean: val = iterator.boolValue.ToString(); break;
                        }
                        if (!string.IsNullOrEmpty(val) && val.ToLowerInvariant().Contains(lower))
                        {
                            matches.Add($"{iterator.displayName}: <color=#FFFF00>{val}</color>");
                        }
                    }
                }

                if (nameMatch || matches.Count > 0)
                {
                    _filteredItems.Add(new SearchMatch { Item = item, MatchDetails = matches });
                }
            }
            
            // Auto select mass edit property
            if (_filteredItems.Count > 0 && !string.IsNullOrEmpty(_searchString))
            {
                SerializedObject r = new SerializedObject(_filteredItems[0].Item);
                SerializedProperty p = r.FindProperty(_searchString);
                if (p != null) _massEditPropertyPath = p.name;
            }
        }

        private void ApplyMassEdit(SerializedProperty src)
        {
            foreach (var match in _filteredItems)
            {
                SerializedObject so = new SerializedObject(match.Item);
                SerializedProperty p = so.FindProperty(src.name);
                if (p != null && p.propertyType == src.propertyType)
                {
                    switch(src.propertyType)
                    {
                        case SerializedPropertyType.Integer: p.intValue = src.intValue; break;
                        case SerializedPropertyType.Boolean: p.boolValue = src.boolValue; break;
                        case SerializedPropertyType.Float: p.floatValue = src.floatValue; break;
                        case SerializedPropertyType.String: p.stringValue = src.stringValue; break;
                        case SerializedPropertyType.Color: p.colorValue = src.colorValue; break;
                        case SerializedPropertyType.ObjectReference: p.objectReferenceValue = src.objectReferenceValue; break;
                        case SerializedPropertyType.Enum: p.enumValueIndex = src.enumValueIndex; break;
                    }
                    so.ApplyModifiedProperties();
                }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
#endif