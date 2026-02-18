using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine.Profiling;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;
using NestedSO;
using NestedSO.Processor;

namespace NestedSO.SOEditor
{
	[CustomEditor(typeof(SOQueryDatabase))]
	public class SOQueryDatabaseEditor : Editor
	{
		private SOQueryDatabase _db;

		// --- Search & Pagination ---
		private string _searchString = "";
		private Vector2 _scrollPos;
		private bool _showConfigs = true;

		// --- Tags Explorer State ---
		private bool _showTagExplorer = false;
		private string _explorerSearchString = "";
		private Vector2 _explorerScrollPos;

		// Data Structures for Analysis
		private class TagStats
		{
			public bool IsContainer;
			public int TypeCount;
			public int EditorCount;
			public int RuntimeCount;
			public int TotalCount => TypeCount + EditorCount + RuntimeCount;
		}
		private Dictionary<string, TagStats> _tagStats = new Dictionary<string, TagStats>();
		private List<string> _sortedTags = new List<string>();

		// Filter State
		[System.Flags]
		public enum TagSourceFilter { Type = 1, Container = 2, Editor = 4, Runtime = 8 }
		private TagSourceFilter _explorerFilter = (TagSourceFilter)~0;

		// --- Cache State ---
		private HashSet<int> _cachedResultIDs = new HashSet<int>();
		private bool _isCacheStale = false;
		private string _staleReason = "";

		// --- Integrity State ---
		private string _cacheValidationResult = "";
		private MessageType _cacheValidationType = MessageType.None;
		private bool _hasDuplicateErrors = false; // <--- NEW FLAG

		// --- Expansion State ---
		private HashSet<int> _expandedItems = new HashSet<int>();

		// --- Pagination ---
		private int _currentPage = 0;
		private const int ITEMS_PER_PAGE = 20;

		// --- Styles ---
		private GUIStyle _tagPillStyle;
		private GUIStyle _resultTagButtonStyle;
		private GUIStyle _typeButtonStyle;
		private GUIStyle _expandedBoxStyle;
		private GUIStyle _toolbarSearchField;
		private GUIStyle _toolbarCancelButton;
		private GUIStyle _badgeContainer, _badgeType, _badgeEditor, _badgeRuntime;
		private GUIStyle _staleRowStyle;

		private void OnEnable()
		{
			_db = (SOQueryDatabase)target;
			AnalyzeTags();
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			InitializeStyles();

			EditorGUILayout.Space(10);
			DrawEditorHeader();

			EditorGUILayout.Space(5);
			DrawTagsExplorer();

			EditorGUILayout.Space(5);
			DrawSearchArea();

			// --- DATABASE OPERATIONS ---
			EditorGUILayout.Space(10);
			EditorGUILayout.LabelField("Database Operations", EditorStyles.boldLabel);

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button(new GUIContent(" Populate List", EditorGUIUtility.IconContent("d_Folder Icon").image), GUILayout.Height(24)))
			{
				SOQueryDatabaseProcessor.PopulateDatabase(_db);
				SOQueryDatabaseProcessor.BuildCache(_db);
				EditorUtility.SetDirty(_db);
				AnalyzeTags();
			}
			if (GUILayout.Button(new GUIContent(" Rebuild Cache", EditorGUIUtility.IconContent("d_Refresh").image), GUILayout.Height(24)))
			{
				SOQueryDatabaseProcessor.BuildCache(_db);
				EditorUtility.SetDirty(_db);
				AnalyzeTags();
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(5);
			if (GUILayout.Button("Verify Integrity", GUILayout.Height(24)))
			{
				RunCacheIntegrityCheck();
			}

			if (!string.IsNullOrEmpty(_cacheValidationResult))
			{
				EditorGUILayout.HelpBox(_cacheValidationResult, _cacheValidationType);

				if (_hasDuplicateErrors)
				{
					GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
					if (GUILayout.Button("Auto-Fix Duplicates (Assign New IDs)", GUILayout.Height(30)))
					{
						FixDuplicateIDs();
					}
					GUI.backgroundColor = Color.white;
				}
			}

			EditorGUILayout.Space(10);
			EditorGUILayout.LabelField("Raw Data", EditorStyles.boldLabel);
			SerializedProperty listProp = serializedObject.FindProperty("SOQueryEntities");

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(listProp, true);
			if (EditorGUI.EndChangeCheck()) AnalyzeTags();

			EditorGUILayout.Space(10);
			serializedObject.ApplyModifiedProperties();
		}

