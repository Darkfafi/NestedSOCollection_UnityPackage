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
		private List<ScriptableObject> _breadcrumbs = new List<ScriptableObject>();
		private Dictionary<UnityEngine.Object, Editor> _editorCache = new Dictionary<UnityEngine.Object, Editor>();

		// Mass Edit
		private string _massEditPropertyPath;
		private bool _massEditExpanded = true;

		private class SearchMatch
		{
			public ScriptableObject Item;
			public List<string> MatchDetails = new List<string>();
		}

		// --- PUBLIC STATIC API (Multi-Window Support) ---

		public static void Open(SerializedProperty listWrapperProperty)
		{
			var win = GetOrCreateWindow(listWrapperProperty);
			win.Show();
			win.Focus();
		}

		public static void OpenItem(SerializedProperty listWrapperProperty, ScriptableObject item)
		{
			var win = GetOrCreateWindow(listWrapperProperty);
			if (item != null)
			{
				if (win._breadcrumbs.LastOrDefault() != item)
				{
					win._breadcrumbs.Clear();
					win._breadcrumbs.Add(item);
				}
			}
			win.Show();
			win.Focus();
		}

		private static NestedSOCollectionWindow GetOrCreateWindow(SerializedProperty prop)
		{
			var wins = Resources.FindObjectsOfTypeAll<NestedSOCollectionWindow>();
			foreach (var w in wins) if (w.IsTargeting(prop)) return w;

			var newWin = CreateInstance<NestedSOCollectionWindow>();
			newWin.titleContent = new GUIContent(prop.displayName);
			newWin.Init(prop);
			return newWin;
		}

		public bool IsTargeting(SerializedProperty prop)
		{
			if (_targetSO == null || prop == null) return false;
			return _targetSO.targetObject == prop.serializedObject.targetObject && _propertyPath == prop.propertyPath;
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

		private void OnEnable() => _searchField = new SearchField();

		private void OnGUI()
		{
			bool isValid = false;
			try { if (_targetSO != null && _targetSO.targetObject != null) isValid = true; } catch { }

			if (!isValid)
			{
				EditorGUILayout.HelpBox("Target Object Lost", MessageType.Warning);
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

			if (!string.IsNullOrEmpty(_searchString)) DrawSearchResults();
			else if (_breadcrumbs.Count > 0) DrawDeepDive();
			else DrawRootList();

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
				_reorderableList = null;

			if (_reorderableList == null) InitReorderableList();

			_reorderableList.DoLayoutList();
		}

		private void InitReorderableList()
		{
			_reorderableList = new ReorderableList(_targetSO, _itemsListProperty, true, true, true, true);

			_reorderableList.drawHeaderCallback = (Rect r) =>
			{
				EditorGUI.LabelField(r, _listWrapperProperty.displayName + " (Drag & Drop to Push)");

				Event evt = Event.current;
				if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && r.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
					if (evt.type == EventType.DragPerform)
					{
						DragAndDrop.AcceptDrag();
						Type elementType = GetListElementType();
						foreach (var obj in DragAndDrop.objectReferences)
						{
							if (obj is ScriptableObject so && AssetDatabase.IsMainAsset(so))
							{
								if (elementType == null || elementType.IsAssignableFrom(so.GetType()))
								{
									ScriptableObject clone = NestedSOEditorUtils.PushExternalAsset(so, _targetSO.targetObject);
									if (clone != null)
									{
										_itemsListProperty.arraySize++;
										_itemsListProperty.GetArrayElementAtIndex(_itemsListProperty.arraySize - 1).objectReferenceValue = clone;
										_itemsListProperty.serializedObject.ApplyModifiedProperties();
										AssetDatabase.SaveAssets();
									}
								}
							}
						}
					}
					evt.Use();
				}
			};

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

				float editBtnW = 50; float menuBtnW = 24; float nameW = rect.width - editBtnW - menuBtnW - 5;

				string newName = EditorGUI.TextField(new Rect(rect.x, rect.y + 1, nameW, EditorGUIUtility.singleLineHeight), item.name);
				if (newName != item.name) { item.name = newName; EditorUtility.SetDirty(item); }

				Rect editBtnRect = new Rect(rect.x + nameW + 2, rect.y, editBtnW, EditorGUIUtility.singleLineHeight);
				if (GUI.Button(editBtnRect, "Edit"))
				{
					_breadcrumbs.Add(item);
					_searchString = "";
					GUI.FocusControl(null);
				}

				Rect menuBtnRect = new Rect(editBtnRect.xMax + 2, rect.y, menuBtnW, EditorGUIUtility.singleLineHeight);
				GUIContent menuIcon = EditorGUIUtility.IconContent("pane options");
				if (GUI.Button(menuBtnRect, menuIcon, new GUIStyle("IconButton")))
				{
					GenericMenu menu = new GenericMenu();

					int capturedIndex = index;
					string propertyPath = _itemsListProperty.propertyPath;
					SerializedObject serializedObject = _itemsListProperty.serializedObject;

					// Root Collection needed for Move
					NestedSOCollectionBase currentCollection = serializedObject.targetObject as NestedSOCollectionBase;

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

					if (currentCollection != null)
					{
						var compatibleCollections = NestedSOEditorUtils.FindCompatibleCollections(item.GetType(), currentCollection);
						if (compatibleCollections.Count > 0)
						{
							foreach (var targetCol in compatibleCollections)
							{
								var tCol = targetCol;
								menu.AddItem(new GUIContent($"Move to.../{tCol.name}"), false, () =>
								{
									NestedSOEditorUtils.MoveSubAssetToCollection(item, currentCollection, tCol);
								});
							}
						}
						else
						{
							menu.AddDisabledItem(new GUIContent("Move to... (No compatible collections)"));
						}
					}

					menu.AddSeparator("");
					menu.AddItem(new GUIContent("Pop (Extract to File)"), false, () =>
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

			_reorderableList.onAddDropdownCallback = (Rect r, ReorderableList l) => ShowAddMenu(_itemsListProperty);
			_reorderableList.onRemoveCallback = (ReorderableList l) =>
			{
				SerializedProperty element = _itemsListProperty.GetArrayElementAtIndex(l.index);
				ScriptableObject item = element.objectReferenceValue as ScriptableObject;
				if (item != null) NestedSOEditorUtils.DestroyAsset(item);
				element.objectReferenceValue = null;
				_itemsListProperty.DeleteArrayElementAtIndex(l.index);
				_itemsListProperty.serializedObject.ApplyModifiedProperties();
				AssetDatabase.SaveAssets();
			};
		}

		private Type GetListElementType()
		{
			Type wrapperType = GetTargetTypeFromPath(_targetSO.targetObject.GetType(), _propertyPath);
			if (wrapperType != null)
			{
				var itemsField = wrapperType.GetField("Items", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (itemsField != null && itemsField.FieldType.IsGenericType)
				{
					return itemsField.FieldType.GetGenericArguments()[0];
				}
			}
			return null;
		}

		private void ShowAddMenu(SerializedProperty listProp)
		{
			Type listType = GetListElementType();
			if (listType == null) return;

			GenericMenu menu = new GenericMenu();
			var types = TypeCache.GetTypesDerivedFrom(listType).Where(t => !t.IsAbstract && !t.IsInterface).ToList();
			if (!listType.IsAbstract && !listType.IsInterface && typeof(ScriptableObject).IsAssignableFrom(listType))
				if (!types.Contains(listType)) types.Insert(0, listType);

			foreach (var t in types)
			{
				menu.AddItem(new GUIContent(t.Name), false, () =>
				{
					ScriptableObject newAsset = ScriptableObject.CreateInstance(t);
					newAsset.name = "New " + t.Name;
					AssetDatabase.AddObjectToAsset(newAsset, listProp.serializedObject.targetObject);
					AssetDatabase.SaveAssets();

					listProp.arraySize++;
					SerializedProperty element = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
					element.objectReferenceValue = newAsset;
					listProp.serializedObject.ApplyModifiedProperties();
				});
			}
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
				EditorGUILayout.BeginHorizontal();
				GUILayout.Label($"Results: {_filteredItems.Count}", EditorStyles.boldLabel);
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Pop Range", GUILayout.Width(150)))
				{
					List<ScriptableObject> itemsToPop = _filteredItems.Select(x => x.Item).ToList();

					NestedSOEditorUtils.PopRangeSubAssets(itemsToPop, (poppedAsset) =>
					{
						for (int j = _itemsListProperty.arraySize - 1; j >= 0; j--)
						{
							if (_itemsListProperty.GetArrayElementAtIndex(j).objectReferenceValue == poppedAsset)
							{
								_itemsListProperty.GetArrayElementAtIndex(j).objectReferenceValue = null;
								_itemsListProperty.DeleteArrayElementAtIndex(j);
								break;
							}
						}
						_itemsListProperty.serializedObject.ApplyModifiedProperties();
					});

					_searchString = "";
					_filteredItems.Clear();
					GUI.FocusControl(null);
					GUIUtility.ExitGUI();
				}
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.Space(5);

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
				if (iter.NextVisible(true)) { while (iter.NextVisible(false)) if (iter.name != "m_Script") props.Add(iter.name); }

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
					switch (src.propertyType)
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