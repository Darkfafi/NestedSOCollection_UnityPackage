#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine.Profiling;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;
using NestedSO.Processor;

namespace NestedSO.SOEditor
{
	[CustomEditor(typeof(SOQueryDatabase))]
	public class SOQueryDatabaseEditor : Editor
	{
		private SOQueryDatabase _db;

		// --- Search & Pagination ---
		private string _searchString = "";
		private string _idSearchString = "";
		private Vector2 _scrollPos;
		private bool _showConfigs = true;

		// --- Tags Explorer State ---
		private string _explorerSearchString = "";
		private Vector2 _explorerScrollPos;
		private int _explorerCurrentPage = 0;

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
		private bool _hasDuplicateErrors = false;

		// --- Expansion State ---
		private HashSet<int> _expandedItems = new HashSet<int>();
		private bool _showRawData = false;

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

		private void OnEnable()
		{
			_db = (SOQueryDatabase)target;
			AnalyzeTags();
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			InitializeStyles();

			EditorGUILayout.Space(5);
			DrawEditorHeaderAndOperations();

			EditorGUILayout.Space(10);
			DrawSearchArea();

			EditorGUILayout.Space(10);
			DrawTagsExplorer();

			EditorGUILayout.Space(10);
			DrawConfiguration();

			EditorGUILayout.Space(10);
			DrawRawData();

			serializedObject.ApplyModifiedProperties();
		}

		// ==================================================================================
		// HEADER & OPERATIONS
		// ==================================================================================

		private void DrawEditorHeaderAndOperations()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);

			// Stats Row
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField($"Database Contains {_db.SOQueryEntities.Count} Entities", EditorStyles.boldLabel);
			long totalSize = CalculateTotalSize(_db.SOQueryEntities);
			EditorGUILayout.LabelField($"Memory: {FormatBytes(totalSize)}", EditorStyles.miniLabel, GUILayout.Width(100));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(5);

			// Operations Row
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
			if (GUILayout.Button("Verify IDs & Cache", GUILayout.Height(24)))
			{
				RunCacheIntegrityCheck();
			}
			EditorGUILayout.EndHorizontal();

			// Validation Results
			if (!string.IsNullOrEmpty(_cacheValidationResult))
			{
				EditorGUILayout.Space(5);
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

			EditorGUILayout.EndVertical();
		}

		// ==================================================================================
		// QUERY AREA (Playground)
		// ==================================================================================

