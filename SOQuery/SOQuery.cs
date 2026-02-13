using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NestedSO
{
    public static class SOQuery
    {
        // Baked Caching
        private static Dictionary<Type, Dictionary<string, HashSet<SOQueryEntity>>> _index;

        // Real Time Caching
        private static Dictionary<Type, Dictionary<string, object>> _queryCache;

        private static bool _isInitialized = false;

        public static void Initialize(SOQueryDatabase database)
        {
            if (_isInitialized) return;

            _index = new Dictionary<Type, Dictionary<string, HashSet<SOQueryEntity>>>();
            _queryCache = new Dictionary<Type, Dictionary<string, object>>();

            // Build the base index (same as before)
            foreach (var config in database.SOQueryEntities)
            {
                if (config == null) continue;

                Type currentType = config.GetType();
                while (currentType != null && currentType != typeof(ScriptableObject))
                {
                    if (!_index.ContainsKey(currentType))
                        _index[currentType] = new Dictionary<string, HashSet<SOQueryEntity>>();

                    foreach (var tag in config.Tags)
                    {
                        if (!_index[currentType].ContainsKey(tag))
                            _index[currentType][tag] = new HashSet<SOQueryEntity>();

                        _index[currentType][tag].Add(config);
                    }
                    currentType = currentType.BaseType;
                }
            }

            _isInitialized = true;
        }

        public static List<T> Find<T>(params string[] tags) where T : SOQueryEntity
        {
            if (!_isInitialized)
            {
                Debug.LogError("SOQuery has not been initialized with a database yet!");
                return new List<T>();
            }

            Type type = typeof(T);

            // 1. Generate a consistent cache key (Sorting ensures order doesn't matter)
            string cacheKey = string.Join("|", tags.OrderBy(t => t));

            // 2. Check the Cache FIRST
            if (!_queryCache.ContainsKey(type))
            {
                _queryCache[type] = new Dictionary<string, object>();
            }

            if (_queryCache[type].TryGetValue(cacheKey, out object cachedResult))
            {
                // CACHE HIT: It's baked in! Return instantly.
                return (List<T>)cachedResult;
            }

            // 3. CACHE MISS: We haven't searched this exact combination yet.
            if (!_index.ContainsKey(type)) return new List<T>();
            var typeIndex = _index[type];
            HashSet<SOQueryEntity> results = null;

            foreach (var tag in tags)
            {
                if (!typeIndex.ContainsKey(tag))
                    return new List<T>();

                var taggedConfigs = typeIndex[tag];

                if (results == null)
                {
                    results = new HashSet<SOQueryEntity>(taggedConfigs);
                }
                else
                {
                    results.IntersectWith(taggedConfigs);
                }
            }

            if (results == null) return new List<T>();

            // 4. Bake the final result into a List
            List<T> finalResult = results.Cast<T>().ToList();

            // 5. Save it to the cache for next time
            _queryCache[type][cacheKey] = finalResult;

            return finalResult;
        }
    }
}
