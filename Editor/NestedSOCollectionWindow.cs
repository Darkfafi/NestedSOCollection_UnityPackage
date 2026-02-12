#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace NestedSO.SOEditor
{
    public class NestedSOCollectionWindow : EditorWindow
    {
        private SerializedObject _targetSO;
        private string _propertyPath;
        private SerializedProperty _listWrapperProperty;
        private SerializedProperty _itemsListProperty;

        private ReorderableList _reorderableList;
        private Vector2 _scrollPosition;

        // Search & Navigation
        private SearchField _searchField;
        private string _searchString = "";
        private List<SearchMatch> _filteredItems = new List<SearchMatch>();
        private List<UnityEngine.Object> _breadcrumbs = new List<UnityEngine.Object>();
        private Dictionary<UnityEngine.Object, Editor> _editorCache = new Dictionary<UnityEngine.Object, Editor>();

        // Mass Edit
        private string _massEditPropertyPath;
        private bool _massEditExpanded = true;

        private class SearchMatch
        {
            public ScriptableObject Item;
            public List<string> MatchDetails = new List<string>();
        }

        public static void Open(SerializedProperty listWrapperProperty)
        {
            if (HasOpenInstances<NestedSOCollectionWindow>())
            {
                var win = GetWindow<NestedSOCollectionWindow>();
                win.Focus();
                win.Init(listWrapperProperty);
            }
            else
            {
                NestedSOCollectionWindow newWin = GetWindow<NestedSOCollectionWindow>("Collection Editor");
                newWin.Init(listWrapperProperty);
                newWin.Show();
            }
        }

        public static void OpenItem(SerializedProperty listWrapperProperty, ScriptableObject item)
        {
            NestedSOCollectionWindow win = GetWindow<NestedSOCollectionWindow>("Collection Editor");
            win.Init(listWrapperProperty);
            if (item != null) win._breadcrumbs.Add(item);
            win.Show();
        }

        private void Init(SerializedProperty listWrapperProperty)
        {
            _targetSO = listWrapperProperty.serializedObject;
            _propertyPath = listWrapperProperty.propertyPath;
            
            _breadcrumbs.Clear();
            _searchString = "";
            _filteredItems.Clear();
            _editorCache.Clear();
            _reorderableList = null;
            _scrollPosition = Vector2.zero;
        }

        private void OnEnable()
        {
            _searchField = new SearchField();
        }

        private void OnGUI()
        {
            bool isValid = false;
            try
            {
                if (_targetSO != null && _targetSO.targetObject != null) isValid = true;
            }
            catch { }

            if (!isValid)
            {
                Close();
                return;
            }

            _targetSO.Update();
            _listWrapperProperty = _targetSO.FindProperty(_propertyPath);
            
            if (_listWrapperProperty == null)
            {
                EditorGUILayout.HelpBox($"Could not find property: {_propertyPath}", MessageType.Error);
                return;
            }

            _itemsListProperty = _listWrapperProperty.FindPropertyRelative("Items");

            DrawToolbar();

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
            _targetSO.ApplyModifiedProperties();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

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
                        int index = _breadcrumbs.IndexOf(item);
                        _breadcrumbs.RemoveRange(index + 1, _breadcrumbs.Count - (index + 1));
                    }
                }
            }

            GUILayout.FlexibleSpace();

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
            if (_reorderableList != null && _reorderableList.serializedProperty.propertyPath != _itemsListProperty.propertyPath)
            {
                _reorderableList = null;
            }

            if (_reorderableList == null) InitReorderableList();
            
            _reorderableList.DoLayoutList();
        }

        private void InitReorderableList()
        {
            _reorderableList = new ReorderableList(_targetSO, _itemsListProperty, true, true, true, true);

            _reorderableList.drawHeaderCallback = (Rect r) => EditorGUI.LabelField(r, _listWrapperProperty.displayName);

            _reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= _itemsListProperty.arraySize) return;

                SerializedProperty element = _itemsListProperty.GetArrayElementAtIndex(index);
                ScriptableObject item = element.objectReferenceValue as ScriptableObject;

                if (item == null)
                {
                    EditorGUI.LabelField(rect, "Null / Empty");
                    return;
                }

                float btnWidth = 50;
                float nameWidth = rect.width - btnWidth - 5;

                string newName = EditorGUI.TextField(new Rect(rect.x, rect.y + 1, nameWidth, EditorGUIUtility.singleLineHeight), item.name);
                if (newName != item.name) { item.name = newName; EditorUtility.SetDirty(item); }

                if (GUI.Button(new Rect(rect.x + rect.width - btnWidth, rect.y, btnWidth, EditorGUIUtility.singleLineHeight), "Edit"))
                {
                    _breadcrumbs.Add(item);
                    GUI.FocusControl(null);
                }
            };

            _reorderableList.onAddDropdownCallback = (Rect r, ReorderableList l) => ShowAddMenu(_itemsListProperty);
            _reorderableList.onRemoveCallback = (ReorderableList l) => RemoveItem(_itemsListProperty, l.index);
        }
        
        private void ShowAddMenu(SerializedProperty listProp)
        {
            Type listType = null;
            Type wrapperType = GetTargetTypeFromPath(_targetSO.targetObject.GetType(), _propertyPath);
            
            if (wrapperType != null)
            {
                 var itemsField = wrapperType.GetField("Items", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                 if (itemsField != null && itemsField.FieldType.IsGenericType)
                 {
                     listType = itemsField.FieldType.GetGenericArguments()[0];
                 }
            }

            if (listType == null) return;

            GenericMenu menu = new GenericMenu();
            var types = TypeCache.GetTypesDerivedFrom(listType).Where(t => !t.IsAbstract && !t.IsInterface).ToList();
            if (!listType.IsAbstract && !listType.IsInterface && typeof(ScriptableObject).IsAssignableFrom(listType))
                 if (!types.Contains(listType)) types.Insert(0, listType);

            foreach (var t in types) menu.AddItem(new GUIContent(t.Name), false, () => CreateAndAddAsset(listProp, t));
            menu.ShowAsContext();
        }

        private Type GetTargetTypeFromPath(Type hostType, string path)
        {
            Type currentType = hostType;
            string[] parts = path.Split('.');

            foreach (var part in parts)
            {
                if (currentType == null) return null;
                if (part == "Array") return null; 

                FieldInfo field = null;
                Type searchType = currentType;
                while (searchType != null)
                {
                    field = searchType.GetField(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null) break;
                    searchType = searchType.BaseType;
                }

                if (field == null) return null;
                currentType = field.FieldType;
            }
            return currentType;
        }

        private void CreateAndAddAsset(SerializedProperty listProp, Type type)
        {
            ScriptableObject newAsset = ScriptableObject.CreateInstance(type);
            newAsset.name = "New " + type.Name;
            AssetDatabase.AddObjectToAsset(newAsset, listProp.serializedObject.targetObject);
            AssetDatabase.SaveAssets();

            listProp.arraySize++;
            SerializedProperty element = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
            element.objectReferenceValue = newAsset;
            listProp.serializedObject.ApplyModifiedProperties();
        }

        private void RemoveItem(SerializedProperty listProp, int index)
        {
            SerializedProperty element = listProp.GetArrayElementAtIndex(index);
            ScriptableObject asset = element.objectReferenceValue as ScriptableObject;
            
            if (asset != null)
            {
                NestedSOAssetUtils.DestroyAsset(asset);
            }
            
            element.objectReferenceValue = null;
            listProp.DeleteArrayElementAtIndex(index);
            listProp.serializedObject.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        private void DrawDeepDive()
        {
            UnityEngine.Object item = _breadcrumbs.LastOrDefault();
            if (item == null) { _breadcrumbs.RemoveAt(_breadcrumbs.Count - 1); return; }

            if (!_editorCache.TryGetValue(item, out Editor editor))
                _editorCache[item] = editor = Editor.CreateEditor(item);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Editing: {item.name}", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField("Name", item.name);
            if (EditorGUI.EndChangeCheck()) { item.name = newName; EditorUtility.SetDirty(item); }
            EditorGUILayout.Space();

            if (editor != null)
            {
                try { editor.OnInspectorGUI(); } catch { }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSearchResults()
        {
            if (_filteredItems.Count > 0)
            {
                DrawMassEdit();
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
                        GUIStyle s = new GUIStyle(EditorStyles.miniLabel) { richText = true };
                        foreach (var d in match.MatchDetails) EditorGUILayout.LabelField(d, s);
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
            _massEditExpanded = EditorGUILayout.Foldout(_massEditExpanded, $"Mass Edit ({_filteredItems.Count})", true);
            if (_massEditExpanded)
            {
                EditorGUILayout.BeginHorizontal();
                SerializedObject rep = new SerializedObject(_filteredItems[0].Item);
                List<string> props = new List<string>();
                SerializedProperty iter = rep.GetIterator();
                if (iter.NextVisible(true)) { while(iter.NextVisible(false)) if(iter.name != "m_Script") props.Add(iter.name); }

                int idx = props.IndexOf(_massEditPropertyPath);
                int newIdx = EditorGUILayout.Popup(idx, props.ToArray());
                if (newIdx != idx && newIdx >= 0) _massEditPropertyPath = props[newIdx];

                if (!string.IsNullOrEmpty(_massEditPropertyPath))
                {
                    SerializedProperty p = rep.FindProperty(_massEditPropertyPath);
                    if (p != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(p, GUIContent.none, true);
                        if (EditorGUI.EndChangeCheck()) rep.ApplyModifiedProperties();
                        if (GUILayout.Button("Apply", GUILayout.Width(60))) ApplyMassEdit(p);
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
            for (int i = 0; i < _itemsListProperty.arraySize; i++)
            {
                ScriptableObject item = _itemsListProperty.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableObject;
                if (item == null) continue;
                List<string> matches = new List<string>();
                bool nameMatch = item.name.ToLowerInvariant().Contains(lower);
                if (item.GetType().Name.ToLowerInvariant().Contains(lower)) matches.Add($"Type: <color=#FFFF00>{item.GetType().Name}</color>");

                SerializedObject so = new SerializedObject(item);
                SerializedProperty it = so.GetIterator();
                if (it.NextVisible(true))
                {
                    while (it.NextVisible(false))
                    {
                        if (it.name == "m_Script") continue;
                        string v = "";
                        switch (it.propertyType)
                        {
                            case SerializedPropertyType.String: v = it.stringValue; break;
                            case SerializedPropertyType.Integer: v = it.intValue.ToString(); break;
                            case SerializedPropertyType.Float: v = it.floatValue.ToString(); break;
                            case SerializedPropertyType.Boolean: v = it.boolValue.ToString(); break;
                        }
                        if (!string.IsNullOrEmpty(v) && v.ToLowerInvariant().Contains(lower)) matches.Add($"{it.displayName}: <color=#FFFF00>{v}</color>");
                    }
                }
                if (nameMatch || matches.Count > 0) _filteredItems.Add(new SearchMatch { Item = item, MatchDetails = matches });
            }
            
            if (_filteredItems.Count > 0 && !string.IsNullOrEmpty(_searchString))
            {
                SerializedObject r = new SerializedObject(_filteredItems[0].Item);
                SerializedProperty p = r.FindProperty(_searchString);
                if (p != null) _massEditPropertyPath = p.name;
            }
        }

        private void ApplyMassEdit(SerializedProperty src)
        {
            foreach (var m in _filteredItems)
            {
                SerializedObject so = new SerializedObject(m.Item);
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