		private void DrawSearchArea()
		{
			EditorGUILayout.LabelField("Query Playground", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical("box");

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("ID Contains:", GUILayout.Width(75));
			_idSearchString = EditorGUILayout.TextField(_idSearchString, _toolbarSearchField);
			if (GUILayout.Button(GUIContent.none, _toolbarCancelButton))
			{
				_idSearchString = "";
				_currentPage = 0;
				GUI.FocusControl(null);
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(2);

			EditorGUILayout.BeginHorizontal();
			var currentTags = SOQueryDatabase.ParseTags(_searchString);
			foreach (var tag in currentTags.ToList())
			{
				if (GUILayout.Button($"{tag}  ×", _tagPillStyle, GUILayout.Height(20))) RemoveTag(tag);
			}
			GUILayout.FlexibleSpace();

			Color c = GUI.backgroundColor; GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
			if (GUILayout.Button("+ Add Tag Filter", GUILayout.Width(110), GUILayout.Height(20))) ShowAddFilterDropdown();
			GUI.backgroundColor = c;
			EditorGUILayout.EndHorizontal();

			// Calculate Results
			var liveResults = FilterList(currentTags, _idSearchString);
			ValidateCacheAgainstLive(currentTags, _idSearchString, liveResults);

			// Draw Cache Status
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
			else if (currentTags.Count > 0 || !string.IsNullOrEmpty(_idSearchString))
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

			// Result List
			_showConfigs = EditorGUILayout.Foldout(_showConfigs, "Results Preview", true);
			if (_showConfigs)
			{
				_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(350));
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

		// ==================================================================================
		// TAGS EXPLORER (With Pagination)
		// ==================================================================================

		private void DrawTagsExplorer()
		{
			EditorGUILayout.LabelField("Tags Explorer", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical("box");

			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

			EditorGUI.BeginChangeCheck();
			_explorerSearchString = EditorGUILayout.TextField(_explorerSearchString, _toolbarSearchField, GUILayout.ExpandWidth(true));
			if (EditorGUI.EndChangeCheck()) { _explorerCurrentPage = 0; } // Reset page on search

			if (!string.IsNullOrEmpty(_explorerSearchString))
			{
				if (GUILayout.Button(GUIContent.none, _toolbarCancelButton))
				{
					_explorerSearchString = "";
					_explorerCurrentPage = 0;
					GUI.FocusControl(null);
				}
			}

			EditorGUILayout.Space(10);
			EditorGUI.BeginChangeCheck();

			_explorerFilter = (TagSourceFilter)EditorGUILayout.EnumFlagsField(GUIContent.none, _explorerFilter, GUILayout.Width(100));

			if (EditorGUI.EndChangeCheck()) { _explorerCurrentPage = 0; } // Reset page on filter change

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

			// --- NEW: Pagination Logic for Tags Explorer ---
			int totalCount = visibleTags.Count;
			int totalPages = Mathf.CeilToInt((float)totalCount / ITEMS_PER_PAGE);
			if (_explorerCurrentPage >= totalPages) _explorerCurrentPage = Mathf.Max(0, totalPages - 1);
			var pageTags = visibleTags.Skip(_explorerCurrentPage * ITEMS_PER_PAGE).Take(ITEMS_PER_PAGE).ToList();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField($"Found {totalCount} matching tags", EditorStyles.miniLabel, GUILayout.Width(180));
			GUILayout.FlexibleSpace();
			if (totalPages > 1)
			{
				if (GUILayout.Button("◄", EditorStyles.miniButtonLeft, GUILayout.Width(25))) _explorerCurrentPage = Mathf.Max(0, _explorerCurrentPage - 1);
				GUILayout.Label($"{_explorerCurrentPage + 1} / {totalPages}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(50));
				if (GUILayout.Button("►", EditorStyles.miniButtonRight, GUILayout.Width(25))) _explorerCurrentPage = Mathf.Min(totalPages - 1, _explorerCurrentPage + 1);
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(2);

			if (pageTags.Count > 0)
			{
				try
				{
					_explorerScrollPos = EditorGUILayout.BeginScrollView(_explorerScrollPos, GUILayout.Height(300));
				}
				catch (InvalidCastException) { }
				foreach (var tag in pageTags) DrawTagExplorerRow(tag, _tagStats[tag]);
				EditorGUILayout.EndScrollView();
			}
			else
			{
				EditorGUILayout.HelpBox("No tags match filters.", MessageType.Info);
			}

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

		// ==================================================================================
		// CONFIGURATION & RAW DATA
		// ==================================================================================

		private void DrawConfiguration()
		{
			EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical("box");

			EditorGUI.BeginChangeCheck();
			bool autoRefresh = EditorGUILayout.Toggle("Auto Refresh On Play", SOQueryDatabaseProcessor.AutoRefreshOnPlay);
			if (EditorGUI.EndChangeCheck())
			{
				SOQueryDatabaseProcessor.AutoRefreshOnPlay = autoRefresh;
			}

			EditorGUILayout.EndVertical();
		}

		private void DrawRawData()
		{
			_showRawData = EditorGUILayout.Foldout(_showRawData, "Raw Data (Advanced)", true);
			if (_showRawData)
			{
				SerializedProperty listProp = serializedObject.FindProperty("SOQueryEntities");
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(listProp, true);
				if (EditorGUI.EndChangeCheck()) AnalyzeTags();
			}
		}

		// ==================================================================================
		// VALIDATION & ENTITY DRAWING
		// ==================================================================================

		private void ValidateCacheAgainstLive(List<string> tags, string idSearch, List<ScriptableObject> liveResults)
		{
			if ((tags == null || tags.Count == 0) && string.IsNullOrEmpty(idSearch))
			{
				_isCacheStale = false;
				_cachedResultIDs.Clear();
				return;
			}

			var tagIndexProp = serializedObject.FindProperty("_serializedTagIndex");
			var queryCacheProp = serializedObject.FindProperty("_serializedQueryCache");
			var idIndexProp = serializedObject.FindProperty("_serializedIdIndex");
			var mainListProp = serializedObject.FindProperty("SOQueryEntities");

			HashSet<int> cacheResultInstanceIDs = null;

			if (tags != null && tags.Count > 0)
			{
				string key = string.Join("|", tags);
				bool prewarmFound = false;

				for (int i = 0; i < queryCacheProp.arraySize; i++)
				{
					var entry = queryCacheProp.GetArrayElementAtIndex(i);
					if (entry.FindPropertyRelative("QueryKey").stringValue == key)
					{
						cacheResultInstanceIDs = new HashSet<int>();
						var indices = entry.FindPropertyRelative("ResultIndices");

						for (int k = 0; k < indices.arraySize; k++)
						{
							int index = indices.GetArrayElementAtIndex(k).intValue;
							if (index >= 0 && index < mainListProp.arraySize)
							{
								var obj = mainListProp.GetArrayElementAtIndex(index).objectReferenceValue;
								if (obj != null) cacheResultInstanceIDs.Add(obj.GetInstanceID());
							}
						}
						prewarmFound = true;
						break;
					}
				}

				if (!prewarmFound)
				{
					foreach (var tag in tags)
					{
						HashSet<int> currentTagInstanceIDs = new HashSet<int>();
						bool tagFound = false;

						for (int i = 0; i < tagIndexProp.arraySize; i++)
						{
							var entry = tagIndexProp.GetArrayElementAtIndex(i);
							if (entry.FindPropertyRelative("Tag").stringValue == tag)
							{
								var indices = entry.FindPropertyRelative("EntityIndices");
								for (int k = 0; k < indices.arraySize; k++)
								{
									int index = indices.GetArrayElementAtIndex(k).intValue;
									if (index >= 0 && index < mainListProp.arraySize)
									{
										var obj = mainListProp.GetArrayElementAtIndex(index).objectReferenceValue;
										if (obj != null) currentTagInstanceIDs.Add(obj.GetInstanceID());
									}
								}
								tagFound = true;
								break;
							}
						}

						if (!tagFound)
						{
							cacheResultInstanceIDs = new HashSet<int>();
							break;
						}

						if (cacheResultInstanceIDs == null) cacheResultInstanceIDs = currentTagInstanceIDs;
						else cacheResultInstanceIDs.IntersectWith(currentTagInstanceIDs);
					}
				}
			}

			if (!string.IsNullOrEmpty(idSearch))
			{
				HashSet<int> idMatchInstanceIDs = new HashSet<int>();
				string lowerIdSearch = idSearch.ToLowerInvariant();

				for (int i = 0; i < idIndexProp.arraySize; i++)
				{
					var entry = idIndexProp.GetArrayElementAtIndex(i);
					string cachedId = entry.FindPropertyRelative("Id").stringValue;

					if (cachedId.ToLowerInvariant().Contains(lowerIdSearch))
					{
						int index = entry.FindPropertyRelative("EntityIndex").intValue;
						if (index >= 0 && index < mainListProp.arraySize)
						{
							var obj = mainListProp.GetArrayElementAtIndex(index).objectReferenceValue;
							if (obj != null) idMatchInstanceIDs.Add(obj.GetInstanceID());
						}
					}
				}

				if (cacheResultInstanceIDs == null) cacheResultInstanceIDs = idMatchInstanceIDs;
				else cacheResultInstanceIDs.IntersectWith(idMatchInstanceIDs);
			}

			if (cacheResultInstanceIDs == null) cacheResultInstanceIDs = new HashSet<int>();
			_cachedResultIDs = cacheResultInstanceIDs;

			if (liveResults.Count != cacheResultInstanceIDs.Count)
			{
				_isCacheStale = true;
				_staleReason = $"Live: {liveResults.Count}, Cache: {cacheResultInstanceIDs.Count}";
			}
			else
			{
				bool mismatch = false;
				foreach (var liveItem in liveResults)
				{
					if (!_cachedResultIDs.Contains(liveItem.GetInstanceID())) { mismatch = true; break; }
				}
				_isCacheStale = mismatch;
				_staleReason = mismatch ? "Data Mismatch" : "";
			}
		}

		private void DrawEntityLine(ScriptableObject obj, bool missingFromCache)
		{
			if (obj == null) return;
			int instanceId = obj.GetInstanceID();
			bool isExpanded = _expandedItems.Contains(instanceId);

			if (missingFromCache && _isCacheStale) GUI.backgroundColor = new Color(1f, 0.8f, 0.8f);

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

		private void DrawExpandedDetails(ScriptableObject obj, ISOQueryEntity entity)
		{
			var allTags = SOQueryDatabase.GetSearchableTags(obj).ToList();
			EditorGUILayout.BeginVertical(_expandedBoxStyle);

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("ID:", EditorStyles.miniBoldLabel, GUILayout.Width(25));
			EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(entity.Id) ? "[No ID]" : entity.Id, EditorStyles.miniLabel, GUILayout.Height(16));
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.Space(2);

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

		// ==================================================================================
		// INTEGRITY & CACHE HELPERS
		// ==================================================================================

		private void RunCacheIntegrityCheck()
		{
			_hasDuplicateErrors = false;

			var idToIndices = new Dictionary<string, List<int>>();
			int nullRefs = 0;

			for (int i = 0; i < _db.SOQueryEntities.Count; i++)
			{
				var obj = _db.SOQueryEntities[i];
				if (obj == null)
				{
					nullRefs++;
					continue;
				}

				if (obj is ISOQueryEntity entity && !string.IsNullOrEmpty(entity.Id))
				{
					if (!idToIndices.ContainsKey(entity.Id)) idToIndices[entity.Id] = new List<int>();
					idToIndices[entity.Id].Add(i);
				}
			}

			var duplicates = idToIndices.Where(kvp => kvp.Value.Count > 1).ToList();

			if (duplicates.Count > 0)
			{
				_hasDuplicateErrors = true;
				string errorMsg = "CRITICAL: Duplicate IDs detected!\n";
				foreach (var dup in duplicates)
				{
					errorMsg += $"- ID '{dup.Key}' used by: {string.Join(", ", dup.Value.Select(idx => _db.SOQueryEntities[idx].name))}\n";
				}

				_cacheValidationResult = errorMsg;
				_cacheValidationType = MessageType.Error;
				return;
			}

			if (nullRefs > 0)
			{
				_cacheValidationResult = $"Database contains {nullRefs} null references. Please click 'Populate List'.";
				_cacheValidationType = MessageType.Warning;
				return;
			}

			var idIndexProp = serializedObject.FindProperty("_serializedIdIndex");

			if (idIndexProp.arraySize != idToIndices.Count)
			{
				_cacheValidationResult = $"Cache Desync: Source has {idToIndices.Count} unique IDs, but Cache has {idIndexProp.arraySize}. Please 'Rebuild Cache'.";
				_cacheValidationType = MessageType.Warning;
				return;
			}

			for (int i = 0; i < idIndexProp.arraySize; i++)
			{
				var entry = idIndexProp.GetArrayElementAtIndex(i);
				string cachedId = entry.FindPropertyRelative("Id").stringValue;
				int cachedIndex = entry.FindPropertyRelative("EntityIndex").intValue;

				if (!idToIndices.TryGetValue(cachedId, out var liveIndices))
				{
					_cacheValidationResult = $"Cache Desync: ID '{cachedId}' is in cache but missing from live data. Please 'Rebuild Cache'.";
					_cacheValidationType = MessageType.Warning;
					return;
				}

				if (liveIndices[0] != cachedIndex)
				{
					_cacheValidationResult = $"Cache Desync: ID '{cachedId}' points to wrong list index (Live: {liveIndices[0]}, Cache: {cachedIndex}). Please 'Rebuild Cache'.";
					_cacheValidationType = MessageType.Warning;
					return;
				}
			}

			_cacheValidationResult = $"Database Healthy. {idToIndices.Count} Unique IDs indexed and matched exactly.";
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

		// ==================================================================================
		// MISC UTILITIES
		// ==================================================================================

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

		private List<ScriptableObject> FilterList(List<string> searchTerms, string idSearchString)
		{
			var filtered = new List<ScriptableObject>();
			string lowerIdSearch = string.IsNullOrEmpty(idSearchString) ? "" : idSearchString.ToLowerInvariant();

			foreach (var obj in _db.SOQueryEntities)
			{
				if (obj == null) continue;

				if (searchTerms != null && searchTerms.Count > 0)
				{
					var entityTags = new HashSet<string>(SOQueryDatabase.GetSearchableTags(obj));
					if (!searchTerms.All(term => entityTags.Contains(term))) continue;
				}

				if (!string.IsNullOrEmpty(lowerIdSearch))
				{
					if (obj is ISOQueryEntity entity && !string.IsNullOrEmpty(entity.Id))
					{
						if (!entity.Id.ToLowerInvariant().Contains(lowerIdSearch)) continue;
					}
					else
					{
						continue;
					}
				}

				filtered.Add(obj);
			}

			return filtered;
		}

		private long CalculateTotalSize(IEnumerable<ScriptableObject> objs) { long total = 0; foreach (var obj in objs) if (obj != null) total += Profiler.GetRuntimeMemorySizeLong(obj); return total; }
		private string FormatBytes(long bytes) { if (bytes < 1024) return $"{bytes} B"; if (bytes < 1024 * 1024) return $"{(bytes / 1024f):F2} KB"; return $"{(bytes / (1024f * 1024f)):F2} MB"; }
	}
}
#endif