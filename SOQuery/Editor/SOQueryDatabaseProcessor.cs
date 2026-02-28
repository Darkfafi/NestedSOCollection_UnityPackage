#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;

namespace NestedSO.Processor
{
	public class SOQueryDatabaseProcessor : IPreprocessBuildWithReport
	{
		public int callbackOrder => 0;

		private const string AUTO_REFRESH_PREF_KEY = "NestedSO_AutoRefreshOnPlay";

		public static bool AutoRefreshOnPlay
		{
			get => EditorPrefs.GetBool(AUTO_REFRESH_PREF_KEY, true);
			set => EditorPrefs.SetBool(AUTO_REFRESH_PREF_KEY, value);
		}

		[MenuItem("Tools/NestedSO/Auto Refresh On Play")]
		private static void ToggleAutoRefresh()
		{
			AutoRefreshOnPlay = !AutoRefreshOnPlay;
		}

		[MenuItem("Tools/NestedSO/Auto Refresh On Play", true)]
		private static bool ToggleAutoRefreshValidate()
		{
			Menu.SetChecked("Tools/NestedSO/Auto Refresh On Play", AutoRefreshOnPlay);
			return true;
		}

		public void OnPreprocessBuild(BuildReport report)
		{
			Debug.Log("[SOQuery] Pre-processing build: Refreshing Databases...");
			RefreshAllDatabases();
		}

		[InitializeOnLoadMethod]
		private static void Initialize()
		{
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.ExitingEditMode)
			{
				if (AutoRefreshOnPlay)
				{
					RefreshAllDatabases();
				}
			}
		}

