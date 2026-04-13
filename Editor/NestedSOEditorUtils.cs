#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace NestedSO.SOEditor
{
	public static class NestedSOEditorUtils
	{
		public static void PushAssetToCollection(NestedSOCollectionBase collection, ScriptableObject externalAsset)
		{
			ScriptableObject clone = NestedSOEditorUtils.PushExternalAsset(externalAsset, collection);
			if (clone != null)
			{
				collection._AddAsset(clone);
				collection._MarkAsAddedAsset(clone);
				AssetDatabase.SaveAssets();
				EditorUtility.SetDirty(collection);
			}
		}

		public static void PopAssetFromCollection(NestedSOCollectionBase collection, ScriptableObject subAsset)
		{
			if (NestedSOEditorUtils.PopSubAsset(subAsset, out _))
			{
				collection._RemoveAsset(subAsset);
				collection._MarkAsRemovedAsset(subAsset);
				AssetDatabase.SaveAssets();
				EditorUtility.SetDirty(collection);
			}
		}

		public static void RemoveAssetFromCollection(NestedSOCollectionBase collection, ScriptableObject asset)
		{
			if (!collection._HasAsset(asset)) throw new Exception($"Collection {collection} does not contain asset {asset}");
			collection._RemoveAsset(asset);
			collection._MarkAsRemovedAsset(asset);
			NestedSOEditorUtils.DestroyAsset(asset);
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
			if (baseType == null) throw new Exception($"No BaseType could be found for {collection}");
			if (!baseType.IsAssignableFrom(type)) throw new Exception($"The collection requires BaseType {baseType}, which {type} does not derive from");

			ScriptableObject nestedSOItemInstance = ScriptableObject.CreateInstance(type);
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
			if (collection == null) return null;
			Type baseType;
			try { baseType = collection.GetType(); } catch { baseType = null; }
			Type[] types = null;
			while (baseType != null && (types == null || types.Length == 0))
			{
				baseType = baseType.BaseType;
				if (baseType != null && baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(NestedSOCollectionBase<>))
					types = baseType.GetGenericArguments();
			}
			if (types == null || types.Length == 0) return null;
			return types[0];
		}

		public static List<ScriptableObject> GetNestedAssetsRecursive(ScriptableObject root)
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

			var fieldsList = new List<FieldInfo>();
			Type currentType = root.GetType();

			while (currentType != null && currentType != typeof(ScriptableObject) && currentType != typeof(UnityEngine.Object))
			{
				var declaredFields = currentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
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
				var listWrapper = field.GetValue(root);
				if (listWrapper != null)
				{
					FieldInfo itemsField = null;
					Type searchFieldType = field.FieldType;

					while (searchFieldType != null)
					{
						itemsField = searchFieldType.GetField("Items", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
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
									result.Add(child);
									result.AddRange(GetNestedAssetsRecursive(child));
								}
							}
						}
					}
				}
			}
			return result.Distinct().ToList();
		}

		public static void DeepCopyNestedAssets(ScriptableObject cloneParent, UnityEngine.Object rootAsset)
		{
			if (cloneParent == null) return;

			if (cloneParent is NestedSOCollectionBase internalCollection)
			{
				var oldItems = internalCollection.GetRawItems().ToList();
				foreach (var oldItem in oldItems)
				{
					if (oldItem != null) internalCollection._RemoveAsset(oldItem);
				}
				foreach (var oldItem in oldItems)
				{
					if (oldItem != null)
					{
						var childClone = UnityEngine.Object.Instantiate(oldItem);
						childClone.name = oldItem.name;
						AssetDatabase.AddObjectToAsset(childClone, rootAsset);
						internalCollection._AddAsset(childClone);
						internalCollection._MarkAsAddedAsset(childClone);
						DeepCopyNestedAssets(childClone, rootAsset);
					}
				}
			}

			var fieldsList = new List<FieldInfo>();
			Type currentType = cloneParent.GetType();

			while (currentType != null && currentType != typeof(ScriptableObject) && currentType != typeof(UnityEngine.Object))
			{
				var declaredFields = currentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
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
				var listWrapper = field.GetValue(cloneParent);
				if (listWrapper != null)
				{
					FieldInfo itemsField = null;
					Type searchFieldType = field.FieldType;

					while (searchFieldType != null)
					{
						itemsField = searchFieldType.GetField("Items", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
						if (itemsField != null) break;
						searchFieldType = searchFieldType.BaseType;
					}

					if (itemsField != null)
					{
						if (itemsField.GetValue(listWrapper) is System.Collections.IList list)
						{
							for (int i = 0; i < list.Count; i++)
							{
								var child = list[i] as ScriptableObject;
								if (child != null)
								{
									var childClone = UnityEngine.Object.Instantiate(child);
									childClone.name = child.name;
									AssetDatabase.AddObjectToAsset(childClone, rootAsset);
									list[i] = childClone;
									DeepCopyNestedAssets(childClone, rootAsset);
								}
							}
						}
					}
				}
			}
			EditorUtility.SetDirty(cloneParent);
		}

		public static ScriptableObject PushExternalAsset(ScriptableObject externalAsset, UnityEngine.Object rootObject)
		{
			if (!EditorUtility.DisplayDialog("Push Asset", $"Merging '{externalAsset.name}' will delete the standalone file.\n\nContinue?", "Yes, Push It", "Cancel"))
				return null;

			string oldPath = AssetDatabase.GetAssetPath(externalAsset);
			var nestedAssets = GetNestedAssetsRecursive(externalAsset);

			ScriptableObject clonedParent = UnityEngine.Object.Instantiate(externalAsset);
			clonedParent.name = externalAsset.name;

			AssetDatabase.AddObjectToAsset(clonedParent, rootObject);

			foreach (var child in nestedAssets)
			{
				if (child != null && AssetDatabase.GetAssetPath(child) == oldPath && !AssetDatabase.IsMainAsset(child))
				{
					AssetDatabase.RemoveObjectFromAsset(child);
					AssetDatabase.AddObjectToAsset(child, rootObject);
				}
			}

			AssetDatabase.DeleteAsset(oldPath);
			return clonedParent;
		}

		public static bool PopSubAsset(ScriptableObject subAsset, out string newPath)
		{
			newPath = EditorUtility.SaveFilePanelInProject("Pop Asset", subAsset.name, "asset", "Choose location to extract to.");
			if (string.IsNullOrEmpty(newPath)) return false;

			string oldPath = AssetDatabase.GetAssetPath(subAsset);
			var nestedAssets = GetNestedAssetsRecursive(subAsset);

			AssetDatabase.RemoveObjectFromAsset(subAsset);
			AssetDatabase.CreateAsset(subAsset, newPath);

			foreach (var child in nestedAssets)
			{
				if (child != null && AssetDatabase.GetAssetPath(child) == oldPath && !AssetDatabase.IsMainAsset(child))
				{
					AssetDatabase.RemoveObjectFromAsset(child);
					AssetDatabase.AddObjectToAsset(child, subAsset);
				}
			}
			return true;
		}

		public static ScriptableObject DuplicateSubAsset(ScriptableObject original, UnityEngine.Object rootObject)
		{
			ScriptableObject clone = UnityEngine.Object.Instantiate(original);
			clone.name = original.name + " (Copy)";

			AssetDatabase.AddObjectToAsset(clone, rootObject);
			DeepCopyNestedAssets(clone, rootObject);

			return clone;
		}

		public static void PopRangeSubAssets(List<ScriptableObject> subAssets, Action<ScriptableObject> onAssetPopped)
		{
			if (subAssets == null || subAssets.Count == 0) return;

			string absolutePath = EditorUtility.OpenFolderPanel("Select Export Folder", "Assets", "");
			if (string.IsNullOrEmpty(absolutePath)) return;

			if (!absolutePath.StartsWith(Application.dataPath))
			{
				EditorUtility.DisplayDialog("Invalid Folder", "The selected folder must be inside the project's Assets directory.", "OK");
				return;
			}

			string relativePath = "Assets" + absolutePath.Substring(Application.dataPath.Length);

			try
			{
				for (int i = 0; i < subAssets.Count; i++)
				{
					ScriptableObject subAsset = subAssets[i];
					if (subAsset == null) continue;

					EditorUtility.DisplayProgressBar("Popping Range", $"Extracting {subAsset.name}...", (float)i / subAssets.Count);

					string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{relativePath}/{subAsset.name}.asset");
					string oldPath = AssetDatabase.GetAssetPath(subAsset);

					var nestedAssets = GetNestedAssetsRecursive(subAsset);

					// Fire the delegate so the caller can remove the item from their collection/list BEFORE we modify the asset DB
					onAssetPopped?.Invoke(subAsset);

					AssetDatabase.RemoveObjectFromAsset(subAsset);
					AssetDatabase.CreateAsset(subAsset, assetPath);

					foreach (var child in nestedAssets)
					{
						if (child != null && AssetDatabase.GetAssetPath(child) == oldPath && !AssetDatabase.IsMainAsset(child))
						{
							AssetDatabase.RemoveObjectFromAsset(child);
							AssetDatabase.AddObjectToAsset(child, subAsset);
						}
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				AssetDatabase.SaveAssets();
			}
		}

		public static List<NestedSOCollectionBase> FindCompatibleCollections(Type itemType, NestedSOCollectionBase excludeCollection)
		{
			List<NestedSOCollectionBase> result = new List<NestedSOCollectionBase>();
			string[] guids = AssetDatabase.FindAssets("t:NestedSOCollectionBase");

			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var collection = AssetDatabase.LoadAssetAtPath<NestedSOCollectionBase>(path);
				if (collection == null || collection == excludeCollection) continue;

				Type baseType = GetBaseTypeFromCollection(collection);
				if (baseType != null && baseType.IsAssignableFrom(itemType))
				{
					result.Add(collection);
				}
			}

			return result;
		}

		public static void MoveSubAssetToCollection(ScriptableObject subAsset, NestedSOCollectionBase sourceCollection, NestedSOCollectionBase targetCollection)
		{
			if (subAsset == null || sourceCollection == null || targetCollection == null) return;

			string oldRootPath = AssetDatabase.GetAssetPath(sourceCollection);
			var nestedAssets = GetNestedAssetsRecursive(subAsset);

			// 1. Detach from old collection
			sourceCollection._RemoveAsset(subAsset);
			sourceCollection._MarkAsRemovedAsset(subAsset);

			// 2. Move main asset
			AssetDatabase.RemoveObjectFromAsset(subAsset);
			AssetDatabase.AddObjectToAsset(subAsset, targetCollection);

			// 3. Move all nested sub-assets
			foreach (var child in nestedAssets)
			{
				if (child != null && AssetDatabase.GetAssetPath(child) == oldRootPath && !AssetDatabase.IsMainAsset(child))
				{
					AssetDatabase.RemoveObjectFromAsset(child);
					AssetDatabase.AddObjectToAsset(child, targetCollection);
				}
			}

			// 4. Attach to new collection
			targetCollection._AddAsset(subAsset);
			targetCollection._MarkAsAddedAsset(subAsset);

			EditorUtility.SetDirty(sourceCollection);
			EditorUtility.SetDirty(targetCollection);
			EditorUtility.SetDirty(subAsset);
			AssetDatabase.SaveAssets();
		}

		public static void MoveSubAssetToList(ScriptableObject subAsset, ScriptableObject sourceRoot, NestedSOListBase sourceList, ScriptableObject targetRoot, NestedSOListBase targetList)
		{
			if (subAsset == null || sourceRoot == null || targetRoot == null) return;

			string oldRootPath = AssetDatabase.GetAssetPath(sourceRoot);
			var nestedAssets = GetNestedAssetsRecursive(subAsset);

			// 1. Detach from old list
			if (sourceList != null && sourceList.Contains(subAsset))
			{
				sourceList.Remove(subAsset);
			}

			// 2. Move main asset
			AssetDatabase.RemoveObjectFromAsset(subAsset);
			AssetDatabase.AddObjectToAsset(subAsset, targetRoot);

			// 3. Move all nested sub-assets
			foreach (var child in nestedAssets)
			{
				if (child != null && AssetDatabase.GetAssetPath(child) == oldRootPath && !AssetDatabase.IsMainAsset(child))
				{
					AssetDatabase.RemoveObjectFromAsset(child);
					AssetDatabase.AddObjectToAsset(child, targetRoot);
				}
			}

			// 4. Attach to new list
			if (targetList != null && !targetList.Contains(subAsset))
			{
				targetList.Add(subAsset);
			}

			EditorUtility.SetDirty(sourceRoot);
			EditorUtility.SetDirty(targetRoot);
			EditorUtility.SetDirty(subAsset);
			AssetDatabase.SaveAssets();
		}

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