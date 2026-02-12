#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace NestedSO.SOEditor
{
    // Targets the Base class to work for all generic variations
    [CustomPropertyDrawer(typeof(NestedSOListBase), true)]
    public class NestedSOListDrawer : PropertyDrawer
    {
        private ReorderableList _list;
        private bool _isExpanded = true;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!_isExpanded) return EditorGUIUtility.singleLineHeight;
            
            SerializedProperty listProp = property.FindPropertyRelative("Items");
            if (_list == null) InitList(listProp);
            
            // Height = Title + List + Padding
            return EditorGUIUtility.singleLineHeight + _list.GetHeight() + 5;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty listProp = property.FindPropertyRelative("Items");
            
            EditorGUI.BeginProperty(position, label, property);

            // Foldout Title
            Rect titleRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            string count = listProp != null ? listProp.arraySize.ToString() : "0";
            _isExpanded = EditorGUI.Foldout(titleRect, _isExpanded, $"{label.text} [{count}]", true);

            if (_isExpanded)
            {
                if (_list == null) InitList(listProp);
                
                // Draw List below title
                Rect listRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + 2, position.width, position.height - EditorGUIUtility.singleLineHeight - 2);
                _list.DoList(listRect);
            }

            EditorGUI.EndProperty();
        }

        private void InitList(SerializedProperty listProp)
        {
            _list = new ReorderableList(listProp.serializedObject, listProp, true, true, true, true);
            
            _list.drawHeaderCallback = (Rect r) => 
            {
                EditorGUI.LabelField(r, "Items");
                
                // 'Open All' Button in Header
                Rect btnRect = new Rect(r.x + r.width - 100, r.y, 100, r.height);
                if (GUI.Button(btnRect, "Open Window", EditorStyles.miniButton))
                {
                    NestedSOCollectionWindow.Open(listProp);
                }
            };

            _list.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= listProp.arraySize) return;
                SerializedProperty element = listProp.GetArrayElementAtIndex(index);
                ScriptableObject item = element.objectReferenceValue as ScriptableObject;

                if (item == null)
                {
                    EditorGUI.LabelField(rect, "Null / Empty");
                    return;
                }

                // Name Field
                float btnWidth = 50;
                float nameWidth = rect.width - btnWidth - 30; // 30 for delete button roughly
                
                // Editable Name directly in list
                string newName = EditorGUI.TextField(new Rect(rect.x, rect.y + 1, nameWidth, EditorGUIUtility.singleLineHeight), item.name);
                if (newName != item.name) { item.name = newName; EditorUtility.SetDirty(item); }

                // "Open" Button
                Rect openRect = new Rect(rect.x + rect.width - btnWidth, rect.y, btnWidth, EditorGUIUtility.singleLineHeight);
                if (GUI.Button(openRect, "Edit", EditorStyles.miniButton))
                {
                    NestedSOCollectionWindow.OpenItem(listProp, item);
                }
            };

            _list.onAddDropdownCallback = (Rect r, ReorderableList l) => ShowAddMenu(listProp);
            _list.onRemoveCallback = (ReorderableList l) => RemoveItem(listProp, l.index);
        }

        private void ShowAddMenu(SerializedProperty listProp)
        {
            // Determine 'T' from NestedSOList<T>
            Type listType = null;
            if (typeof(NestedSOListBase).IsAssignableFrom(fieldInfo.FieldType))
            {
                var itemsField = fieldInfo.FieldType.GetField("Items");
                if (itemsField != null)
                {
                   if (itemsField.FieldType.IsGenericType) 
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
                AssetDatabase.RemoveObjectFromAsset(asset);
                GameObject.DestroyImmediate(asset, true);
            }
            element.objectReferenceValue = null;
            listProp.DeleteArrayElementAtIndex(index);
            listProp.serializedObject.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }
}
#endif