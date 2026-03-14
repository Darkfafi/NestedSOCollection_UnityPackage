#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace NestedSO.SOEditor
{
	[CustomPropertyDrawer(typeof(NestedSOListBase), true)]
	public class NestedSOListDrawer : PropertyDrawer
	{
		private ReorderableList _list;
		private bool _isExpanded = true;

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (!_isExpanded) return EditorGUIUtility.singleLineHeight;
			SerializedProperty listProp = property.FindPropertyRelative("Items");
			if (_list == null) InitList(listProp, property);
			return EditorGUIUtility.singleLineHeight + _list.GetHeight() + 5;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			SerializedProperty listProp = property.FindPropertyRelative("Items");
			EditorGUI.BeginProperty(position, label, property);

			Rect titleRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
			string count = listProp != null ? listProp.arraySize.ToString() : "0";
			_isExpanded = EditorGUI.Foldout(titleRect, _isExpanded, $"{label.text} [{count}]", true);

			if (_isExpanded)
			{
				if (_list == null) InitList(listProp, property);
				Rect listRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + 2, position.width, position.height - EditorGUIUtility.singleLineHeight - 2);
				_list.DoList(listRect);
			}
			EditorGUI.EndProperty();
		}

		private void InitList(SerializedProperty listProp, SerializedProperty wrapperProp)
		{
			_list = new ReorderableList(listProp.serializedObject, listProp, true, true, true, true);

			_list.drawHeaderCallback = (Rect r) =>
			{
				EditorGUI.LabelField(new Rect(r.x, r.y, r.width - 120, r.height), "Items");

				if (GUI.Button(new Rect(r.x + r.width - 110, r.y, 110, r.height), "Search / Details", EditorStyles.miniButton))
				{
					NestedSOCollectionWindow.Open(wrapperProp);
				}
			};

			_list.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
			{
				if (index >= listProp.arraySize) return;
				SerializedProperty element = listProp.GetArrayElementAtIndex(index);
				ScriptableObject item = element.objectReferenceValue as ScriptableObject;

				if (item == null) { EditorGUI.LabelField(rect, "Null"); return; }

				float btnW = 50;
				float nameW = rect.width - btnW - 5;

				string newName = EditorGUI.TextField(new Rect(rect.x, rect.y + 1, nameW, EditorGUIUtility.singleLineHeight), item.name);
				if (newName != item.name) { item.name = newName; EditorUtility.SetDirty(item); }

				if (GUI.Button(new Rect(rect.x + rect.width - btnW, rect.y, btnW, EditorGUIUtility.singleLineHeight), "Edit"))
				{
					NestedSOCollectionWindow.OpenItem(wrapperProp, item);
				}
			};

			_list.onAddDropdownCallback = (Rect r, ReorderableList l) => ShowAddMenu(listProp);
			_list.onRemoveCallback = (ReorderableList l) => RemoveItem(listProp, l.index);
		}

		private void ShowAddMenu(SerializedProperty listProp)
		{
			Type listType = null;
			if (typeof(NestedSOListBase).IsAssignableFrom(fieldInfo.FieldType))
			{
				var itemsField = fieldInfo.FieldType.GetField("Items");
				if (itemsField != null && itemsField.FieldType.IsGenericType)
					listType = itemsField.FieldType.GetGenericArguments()[0];
			}
			if (listType == null) return;

			GenericMenu menu = new GenericMenu();
			var types = TypeCache.GetTypesDerivedFrom(listType).Where(t => !t.IsAbstract && !t.IsInterface).ToList();
			if (!listType.IsAbstract && !listType.IsInterface && typeof(ScriptableObject).IsAssignableFrom(listType))
				if (!types.Contains(listType)) types.Insert(0, listType);

			foreach (var t in types) menu.AddItem(new GUIContent(t.Name), false, () => CreateAndAddAsset(listProp, t));
			menu.ShowAsContext();
		}

		public static ScriptableObject CreateAndAddAsset(SerializedProperty listProp, Type type, string name = null)
		{
			ScriptableObject newAsset = ScriptableObject.CreateInstance(type);
			newAsset.name = string.IsNullOrEmpty(name) ? "New " + type.Name : name;
			AssetDatabase.AddObjectToAsset(newAsset, listProp.serializedObject.targetObject);
			AssetDatabase.SaveAssets();
			listProp.arraySize++;
			SerializedProperty element = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
			element.objectReferenceValue = newAsset;
			listProp.serializedObject.ApplyModifiedProperties();
			return newAsset;
		}

		public static void RemoveItem(SerializedProperty listProp, int index)
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

		public static T AddAssetToTarget<T>(ScriptableObject parentAsset, NestedSOList<T> listMember, string name = null)
			where T : ScriptableObject
		{
			return AddAssetToTarget(parentAsset, listMember, typeof(T), name) as T;
		}

		public static ScriptableObject AddAssetToTarget(ScriptableObject parentAsset, NestedSOListBase listMember, Type type, string name = null)
		{
			ScriptableObject newAsset = ScriptableObject.CreateInstance(type);
			newAsset.name = string.IsNullOrEmpty(name) ? "New " + type.Name : name;

			AssetDatabase.AddObjectToAsset(newAsset, parentAsset);
			listMember.Add(newAsset);

			EditorUtility.SetDirty(parentAsset);
			EditorUtility.SetDirty(newAsset);
			AssetDatabase.SaveAssets();

			return newAsset;
		}

		public static void RemoveAssetFromTarget(ScriptableObject parentAsset, NestedSOListBase listMember, ScriptableObject asset)
		{
			if (asset == null) return;

			if (listMember.Contains(asset))
			{
				listMember.Remove(asset);
			}

			NestedSOAssetUtils.DestroyAsset(asset);

			EditorUtility.SetDirty(parentAsset);
			AssetDatabase.SaveAssets();
		}
	}
}
#endif