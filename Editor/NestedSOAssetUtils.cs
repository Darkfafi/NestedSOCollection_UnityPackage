#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NestedSO.SOEditor
{
	public static class NestedSOAssetUtils
	{
		public static void DestroyAsset(ScriptableObject asset)
		{
			if (asset == null) return;

			// 1. Destroy items in Collections
			if (asset is NestedSOCollectionBase collectionBase)
			{
				var items = collectionBase.GetRawItems();
				for (int i = items.Count - 1; i >= 0; i--)
				{
					DestroyAsset(items[i]);
				}
			}

			// 2. Destroy items in Nested Lists (Inheritance Supported)
			var fieldsList = new List<System.Reflection.FieldInfo>();
			Type currentType = asset.GetType();

			// Walk up the inheritance chain to grab all private/public fields
			while (currentType != null && currentType != typeof(ScriptableObject) && currentType != typeof(UnityEngine.Object))
			{
				var declaredFields = currentType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
				foreach (var f in declaredFields)
				{
					if (typeof(NestedSOListBase).IsAssignableFrom(f.FieldType))
					{
						fieldsList.Add(f);
					}
				}
				currentType = currentType.BaseType;
			}

			foreach (var field in fieldsList)
			{
				var listWrapper = field.GetValue(asset);
				if (listWrapper != null)
				{
					// Safely find the "Items" field by checking base types as well
					System.Reflection.FieldInfo itemsField = null;
					Type searchFieldType = field.FieldType;

					while (searchFieldType != null)
					{
						itemsField = searchFieldType.GetField("Items", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
						if (itemsField != null) break;
						searchFieldType = searchFieldType.BaseType;
					}

					if (itemsField != null)
					{
						if (itemsField.GetValue(listWrapper) is System.Collections.IList list)
						{
							for (int i = list.Count - 1; i >= 0; i--)
							{
								var child = list[i] as ScriptableObject;
								if (child != null)
								{
									DestroyAsset(child);
								}
							}
						}
					}
				}
			}

			// 3. Destroy the actual asset itself
			UnityEditor.AssetDatabase.RemoveObjectFromAsset(asset);
			UnityEngine.Object.DestroyImmediate(asset, true);
		}
	}
}
#endif