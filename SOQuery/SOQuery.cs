using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NestedSO
{
	public static class SOQuery
	{
		// "Tag" (or Type) -> Set of Entities
		private static Dictionary<string, HashSet<ISOQueryEntity>> _tagIndex;

		// "ID" -> Entity
		private static Dictionary<string, ISOQueryEntity> _idIndex;

		// Query Cache: "SortedTag|String" -> List Result
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

			// Custom Queries - Prewarm Rules for Indexing
			var prewarmRules = new List<PrewarmRule>();
			if (queriesToCache != null)
			{
				foreach (string query in queriesToCache)
				{
					if (string.IsNullOrWhiteSpace(query)) continue;
					
					var tags = ParseTags(query, sort: true);
					if (tags.Count == 0) continue;

					string cacheKey = string.Join("|", tags);

					if (!_queryCache.ContainsKey(cacheKey))
					{
						var list = new List<ISOQueryEntity>();
						_queryCache[cacheKey] = list;

						prewarmRules.Add(new PrewarmRule
						{
							RequiredTags = new HashSet<string>(tags),
							TargetList = list
						});
					}
				}
			}

			// Indexing
			foreach (var obj in database.SOQueryEntities)
			{
				if (obj is not ISOQueryEntity entity) continue;

				if (!string.IsNullOrEmpty(entity.Id))
				{
					_idIndex[entity.Id] = entity;
				}

				var currentEntityTags = new HashSet<string>();

				foreach (var tag in GetSearchableTags(obj))
				{
					currentEntityTags.Add(tag);
					AddToIndex(tag, entity);
				}

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

		public static void Deinitialize()
		{
			if (_isInitialized)
			{
				_tagIndex.Clear();
				_queryCache.Clear();
				_idIndex.Clear();
				_isInitialized = false;
			}
		}

		public static IEnumerable<string> GetSearchableTags(ScriptableObject obj)
		{
			// 1. Manual Tags
			if (obj is ISOQueryEntity entity)
			{
				foreach (var t in entity.Tags) yield return t;
			}

			// 2. Type Hierarchy
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

		public static List<string> ParseTags(string input, bool sort = false)
		{
			if (string.IsNullOrWhiteSpace(input)) return new List<string>();

			var result = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
							  .Select(t => t.Trim())
							  .ToList();

			if (sort) result.Sort();

			return result;
		}

		public static bool IsTypeExcluded(Type t)
		{
			return t.IsDefined(typeof(SOQueryExcludeTypeAttribute), false) ||
				   t.IsAbstract;
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

		public static T Get<T>(string id)
			where T : ScriptableObject, ISOQueryEntity
		{
			if (_idIndex.TryGetValue(id, out var entity))
			{
				return entity as T;
			}
			return null;
		}

		public static bool TryGet<T>(string id, out T value)
			where T : ScriptableObject, ISOQueryEntity
		{
			if (_idIndex.TryGetValue(id, out var entity))
			{
				value = entity as T;
			}
			else
			{
				value = default;
			}

			return value != null;
		}

		public static T FindFirst<T>(params string[] tags)
			where T : ScriptableObject, ISOQueryEntity
		{
			if (TryFindFirst(out T target, tags))
			{
				return target;
			}

			return default;
		}

		public static bool TryFindFirst<T>(out T target, params string[] tags)
			where T : ScriptableObject, ISOQueryEntity
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

		public static List<T> Find<T>(params string[] tags)
			where T : ScriptableObject, ISOQueryEntity
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

		private struct PrewarmRule
		{
			public HashSet<string> RequiredTags;
			public List<ISOQueryEntity> TargetList;
		}
	}
}