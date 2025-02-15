﻿#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace NestedSO.SOEditor
{
	[CustomEditor(typeof(NestedSOCollectionBase), true)]
	public class NestedSOCollectionEditor : Editor
	{
		#region Consts

		public const string NestedSOItemsFieldName = "_nestedSOItems";

		#endregion

		#region Variables

		private static List<UnityEngine.Object> _breadcrumbs = new List<UnityEngine.Object>();
		private ReorderableList _orderableList;
		private SerializedProperty _nestedSOsProperty;
		private GenericMenu _menu = null;
		private Type _baseType;
		private string _baseTypeName;
		private bool _editMode = true;
		private Dictionary<UnityEngine.Object, Editor> _cachedEditors = new Dictionary<UnityEngine.Object, Editor>();
		private bool _isGlobalExpanded = false;

		#endregion

		#region Lifecycle

		protected void OnEnable()
		{
			Load();

			Type baseType = GetBaseTypeFromCollection(serializedObject.targetObject as NestedSOCollectionBase);

			if(baseType == null)
			{
				return;
			}

			_baseType = baseType;

			_baseTypeName = _baseType.Name;
			_menu = GenericMenuEditorUtils.CreateSOWindow(_baseType, OnItemToCreateSelected);
			_nestedSOsProperty = serializedObject.FindProperty(NestedSOItemsFieldName);
			_orderableList = new ReorderableList(serializedObject, _nestedSOsProperty, true, true, false, false);
			_orderableList.drawElementCallback = OnDrawNestedItem;
			_orderableList.drawHeaderCallback = OnDrawHeader;


			// If the current item is not within the bread crumbs, clear the bread crumbs for it is a new tree
			if (!_breadcrumbs.Contains(serializedObject.targetObject))
			{
				_breadcrumbs.Clear();
				_breadcrumbs.Add(serializedObject.targetObject);
			}
			else
			{
				// While the current item is within the breadcrumbs and not the peak, make it the peak
				while (_breadcrumbs.Contains(serializedObject.targetObject) && _breadcrumbs[_breadcrumbs.Count - 1] != serializedObject.targetObject)
				{
					_breadcrumbs.RemoveAt(_breadcrumbs.Count - 1);
				}
			}
			Save();
		}

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			if(_baseType == null)
			{
				GUILayout.Label("Unable to Load BaseType");
				return;
			}

			GUILayout.Space(15);

			EditorGUILayout.BeginVertical("framebox");
			{
				EditorGUILayout.BeginHorizontal();
				{
					EditorGUILayout.LabelField(_baseTypeName + " Collection", EditorStyles.boldLabel);
					if(_editMode)
					{
						if (IconButton(_isGlobalExpanded ? "scenevis_visible_hover@2x" : "scenevis_visible@2x", 20f))
						{
							_isGlobalExpanded = !_isGlobalExpanded;

							if (_nestedSOsProperty != null && _nestedSOsProperty.isArray)
							{
								for (int i = 0; i < _nestedSOsProperty.arraySize; i++)
								{
									var element = _nestedSOsProperty.GetArrayElementAtIndex(i);
									if(element != null)
									{
										element.isExpanded = _isGlobalExpanded;
									}
								}
							}
						}
					}
					if (IconButton("CollabCreate Icon", 20))
					{
						_menu.ShowAsContext();
					}
					if (IconButton(_editMode ? "CollabMoved Icon" : "CollabEdit Icon", 20))
					{
						_editMode = !_editMode;
					}
				}
				EditorGUILayout.EndHorizontal();
				GUILayout.Space(5);

				serializedObject.Update();

				if(_editMode)
				{
					if (_nestedSOsProperty != null && _nestedSOsProperty.isArray)
					{
						int size = _nestedSOsProperty.arraySize;
						for (int i = 0; i < size; i++)
						{
							if(_nestedSOsProperty == null)
							{
								break;
							}

							try
							{
								if(_nestedSOsProperty.arraySize != size)
								{
									break;
								}
							}
							catch
							{
								return;
							}

							DrawSO(_nestedSOsProperty.GetArrayElementAtIndex(i));
						}
					}
				}
				else if (_orderableList != null)
				{
					_orderableList.DoLayoutList();
				}
				serializedObject.ApplyModifiedProperties();
			}
			EditorGUILayout.EndVertical();
		}

		#endregion

		#region Public Methods

		public static void RemoveAssetFromCollection(NestedSOCollectionBase collection, ScriptableObject asset)
		{
			if(!collection._HasAsset(asset))
			{
				throw new Exception($"Collection {collection} does not contains asset {asset}");
			}

			RemoveAssetRecursive(collection, asset);
			AssetDatabase.SaveAssets();
			EditorUtility.SetDirty(collection);
		}

		public static T AddAssetToCollection<T>(NestedSOCollectionBase collection)
			where T : ScriptableObject
		{
			return AddAssetToCollection(collection, typeof(T)) as T;
		}

		public static ScriptableObject AddAssetToCollection(NestedSOCollectionBase collection, Type type)
		{
			Type baseType = GetBaseTypeFromCollection(collection);

			if(baseType == null)
			{
				throw new Exception($"No BaseType could be found for {collection}");
			}

			if(!baseType.IsAssignableFrom(type))
			{
				throw new Exception($"The collection requires BaseType {baseType}, which {type} does not derive from");
			}

			if(type.IsAbstract)
			{
				throw new Exception($"{type} is abstract, which can't be used to Create an Asset.");
			}

			if(type.IsInterface)
			{
				throw new Exception($"{type} is an interface, which can't be used to Create an Asset.");
			}

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
			if(collection == null)
			{
				return null;
			}

			Type baseType;
			try
			{
				baseType = collection.GetType();
			}
			catch
			{
				baseType = null;
			}

			Type[] types = null;

			while(baseType != null && (types == null || types.Length == 0))
			{
				baseType = baseType.BaseType;
				if(baseType != null && baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(NestedSOCollectionBase<>))
				{
					types = baseType.GetGenericArguments();
				}
			}

			if(types == null || types.Length == 0)
			{
				return null;
			}

			return types[0];
		}

		#endregion

		#region Private Methods

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

		private void DrawSO(SerializedProperty serializedProp)
		{
			var item = serializedProp.objectReferenceValue;

			if(item == null)
			{
				EditorGUILayout.LabelField("Item Empty");
				return;
			}

			EditorGUILayout.BeginHorizontal("helpBox");
			{
				item.name = EditorGUILayout.TextField(item.name);
				if(IconButton(serializedProp.isExpanded ? "scenevis_visible_hover@2x" : "scenevis_visible@2x", 20f))
				{
					serializedProp.isExpanded = !serializedProp.isExpanded;
				}
			}
			EditorGUILayout.EndHorizontal();
			if (serializedProp.isExpanded)
			{
				if (!_cachedEditors.TryGetValue(item, out Editor editor))
				{
					_cachedEditors[item] = editor = CreateEditor(item);
				}
				EditorGUILayout.BeginVertical("frameBox");
				{
					editor.OnInspectorGUI();
					editor.serializedObject.ApplyModifiedProperties();
				}
				EditorGUILayout.EndVertical();
			}
		}

		private void OnDrawHeader(Rect rect)
		{
			GUILayout.BeginHorizontal();
			{
				int currentWidth = 0;
				int btnSize = 20;

				if (_breadcrumbs.Count > 1)
				{
					currentWidth += btnSize;

					if (IconButton(new Rect(rect.width - currentWidth, rect.y, btnSize, EditorGUIUtility.singleLineHeight), "back@2x"))
					{
						Selection.activeObject = _breadcrumbs[_breadcrumbs.Count - 2];
					}

					currentWidth += btnSize;

					if (_breadcrumbs.Count > 2)
					{
						if (IconButton(new Rect(rect.width - currentWidth, rect.y, btnSize, EditorGUIUtility.singleLineHeight), "beginButton"))
						{
							Selection.activeObject = _breadcrumbs[0];
						}
					}

					currentWidth += btnSize * 2;
				}

				StringBuilder sb = new StringBuilder();

				for (int i = _breadcrumbs.Count - 1; i >= 0; i--)
				{
					if (i < _breadcrumbs.Count - 1)
					{
						sb.Append("←");
					}
					sb.Append(_breadcrumbs[i].name);
				}

				GUI.Label(new Rect(rect.x, rect.y, rect.width - currentWidth, EditorGUIUtility.singleLineHeight), sb.ToString());
			}
			GUILayout.EndHorizontal();
		}

		private void OnDrawNestedItem(Rect rect, int index, bool isActive, bool isFocused)
		{
			SerializedProperty serializedNestedItem = _orderableList.serializedProperty.GetArrayElementAtIndex(index);
			ScriptableObject nestedItem = serializedNestedItem.objectReferenceValue as ScriptableObject;

			int currentWidth = 0;

			string objectName = serializedNestedItem.objectReferenceValue.name;
			string newObjectName = EditorGUI.TextField(new Rect(rect.x, rect.y, currentWidth += 250, EditorGUIUtility.singleLineHeight), objectName);
			if (objectName != newObjectName)
			{
				nestedItem.name = newObjectName;
				AssetDatabase.SaveAssets();
				EditorUtility.SetDirty(nestedItem);
			}

			int bWidth = 20;
			currentWidth = bWidth;
			if (IconButton(new Rect(rect.x + rect.width - bWidth, rect.y, bWidth, EditorGUIUtility.singleLineHeight), "CollabDeleted Icon"))
			{
				RemoveAssetFromCollection(serializedObject.targetObject as NestedSOCollectionBase, nestedItem);
			}

			if (IconButton(new Rect(rect.x + rect.width - bWidth - currentWidth, rect.y, bWidth, EditorGUIUtility.singleLineHeight), "d_CollabEdit Icon"))
			{
				Selection.activeObject = nestedItem;
				_breadcrumbs.Add(nestedItem);
				Save();
			}
		}

		private void OnItemToCreateSelected(object userData)
		{
			NestedSOCollectionBase collection = serializedObject.targetObject as NestedSOCollectionBase;
			Type type = userData as Type;
			AddAssetToCollection(collection, type);

			serializedObject.Update();

			if(_nestedSOsProperty != null && _nestedSOsProperty.isArray && _nestedSOsProperty.arraySize > 0)
			{
				SerializedProperty prop = _nestedSOsProperty.GetArrayElementAtIndex(_nestedSOsProperty.arraySize - 1);
				prop.isExpanded = _isGlobalExpanded;
			}
		}

		private void Save()
		{
			StringBuilder dataSB = new StringBuilder();
			for (int i = 0; i < _breadcrumbs.Count; i++)
			{
				UnityEngine.Object obj = _breadcrumbs[i];
				if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
				{
					dataSB.Append(obj.GetInstanceID());
					if (i < _breadcrumbs.Count - 1)
					{
						dataSB.Append('/');
					}
				}
			}
			EditorPrefs.SetString(nameof(NestedSOCollectionEditor), dataSB.ToString());
		}

		private void Load()
		{
			if (EditorPrefs.HasKey(nameof(NestedSOCollectionEditor)))
			{
				string data = EditorPrefs.GetString(nameof(NestedSOCollectionEditor));
				string[] instanceIds = data.Split('/');
				_breadcrumbs.Clear();
				for (int i = 0; i < instanceIds.Length; i++)
				{
					if (int.TryParse(instanceIds[i], out int instanceID))
					{
						_breadcrumbs.Add(EditorUtility.InstanceIDToObject(instanceID));
					}
				}
				EditorPrefs.DeleteKey(nameof(NestedSOCollectionEditor));
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