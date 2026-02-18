using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine.Profiling;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;

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
		public enum TagSourceFilter
		{
			Type = 1 << 0,
			Container = 1 << 1,
			Editor = 1 << 2,
			Runtime = 1 << 3
		}
		private TagSourceFilter _explorerFilter = (TagSourceFilter)~0; // Default: All checked

		// --- Expansion State ---
		private HashSet<int> _expandedItems = new HashSet<int>();

		// --- Pagination ---
		private int _currentPage = 0;
		private const int ITEMS_PER_PAGE = 20;

		// --- Cached Styles ---
		private GUIStyle _tagPillStyle;
		private GUIStyle _resultTagButtonStyle;
		private GUIStyle _typeButtonStyle;
		private GUIStyle _expandedBoxStyle;

		// Search Bar Styles
		private GUIStyle _toolbarSearchField;
		private GUIStyle _toolbarCancelButton;

		// Badge Styles
		private GUIStyle _badgeContainer;
		private GUIStyle _badgeType;
		private GUIStyle _badgeEditor;
		private GUIStyle _badgeRuntime;

		private void OnEnable()
		{
			_db = (SOQueryDatabase)target;
			AnalyzeTags();
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			InitializeStyles(); // <--- Now fully safe

			EditorGUILayout.Space(10);
			DrawEditorHeader();

			EditorGUILayout.Space(5);
			DrawTagsExplorer();

			EditorGUILayout.Space(5);
			DrawSearchArea();

			EditorGUILayout.Space(10);

			EditorGUILayout.LabelField("Raw Data", EditorStyles.boldLabel);
			SerializedProperty listProp = serializedObject.FindProperty("SOQueryEntities");

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(listProp, true);
			if (EditorGUI.EndChangeCheck())
			{
				AnalyzeTags();
			}

			serializedObject.ApplyModifiedProperties();
		}

		// ==================================================================================
		// TAGS EXPLORER (Corrected Styles)
		// ==================================================================================

		private void AnalyzeTags()
		{
			_tagStats.Clear();

			// Mark Container Tags
			foreach (var tag in GetConstantTags())
			{
				GetOrAddStats(tag).IsContainer = true;
			}

			if (_db.SOQueryEntities == null) return;

			foreach (var obj in _db.SOQueryEntities)
			{
				if (obj == null) continue;

				// Type Tags
				Type tType = obj.GetType();
				while (tType != null && tType != typeof(ScriptableObject))
				{
					if (!SOQuery.IsTypeExcluded(tType))
					{
						GetOrAddStats(tType.Name).TypeCount++;
					}
					tType = tType.BaseType;
				}

				// Instance Tags (Distinguish Editor vs Runtime)
				if (obj is ISOQueryEntity entity)
				{
					if (entity.Tags is SOQueryTags soTags)
					{
						// Editor Tags
						var editorList = GetPrivateField<List<string>>(soTags, "_tags") ?? new List<string>();
						foreach (var t in editorList) GetOrAddStats(t).EditorCount++;

						// Runtime Tags
						var runtimeSet = GetPrivateProperty<IEnumerable<string>>(soTags, "RuntimeTags") ??
										 GetPrivateField<HashSet<string>>(soTags, "_runtimeTags");

						if (runtimeSet != null)
						{
							foreach (var t in runtimeSet) GetOrAddStats(t).RuntimeCount++;
						}
					}
					else
					{
						foreach (var t in entity.Tags) GetOrAddStats(t).EditorCount++;
					}
				}
			}

			_sortedTags = _tagStats.Keys.OrderByDescending(k => _tagStats[k].TotalCount).ThenBy(k => k).ToList();
		}

		private TagStats GetOrAddStats(string tag)
		{
			if (!_tagStats.TryGetValue(tag, out var stats))
			{
				stats = new TagStats();
				_tagStats[tag] = stats;
			}
			return stats;
		}

		private void DrawTagsExplorer()
		{
			_showTagExplorer = EditorGUILayout.Foldout(_showTagExplorer, "Tags Explorer", true);
			if (!_showTagExplorer) return;

			EditorGUILayout.BeginVertical("box");

			// --- Safe Search Bar ---
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

			// Search Field
			EditorGUI.BeginChangeCheck();
			_explorerSearchString = EditorGUILayout.TextField(_explorerSearchString, _toolbarSearchField, GUILayout.ExpandWidth(true));
			EditorGUI.EndChangeCheck();

			// Cancel Button
			if (!string.IsNullOrEmpty(_explorerSearchString))
			{
				if (GUILayout.Button(GUIContent.none, _toolbarCancelButton))
				{
					_explorerSearchString = "";
					GUI.FocusControl(null);
				}
			}

			EditorGUILayout.Space(10);

			// Filter Dropdown
			_explorerFilter = (TagSourceFilter)EditorGUILayout.EnumFlagsField(_explorerFilter, EditorStyles.toolbarDropDown, GUILayout.Width(100));

			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(2);

			// --- Filter Logic ---
			var visibleTags = _sortedTags.Where(tag =>
			{
				var stats = _tagStats[tag];

				// Text Search
				if (!string.IsNullOrEmpty(_explorerSearchString))
				{
					if (tag.IndexOf(_explorerSearchString, StringComparison.OrdinalIgnoreCase) < 0) return false;
				}

				// Source Filter
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

				foreach (var tag in visibleTags)
				{
					DrawTagExplorerRow(tag, _tagStats[tag]);
				}

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

			// Tag Name Button
			if (GUILayout.Button(tag, _resultTagButtonStyle, GUILayout.Height(18))) AddTag(tag);

			GUILayout.FlexibleSpace();

			// --- Badges (R, E, C, T) ---

			// R (Runtime) - Orange
			if (stats.RuntimeCount > 0)
				GUILayout.Label(new GUIContent($"R ({stats.RuntimeCount})", "Runtime Dynamic Usage"), _badgeRuntime, GUILayout.Width(40));

			// E (Editor) - Grey
			if (stats.EditorCount > 0)
				GUILayout.Label(new GUIContent($"E ({stats.EditorCount})", "Editor Serialized Usage"), _badgeEditor, GUILayout.Width(40));

			// T (Type) - Blue
			if (stats.TypeCount > 0)
				GUILayout.Label(new GUIContent($"T ({stats.TypeCount})", "Class Type Usage"), _badgeType, GUILayout.Width(40));

			// C (Container) - Green
			// Containers usually don't have a "count" of definitions, it's just a boolean flag of existence
			if (stats.IsContainer)
				GUILayout.Label(new GUIContent("C", "Defined in Code Container"), _badgeContainer, GUILayout.Width(20));

			EditorGUILayout.EndHorizontal();
		}

		// ==================================================================================
		// QUERY AREA & REST (Standard)
		// ==================================================================================

		private void DrawSearchArea()
		{
			EditorGUILayout.LabelField("Query Playground", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical("box");

			EditorGUILayout.BeginHorizontal();
			var currentTags = SOQuery.ParseTags(_searchString);
			foreach (var tag in currentTags.ToList())
			{
				if (GUILayout.Button($"{tag}  ×", _tagPillStyle, GUILayout.Height(20))) RemoveTag(tag);
			}
			GUILayout.FlexibleSpace();

			Color c = GUI.backgroundColor; GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
			if (GUILayout.Button("+ Add Filter", GUILayout.Width(100), GUILayout.Height(20))) ShowAddFilterDropdown();
			GUI.backgroundColor = c;

			EditorGUILayout.EndHorizontal();

			var allResults = FilterList(currentTags);
			int totalCount = allResults.Count;

			int totalPages = Mathf.CeilToInt((float)totalCount / ITEMS_PER_PAGE);
			if (_currentPage >= totalPages) _currentPage = Mathf.Max(0, totalPages - 1);
			var pageResults = allResults.Skip(_currentPage * ITEMS_PER_PAGE).Take(ITEMS_PER_PAGE).ToList();

			EditorGUILayout.Space(5);
			EditorGUILayout.BeginHorizontal();
			long subsetSize = CalculateTotalSize(allResults);
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

			_showConfigs = EditorGUILayout.Foldout(_showConfigs, "Results Preview", true);
			if (_showConfigs)
			{
				_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(400));
				foreach (var item in pageResults) DrawEntityLine(item);
				if (pageResults.Count == 0) EditorGUILayout.HelpBox("No items match your filter.", MessageType.Info);
				EditorGUILayout.EndScrollView();
			}
			EditorGUILayout.EndVertical();
		}

		private void DrawEntityLine(ScriptableObject obj)
		{
			if (obj == null) return;
			int instanceId = obj.GetInstanceID();
			bool isExpanded = _expandedItems.Contains(instanceId);

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
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

				if (!newExpanded)
				{
					Type t = obj.GetType();
					if (!SOQuery.IsTypeExcluded(t))
					{
						if (GUILayout.Button(t.Name, _typeButtonStyle)) AddTag(t.Name);
					}
					foreach (var tag in entity.Tags.Take(4))
					{
						if (GUILayout.Button(tag, _resultTagButtonStyle)) AddTag(tag);
					}
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
			var allTags = SOQuery.GetSearchableTags(obj).ToList();
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

		// ==================================================================================
		// HELPERS (Including Safe Style Init)
		// ==================================================================================

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
			if (_tagPillStyle == null)
				_tagPillStyle = new GUIStyle("HelpBox") { fontSize = 11, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 20, 3, 3), margin = new RectOffset(0, 5, 0, 0) };

			if (_resultTagButtonStyle == null)
				_resultTagButtonStyle = new GUIStyle("minibutton") { fontSize = 10, alignment = TextAnchor.MiddleCenter, margin = new RectOffset(2, 2, 2, 2), fixedHeight = 18 };

			if (_typeButtonStyle == null)
				_typeButtonStyle = new GUIStyle("minibutton") { fontSize = 11, fontStyle = FontStyle.Bold, fixedHeight = 18, normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.4f, 0.7f, 1f) : new Color(0.1f, 0.3f, 0.8f) } };

			if (_expandedBoxStyle == null)
				_expandedBoxStyle = new GUIStyle("CN Box") { padding = new RectOffset(10, 10, 5, 5), margin = new RectOffset(5, 5, 0, 5) };

			// --- Badges ---
			if (_badgeContainer == null)
			{
				_badgeContainer = new GUIStyle("CN CountBadge") { alignment = TextAnchor.MiddleCenter, fontSize = 9, fixedHeight = 16 };
				_badgeContainer.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 1f, 0.7f) : new Color(0.1f, 0.4f, 0.1f);
			}
			if (_badgeType == null)
			{
				_badgeType = new GUIStyle("CN CountBadge") { alignment = TextAnchor.MiddleCenter, fontSize = 9, fixedHeight = 16 };
				_badgeType.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.8f, 1f) : new Color(0.1f, 0.3f, 0.8f);
			}
			if (_badgeEditor == null)
			{
				_badgeEditor = new GUIStyle("CN CountBadge") { alignment = TextAnchor.MiddleCenter, fontSize = 9, fixedHeight = 16 };
			}
			if (_badgeRuntime == null)
			{
				_badgeRuntime = new GUIStyle("CN CountBadge") { alignment = TextAnchor.MiddleCenter, fontSize = 9, fixedHeight = 16 };
				_badgeRuntime.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1f, 0.8f, 0.4f) : new Color(0.8f, 0.5f, 0.1f);
			}

			if (_toolbarSearchField == null)
			{
				// Try to find the internal one (most rounded)
				_toolbarSearchField = GUI.skin.FindStyle("ToolbarSeachTextField");
				if (_toolbarSearchField == null) _toolbarSearchField = GUI.skin.FindStyle("ToolbarSearchTextField");
				if (_toolbarSearchField == null) _toolbarSearchField = EditorStyles.toolbarTextField;
			}

			if (_toolbarCancelButton == null)
			{
				_toolbarCancelButton = GUI.skin.FindStyle("ToolbarSeachCancelButton");
				if (_toolbarCancelButton == null) _toolbarCancelButton = GUI.skin.FindStyle("ToolbarSearchCancelButton");
				if (_toolbarCancelButton == null) _toolbarCancelButton = GUIStyle.none;
			}
		}

		private void DrawEditorHeader()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField($"Database Contains {_db.SOQueryEntities.Count} Entities", EditorStyles.boldLabel);
			long totalSize = CalculateTotalSize(_db.SOQueryEntities);
			EditorGUILayout.LabelField($"Total Memory: {FormatBytes(totalSize)}", EditorStyles.miniLabel);
			EditorGUILayout.EndVertical();
		}

		// --- Shared Logic ---

		public static HashSet<string> GetSOQueryEntityTags(SOQueryDatabase db)
		{
			HashSet<string> returnValue = new HashSet<string>();
			foreach (var obj in db.SOQueryEntities)
			{
				if (obj == null) continue;
				foreach (var tag in SOQuery.GetSearchableTags(obj)) returnValue.Add(tag);
			}
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
			var containerTypes = System.AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(a => a.GetTypes())
				.Where(t => t.IsDefined(typeof(SOQueryTagsContainerAttribute), false));

			foreach (var type in containerTypes)
			{
				if (type.IsEnum) foreach (string n in System.Enum.GetNames(type)) yield return n;
				else
				{
					var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
						.Where(fi => fi.IsLiteral && !fi.IsInitOnly && fi.FieldType == typeof(string));
					foreach (var f in fields) { string v = (string)f.GetRawConstantValue(); if (!string.IsNullOrEmpty(v)) yield return v; }
				}
			}
		}

		private void AddTag(string newTag)
		{
			var tags = SOQuery.ParseTags(_searchString);
			string trimmed = newTag.Trim();
			if (!tags.Contains(trimmed))
			{
				if (tags.Count > 0) _searchString += ", ";
				_searchString += trimmed;
				_currentPage = 0;
				Repaint();
			}
		}

		private void RemoveTag(string tagToRemove)
		{
			var tags = SOQuery.ParseTags(_searchString);
			tags.Remove(tagToRemove);
			_searchString = string.Join(", ", tags);
			_currentPage = 0;
			Repaint();
		}

		private List<ScriptableObject> FilterList(List<string> searchTerms)
		{
			if (searchTerms == null || searchTerms.Count == 0) return _db.SOQueryEntities.Cast<ScriptableObject>().ToList();
			var filtered = new List<ScriptableObject>();
			foreach (var obj in _db.SOQueryEntities)
			{
				if (obj == null) continue;
				var entityTags = new HashSet<string>(SOQuery.GetSearchableTags(obj));
				if (searchTerms.All(term => entityTags.Contains(term))) filtered.Add(obj);
			}
			return filtered;
		}

		private long CalculateTotalSize(IEnumerable<ScriptableObject> objs)
		{
			long total = 0;
			foreach (var obj in objs) if (obj != null) total += Profiler.GetRuntimeMemorySizeLong(obj);
			return total;
		}

		private string FormatBytes(long bytes)
		{
			if (bytes < 1024) return $"{bytes} B";
			if (bytes < 1024 * 1024) return $"{(bytes / 1024f):F2} KB";
			return $"{(bytes / (1024f * 1024f)):F2} MB";
		}
	}
}