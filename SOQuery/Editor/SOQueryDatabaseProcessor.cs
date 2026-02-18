using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.Collections.Generic;
using System.Linq;
using System;
using NestedSO;

namespace NestedSO.Processor
{
	public class SOQueryDatabaseProcessor : IPreprocessBuildWithReport
	{
		public int callbackOrder => 0;

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
				RefreshAllDatabases();
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

			var tempTagMap = new Dictionary<string, HashSet<ScriptableObject>>();
			var tempIdMap = new Dictionary<string, ScriptableObject>();

			// Scan the entities we just populated
			foreach (var obj in db.SOQueryEntities)
			{
				if (obj == null) continue;
				if (obj is not ISOQueryEntity entity) continue;

				// Index ID
				if (!string.IsNullOrEmpty(entity.Id))
				{
					if (!tempIdMap.TryGetValue(entity.Id, out var otherEntity))
					{
						tempIdMap[entity.Id] = obj;

						int index = idIndexProp.arraySize;
						idIndexProp.InsertArrayElementAtIndex(index);
						var entry = idIndexProp.GetArrayElementAtIndex(index);
						entry.FindPropertyRelative("Id").stringValue = entity.Id;
						entry.FindPropertyRelative("Entity").objectReferenceValue = obj;
					}
					else
					{
						Debug.LogWarning($"Entity {entity} and {otherEntity} have same Id: {entity.Id}");
					}
				}
				else
				{
					Debug.LogWarning("Entity has no Id: " + entity, obj);
				}

				// Index Tags
				foreach (var tag in SOQueryDatabase.GetSearchableTags(obj))
				{
					if (!tempTagMap.ContainsKey(tag)) tempTagMap[tag] = new HashSet<ScriptableObject>();
					tempTagMap[tag].Add(obj);
				}
			}

			// Serialize Tag Index
			foreach (var kvp in tempTagMap)
			{
				int index = tagIndexProp.arraySize;
				tagIndexProp.InsertArrayElementAtIndex(index);
				var entry = tagIndexProp.GetArrayElementAtIndex(index);

				entry.FindPropertyRelative("Tag").stringValue = kvp.Key;

				var listProp = entry.FindPropertyRelative("Entities");
				listProp.ClearArray();
				foreach (var item in kvp.Value)
				{
					int eIndex = listProp.arraySize;
					listProp.InsertArrayElementAtIndex(eIndex);
					listProp.GetArrayElementAtIndex(eIndex).objectReferenceValue = item;
				}
			}

			// Pre-Calculate Queries
			foreach (var queryStr in db.PrewarmQueries)
			{
				var tags = SOQueryDatabase.ParseTags(queryStr, sort: true);
				if (tags.Count == 0) continue;

				string key = string.Join("|", tags);
				HashSet<ScriptableObject> result = null;
				bool possible = true;

				foreach (var tag in tags)
				{
					if (!tempTagMap.TryGetValue(tag, out var bucket))
					{
						possible = false;
						break;
					}
					if (result == null) result = new HashSet<ScriptableObject>(bucket);
					else result.IntersectWith(bucket);
				}

				int qIndex = queryCacheProp.arraySize;
				queryCacheProp.InsertArrayElementAtIndex(qIndex);
				var qEntry = queryCacheProp.GetArrayElementAtIndex(qIndex);
				qEntry.FindPropertyRelative("QueryKey").stringValue = key;

				var resultsList = qEntry.FindPropertyRelative("Results");
				resultsList.ClearArray();

				if (possible && result != null)
				{
					foreach (var item in result)
					{
						int rIndex = resultsList.arraySize;
						resultsList.InsertArrayElementAtIndex(rIndex);
						resultsList.GetArrayElementAtIndex(rIndex).objectReferenceValue = item;
					}
				}
			}

			sobj.ApplyModifiedProperties();
			Debug.Log($"[SOQuery] Database '{db.name}' Cache Rebuilt.");
		}
	}
}