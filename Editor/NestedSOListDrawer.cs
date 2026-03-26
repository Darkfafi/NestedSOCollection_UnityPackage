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
				EditorGUI.LabelField(new Rect(r.x, r.y, r.width - 120, r.height), "Items (Drag & Drop to Push)");

				if (GUI.Button(new Rect(r.x + r.width - 110, r.y, 110, r.height), "Search / Details", EditorStyles.miniButton))
				{
					NestedSOCollectionWindow.Open(wrapperProp);
				}

				// Handle Drag & Drop Pushing
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
									PushAssetToList(so, listProp, wrapperProp.serializedObject.targetObject);
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

				float popBtnW = 35;
				float btnW = 50;
				float nameW = rect.width - btnW - popBtnW - 10;

				string newName = EditorGUI.TextField(new Rect(rect.x, rect.y + 1, nameW, EditorGUIUtility.singleLineHeight), item.name);
				if (newName != item.name) { item.name = newName; EditorUtility.SetDirty(item); }

				Rect popBtnRect = new Rect(rect.x + nameW + 5, rect.y, popBtnW, EditorGUIUtility.singleLineHeight);
				if (GUI.Button(popBtnRect, "Pop"))
				{
					PopAssetFromList(item, listProp, index);
					return;
				}

				Rect editBtnRect = new Rect(popBtnRect.xMax + 5, rect.y, btnW, EditorGUIUtility.singleLineHeight);
				if (GUI.Button(editBtnRect, "Edit"))
				{
					NestedSOCollectionWindow.OpenItem(wrapperProp, item);
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

		// =================================================================================================
		// PUSH & POP LOGIC
		// =================================================================================================

		private static List<ScriptableObject> GetNestedAssetsRecursive(ScriptableObject root)
		{
			var result = new List<ScriptableObject>();
			if (root == null) return result;

			if (root is NestedSOCollectionBase internalCollection)
			{
				var items = internalCollection.GetRawItems();
				for (int i = items.Count - 1; i >= 0; i--)
				{
					if (items[i] != null)
					{
						result.Add(items[i]);
						result.AddRange(GetNestedAssetsRecursive(items[i]));
					}
				}
			}

			var fields = root.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
			foreach (var field in fields)
			{
				if (typeof(NestedSOListBase).IsAssignableFrom(field.FieldType))
				{
					var listWrapper = field.GetValue(root);
					if (listWrapper != null)
					{
						var itemsField = field.FieldType.GetField("Items");
						if (itemsField != null)
						{
							var list = itemsField.GetValue(listWrapper) as System.Collections.IList;
							if (list != null)
							{
								for (int i = list.Count - 1; i >= 0; i--)
								{
									var child = list[i] as ScriptableObject;
									if (child != null)
									{
										result.Add(child);
										result.AddRange(GetNestedAssetsRecursive(child));
									}
								}
							}
						}
					}
				}
			}
			return result.Distinct().ToList();
		}

		private void PushAssetToList(ScriptableObject externalAsset, SerializedProperty listProp, UnityEngine.Object rootObject)
		{
			if (!EditorUtility.DisplayDialog("Push Asset", $"Merging '{externalAsset.name}' into this list will delete the standalone file.\n\nContinue?", "Yes, Push It", "Cancel"))
				return;

			string oldPath = AssetDatabase.GetAssetPath(externalAsset);

			// Gather nested items from original file BEFORE cloning
			var nestedAssets = GetNestedAssetsRecursive(externalAsset);

			// Clone the main asset
			ScriptableObject clonedParent = UnityEngine.Object.Instantiate(externalAsset);
			clonedParent.name = externalAsset.name;

			// Add cloned parent to target root
			AssetDatabase.AddObjectToAsset(clonedParent, rootObject);

			listProp.arraySize++;
			var element = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
			element.objectReferenceValue = clonedParent;
			listProp.serializedObject.ApplyModifiedProperties();

			// Safely detach sub-assets from old file and move to new file
			foreach (var child in nestedAssets)
			{
				if (child != null && AssetDatabase.GetAssetPath(child) == oldPath && !AssetDatabase.IsMainAsset(child))
				{
					AssetDatabase.RemoveObjectFromAsset(child);
					AssetDatabase.AddObjectToAsset(child, rootObject);
				}
			}

			AssetDatabase.DeleteAsset(oldPath);
			AssetDatabase.SaveAssets();
		}

		private void PopAssetFromList(ScriptableObject subAsset, SerializedProperty listProp, int index)
		{
			string path = EditorUtility.SaveFilePanelInProject("Pop Asset", subAsset.name, "asset", "Choose location to extract to.");
			if (string.IsNullOrEmpty(path)) return;

			string oldPath = AssetDatabase.GetAssetPath(subAsset);

			// Gather nested items recursively
			var nestedAssets = GetNestedAssetsRecursive(subAsset);

			// Remove from the serialized array
			var element = listProp.GetArrayElementAtIndex(index);
			element.objectReferenceValue = null;
			listProp.DeleteArrayElementAtIndex(index);
			listProp.serializedObject.ApplyModifiedProperties();

			// Strip sub-asset from current hierarchy and build the new file
			AssetDatabase.RemoveObjectFromAsset(subAsset);
			AssetDatabase.CreateAsset(subAsset, path);

			// Transfer all nested sub-objects over to the new file safely
			foreach (var child in nestedAssets)
			{
				if (child != null && AssetDatabase.GetAssetPath(child) == oldPath && !AssetDatabase.IsMainAsset(child))
				{
					AssetDatabase.RemoveObjectFromAsset(child);
					AssetDatabase.AddObjectToAsset(child, subAsset);
				}
			}

			AssetDatabase.SaveAssets();
		}

		// =================================================================================================
		// STATIC PUBLIC API
		// =================================================================================================

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