		public static void RefreshAllDatabases()
		{
			string[] guids = AssetDatabase.FindAssets($"t:{nameof(SOQueryDatabase)}");
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var db = AssetDatabase.LoadAssetAtPath<SOQueryDatabase>(path);
				if (db != null)
				{
					PopulateDatabase(db);
					BuildCache(db);
					EditorUtility.SetDirty(db);
				}
			}
			AssetDatabase.SaveAssets();
		}

		public static void PopulateDatabase(SOQueryDatabase db)
		{
			if (db == null) return;

			db.SOQueryEntities.Clear();

			ForEachScriptableObjects<ScriptableObject>(entity =>
			{
				if (entity is ISOQueryEntity)
				{
					if (entity != db && !SOQueryDatabase.IsTypeExcluded(entity.GetType()))
					{
						db.SOQueryEntities.Add(entity);
					}
				}
			});

			EditorUtility.SetDirty(db);
		}

		public static void ForEachScriptableObjects<T>(Action<T> method) where T : ScriptableObject
		{
			string[] guids = AssetDatabase.FindAssets($"t:{nameof(ScriptableObject)}");

			var typeFieldsCache = new Dictionary<Type, FieldInfo[]>();

			for (int i = 0, c = guids.Length; i < c; i++)
			{
				string guid = guids[i];
				string assetPath = AssetDatabase.GUIDToAssetPath(guid);
				ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);

				if (so != null)
				{
					CallMethod(so);
				}
			}

			void CallMethod(ScriptableObject _so)
			{
				if (_so is T castedSO)
				{
					method(castedSO);
				}

				if (_so is NestedSOCollectionBase nestedSOCollection)
				{
					foreach (var x in nestedSOCollection.GetRawItems())
					{
						if (x != null)
						{
							CallMethod(x);
						}
					}
				}

				Type soType = _so.GetType();
				if (!typeFieldsCache.TryGetValue(soType, out var listFields))
				{
					// Walk up the inheritance chain to grab all fields, including private inherited ones
					var fieldsList = new List<FieldInfo>();
					Type currentType = soType;

					while (currentType != null && currentType != typeof(ScriptableObject))
					{
						var fields = currentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
						foreach (var f in fields)
						{
							if (typeof(NestedSOListBase).IsAssignableFrom(f.FieldType))
							{
								fieldsList.Add(f);
							}
						}
						currentType = currentType.BaseType;
					}

					listFields = fieldsList.ToArray();
					typeFieldsCache[soType] = listFields;
				}

				foreach (var field in listFields)
				{
					var listWrapper = field.GetValue(_so);
					if (listWrapper != null)
					{
						var itemsField = field.FieldType.GetField("Items");
						if (itemsField != null)
						{
							if (itemsField.GetValue(listWrapper) is System.Collections.IList list)
							{
								foreach (var item in list)
								{
									if (item is ScriptableObject childSO)
									{
										CallMethod(childSO);
									}
								}
							}
						}
					}
				}
			}
		}

		public static void BuildCache(SOQueryDatabase db)
		{
			if (db == null) return;

			SerializedObject sobj = new SerializedObject(db);
			sobj.Update();

			var tagIndexProp = sobj.FindProperty("_serializedTagIndex");
			var idIndexProp = sobj.FindProperty("_serializedIdIndex");
			var queryCacheProp = sobj.FindProperty("_serializedQueryCache");

			tagIndexProp.ClearArray();
			idIndexProp.ClearArray();
			queryCacheProp.ClearArray();

			var tempTagMap = new Dictionary<string, List<int>>();
			var tempIdMap = new Dictionary<string, int>();

			for (int i = 0; i < db.SOQueryEntities.Count; i++)
			{
				var obj = db.SOQueryEntities[i];
				if (obj == null) continue;
				if (obj is not ISOQueryEntity entity) continue;

				if (!string.IsNullOrEmpty(entity.Id))
				{
					if (!tempIdMap.ContainsKey(entity.Id))
					{
						tempIdMap[entity.Id] = i;

						int index = idIndexProp.arraySize;
						idIndexProp.InsertArrayElementAtIndex(index);
						var entry = idIndexProp.GetArrayElementAtIndex(index);
						entry.FindPropertyRelative("Id").stringValue = entity.Id;
						entry.FindPropertyRelative("EntityIndex").intValue = i;
					}
					else
					{
						Debug.LogWarning($"[SOQuery] Duplicate ID '{entity.Id}' found on {obj.name}. Skipping.");
					}
				}

				foreach (var tag in SOQueryDatabase.GetSearchableTags(obj))
				{
					if (!tempTagMap.ContainsKey(tag)) tempTagMap[tag] = new List<int>();
					tempTagMap[tag].Add(i);
				}
			}

			foreach (var kvp in tempTagMap)
			{
				int index = tagIndexProp.arraySize;
				tagIndexProp.InsertArrayElementAtIndex(index);
				var entry = tagIndexProp.GetArrayElementAtIndex(index);

				entry.FindPropertyRelative("Tag").stringValue = kvp.Key;

				var indicesProp = entry.FindPropertyRelative("EntityIndices");
				indicesProp.ClearArray();
				foreach (int entityIdx in kvp.Value)
				{
					int k = indicesProp.arraySize;
					indicesProp.InsertArrayElementAtIndex(k);
					indicesProp.GetArrayElementAtIndex(k).intValue = entityIdx;
				}
			}

			foreach (var queryStr in db.PrewarmQueries)
			{
				var tags = SOQueryDatabase.ParseTags(queryStr, sort: true);
				if (tags.Count == 0) continue;

				string key = string.Join("|", tags);
				List<int> resultIndices = null;

				foreach (var tag in tags)
				{
					if (!tempTagMap.TryGetValue(tag, out var currentIndices))
					{
						resultIndices = new List<int>();
						break;
					}

					if (resultIndices == null) resultIndices = new List<int>(currentIndices);
					else resultIndices = resultIndices.Intersect(currentIndices).ToList();
				}

				if (resultIndices == null) resultIndices = new List<int>();

				int qIndex = queryCacheProp.arraySize;
				queryCacheProp.InsertArrayElementAtIndex(qIndex);
				var qEntry = queryCacheProp.GetArrayElementAtIndex(qIndex);
				qEntry.FindPropertyRelative("QueryKey").stringValue = key;

				var resultsList = qEntry.FindPropertyRelative("ResultIndices");
				resultsList.ClearArray();
				foreach (int entityIdx in resultIndices)
				{
					int k = resultsList.arraySize;
					resultsList.InsertArrayElementAtIndex(k);
					resultsList.GetArrayElementAtIndex(k).intValue = entityIdx;
				}
			}

			sobj.ApplyModifiedProperties();
			Debug.Log($"[SOQuery] Database '{db.name}' Cache Rebuilt (Optimized).");
		}
	}
}
#endif