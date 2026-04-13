#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
				float bulkPopBtnW = 70;
				EditorGUI.LabelField(new Rect(r.x, r.y, r.width - bulkPopBtnW - 10, r.height), "Items (Drag & Drop to Push)");

				Rect bulkPopRect = new Rect(r.x + r.width - bulkPopBtnW, r.y, bulkPopBtnW, r.height);
				if (GUI.Button(bulkPopRect, "Pop Range", EditorStyles.miniButton))
				{
					List<ScriptableObject> itemsToPop = new List<ScriptableObject>();
					for (int i = 0; i < listProp.arraySize; i++)
					{
						var obj = listProp.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableObject;
						if (obj != null) itemsToPop.Add(obj);
					}

					NestedSOEditorUtils.PopRangeSubAssets(itemsToPop, (poppedAsset) =>
					{
						for (int j = listProp.arraySize - 1; j >= 0; j--)
						{
							if (listProp.GetArrayElementAtIndex(j).objectReferenceValue == poppedAsset)
							{
								listProp.GetArrayElementAtIndex(j).objectReferenceValue = null;
								listProp.DeleteArrayElementAtIndex(j);
								break;
							}
						}
						listProp.serializedObject.ApplyModifiedProperties();
					});

					GUIUtility.ExitGUI();
				}

				Event evt = Event.current;
				if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && r.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
					if (evt.type == EventType.DragPerform)
					{
						DragAndDrop.AcceptDrag();
						Type listType = GetListType();
						foreach (var obj in DragAndDrop.objectReferences)
						{
							if (obj is ScriptableObject so && AssetDatabase.IsMainAsset(so))
							{
								if (listType == null || listType.IsAssignableFrom(so.GetType()))
								{
									ScriptableObject clone = NestedSOEditorUtils.PushExternalAsset(so, wrapperProp.serializedObject.targetObject);
									if (clone != null)
									{
										listProp.arraySize++;
										listProp.GetArrayElementAtIndex(listProp.arraySize - 1).objectReferenceValue = clone;
										listProp.serializedObject.ApplyModifiedProperties();
										AssetDatabase.SaveAssets();
									}
								}
							}
						}
					}
					evt.Use();
				}
			};

			_list.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
			{
				if (index >= listProp.arraySize) return;
				SerializedProperty element = listProp.GetArrayElementAtIndex(index);
				ScriptableObject item = element.objectReferenceValue as ScriptableObject;

				if (item == null) { EditorGUI.LabelField(rect, "Null"); return; }

				float editBtnW = 50;
				float menuBtnW = 24;
				float nameW = rect.width - editBtnW - menuBtnW - 5;

				string newName = EditorGUI.TextField(new Rect(rect.x, rect.y + 1, nameW, EditorGUIUtility.singleLineHeight), item.name);
				if (newName != item.name) { item.name = newName; EditorUtility.SetDirty(item); }

				Rect editBtnRect = new Rect(rect.x + nameW + 2, rect.y, editBtnW, EditorGUIUtility.singleLineHeight);
				if (GUI.Button(editBtnRect, "Edit"))
				{
					NestedSOCollectionWindow.OpenItem(wrapperProp, item);
				}

				Rect menuBtnRect = new Rect(editBtnRect.xMax + 2, rect.y, menuBtnW, EditorGUIUtility.singleLineHeight);
				GUIContent menuIcon = EditorGUIUtility.IconContent("pane options");
				if (GUI.Button(menuBtnRect, menuIcon, new GUIStyle("IconButton")))
				{
					GenericMenu menu = new GenericMenu();
					int capturedIndex = index;
					string propertyPath = listProp.propertyPath;
					SerializedObject serializedObject = listProp.serializedObject;

					menu.AddItem(new GUIContent("Duplicate"), false, () =>
					{
						serializedObject.Update();
						var prop = serializedObject.FindProperty(propertyPath);
						ScriptableObject clone = NestedSOEditorUtils.DuplicateSubAsset(item, serializedObject.targetObject);

						prop.InsertArrayElementAtIndex(capturedIndex + 1);
						prop.GetArrayElementAtIndex(capturedIndex + 1).objectReferenceValue = clone;
						prop.serializedObject.ApplyModifiedProperties();
						AssetDatabase.SaveAssets();
					});
					menu.AddSeparator("");
					menu.AddItem(new GUIContent("Pop"), false, () =>
					{
						serializedObject.Update();
						var prop = serializedObject.FindProperty(propertyPath);
						if (NestedSOEditorUtils.PopSubAsset(item, out _))
						{
							prop.GetArrayElementAtIndex(capturedIndex).objectReferenceValue = null;
							prop.DeleteArrayElementAtIndex(capturedIndex);
							prop.serializedObject.ApplyModifiedProperties();
							AssetDatabase.SaveAssets();
						}
					});
					menu.AddSeparator("");
					menu.AddItem(new GUIContent("Remove"), false, () =>
					{
						serializedObject.Update();
						var prop = serializedObject.FindProperty(propertyPath);

						NestedSOEditorUtils.DestroyAsset(item);

						prop.GetArrayElementAtIndex(capturedIndex).objectReferenceValue = null;
						prop.DeleteArrayElementAtIndex(capturedIndex);
						prop.serializedObject.ApplyModifiedProperties();
						AssetDatabase.SaveAssets();
					});

					menu.ShowAsContext();
				}
			};

			_list.onAddDropdownCallback = (Rect r, ReorderableList l) => ShowAddMenu(listProp);
			_list.onRemoveCallback = (ReorderableList l) => RemoveItem(listProp, l.index);
		}

		private Type GetListType()
		{
			if (typeof(NestedSOListBase).IsAssignableFrom(fieldInfo.FieldType))
			{
				var itemsField = fieldInfo.FieldType.GetField("Items");
				if (itemsField != null && itemsField.FieldType.IsGenericType)
					return itemsField.FieldType.GetGenericArguments()[0];
			}
			return null;
		}

		private void ShowAddMenu(SerializedProperty listProp)
		{
			Type listType = GetListType();
			if (listType == null) return;

			GenericMenu menu = new GenericMenu();
			var types = TypeCache.GetTypesDerivedFrom(listType).Where(t => !t.IsAbstract && !t.IsInterface).ToList();
			if (!listType.IsAbstract && !listType.IsInterface && typeof(ScriptableObject).IsAssignableFrom(listType))
				if (!types.Contains(listType)) types.Insert(0, listType);

			foreach (var t in types) menu.AddItem(new GUIContent(t.Name), false, () => CreateAndAddAsset(listProp, t));
			menu.ShowAsContext();
		}

		#region Static Public API

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

			if (asset != null) NestedSOEditorUtils.DestroyAsset(asset);

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

			NestedSOEditorUtils.DestroyAsset(asset);

			EditorUtility.SetDirty(parentAsset);
			AssetDatabase.SaveAssets();
		}

		#endregion
	}
}
#endif