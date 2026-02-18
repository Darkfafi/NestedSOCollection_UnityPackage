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
		public List<ScriptableObject> SOQueryEntities = new List<ScriptableObject>();
		public List<string> PrewarmQueries = new List<string>();

		[SerializeField, HideInInspector] private List<TagIndexEntry> _serializedTagIndex = new List<TagIndexEntry>();
		[SerializeField, HideInInspector] private List<QueryCacheEntry> _serializedQueryCache = new List<QueryCacheEntry>();
		[SerializeField, HideInInspector] private List<IdIndexEntry> _serializedIdIndex = new List<IdIndexEntry>();

		private Dictionary<string, HashSet<ISOQueryEntity>> _runtimeTagIndex = new Dictionary<string, HashSet<ISOQueryEntity>>();
		private Dictionary<string, List<ISOQueryEntity>> _runtimeQueryCache = new Dictionary<string, List<ISOQueryEntity>>();
		private Dictionary<string, ISOQueryEntity> _runtimeIdIndex = new Dictionary<string, ISOQueryEntity>();

		private Dictionary<string, int> _tagLookup;
		private Dictionary<string, int> _queryLookup;
		private Dictionary<string, int> _idLookup;

		private bool _isInitialized = false;

		public void Initialize()
		{
			if (_isInitialized) return;

			_tagLookup = new Dictionary<string, int>(_serializedTagIndex.Count);
			for (int i = 0; i < _serializedTagIndex.Count; i++)
			{
				if (!_tagLookup.ContainsKey(_serializedTagIndex[i].Tag))
					_tagLookup[_serializedTagIndex[i].Tag] = i;
			}

			_queryLookup = new Dictionary<string, int>(_serializedQueryCache.Count);
			for (int i = 0; i < _serializedQueryCache.Count; i++)
			{
				if (!_queryLookup.ContainsKey(_serializedQueryCache[i].QueryKey))
					_queryLookup[_serializedQueryCache[i].QueryKey] = i;
			}

			_idLookup = new Dictionary<string, int>(_serializedIdIndex.Count);
			for (int i = 0; i < _serializedIdIndex.Count; i++)
			{
				if (!_idLookup.ContainsKey(_serializedIdIndex[i].Id))
					_idLookup[_serializedIdIndex[i].Id] = i;
			}

			_isInitialized = true;
		}

		public void Deinitialize()
		{
			if (_isInitialized)
			{
				Unload();
				_tagLookup = null;
				_queryLookup = null;
				_idLookup = null;
				_isInitialized = false;
			}
		}

		public void Unload()
		{
			if (_isInitialized)
			{
				_runtimeTagIndex.Clear();
				_runtimeQueryCache.Clear();
				_runtimeIdIndex.Clear();
			}
		}

		public T Get<T>(string id) where T : ScriptableObject, ISOQueryEntity
		{
			EnsureInitialized();

			if (_runtimeIdIndex.TryGetValue(id, out var entity)) return entity as T;

			if (_idLookup.TryGetValue(id, out int index))
			{
				var entry = _serializedIdIndex[index];
				if (IsValidIndex(entry.EntityIndex))
				{
					entity = SOQueryEntities[entry.EntityIndex] as ISOQueryEntity;
					if (entity != null)
					{
						_runtimeIdIndex[id] = entity;
						return entity as T;
					}
				}
			}

			return null;
		}

		public bool TryGet<T>(string id, out T value) where T : ScriptableObject, ISOQueryEntity
		{
			value = Get<T>(id);
			return value != null;
		}

		public List<T> Find<T>(params string[] tags) where T : ScriptableObject, ISOQueryEntity
		{
			EnsureInitialized();

			var searchTags = new List<string>(tags);
			if (typeof(T) != typeof(ISOQueryEntity)) searchTags.Add(typeof(T).Name);
			searchTags.Sort();
			string cacheKey = string.Join("|", searchTags);

			if (_runtimeQueryCache.TryGetValue(cacheKey, out List<ISOQueryEntity> cachedResult))
			{
				return CastResult<T>(cachedResult);
			}

			if (_queryLookup.TryGetValue(cacheKey, out int queryIndex))
			{
				var entry = _serializedQueryCache[queryIndex];
				var results = ResolveIndices(entry.ResultIndices);

				_runtimeQueryCache[cacheKey] = results;
				return CastResult<T>(results);
			}

			HashSet<ISOQueryEntity> resultSet = null;

			foreach (var tag in searchTags)
			{
				var entitiesWithTag = GetOrLoadTagSet(tag);

				if (entitiesWithTag == null || entitiesWithTag.Count == 0)
				{
					resultSet = null;
					break;
				}

				if (resultSet == null) resultSet = new HashSet<ISOQueryEntity>(entitiesWithTag);
				else resultSet.IntersectWith(entitiesWithTag);
			}

			var finalResult = resultSet == null ? new List<ISOQueryEntity>() : resultSet.ToList();

			_runtimeQueryCache[cacheKey] = finalResult;

			return CastResult<T>(finalResult);
		}

		public T FindFirst<T>(params string[] tags) where T : ScriptableObject, ISOQueryEntity
		{
			var results = Find<T>(tags);
			return results.Count > 0 ? results[0] : null;
		}

		private HashSet<ISOQueryEntity> GetOrLoadTagSet(string tag)
		{
			if (_runtimeTagIndex.TryGetValue(tag, out var set)) return set;

			if (_tagLookup.TryGetValue(tag, out int index))
			{
				var entry = _serializedTagIndex[index];

				set = new HashSet<ISOQueryEntity>();
				foreach (int entityIndex in entry.EntityIndices)
				{
					if (IsValidIndex(entityIndex))
					{
						if (SOQueryEntities[entityIndex] is ISOQueryEntity entity)
							set.Add(entity);
					}
				}

				_runtimeTagIndex[tag] = set;
				return set;
			}

			return null;
		}

		private List<ISOQueryEntity> ResolveIndices(List<int> indices)
		{
			var list = new List<ISOQueryEntity>(indices.Count);
			foreach (int index in indices)
			{
				if (IsValidIndex(index))
				{
					if (SOQueryEntities[index] is ISOQueryEntity entity)
						list.Add(entity);
				}
			}
			return list;
		}

		private bool IsValidIndex(int index)
		{
			return index >= 0 && index < SOQueryEntities.Count && SOQueryEntities[index] != null;
		}

		private List<T> CastResult<T>(List<ISOQueryEntity> list) where T : ScriptableObject, ISOQueryEntity
		{
			if (typeof(T) == typeof(ISOQueryEntity)) return list as List<T>;
			return list.Cast<T>().ToList();
		}

		private void EnsureInitialized() { if (!_isInitialized) Initialize(); }

		public static IEnumerable<string> GetSearchableTags(ScriptableObject obj)
		{
			if (obj is ISOQueryEntity entity) foreach (var t in entity.Tags) yield return t;
			Type currentType = obj.GetType();
			while (currentType != null && currentType != typeof(ScriptableObject))
			{
				if (!IsTypeExcluded(currentType)) yield return currentType.Name;
				currentType = currentType.BaseType;
			}
		}

		public static bool IsTypeExcluded(Type t) => t.IsDefined(typeof(SOQueryExcludeTypeAttribute), false) || t.IsGenericType;

		public static List<string> ParseTags(string input, bool sort = false)
		{
			if (string.IsNullOrWhiteSpace(input)) return new List<string>();
			var result = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
			if (sort) result.Sort();
			return result;
		}

		[System.Serializable]
		private struct TagIndexEntry { public string Tag; public List<int> EntityIndices; }
		[System.Serializable]
		private struct QueryCacheEntry { public string QueryKey; public List<int> ResultIndices; }
		[System.Serializable]
		private struct IdIndexEntry { public string Id; public int EntityIndex; }

		public void OnBeforeSerialize() { }
		public void OnAfterDeserialize() { Deinitialize(); }
	}
}