		private void RunCacheIntegrityCheck()
		{
			_hasDuplicateErrors = false;

			// Check Source Data for Duplicates
			var idMap = new Dictionary<string, List<string>>();
			int nullRefs = 0;

			foreach (var obj in _db.SOQueryEntities)
			{
				if (obj == null)
				{
					nullRefs++;
					continue;
				}

				if (obj is ISOQueryEntity entity && !string.IsNullOrEmpty(entity.Id))
				{
					if (!idMap.ContainsKey(entity.Id)) idMap[entity.Id] = new List<string>();
					idMap[entity.Id].Add(obj.name);
				}
			}

			var duplicates = idMap.Where(kvp => kvp.Value.Count > 1).ToList();

			if (duplicates.Count > 0)
			{
				_hasDuplicateErrors = true;
				string errorMsg = "CRITICAL: Duplicate IDs detected!\n";
				foreach (var dup in duplicates)
				{
					errorMsg += $"- ID '{dup.Key}' used by: {string.Join(", ", dup.Value)}\n";
				}

				_cacheValidationResult = errorMsg;
				_cacheValidationType = MessageType.Error;
				return;
			}

			// Check Nulls
			if (nullRefs > 0)
			{
				_cacheValidationResult = $"Database contains {nullRefs} null references. Please click 'Populate List'.";
				_cacheValidationType = MessageType.Warning;
				return;
			}

			// Check Serialized Cache vs Source Count
			var idIndexProp = serializedObject.FindProperty("_serializedIdIndex");

			if (idIndexProp.arraySize != idMap.Count)
			{
				_cacheValidationResult = $"Cache Desync: Source has {idMap.Count} unique IDs, but Cache has {idIndexProp.arraySize}. Please 'Rebuild Cache'.";
				_cacheValidationType = MessageType.Warning;
				return;
			}

			_cacheValidationResult = $"Database Healthy. {idMap.Count} Unique IDs indexed.";
			_cacheValidationType = MessageType.Info;
		}

		private void FixDuplicateIDs()
		{
			HashSet<string> seenIds = new HashSet<string>();
			int fixedCount = 0;

			foreach (var obj in _db.SOQueryEntities)
			{
				if (obj == null) continue;
				if (obj is not ISOQueryEntity entity) continue;
				if (string.IsNullOrEmpty(entity.Id)) continue;

				if (seenIds.Contains(entity.Id))
				{
					string newId = $"{entity.Id}_{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";

					SerializedObject so = new SerializedObject(obj);
					SerializedProperty idProp = so.FindProperty("Id");
					if (idProp == null) idProp = so.FindProperty("id");
					if (idProp == null) idProp = so.FindProperty("_id");
					if (idProp == null) idProp = so.FindProperty("<Id>k__BackingField");

					if (idProp != null)
					{
						idProp.stringValue = newId;
						so.ApplyModifiedProperties();
						fixedCount++;
						Debug.Log($"[SOQuery] Fixed duplicate on '{obj.name}'. ID changed from '{entity.Id}' to '{newId}'");
					}
					else
					{
						Debug.LogError($"[SOQuery] Could not auto-fix '{obj.name}'. Could not find SerializedProperty for 'Id'.");
					}
				}
				else
				{
					seenIds.Add(entity.Id);
				}
			}

			if (fixedCount > 0)
			{
				AssetDatabase.SaveAssets();
				SOQueryDatabaseProcessor.BuildCache(_db);
				RunCacheIntegrityCheck();
			}
		}

