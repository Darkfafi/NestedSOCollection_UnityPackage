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

		// Search & Pagination State
		private string _searchString = "";
		private Vector2 _scrollPos;
		private bool _showConfigs = true;

		// Tags Explorer State
		private bool _showTagExplorer = false;
		private string _explorerSearchString = "";
		private Vector2 _explorerScrollPos;
		private Dictionary<string, int> _cachedTagCounts;
		private List<KeyValuePair<string, int>> _sortedTags;

		// Expansion State
		private HashSet<int> _expandedItems = new HashSet<int>();

		// Pagination
		private int _currentPage = 0;
		private const int ITEMS_PER_PAGE = 20;

		// Cached Styles
		private GUIStyle _tagPillStyle;
		private GUIStyle _resultTagButtonStyle;
		private GUIStyle _typeButtonStyle;
		private GUIStyle _explorerCountStyle;
		private GUIStyle _expandedBoxStyle;

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

		// --- TAGS EXPLORER LOGIC ---

		private void AnalyzeTags()
		{
			_cachedTagCounts = new Dictionary<string, int>();
			if (_db.SOQueryEntities == null) return;

			foreach (var obj in _db.SOQueryEntities)
			{
				if (obj is not ISOQueryEntity entity) continue;

				// 1. Manual Tags
				foreach (var t in entity.Tags) IncrementTagCount(t);

				Type tType = obj.GetType();
				while (tType != null && tType != typeof(ScriptableObject))
				{
					if (!SOQuery.IsTypeExcluded(tType))
					{
						IncrementTagCount(tType.Name);
					}
					tType = tType.BaseType;
				}
			}
			_sortedTags = _cachedTagCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToList();
		}

		private void IncrementTagCount(string tag)
		{
			if (!_cachedTagCounts.ContainsKey(tag)) _cachedTagCounts[tag] = 0;
			_cachedTagCounts[tag]++;
		}

		private void DrawTagsExplorer()
		{
			_showTagExplorer = EditorGUILayout.Foldout(_showTagExplorer, "Tags Explorer", true);
			if (!_showTagExplorer) return;

			EditorGUILayout.BeginVertical("box");

			EditorGUILayout.BeginHorizontal();
			_explorerSearchString = EditorGUILayout.TextField(_explorerSearchString);
			if (GUILayout.Button("x", GUILayout.Width(20))) _explorerSearchString = "";
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(2);

			// Filter the view based on search string
			var visibleTags = _sortedTags;
			if (!string.IsNullOrEmpty(_explorerSearchString))
			{
				visibleTags = _sortedTags
					.Where(x => x.Key.IndexOf(_explorerSearchString, StringComparison.OrdinalIgnoreCase) >= 0)
					.ToList();
			}

			// Stats Header
			EditorGUILayout.LabelField($"Found {visibleTags?.Count ?? 0} matching tags", EditorStyles.miniLabel);

			if (visibleTags != null && visibleTags.Count > 0)
			{
				float height = Mathf.Min(250, visibleTags.Count * 22 + 10);
				_explorerScrollPos = EditorGUILayout.BeginScrollView(_explorerScrollPos, GUILayout.Height(height));

				foreach (var kvp in visibleTags)
				{
					DrawTagExplorerRow(kvp.Key, kvp.Value);
				}

				EditorGUILayout.EndScrollView();
			}
			else
			{
				EditorGUILayout.HelpBox("No tags found.", MessageType.Info);
			}

			if (GUILayout.Button("Refresh Stats", EditorStyles.miniButton)) AnalyzeTags();

			EditorGUILayout.EndVertical();
		}

		private void DrawTagExplorerRow(string tag, int count)
		{
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button(tag, _resultTagButtonStyle, GUILayout.Height(18))) AddTag(tag);
			GUILayout.FlexibleSpace();
			GUILayout.Label($"{count}", _explorerCountStyle, GUILayout.Width(40));
			EditorGUILayout.EndHorizontal();
		}

		private void DrawEditorHeader()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField($"Database Contains {_db.SOQueryEntities.Count} Entities", EditorStyles.boldLabel);
			long totalSize = CalculateTotalSize(_db.SOQueryEntities);
			EditorGUILayout.LabelField($"Total Memory: {FormatBytes(totalSize)}", EditorStyles.miniLabel);
			EditorGUILayout.EndVertical();
		}

		// --- UPDATED SEARCH AREA ---
		private void DrawSearchArea()
		{
			EditorGUILayout.LabelField("Query Playground", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical("box");

			// 1. Active Filters
			EditorGUILayout.BeginHorizontal();
			var currentTags = _searchString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
										   .Select(s => s.Trim()).ToList();

			foreach (var tag in currentTags.ToList())
			{
				if (GUILayout.Button($"{tag}  ×", _tagPillStyle, GUILayout.Height(20))) RemoveTag(tag);
			}

			GUILayout.FlexibleSpace();

			Color originalColor = GUI.backgroundColor;
			GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
			if (GUILayout.Button("+ Add Filter", GUILayout.Width(100), GUILayout.Height(20))) ShowAddFilterDropdown();
			GUI.backgroundColor = originalColor;
			EditorGUILayout.EndHorizontal();

			// 2. Filter Logic
			var allResults = FilterList(currentTags);
			int totalCount = allResults.Count;

			// 3. Pagination
			int totalPages = Mathf.CeilToInt((float)totalCount / ITEMS_PER_PAGE);
			if (_currentPage >= totalPages) _currentPage = Mathf.Max(0, totalPages - 1);
			var pageResults = allResults.Skip(_currentPage * ITEMS_PER_PAGE).Take(ITEMS_PER_PAGE).ToList();

			// 4. Stats
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

			// 5. Result List (Dynamic Height)
			_showConfigs = EditorGUILayout.Foldout(_showConfigs, "Results Preview", true);
			if (_showConfigs)
			{
				_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(400));

				foreach (var item in pageResults)
				{
					DrawEntityLine(item);
				}

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

			// --- Top Row (Summary) ---
			EditorGUILayout.BeginHorizontal();

			// 1. Foldout Arrow
			bool newExpanded = EditorGUILayout.Foldout(isExpanded, GUIContent.none, true);
			if (newExpanded != isExpanded)
			{
				if (newExpanded) _expandedItems.Add(instanceId);
				else _expandedItems.Remove(instanceId);
			}

			// 2. Object Field
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ObjectField(obj, typeof(ScriptableObject), false, GUILayout.Width(180));
			}

			if (obj is ISOQueryEntity entity)
			{
				// 3. Size
				long size = Profiler.GetRuntimeMemorySizeLong(obj);
				EditorGUILayout.LabelField(FormatBytes(size), EditorStyles.miniLabel, GUILayout.Width(45));

				// 4. Preview Tags 
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

			// --- Expanded Detail View ---
			if (newExpanded && obj is ISOQueryEntity expandedEntity)
			{
				DrawExpandedDetails(obj, expandedEntity);
			}

			EditorGUILayout.EndVertical();
		}

		private void DrawExpandedDetails(ScriptableObject obj, ISOQueryEntity entity)
		{
			var allTags = new List<string>();

			Type t = obj.GetType();
			while (t != null && t != typeof(ScriptableObject))
			{
				if (!SOQuery.IsTypeExcluded(t))
				{
					allTags.Add(t.Name);
				}
				t = t.BaseType;
			}
			allTags.AddRange(entity.Tags);

			// Draw Container
			EditorGUILayout.BeginVertical(_expandedBoxStyle);

			EditorGUILayout.LabelField("All Searchable Tags:", EditorStyles.miniBoldLabel);

			float width = EditorGUIUtility.currentViewWidth - 60;
			float currentX = 0;

			EditorGUILayout.BeginHorizontal();
			foreach (var tag in allTags)
			{
				// Decide Style (Type vs Manual)
				bool isType = !entity.Tags.Contains(tag);
				GUIStyle style = isType ? _typeButtonStyle : _resultTagButtonStyle;

				float btnWidth = style.CalcSize(new GUIContent(tag)).x;

				// Wrap
				if (currentX + btnWidth > width)
				{
					currentX = 0;
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.BeginHorizontal();
				}

				if (GUILayout.Button(tag, style)) AddTag(tag);

				currentX += btnWidth + 4;
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.EndVertical();
		}

		// --- Helpers ---

		private void InitializeStyles()
		{
			if (_tagPillStyle == null)
			{
				_tagPillStyle = new GUIStyle("HelpBox") { fontSize = 11, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 20, 3, 3), margin = new RectOffset(0, 5, 0, 0) };
			}
			if (_resultTagButtonStyle == null)
			{
				_resultTagButtonStyle = new GUIStyle("minibutton") { fontSize = 10, alignment = TextAnchor.MiddleCenter, margin = new RectOffset(2, 2, 2, 2), fixedHeight = 18 };
			}
			if (_typeButtonStyle == null)
			{
				_typeButtonStyle = new GUIStyle("minibutton") { fontSize = 11, fontStyle = FontStyle.Bold, fixedHeight = 18, normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.4f, 0.7f, 1f) : new Color(0.1f, 0.3f, 0.8f) } };
			}
			if (_explorerCountStyle == null)
			{
				_explorerCountStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight, fontSize = 10, normal = { textColor = Color.gray } };
			}
			if (_expandedBoxStyle == null)
			{
				_expandedBoxStyle = new GUIStyle("CN Box") { padding = new RectOffset(10, 10, 5, 5), margin = new RectOffset(5, 5, 0, 5) };
			}
		}

		// --- Standard Logic (Filtering, Adding Tags, etc.) ---

		public static HashSet<string> GetSOQueryEntityTags(SOQueryDatabase db)
		{
			HashSet<string> returnValue = new HashSet<string>();
			foreach (var obj in db.SOQueryEntities)
			{
				if (obj is not ISOQueryEntity entity) continue;
				foreach (var t in entity.Tags) returnValue.Add(t);
				Type tType = obj.GetType();
				while (tType != null && tType != typeof(ScriptableObject))
				{
					if (!SOQuery.IsTypeExcluded(tType))
					{
						returnValue.Add(tType.Name);
					}
					tType = tType.BaseType;
				}
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
			var tags = _searchString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
			if (!tags.Contains(newTag.Trim()))
			{
				if (tags.Count > 0) _searchString += ", ";
				_searchString += newTag.Trim();
				_currentPage = 0;
				Repaint();
			}
		}

		private void RemoveTag(string tagToRemove)
		{
			var tags = _searchString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
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
				if (obj is not ISOQueryEntity entity) continue;
				bool match = true;
				foreach (var term in searchTerms)
				{
					bool hasTag = entity.Tags.Contains(term);
					bool isType = false;
					Type t = obj.GetType();
					while (t != null && t != typeof(ScriptableObject)) { if (t.Name.Equals(term, StringComparison.OrdinalIgnoreCase)) { isType = true; break; } t = t.BaseType; }
					if (!hasTag && !isType) { match = false; break; }
				}
				if (match) filtered.Add(obj);
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