using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NestedSO
{
	public static class SOQuery
	{
		// 1. The Master Index: "Tag" (or ClassName) -> Set of Entities
		private static Dictionary<string, HashSet<ISOQueryEntity>> _tagIndex;

		// 2. The ID Lookup: "ID" -> Entity
		private static Dictionary<string, ISOQueryEntity> _idIndex;

		// 3. The Query Cache: "Tag|Tag|Tag" -> List Result
		private static Dictionary<string, object> _queryCache;

		private static bool _isInitialized = false;

		public static void Initialize(SOQueryDatabase database)
		{
			if (_isInitialized) return;

			_tagIndex = new Dictionary<string, HashSet<ISOQueryEntity>>();
			_idIndex = new Dictionary<string, ISOQueryEntity>();
			_queryCache = new Dictionary<string, object>();

			foreach (var obj in database.SOQueryEntities)
			{
				if (obj is not ISOQueryEntity entity) continue;

				// A. Map by ID
				if (!string.IsNullOrEmpty(entity.Id))
				{
					_idIndex[entity.Id] = entity;
				}

				// B. Map by ManualTags
				foreach (var tag in entity.Tags)
				{
					AddToIndex(tag, entity);
				}

				Type currentType = obj.GetType();
				while (currentType != null && currentType != typeof(ScriptableObject))
				{
					AddToIndex(currentType.Name, entity);
					currentType = currentType.BaseType;
				}
			}

			_isInitialized = true;
		}

		private static void AddToIndex(string tag, ISOQueryEntity entity)
		{
			if (!_tagIndex.ContainsKey(tag))
			{
				_tagIndex[tag] = new HashSet<ISOQueryEntity>();
			}
			_tagIndex[tag].Add(entity);
		}

		public static T Get<T>(string id) where T : class, ISOQueryEntity
		{
			if (_idIndex.TryGetValue(id, out var entity))
			{
				return entity as T;
			}
			return null;
		}

		public static List<T> Find<T>(params string[] tags) where T : class, ISOQueryEntity
		{
			// 1. Combine user tags with the Type name
			var allTags = new List<string>(tags);

			// Only add the type name if it's not the generic interface
			if (typeof(T) != typeof(ISOQueryEntity))
			{
				allTags.Add(typeof(T).Name);
			}

			// 2. Sort tags to generate a consistent Cache Key
			allTags.Sort();
			string cacheKey = string.Join("|", allTags);

			// 3. Check Cache
			if (_queryCache.TryGetValue(cacheKey, out object cachedResult))
			{
				return (List<T>)cachedResult;
			}

			// 4. Perform Intersection
			HashSet<ISOQueryEntity> resultSet = null;

			foreach (var tag in allTags)
			{
				if (!_tagIndex.ContainsKey(tag))
				{
					// If any tag is missing, the result is empty.
					resultSet = null;
					break;
				}

				var entitiesWithTag = _tagIndex[tag];

				if (resultSet == null)
				{
					resultSet = new HashSet<ISOQueryEntity>(entitiesWithTag);
				}
				else
				{
					resultSet.IntersectWith(entitiesWithTag);
				}
			}

			// 5. Convert and Cache
			var finalResult = resultSet == null ? new List<T>() : resultSet.Cast<T>().ToList();
			_queryCache[cacheKey] = finalResult;

			return finalResult;
		}

		public static void Clear()
		{
			_tagIndex?.Clear();
			_idIndex?.Clear();
			_queryCache?.Clear();
			_isInitialized = false;
		}
	}
}