		private void DrawSearchArea()
		{
			EditorGUILayout.LabelField("Query Playground", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical("box");

			// Search Bar & Pills
			EditorGUILayout.BeginHorizontal();
			var currentTags = SOQueryDatabase.ParseTags(_searchString);
			foreach (var tag in currentTags.ToList())
			{
				if (GUILayout.Button($"{tag}  ×", _tagPillStyle, GUILayout.Height(20))) RemoveTag(tag);
			}
			GUILayout.FlexibleSpace();

			Color c = GUI.backgroundColor; GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
			if (GUILayout.Button("+ Add Filter", GUILayout.Width(100), GUILayout.Height(20))) ShowAddFilterDropdown();
			GUI.backgroundColor = c;
			EditorGUILayout.EndHorizontal();

			// Calculate Results (Live vs Cache)
			var liveResults = FilterList(currentTags);
			ValidateCacheAgainstLive(currentTags, liveResults);

			// Draw Cache Status Banner
			if (_isCacheStale)
			{
				EditorGUILayout.BeginVertical(EditorStyles.helpBox);
				EditorGUILayout.BeginHorizontal();
				var icon = EditorGUIUtility.IconContent("console.warnicon.sml");
				GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));

				GUIStyle redLabel = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.4f, 0.4f) }, fontSize = 11, fontStyle = FontStyle.Bold };
				GUILayout.Label($"Cache Mismatch: {_staleReason}", redLabel);

				if (GUILayout.Button("Fix Now", EditorStyles.miniButton, GUILayout.Width(60)))
				{
					SOQueryDatabaseProcessor.BuildCache(_db);
					EditorUtility.SetDirty(_db);
					Repaint();
				}
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.EndVertical();
			}
			else if (currentTags.Count > 0)
			{
				EditorGUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();

				var icon = EditorGUIUtility.IconContent("TestPassed");
				if (icon == null) icon = EditorGUIUtility.IconContent("Collab");

				if (icon != null) GUILayout.Label(icon, GUILayout.Width(16), GUILayout.Height(14));

				GUILayout.Label("Cache Verified", EditorStyles.miniLabel);
				EditorGUILayout.EndHorizontal();
			}

			// Pagination
			int totalCount = liveResults.Count;
			int totalPages = Mathf.CeilToInt((float)totalCount / ITEMS_PER_PAGE);
			if (_currentPage >= totalPages) _currentPage = Mathf.Max(0, totalPages - 1);
			var pageResults = liveResults.Skip(_currentPage * ITEMS_PER_PAGE).Take(ITEMS_PER_PAGE).ToList();

			EditorGUILayout.Space(5);
			EditorGUILayout.BeginHorizontal();
			long subsetSize = CalculateTotalSize(liveResults);
			GUI.color = Color.cyan;
			EditorGUILayout.LabelField($"Showing {totalCount} items ({FormatBytes(subsetSize)})", EditorStyles.miniLabel, GUILayout.Width(180));
			GUI.color = Color.white;
			GUILayout.FlexibleSpace();
			if (totalPages > 1)
			{
				if (GUILayout.Button("◄", EditorStyles.miniButtonLeft, GUILayout.Width(25))) _currentPage = Mathf.Max(0, _currentPage - 1);
				GUILayout.Label($"{_currentPage + 1} / {totalPages}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(50));
				if (GUILayout.Button("►", EditorStyles.miniButtonRight, GUILayout.Width(25))) _currentPage = Mathf.Min(totalPages - 1, _currentPage + 1);
			}
			EditorGUILayout.EndHorizontal();

			// Result
			_showConfigs = EditorGUILayout.Foldout(_showConfigs, "Results Preview", true);
			if (_showConfigs)
			{
				_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(400));
				foreach (var item in pageResults)
				{
					bool isMissingFromCache = !_cachedResultIDs.Contains(item.GetInstanceID());
					DrawEntityLine(item, isMissingFromCache);
				}
				if (pageResults.Count == 0) EditorGUILayout.HelpBox("No items match your filter.", MessageType.Info);
				EditorGUILayout.EndScrollView();
			}
			EditorGUILayout.EndVertical();
		}

		// --- Cache Validation Logic ---

		private void ValidateCacheAgainstLive(List<string> tags, List<ScriptableObject> liveResults)
		{
			if (tags == null || tags.Count == 0)
			{
				_isCacheStale = false;
				_cachedResultIDs.Clear();
				return;
			}

			// Simulate Cache Lookup
			var tagIndexProp = serializedObject.FindProperty("_serializedTagIndex");
			var queryCacheProp = serializedObject.FindProperty("_serializedQueryCache");

			HashSet<int> cacheResultIDs = null;

			// Check Prewarm first
			string key = string.Join("|", tags);
			bool prewarmFound = false;
			for (int i = 0; i < queryCacheProp.arraySize; i++)
			{
				var entry = queryCacheProp.GetArrayElementAtIndex(i);
				if (entry.FindPropertyRelative("QueryKey").stringValue == key)
				{
					cacheResultIDs = new HashSet<int>();
					var results = entry.FindPropertyRelative("Results");
					for (int k = 0; k < results.arraySize; k++)
					{
						var obj = results.GetArrayElementAtIndex(k).objectReferenceValue;
						if (obj != null) cacheResultIDs.Add(obj.GetInstanceID());
					}
					prewarmFound = true;
					break;
				}
			}

			// If not prewarmed, simulate index intersection
			if (!prewarmFound)
			{
				foreach (var tag in tags)
				{
					HashSet<int> currentTagIDs = new HashSet<int>();
					bool tagFound = false;

					for (int i = 0; i < tagIndexProp.arraySize; i++)
					{
						var entry = tagIndexProp.GetArrayElementAtIndex(i);
						if (entry.FindPropertyRelative("Tag").stringValue == tag)
						{
							var entityList = entry.FindPropertyRelative("Entities");
							for (int k = 0; k < entityList.arraySize; k++)
							{
								var obj = entityList.GetArrayElementAtIndex(k).objectReferenceValue;
								if (obj != null) currentTagIDs.Add(obj.GetInstanceID());
							}
							tagFound = true;
							break;
						}
					}

					if (!tagFound)
					{
						cacheResultIDs = new HashSet<int>();
						break;
					}

					if (cacheResultIDs == null) cacheResultIDs = currentTagIDs;
					else cacheResultIDs.IntersectWith(currentTagIDs);
				}
			}

			if (cacheResultIDs == null) cacheResultIDs = new HashSet<int>();
			_cachedResultIDs = cacheResultIDs;

			// Compare
			int liveCount = liveResults.Count;
			int cacheCount = cacheResultIDs.Count;

			if (liveCount != cacheCount)
			{
				_isCacheStale = true;
				_staleReason = $"Live found {liveCount}, Cache found {cacheCount}.";
			}
			else
			{
				bool mismatch = false;
				foreach (var liveItem in liveResults)
				{
					if (!cacheResultIDs.Contains(liveItem.GetInstanceID()))
					{
						mismatch = true;
						break;
					}
				}

				if (mismatch)
				{
					_isCacheStale = true;
					_staleReason = "Results differ (IDs mismatch).";
				}
				else
				{
					_isCacheStale = false;
					_staleReason = "";
				}
			}
		}

		private void DrawEntityLine(ScriptableObject obj, bool missingFromCache)
		{
			if (obj == null) return;
			int instanceId = obj.GetInstanceID();
			bool isExpanded = _expandedItems.Contains(instanceId);

			if (missingFromCache && _isCacheStale)
			{
				GUI.backgroundColor = new Color(1f, 0.8f, 0.8f);
			}

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			GUI.backgroundColor = Color.white;

			EditorGUILayout.BeginHorizontal();

			bool newExpanded = EditorGUILayout.Foldout(isExpanded, GUIContent.none, true);
			if (newExpanded != isExpanded) { if (newExpanded) _expandedItems.Add(instanceId); else _expandedItems.Remove(instanceId); }

			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ObjectField(obj, typeof(ScriptableObject), false, GUILayout.Width(180));
			}

			if (obj is ISOQueryEntity entity)
			{
				long size = Profiler.GetRuntimeMemorySizeLong(obj);
				EditorGUILayout.LabelField(FormatBytes(size), EditorStyles.miniLabel, GUILayout.Width(45));

				if (missingFromCache && _isCacheStale)
				{
					var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.red }, fontStyle = FontStyle.Bold };
					GUILayout.Label("! NOT IN CACHE", style, GUILayout.Width(100));
				}
				else if (!newExpanded)
				{
					Type t = obj.GetType();
					if (!SOQueryDatabase.IsTypeExcluded(t)) if (GUILayout.Button(t.Name, _typeButtonStyle)) AddTag(t.Name);
					foreach (var tag in entity.Tags.Take(4)) if (GUILayout.Button(tag, _resultTagButtonStyle)) AddTag(tag);
					if (entity.Tags.Count > 4) GUILayout.Label($"+{entity.Tags.Count - 4}", EditorStyles.miniLabel);
				}
			}
			GUILayout.FlexibleSpace();
			EditorGUILayout.EndHorizontal();

			if (newExpanded && obj is ISOQueryEntity expandedEntity) DrawExpandedDetails(obj, expandedEntity);
			EditorGUILayout.EndVertical();
		}

		private void AnalyzeTags()
		{
			_tagStats.Clear();
			foreach (var tag in GetConstantTags()) GetOrAddStats(tag).IsContainer = true;

			if (_db.SOQueryEntities == null) return;

			foreach (var obj in _db.SOQueryEntities)
			{
				if (obj == null) continue;

				Type tType = obj.GetType();
				while (tType != null && tType != typeof(ScriptableObject))
				{
					if (!SOQueryDatabase.IsTypeExcluded(tType)) GetOrAddStats(tType.Name).TypeCount++;
					tType = tType.BaseType;
				}

				if (obj is ISOQueryEntity entity)
				{
					if (entity.Tags is SOQueryTags soTags)
					{
						var editorList = GetPrivateField<List<string>>(soTags, "_tags") ?? new List<string>();
						foreach (var t in editorList) GetOrAddStats(t).EditorCount++;

						var runtimeSet = GetPrivateProperty<IEnumerable<string>>(soTags, "RuntimeTags") ??
										 GetPrivateField<HashSet<string>>(soTags, "_runtimeTags");
						if (runtimeSet != null) foreach (var t in runtimeSet) GetOrAddStats(t).RuntimeCount++;
					}
					else foreach (var t in entity.Tags) GetOrAddStats(t).EditorCount++;
				}
			}
			_sortedTags = _tagStats.Keys.OrderByDescending(k => _tagStats[k].TotalCount).ThenBy(k => k).ToList();
		}

		private TagStats GetOrAddStats(string tag)
		{
			if (!_tagStats.TryGetValue(tag, out var stats)) { stats = new TagStats(); _tagStats[tag] = stats; }
			return stats;
		}

		private void DrawTagsExplorer()
		{
			_showTagExplorer = EditorGUILayout.Foldout(_showTagExplorer, "Tags Explorer", true);
			if (!_showTagExplorer) return;

			EditorGUILayout.BeginVertical("box");
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

			EditorGUI.BeginChangeCheck();
			_explorerSearchString = EditorGUILayout.TextField(_explorerSearchString, _toolbarSearchField, GUILayout.ExpandWidth(true));
			if (EditorGUI.EndChangeCheck()) { }

			if (!string.IsNullOrEmpty(_explorerSearchString))
			{
				if (GUILayout.Button(GUIContent.none, _toolbarCancelButton)) { _explorerSearchString = ""; GUI.FocusControl(null); }
			}

			EditorGUILayout.Space(10);
			_explorerFilter = (TagSourceFilter)EditorGUILayout.EnumFlagsField(_explorerFilter, EditorStyles.toolbarDropDown, GUILayout.Width(100));
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.Space(2);

			var visibleTags = _sortedTags.Where(tag =>
			{
				var stats = _tagStats[tag];
				if (!string.IsNullOrEmpty(_explorerSearchString)) if (tag.IndexOf(_explorerSearchString, StringComparison.OrdinalIgnoreCase) < 0) return false;
				bool matches = false;
				if ((_explorerFilter & TagSourceFilter.Container) != 0 && stats.IsContainer) matches = true;
				if ((_explorerFilter & TagSourceFilter.Type) != 0 && stats.TypeCount > 0) matches = true;
				if ((_explorerFilter & TagSourceFilter.Editor) != 0 && stats.EditorCount > 0) matches = true;
				if ((_explorerFilter & TagSourceFilter.Runtime) != 0 && stats.RuntimeCount > 0) matches = true;
				return matches;
			}).ToList();

			EditorGUILayout.LabelField($"Found {visibleTags.Count} matching tags", EditorStyles.miniLabel);

			if (visibleTags.Count > 0)
			{
				float height = Mathf.Min(250, visibleTags.Count * 22 + 10);
				_explorerScrollPos = EditorGUILayout.BeginScrollView(_explorerScrollPos, GUILayout.Height(height));
				foreach (var tag in visibleTags) DrawTagExplorerRow(tag, _tagStats[tag]);
				EditorGUILayout.EndScrollView();
			}
			else EditorGUILayout.HelpBox("No tags match filters.", MessageType.Info);

			if (GUILayout.Button("Refresh Stats", EditorStyles.miniButton)) AnalyzeTags();
			EditorGUILayout.EndVertical();
		}

		private void DrawTagExplorerRow(string tag, TagStats stats)
		{
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button(tag, _resultTagButtonStyle, GUILayout.Height(18))) AddTag(tag);
			GUILayout.FlexibleSpace();
			if (stats.RuntimeCount > 0) GUILayout.Label(new GUIContent($"R ({stats.RuntimeCount})", "Runtime"), _badgeRuntime, GUILayout.Width(40));
			if (stats.EditorCount > 0) GUILayout.Label(new GUIContent($"E ({stats.EditorCount})", "Editor"), _badgeEditor, GUILayout.Width(40));
			if (stats.TypeCount > 0) GUILayout.Label(new GUIContent($"T ({stats.TypeCount})", "Type"), _badgeType, GUILayout.Width(40));
			if (stats.IsContainer) GUILayout.Label(new GUIContent("C", "Container"), _badgeContainer, GUILayout.Width(20));
			EditorGUILayout.EndHorizontal();
		}

		private void DrawExpandedDetails(ScriptableObject obj, ISOQueryEntity entity)
		{
			var allTags = SOQueryDatabase.GetSearchableTags(obj).ToList();
			EditorGUILayout.BeginVertical(_expandedBoxStyle);
			EditorGUILayout.LabelField("All Searchable Tags:", EditorStyles.miniBoldLabel);
			float width = EditorGUIUtility.currentViewWidth - 60;
			float currentX = 0;
			EditorGUILayout.BeginHorizontal();
			foreach (var tag in allTags)
			{
				bool isManual = entity.Tags.Contains(tag);
				GUIStyle style = isManual ? _resultTagButtonStyle : _typeButtonStyle;
				float btnWidth = style.CalcSize(new GUIContent(tag)).x;
				if (currentX + btnWidth > width) { currentX = 0; EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); }
				if (GUILayout.Button(tag, style)) AddTag(tag);
				currentX += btnWidth + 4;
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndVertical();
		}

		private T GetPrivateField<T>(object target, string fieldName) where T : class
		{
			var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (field != null) return field.GetValue(target) as T;
			return null;
		}

		private T GetPrivateProperty<T>(object target, string propName) where T : class
		{
			var prop = target.GetType().GetProperty(propName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (prop != null) return prop.GetValue(target) as T;
			return null;
		}

		private void InitializeStyles()
		{
			if (_tagPillStyle == null) _tagPillStyle = new GUIStyle("HelpBox") { fontSize = 11, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 20, 3, 3), margin = new RectOffset(0, 5, 0, 0) };
			if (_resultTagButtonStyle == null) _resultTagButtonStyle = new GUIStyle("minibutton") { fontSize = 10, alignment = TextAnchor.MiddleCenter, margin = new RectOffset(2, 2, 2, 2), fixedHeight = 18 };
			if (_typeButtonStyle == null) _typeButtonStyle = new GUIStyle("minibutton") { fontSize = 11, fontStyle = FontStyle.Bold, fixedHeight = 18, normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.4f, 0.7f, 1f) : new Color(0.1f, 0.3f, 0.8f) } };
			if (_expandedBoxStyle == null) _expandedBoxStyle = new GUIStyle("CN Box") { padding = new RectOffset(10, 10, 5, 5), margin = new RectOffset(5, 5, 0, 5) };
			if (_badgeContainer == null) { _badgeContainer = new GUIStyle("CN CountBadge") { alignment = TextAnchor.MiddleCenter, fontSize = 9, fixedHeight = 16 }; _badgeContainer.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 1f, 0.7f) : new Color(0.1f, 0.4f, 0.1f); }
			if (_badgeType == null) { _badgeType = new GUIStyle("CN CountBadge") { alignment = TextAnchor.MiddleCenter, fontSize = 9, fixedHeight = 16 }; _badgeType.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.8f, 1f) : new Color(0.1f, 0.3f, 0.8f); }
			if (_badgeEditor == null) _badgeEditor = new GUIStyle("CN CountBadge") { alignment = TextAnchor.MiddleCenter, fontSize = 9, fixedHeight = 16 };
			if (_badgeRuntime == null) { _badgeRuntime = new GUIStyle("CN CountBadge") { alignment = TextAnchor.MiddleCenter, fontSize = 9, fixedHeight = 16 }; _badgeRuntime.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1f, 0.8f, 0.4f) : new Color(0.8f, 0.5f, 0.1f); }
			if (_toolbarSearchField == null) { _toolbarSearchField = GUI.skin.FindStyle("ToolbarSeachTextField") ?? GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField; }
			if (_toolbarCancelButton == null) { _toolbarCancelButton = GUI.skin.FindStyle("ToolbarSeachCancelButton") ?? GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? GUIStyle.none; }
			if (_staleRowStyle == null) { _staleRowStyle = new GUIStyle(EditorStyles.helpBox); _staleRowStyle.normal.background = Texture2D.whiteTexture; }
		}

		private void DrawEditorHeader()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField($"Database Contains {_db.SOQueryEntities.Count} Entities", EditorStyles.boldLabel);
			long totalSize = CalculateTotalSize(_db.SOQueryEntities);
			EditorGUILayout.LabelField($"Total Memory: {FormatBytes(totalSize)}", EditorStyles.miniLabel);
			EditorGUILayout.EndVertical();
		}

		public static HashSet<string> GetSOQueryEntityTags(SOQueryDatabase db)
		{
			HashSet<string> returnValue = new HashSet<string>();
			foreach (var obj in db.SOQueryEntities) { if (obj == null) continue; foreach (var tag in SOQueryDatabase.GetSearchableTags(obj)) returnValue.Add(tag); }
			foreach (var tag in GetConstantTags()) returnValue.Add(tag);
			return returnValue;
		}

		private void ShowAddFilterDropdown()
		{
			var dropdown = new TagSearchDropdown(new AdvancedDropdownState(), GetSOQueryEntityTags(_db), AddTag);
			dropdown.Show(new Rect(Event.current.mousePosition, Vector2.zero));
		}

		public static IEnumerable<string> GetConstantTags()
		{
			var containerTypes = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).Where(t => t.IsDefined(typeof(SOQueryTagsContainerAttribute), false));
			foreach (var type in containerTypes) { if (type.IsEnum) foreach (string n in System.Enum.GetNames(type)) yield return n; else { var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).Where(fi => fi.IsLiteral && !fi.IsInitOnly && fi.FieldType == typeof(string)); foreach (var f in fields) { string v = (string)f.GetRawConstantValue(); if (!string.IsNullOrEmpty(v)) yield return v; } } }
		}

		private void AddTag(string newTag) { var tags = SOQueryDatabase.ParseTags(_searchString); string trimmed = newTag.Trim(); if (!tags.Contains(trimmed)) { if (tags.Count > 0) _searchString += ", "; _searchString += trimmed; _currentPage = 0; Repaint(); } }
		private void RemoveTag(string tagToRemove) { var tags = SOQueryDatabase.ParseTags(_searchString); tags.Remove(tagToRemove); _searchString = string.Join(", ", tags); _currentPage = 0; Repaint(); }

		private List<ScriptableObject> FilterList(List<string> searchTerms)
		{
			if (searchTerms == null || searchTerms.Count == 0) return _db.SOQueryEntities.Cast<ScriptableObject>().ToList();
			var filtered = new List<ScriptableObject>();
			foreach (var obj in _db.SOQueryEntities) { if (obj == null) continue; var entityTags = new HashSet<string>(SOQueryDatabase.GetSearchableTags(obj)); if (searchTerms.All(term => entityTags.Contains(term))) filtered.Add(obj); }
			return filtered;
		}

		private long CalculateTotalSize(IEnumerable<ScriptableObject> objs) { long total = 0; foreach (var obj in objs) if (obj != null) total += Profiler.GetRuntimeMemorySizeLong(obj); return total; }
		private string FormatBytes(long bytes) { if (bytes < 1024) return $"{bytes} B"; if (bytes < 1024 * 1024) return $"{(bytes / 1024f):F2} KB"; return $"{(bytes / (1024f * 1024f)):F2} MB"; }
	}
}