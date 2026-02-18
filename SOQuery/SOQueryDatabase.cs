using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NestedSO
{
	[CreateAssetMenu(fileName = "SOQueryDatabase", menuName = "NestedSO/SOQueryDatabase")]
	public class SOQueryDatabase : ScriptableObject, ISerializationCallbackReceiver
	{
		[Header("Configuration")]
		[Tooltip("The raw list of objects to index.")]
		public List<ScriptableObject> SOQueryEntities = new List<ScriptableObject>();

		[Tooltip("Define queries here (e.g. 'Mission, Hard') to pre-calculate their results during the build step.")]
		public List<string> PrewarmQueries = new List<string>();

		// Cached Indexing
		[SerializeField]
		private List<TagIndexEntry> _serializedTagIndex = new List<TagIndexEntry>();

		[SerializeField]
		private List<QueryCacheEntry> _serializedQueryCache = new List<QueryCacheEntry>();

		[SerializeField]
		private List<IdIndexEntry> _serializedIdIndex = new List<IdIndexEntry>();

		// Runtime Look-up Maps (based on Index)
		private Dictionary<string, HashSet<ISOQueryEntity>> _runtimeTagIndex;
		private Dictionary<string, List<ISOQueryEntity>> _runtimeQueryCache;
		private Dictionary<string, ISOQueryEntity> _runtimeIdIndex;

		private bool _isInitialized = false;

		public void Initialize()
		{
			if (_isInitialized) return;

			_runtimeTagIndex = new Dictionary<string, HashSet<ISOQueryEntity>>(_serializedTagIndex.Count);
			foreach (var entry in _serializedTagIndex)
			{
				var set = new HashSet<ISOQueryEntity>();
				foreach (var obj in entry.Entities)
				{
					if (obj is ISOQueryEntity entity) set.Add(entity);
				}
				_runtimeTagIndex[entry.Tag] = set;
			}

			_runtimeQueryCache = new Dictionary<string, List<ISOQueryEntity>>(_serializedQueryCache.Count);
			foreach (var entry in _serializedQueryCache)
			{
				var list = new List<ISOQueryEntity>();
				foreach (var obj in entry.Results)
				{
					if (obj is ISOQueryEntity entity) list.Add(entity);
				}
				_runtimeQueryCache[entry.QueryKey] = list;
			}

			_runtimeIdIndex = new Dictionary<string, ISOQueryEntity>(_serializedIdIndex.Count);
			foreach (var entry in _serializedIdIndex)
			{
				if (entry.Entity is ISOQueryEntity isoEntity)
				{
					_runtimeIdIndex[entry.Id] = isoEntity;
				}
			}

			_isInitialized = true;
		}

		public void Deinitialize()
		{
			if (_isInitialized)
			{
				_runtimeTagIndex?.Clear();
				_runtimeQueryCache?.Clear();
				_runtimeIdIndex?.Clear();
				_isInitialized = false;
			}
		}

		public T Get<T>(string id) where T : ScriptableObject, ISOQueryEntity
		{
			EnsureInitialized();
			if (_runtimeIdIndex.TryGetValue(id, out var entity)) return entity as T;
			return null;
		}

		public bool TryGet<T>(string id, out T value) where T : ScriptableObject, ISOQueryEntity
		{
			EnsureInitialized();
			if (_runtimeIdIndex.TryGetValue(id, out var entity))
			{
				value = entity as T;
				return true;
			}
			value = default;
			return false;
		}

		public List<T> Find<T>(params string[] tags) where T : ScriptableObject, ISOQueryEntity
		{
			EnsureInitialized();

			var searchTags = new List<string>(tags);
			if (typeof(T) != typeof(ISOQueryEntity))
			{
				searchTags.Add(typeof(T).Name);
			}
			searchTags.Sort();
			string cacheKey = string.Join("|", searchTags);

			// Check Cache
			if (_runtimeQueryCache.TryGetValue(cacheKey, out List<ISOQueryEntity> cachedResult))
			{
				if (typeof(T) == typeof(ISOQueryEntity)) return cachedResult as List<T>; // Unsafe cast optimization
				return cachedResult.Cast<T>().ToList();
			}

			HashSet<ISOQueryEntity> resultSet = null;

			foreach (var tag in searchTags)
			{
				if (!_runtimeTagIndex.TryGetValue(tag, out var entitiesWithTag))
				{
					resultSet = null;
					break;
				}

				if (resultSet == null) resultSet = new HashSet<ISOQueryEntity>(entitiesWithTag);
				else resultSet.IntersectWith(entitiesWithTag);
			}

			var finalResult = resultSet == null ? new List<ISOQueryEntity>() : resultSet.ToList();

			_runtimeQueryCache[cacheKey] = finalResult;

			return finalResult.Cast<T>().ToList();
		}

		public T FindFirst<T>(params string[] tags) where T : ScriptableObject, ISOQueryEntity
		{
			var results = Find<T>(tags);
			return results.Count > 0 ? results[0] : null;
		}

		// =================================================================================================
		// INDEX BUILDING (Editor / Build Time)
		// =================================================================================================

		public void RebuildIndex()
		{
			_serializedTagIndex.Clear();
			_serializedQueryCache.Clear();
			_serializedIdIndex.Clear();

			var tempTagMap = new Dictionary<string, HashSet<ScriptableObject>>();
			var tempIdMap = new Dictionary<string, ScriptableObject>();

			foreach (var obj in SOQueryEntities)
			{
				if (obj == null) continue;
				if (obj is not ISOQueryEntity entity) continue;

				// ID Indexing
				if (!string.IsNullOrEmpty(entity.Id))
				{
					if (!tempIdMap.ContainsKey(entity.Id))
					{
						tempIdMap[entity.Id] = obj;
						_serializedIdIndex.Add(new IdIndexEntry { Id = entity.Id, Entity = obj });
					}
					else
					{
						Debug.LogWarning($"[SOQueryDatabase] Duplicate ID found: {entity.Id} on {obj.name}. Skipping.");
					}
				}

				// Tag Indexing
				foreach (var tag in GetSearchableTags(obj))
				{
					if (!tempTagMap.ContainsKey(tag)) tempTagMap[tag] = new HashSet<ScriptableObject>();
					tempTagMap[tag].Add(obj);
				}
			}

			foreach (var kvp in tempTagMap)
			{
				_serializedTagIndex.Add(new TagIndexEntry { Tag = kvp.Key, Entities = kvp.Value.ToList() });
			}

			foreach (var queryStr in PrewarmQueries)
			{
				var tags = ParseTags(queryStr, sort: true);
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

				if (possible && result != null)
				{
					_serializedQueryCache.Add(new QueryCacheEntry { QueryKey = key, Results = result.ToList() });
				}
				else
				{
					_serializedQueryCache.Add(new QueryCacheEntry { QueryKey = key, Results = new List<ScriptableObject>() });
				}
			}

#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
#endif
			Debug.Log($"[SOQueryDatabase] Rebuilt Index. Tags: {_serializedTagIndex.Count}, IDs: {_serializedIdIndex.Count}, Cached Queries: {_serializedQueryCache.Count}");
		}

		// =================================================================================================
		// HELPERS
		// =================================================================================================

		private void EnsureInitialized()
		{
			if (!_isInitialized) Initialize();
		}

		public static IEnumerable<string> GetSearchableTags(ScriptableObject obj)
		{
			if (obj is ISOQueryEntity entity)
			{
				foreach (var t in entity.Tags) yield return t;
			}

			Type currentType = obj.GetType();
			while (currentType != null && currentType != typeof(ScriptableObject))
			{
				if (!IsTypeExcluded(currentType))
				{
					yield return currentType.Name;
				}
				currentType = currentType.BaseType;
			}
		}

		public static bool IsTypeExcluded(Type t)
		{
			return t.IsDefined(typeof(SOQueryExcludeTypeAttribute), false) || t.IsAbstract;
		}

		public static List<string> ParseTags(string input, bool sort = false)
		{
			if (string.IsNullOrWhiteSpace(input)) return new List<string>();
			var result = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
							  .Select(t => t.Trim())
							  .ToList();
			if (sort) result.Sort();
			return result;
		}

		// =================================================================================================
		// DATA STRUCTURES
		// =================================================================================================

		[System.Serializable]
		private struct TagIndexEntry
		{
			public string Tag;
			public List<ScriptableObject> Entities;
		}

		[System.Serializable]
		private struct QueryCacheEntry
		{
			public string QueryKey;
			public List<ScriptableObject> Results;
		}

		[System.Serializable]
		private struct IdIndexEntry
		{
			public string Id;
			public ScriptableObject Entity;
		}

		// =================================================================================================
		// UNITY CALLBACKS
		// =================================================================================================

		public void OnBeforeSerialize() { /* No action needed */ }

		public void OnAfterDeserialize()
		{
			Deinitialize();
		}
	}
}