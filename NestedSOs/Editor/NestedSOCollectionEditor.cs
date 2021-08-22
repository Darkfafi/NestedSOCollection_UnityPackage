using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(NestedSOCollectionBase<>), true)]
public class NestedSOCollectionEditor : Editor
{
	#region Consts

	public const string NestedSOItemsFieldName = "_nestedSOItems";

	#endregion

	#region Variables

	private static List<UnityEngine.Object> _breadcrumbs = new List<UnityEngine.Object>();
	private ReorderableList _orderableList;
	private GenericMenu _menu = null;
	private Type _baseType;
	private string _baseTypeName;

	#endregion

	#region Lifecycle

	protected void OnEnable()
	{
		Load();

		_baseType = serializedObject.targetObject.GetType().BaseType.GetGenericArguments()[0];
		_baseTypeName = _baseType.Name;
		_menu = GenericMenuEditorUtils.CreateSOWindow(_baseType, OnItemToCreateSelected);

		_orderableList = new ReorderableList(serializedObject, serializedObject.FindProperty(NestedSOItemsFieldName), true, true, false, false);
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

		if (_orderableList != null)
		{
			GUILayout.Space(15);
			EditorGUILayout.LabelField("NestedSOCollection Editor", EditorStyles.boldLabel);
			GUILayout.Space(5);

			StringBuilder sb = new StringBuilder();

			for (int i = _breadcrumbs.Count - 1; i >= 0; i--)
			{
				if (i < _breadcrumbs.Count - 1)
				{
					sb.Append("←");
				}
				sb.Append(_breadcrumbs[i].name);
			}

			GUILayout.Label(sb.ToString());

			serializedObject.Update();
			_orderableList.DoLayoutList();
			serializedObject.ApplyModifiedProperties();
		}
	}

	#endregion

	#region Private Methods

	private void OnDrawHeader(Rect rect)
	{
		GUILayout.BeginHorizontal();
		{
			EditorGUI.LabelField(rect, _baseTypeName + " Collection");

			int currentWidth = 0;
			int btnSize = 20;

			if (IconButton(new Rect(rect.width, rect.y, btnSize, EditorGUIUtility.singleLineHeight), "CollabCreate Icon"))
			{
				_menu.ShowAsContext();
			}

			currentWidth += btnSize;

			if (_breadcrumbs.Count > 1)
			{
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
			}
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
			INestedSOCollection collection = serializedObject.targetObject as INestedSOCollection;
			RemoveItem(collection, nestedItem);
			AssetDatabase.SaveAssets();
			EditorUtility.SetDirty(serializedObject.targetObject);
		}

		if (IconButton(new Rect(rect.x + rect.width - bWidth - currentWidth, rect.y, bWidth, EditorGUIUtility.singleLineHeight), "d_CollabEdit Icon"))
		{
			Selection.activeObject = nestedItem;
			_breadcrumbs.Add(nestedItem);
			Save();
		}
	}

	private void RemoveItem(INestedSOCollection collection, ScriptableObject nestedItem)
	{
		if (nestedItem is INestedSOCollection internalCollection)
		{
			IReadOnlyList<ScriptableObject> internalCollectionItems = internalCollection.GetRawItems();
			for (int i = internalCollectionItems.Count - 1; i >= 0; i--)
			{
				RemoveItem(internalCollection, internalCollectionItems[i]);
			}
		}

		collection.RemoveAsset(nestedItem);
		AssetDatabase.RemoveObjectFromAsset(nestedItem);
		EditorUtility.SetDirty(nestedItem);
	}

	private void OnItemToCreateSelected(object userData)
	{
		INestedSOCollection collection = serializedObject.targetObject as INestedSOCollection;

		Type type = userData as Type;
		ScriptableObject nestedSOItemInstance = CreateInstance(type);
		nestedSOItemInstance.name = "New " + type.Name;
		collection.AddAsset(nestedSOItemInstance);

		AssetDatabase.AddObjectToAsset(nestedSOItemInstance, serializedObject.targetObject);
		AssetDatabase.SaveAssets();

		EditorUtility.SetDirty(serializedObject.targetObject);
		EditorUtility.SetDirty(nestedSOItemInstance);
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

	#endregion
}