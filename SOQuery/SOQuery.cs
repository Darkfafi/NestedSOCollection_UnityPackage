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

		// 3. The Query Cache: "SortedTag|String" -> List Result
		// We store List<ISOQueryEntity> here to allow pre-warming without knowing generic types T ahead of time.
		private static Dictionary<string, List<ISOQueryEntity>> _queryCache;

		private static bool _isInitialized = false;

		/// <summary>
		/// Initializes the database and pre-calculates the results for the provided query strings in a SINGLE PASS.
		/// </summary>
		/// <param name="database">The database asset.</param>
		/// <param name="queriesToCache">Strings of comma-separated tags (e.g. "Mission, Hard") to pre-calculate.</param>
		public static void Initialize(SOQueryDatabase database, params string[] queriesToCache)
		{
			if (_isInitialized) return;

			_tagIndex = new Dictionary<string, HashSet<ISOQueryEntity>>();
			_idIndex = new Dictionary<string, ISOQueryEntity>();
			_queryCache = new Dictionary<string, List<ISOQueryEntity>>();

			// --- STEP 1: SETUP PRE-WARM BUCKETS ---
			// We prepare empty lists for the queries we want to cache. 
			// This allows us to fill them as we iterate the entities, avoiding a second loop.

			var prewarmRules = new List<PrewarmRule>();

			if (queriesToCache != null)
			{
				foreach (string query in queriesToCache)
				{
					if (string.IsNullOrWhiteSpace(query)) continue;

					// Parse the tags: "Mission, Hard" -> ["Hard", "Mission"] (Sorted)
					var tags = query.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
									.Select(t => t.Trim())
									.OrderBy(t => t)
									.ToArray();

					if (tags.Length == 0) continue;

					string cacheKey = string.Join("|", tags);

					// Create the bucket if it doesn't exist
					if (!_queryCache.ContainsKey(cacheKey))
					{
						var list = new List<ISOQueryEntity>();
						_queryCache[cacheKey] = list;

						// Create a rule for the loop to check against
						prewarmRules.Add(new PrewarmRule
						{
							RequiredTags = new HashSet<string>(tags),
							TargetList = list
						});
					}
				}
			}

			// --- STEP 2: SINGLE PASS INDEXING & CACHING ---
			foreach (var obj in database.SOQueryEntities)
			{
				if (obj is not ISOQueryEntity entity) continue;

				// A. Map by ID
				if (!string.IsNullOrEmpty(entity.Id))
				{
					_idIndex[entity.Id] = entity;
				}

				// B. Collect ALL Tags for this entity (Manual + Types)
				// We use a local HashSet for O(1) subset checks against pre-warm rules
				var currentEntityTags = new HashSet<string>(entity.Tags);

				// Add Type and BaseTypes as tags
				Type currentType = obj.GetType();
				while (currentType != null && currentType != typeof(ScriptableObject))
				{
					string typeName = currentType.Name;

					// Add to local set
					currentEntityTags.Add(typeName);

					// Add to Global Index
					AddToIndex(typeName, entity);

					currentType = currentType.BaseType;
				}

				// Add Manual tags to Global Index
				foreach (var tag in entity.Tags)
				{
					AddToIndex(tag, entity);
				}

				// C. Check Pre-Warm Rules (Optimized)
				// Since we have the entity right here, check if it belongs in any of our cached lists
				for (int i = 0; i < prewarmRules.Count; i++)
				{
					if (prewarmRules[i].RequiredTags.IsSubsetOf(currentEntityTags))
					{
						prewarmRules[i].TargetList.Add(entity);
					}
				}
			}

			_isInitialized = true;
		}

		private static void AddToIndex(string tag, ISOQueryEntity entity)
		{
			if (!_tagIndex.TryGetValue(tag, out var set))
			{
				set = new HashSet<ISOQueryEntity>();
				_tagIndex[tag] = set;
			}
			set.Add(entity);
		}

		public static T Get<T>(string id) where T : class, ISOQueryEntity
		{
			if (_idIndex.TryGetValue(id, out var entity))
			{
				return entity as T;
			}
			return null;
		}

		public static T FindFirst<T>(params string[] tags)
			where T : class, ISOQueryEntity
		{
			if (TryFindFirst(out T target, tags))
			{
				return target;
			}

			return default;
		}

		public static bool TryFindFirst<T>(out T target, params string[] tags)
			where T : class, ISOQueryEntity
		{
			var entities = Find<T>(tags);
			if (entities.Count > 0)
			{
				target = entities[0];
				return true;
			}

			target = null;
			return false;
		}

		public static List<T> Find<T>(params string[] tags) where T : class, ISOQueryEntity
		{
			var searchTags = new List<string>(tags);

			if (typeof(T) != typeof(ISOQueryEntity))
			{
				searchTags.Add(typeof(T).Name);
			}

			searchTags.Sort();
			string cacheKey = string.Join("|", searchTags);

			if (_queryCache.TryGetValue(cacheKey, out List<ISOQueryEntity> cachedResult))
			{
				if (typeof(T) == typeof(ISOQueryEntity))
				{
					return cachedResult as List<T>;
				}

				return cachedResult.Cast<T>().ToList();
			}

			HashSet<ISOQueryEntity> resultSet = null;

			foreach (var tag in searchTags)
			{
				if (!_tagIndex.TryGetValue(tag, out var entitiesWithTag))
				{
					resultSet = null;
					break;
				}

				if (resultSet == null)
				{
					resultSet = new HashSet<ISOQueryEntity>(entitiesWithTag);
				}
				else
				{
					resultSet.IntersectWith(entitiesWithTag);
				}
			}

			var finalResult = resultSet == null ? new List<ISOQueryEntity>() : resultSet.ToList();
			_queryCache[cacheKey] = finalResult;

			return finalResult.Cast<T>().ToList();
		}

		public static void Clear()
		{
			_tagIndex?.Clear();
			_idIndex?.Clear();
			_queryCache?.Clear();
			_isInitialized = false;
		}

		// Helper struct to hold rules during initialization
		private struct PrewarmRule
		{
			public HashSet<string> RequiredTags;
			public List<ISOQueryEntity> TargetList;
		}
	}
}