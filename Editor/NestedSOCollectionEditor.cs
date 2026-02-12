#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace NestedSO.SOEditor
{
	[CustomEditor(typeof(NestedSOCollectionBase), true)]
	public class NestedSOCollectionEditor : Editor
	{
		#region Consts

		public const string NestedSOItemsFieldName = "_nestedSOItems";

		#endregion

		#region Inner Classes

		private class SearchMatch
		{
			public ScriptableObject Item;
			public List<string> MatchDetails = new List<string>();
		}

		#endregion

		#region Variables

		// Navigation & State
		private List<UnityEngine.Object> _breadcrumbs = new List<UnityEngine.Object>();
		
		private ReorderableList _orderableList;
		private SerializedProperty _nestedSOsProperty;
		private GenericMenu _menu = null;
		private Type _baseType;
		private string _baseTypeName;
		
		// Editor Cache
		private Dictionary<UnityEngine.Object, Editor> _cachedEditors = new Dictionary<UnityEngine.Object, Editor>();

		// Search & Filter
		private SearchField _searchField;
		private string _searchString = "";
		private List<SearchMatch> _filteredItems = new List<SearchMatch>(); // Changed to store Match Details
		
		// Mass Edit
		private string _massEditSelectedPropertyPath;
		private bool _massEditExpanded = true;

		#endregion

		#region Lifecycle

		protected void OnEnable()
		{
			NestedSOCollectionBase targetCollection = serializedObject.targetObject as NestedSOCollectionBase;
			if (targetCollection == null) return;

			LoadNavigationState(targetCollection);

			_searchField = new SearchField();
			_searchField.downOrUpArrowKeyPressed += OnSearchFocus;

			Type baseType = GetBaseTypeFromCollection(targetCollection);
			if(baseType == null) return;

			_baseType = baseType;
			_baseTypeName = _baseType.Name;
			_menu = GenericMenuEditorUtils.CreateSOWindow(_baseType, OnItemToCreateSelected);
			
			_nestedSOsProperty = serializedObject.FindProperty(NestedSOItemsFieldName);
			
			_orderableList = new ReorderableList(serializedObject, _nestedSOsProperty, true, true, false, false);
			_orderableList.drawElementCallback = OnDrawListElement;
			_orderableList.drawHeaderCallback = OnDrawListHeader;

			EnsureRootIsTarget(targetCollection);
		}

		private void EnsureRootIsTarget(UnityEngine.Object target)
		{
			if (_breadcrumbs.Count == 0)
			{
				_breadcrumbs.Add(target);
			}
			else if (_breadcrumbs[0] != target)
			{
				_breadcrumbs.Clear();
				_breadcrumbs.Add(target);
			}
		}

		public override void OnInspectorGUI()
		{
			if(_baseType == null)
			{
				GUILayout.Label("Unable to Load BaseType");
				return;
			}

			DrawBreadcrumbs();
			GUILayout.Space(5);

			EditorGUILayout.BeginVertical("framebox");
			{
				DrawHeaderArea();

				GUILayout.Space(5);
				serializedObject.Update();

				if (!string.IsNullOrEmpty(_searchString))
				{
					DrawMassEditInterface();
					DrawSearchResults();
				}
				else
				{
					UnityEngine.Object currentViewItem = (_breadcrumbs.Count > 0) ? _breadcrumbs[_breadcrumbs.Count - 1] : serializedObject.targetObject;

					if (currentViewItem == serializedObject.targetObject)
					{
						if (_orderableList != null) _orderableList.DoLayoutList();
					}
					else
					{
						DrawDeepDiveInspector(currentViewItem);
					}
				}

				serializedObject.ApplyModifiedProperties();
			}
			EditorGUILayout.EndVertical();
		}

		protected void OnDisable()
		{
			SaveNavigationState();
		}

		#endregion

		#region Drawing Methods

		private void DrawHeaderArea()
		{
			EditorGUILayout.BeginHorizontal();
			{
				UnityEngine.Object currentItem = (_breadcrumbs.Count > 0) ? _breadcrumbs[_breadcrumbs.Count - 1] : serializedObject.targetObject;
				bool isRoot = currentItem == serializedObject.targetObject;
				
				string title = isRoot ? $"{_baseTypeName} Collection" : currentItem.name;

				EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.Width(EditorGUIUtility.labelWidth));
				
				GUILayout.FlexibleSpace();

				Rect searchRect = GUILayoutUtility.GetRect(100, 300, 20, 20, GUILayout.MinWidth(100));
				string newSearch = _searchField.OnGUI(searchRect, _searchString);
				if (newSearch != _searchString)
				{
					_searchString = newSearch;
					PerformSearch();
				}

				GUILayout.Space(5);

				if (isRoot && string.IsNullOrEmpty(_searchString))
				{
					if (IconButton("CollabCreate Icon", 20))
					{
						_menu.ShowAsContext();
					}
				}
			}
			EditorGUILayout.EndHorizontal();
		}

		private void DrawBreadcrumbs()
		{
			if (_breadcrumbs == null || _breadcrumbs.Count == 0) return;

			GUILayout.BeginHorizontal(EditorStyles.toolbar);
			
			GUIStyle breadcrumbStyle = new GUIStyle(EditorStyles.toolbarButton);
			breadcrumbStyle.alignment = TextAnchor.MiddleLeft;
			breadcrumbStyle.richText = true;

			for (int i = 0; i < _breadcrumbs.Count; i++)
			{
				var obj = _breadcrumbs[i];
				if (obj == null) continue;

				string name = obj.name;
				if (name.Length > 20) name = name.Substring(0, 17) + "...";

				if (i == _breadcrumbs.Count - 1)
				{
					GUILayout.Label(name, breadcrumbStyle, GUILayout.MaxWidth(150));
				}
				else
				{
					if (GUILayout.Button(name, breadcrumbStyle, GUILayout.MaxWidth(150)))
					{
						NavigateToBreadcrumbIndex(i);
					}
					GUILayout.Label(">", EditorStyles.miniLabel, GUILayout.Width(10));
				}
			}

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void DrawDeepDiveInspector(UnityEngine.Object item)
		{
			if (item == null)
			{
				EditorGUILayout.HelpBox("Item is null or missing.", MessageType.Warning);
				return;
			}

			if (!_cachedEditors.TryGetValue(item, out Editor editor))
			{
				_cachedEditors[item] = editor = CreateEditor(item);
			}

			EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
			{
				EditorGUI.BeginChangeCheck();
				string newName = EditorGUILayout.TextField("Name", item.name);
				if (EditorGUI.EndChangeCheck())
				{
					item.name = newName;
					EditorUtility.SetDirty(item);
				}

				GUILayout.Space(10);
				
				if (editor != null)
				{
					editor.OnInspectorGUI();
				}
			}
			EditorGUILayout.EndVertical();
		}

		private void DrawMassEditInterface()
		{
			if (_filteredItems.Count == 0) return;

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			_massEditExpanded = EditorGUILayout.Foldout(_massEditExpanded, $"Mass Edit ({_filteredItems.Count} items)", true);
			
			if (_massEditExpanded)
			{
				EditorGUILayout.BeginHorizontal();
				
				SerializedObject representative = new SerializedObject(_filteredItems[0].Item);
				List<string> props = new List<string>();
				SerializedProperty iter = representative.GetIterator();
				
				if (iter.NextVisible(true))
				{
					while (iter.NextVisible(false))
					{
						if (iter.name == "m_Script") continue;
						props.Add(iter.name);
					}
				}

				int selectedIndex = props.IndexOf(_massEditSelectedPropertyPath);
				int newIndex = EditorGUILayout.Popup("Property", selectedIndex, props.ToArray());
				
				if (newIndex != selectedIndex && newIndex >= 0 && newIndex < props.Count)
				{
					_massEditSelectedPropertyPath = props[newIndex];
				}

				EditorGUILayout.EndHorizontal();

				if (!string.IsNullOrEmpty(_massEditSelectedPropertyPath))
				{
					SerializedProperty targetProp = representative.FindProperty(_massEditSelectedPropertyPath);
					if (targetProp != null)
					{
						EditorGUILayout.BeginHorizontal();
						
						EditorGUI.BeginChangeCheck();
						EditorGUILayout.PropertyField(targetProp, new GUIContent("New Value"), true);
						bool valueChanged = EditorGUI.EndChangeCheck();

						if (valueChanged)
						{
							representative.ApplyModifiedProperties(); 
						}
						
						if (GUILayout.Button("Apply to All", GUILayout.Width(100)))
						{
							ApplyMassEdit(representative, targetProp);
						}
						
						EditorGUILayout.EndHorizontal();
					}
				}
			}
			EditorGUILayout.EndVertical();
			GUILayout.Space(5);
		}

		private void DrawSearchResults()
		{
			if (_filteredItems.Count == 0)
			{
				GUILayout.Label("No results found.", EditorStyles.centeredGreyMiniLabel);
				return;
			}

			GUIStyle highlightStyle = new GUIStyle(EditorStyles.miniLabel);
			highlightStyle.richText = true;
			highlightStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);

			for (int i = 0; i < _filteredItems.Count; i++)
			{
				SearchMatch match = _filteredItems[i];
				ScriptableObject item = match.Item;
				if (item == null) continue;

				EditorGUILayout.BeginVertical("helpBox");
				{
					// Top Row: Name + Open Button
					EditorGUILayout.BeginHorizontal();
					{
						GUILayout.Label(item.name, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
						if (GUILayout.Button("Open", GUILayout.Width(60)))
						{
							OpenItem(item);
						}
					}
					EditorGUILayout.EndHorizontal();

					// Bottom Row: Highlighted Matches
					if (match.MatchDetails.Count > 0)
					{
						EditorGUI.indentLevel++;
						foreach (string detail in match.MatchDetails)
						{
							GUILayout.Label(detail, highlightStyle);
						}
						EditorGUI.indentLevel--;
					}
				}
				EditorGUILayout.EndVertical();
			}
		}

		private void OnDrawListHeader(Rect rect)
		{
			EditorGUI.LabelField(rect, "Items");
		}

		private void OnDrawListElement(Rect rect, int index, bool isActive, bool isFocused)
		{
			SerializedProperty serializedNestedItem = _orderableList.serializedProperty.GetArrayElementAtIndex(index);
			ScriptableObject nestedItem = serializedNestedItem.objectReferenceValue as ScriptableObject;
			
			if (nestedItem == null) 
			{
				EditorGUI.LabelField(rect, "Null Item");
				return;
			}

			string objectName = nestedItem.name;
			float btnWidth = 24;
			float padding = 2;
			float nameWidth = rect.width - (btnWidth * 2) - (padding * 2);

			string newObjectName = EditorGUI.TextField(new Rect(rect.x, rect.y + 1, nameWidth, EditorGUIUtility.singleLineHeight), objectName);
			if (objectName != newObjectName)
			{
				nestedItem.name = newObjectName;
				AssetDatabase.SaveAssets();
				EditorUtility.SetDirty(nestedItem);
			}

			Rect openBtnRect = new Rect(rect.x + rect.width - (btnWidth * 2) - padding, rect.y, btnWidth, EditorGUIUtility.singleLineHeight);
			Rect deleteBtnRect = new Rect(rect.x + rect.width - btnWidth, rect.y, btnWidth, EditorGUIUtility.singleLineHeight);

			if (IconButton(openBtnRect, "d_scenepicking_pickable_hover"))
			{
				OpenItem(nestedItem);
			}

			if (IconButton(deleteBtnRect, "CollabDeleted Icon"))
			{
				RemoveAssetFromCollection(serializedObject.targetObject as NestedSOCollectionBase, nestedItem);
			}
		}

		#endregion

		#region Logic & Helpers

		private void OpenItem(ScriptableObject item)
		{
			_searchString = "";
			GUI.FocusControl(null); 

			// Ensure flat navigation (Root -> Item)
			if (_breadcrumbs.Count == 1)
			{
				_breadcrumbs.Add(item);
			}
			else
			{
				UnityEngine.Object root = _breadcrumbs[0];
				_breadcrumbs.Clear();
				_breadcrumbs.Add(root);
				_breadcrumbs.Add(item);
			}
			
			SaveNavigationState();
		}

		private void NavigateToBreadcrumbIndex(int index)
		{
			if (index >= 0 && index < _breadcrumbs.Count)
			{
				int countToRemove = _breadcrumbs.Count - 1 - index;
				if (countToRemove > 0)
				{
					_breadcrumbs.RemoveRange(index + 1, countToRemove);
				}
				SaveNavigationState();
				
				_searchString = "";
				GUI.FocusControl(null);
			}
		}

		private void OnSearchFocus() { }

		private void PerformSearch()
		{
			_filteredItems.Clear();
			if (_nestedSOsProperty == null || !_nestedSOsProperty.isArray) return;

			string lowerSearch = _searchString.ToLowerInvariant();

			for (int i = 0; i < _nestedSOsProperty.arraySize; i++)
			{
				SerializedProperty prop = _nestedSOsProperty.GetArrayElementAtIndex(i);
				ScriptableObject item = prop.objectReferenceValue as ScriptableObject;

				if (item == null) continue;

				List<string> matchDetails = new List<string>();
				bool nameMatch = item.name.ToLowerInvariant().Contains(lowerSearch);
				
				SerializedObject so = new SerializedObject(item);
				SerializedProperty iter = so.GetIterator();
				
				if (iter.NextVisible(true))
				{
					while (iter.NextVisible(false))
					{
						if (iter.name == "m_Script") continue;

						bool valMatch = false;
						string valStr = "";
						string displayValue = "";

						switch(iter.propertyType)
						{
							case SerializedPropertyType.String: 
								valStr = iter.stringValue; 
								displayValue = valStr;
								break;
							case SerializedPropertyType.Integer: 
								valStr = iter.intValue.ToString(); 
								displayValue = valStr;
								break;
							case SerializedPropertyType.Float: 
								valStr = iter.floatValue.ToString(); 
								displayValue = valStr;
								break;
							case SerializedPropertyType.Boolean: 
								valStr = iter.boolValue.ToString(); 
								displayValue = valStr;
								break;
							case SerializedPropertyType.Enum: 
								if(iter.enumValueIndex >= 0 && iter.enumValueIndex < iter.enumNames.Length)
								{
									valStr = iter.enumNames[iter.enumValueIndex];
									displayValue = valStr;
								}
								break;
						}

						// Check if value matches
						if (!string.IsNullOrEmpty(valStr) && valStr.ToLowerInvariant().Contains(lowerSearch))
						{
							valMatch = true;
						}

						// Check if Property Name matches
						bool propNameMatch = iter.displayName.ToLowerInvariant().Contains(lowerSearch);

						if (valMatch || propNameMatch)
						{
							string formattedDetail = "";
							string propLabel = iter.displayName;

							if (valMatch)
							{
								// Highlight value
								formattedDetail = $"{propLabel}: <color=#FFFF00>{displayValue}</color>";
							}
							else
							{
								// Matched property name only
								formattedDetail = $"<color=#FFFF00>{propLabel}</color>: {displayValue}";
							}
							
							matchDetails.Add(formattedDetail);
						}
					}
				}

				if (nameMatch || matchDetails.Count > 0)
				{
					_filteredItems.Add(new SearchMatch 
					{ 
						Item = item, 
						MatchDetails = matchDetails 
					});
				}
			}

			// Auto-select Mass Edit
			if (!string.IsNullOrEmpty(_searchString) && _filteredItems.Count > 0)
			{
				SerializedObject firstSO = new SerializedObject(_filteredItems[0].Item);
				SerializedProperty prop = firstSO.FindProperty(_searchString); 
				if (prop != null)
				{
					_massEditSelectedPropertyPath = prop.name;
				}
			}
		}

		private void ApplyMassEdit(SerializedObject sourceObj, SerializedProperty sourceProp)
		{
			foreach (var match in _filteredItems)
			{
				SerializedObject so = new SerializedObject(match.Item);
				SerializedProperty prop = so.FindProperty(sourceProp.name);
				
				if (prop != null && prop.propertyType == sourceProp.propertyType)
				{
					switch (sourceProp.propertyType)
					{
						case SerializedPropertyType.Integer: prop.intValue = sourceProp.intValue; break;
						case SerializedPropertyType.Boolean: prop.boolValue = sourceProp.boolValue; break;
						case SerializedPropertyType.Float: prop.floatValue = sourceProp.floatValue; break;
						case SerializedPropertyType.String: prop.stringValue = sourceProp.stringValue; break;
						case SerializedPropertyType.Color: prop.colorValue = sourceProp.colorValue; break;
						case SerializedPropertyType.ObjectReference: prop.objectReferenceValue = sourceProp.objectReferenceValue; break;
						case SerializedPropertyType.Enum: prop.enumValueIndex = sourceProp.enumValueIndex; break;
						case SerializedPropertyType.Vector2: prop.vector2Value = sourceProp.vector2Value; break;
						case SerializedPropertyType.Vector3: prop.vector3Value = sourceProp.vector3Value; break;
					}
					so.ApplyModifiedProperties();
				}
			}
			AssetDatabase.SaveAssets();
		}

		private void OnItemToCreateSelected(object userData)
		{
			NestedSOCollectionBase collection = serializedObject.targetObject as NestedSOCollectionBase;
			Type type = userData as Type;
			AddAssetToCollection(collection, type);
		}

		#endregion

		#region Static Public API

		public static void RemoveAssetFromCollection(NestedSOCollectionBase collection, ScriptableObject asset)
		{
			if(!collection._HasAsset(asset)) throw new Exception($"Collection {collection} does not contains asset {asset}");
			RemoveAssetRecursive(collection, asset);
			AssetDatabase.SaveAssets();
			EditorUtility.SetDirty(collection);
		}

		public static T AddAssetToCollection<T>(NestedSOCollectionBase collection) where T : ScriptableObject
		{
			return AddAssetToCollection(collection, typeof(T)) as T;
		}

		public static ScriptableObject AddAssetToCollection(NestedSOCollectionBase collection, Type type)
		{
			Type baseType = GetBaseTypeFromCollection(collection);
			if(baseType == null) throw new Exception($"No BaseType could be found for {collection}");
			if(!baseType.IsAssignableFrom(type)) throw new Exception($"The collection requires BaseType {baseType}, which {type} does not derive from");
			
			ScriptableObject nestedSOItemInstance = CreateInstance(type);
			nestedSOItemInstance.name = "New " + type.Name;
			collection._AddAsset(nestedSOItemInstance);
			
			AssetDatabase.AddObjectToAsset(nestedSOItemInstance, collection);
			AssetDatabase.SaveAssets();

			EditorUtility.SetDirty(collection);
			EditorUtility.SetDirty(nestedSOItemInstance);

			collection._MarkAsAddedAsset(nestedSOItemInstance);
			return nestedSOItemInstance;
		}

		public static Type GetBaseTypeFromCollection(NestedSOCollectionBase collection)
		{
			if(collection == null) return null;
			Type baseType;
			try { baseType = collection.GetType(); } catch { baseType = null; }
			Type[] types = null;
			while(baseType != null && (types == null || types.Length == 0))
			{
				baseType = baseType.BaseType;
				if(baseType != null && baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(NestedSOCollectionBase<>))
				{
					types = baseType.GetGenericArguments();
				}
			}
			if(types == null || types.Length == 0) return null;
			return types[0];
		}

		#endregion

		#region Internal Utilities

		private static void RemoveAssetRecursive(NestedSOCollectionBase collection, ScriptableObject nestedItem)
		{
			if(nestedItem is NestedSOCollectionBase internalCollection)
			{
				IReadOnlyList<ScriptableObject> internalCollectionItems = internalCollection.GetRawItems();
				for(int i = internalCollectionItems.Count - 1; i >= 0; i--)
				{
					RemoveAssetRecursive(internalCollection, internalCollectionItems[i]);
				}
			}
			collection._RemoveAsset(nestedItem);
			AssetDatabase.RemoveObjectFromAsset(nestedItem);
			EditorUtility.SetDirty(nestedItem);
			collection._MarkAsRemovedAsset(nestedItem);
		}

		private void SaveNavigationState()
		{
			UnityEngine.Object target = serializedObject.targetObject;
			string key = $"NestedSOEditor_{target.GetInstanceID()}";
			StringBuilder dataSB = new StringBuilder();
			for (int i = 0; i < _breadcrumbs.Count; i++)
			{
				UnityEngine.Object obj = _breadcrumbs[i];
				if (obj != null)
				{
					if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
					{
						dataSB.Append(obj.GetInstanceID());
						if (i < _breadcrumbs.Count - 1) dataSB.Append('/');
					}
				}
			}
			EditorPrefs.SetString(key, dataSB.ToString());
		}

		private void LoadNavigationState(UnityEngine.Object target)
		{
			string key = $"NestedSOEditor_{target.GetInstanceID()}";
			if (EditorPrefs.HasKey(key))
			{
				string data = EditorPrefs.GetString(key);
				string[] instanceIds = data.Split('/');
				_breadcrumbs.Clear();
				for (int i = 0; i < instanceIds.Length; i++)
				{
					if (int.TryParse(instanceIds[i], out int instanceID))
					{
						var obj = EditorUtility.EntityIdToObject(instanceID);
						if (obj != null) _breadcrumbs.Add(obj);
					}
				}
			}
		}

		private bool IconButton(Rect rect, string icon)
		{
			return GUI.Button(rect, EditorGUIUtility.FindTexture(icon), new GUIStyle("label"));
		}

		private bool IconButton(string icon, float size)
		{
			return GUILayout.Button(EditorGUIUtility.FindTexture(icon), new GUIStyle("label"), GUILayout.MaxWidth(size), GUILayout.MaxHeight(size));
		}

		#endregion
	}
}
#endif