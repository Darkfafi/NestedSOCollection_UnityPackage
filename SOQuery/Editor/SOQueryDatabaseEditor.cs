using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine.Profiling;
using System.Collections.Generic;
using System.Linq;
using System;
using NestedSO;

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

		// Pagination
		private int _currentPage = 0;
		private const int ITEMS_PER_PAGE = 20;

		// Cached Styles
		private GUIStyle _tagPillStyle;
		private GUIStyle _resultTagButtonStyle;
		private GUIStyle _typeButtonStyle;

		private void OnEnable()
		{
			_db = (SOQueryDatabase)target;
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			InitializeStyles();

			EditorGUILayout.Space(10);
			DrawSOHeader();

			EditorGUILayout.Space(5);
			DrawSearchArea();

			EditorGUILayout.Space(10);

			EditorGUILayout.LabelField("Raw Data", EditorStyles.boldLabel);
			SerializedProperty listProp = serializedObject.FindProperty("SOQueryEntities");
			EditorGUILayout.PropertyField(listProp, true);

			serializedObject.ApplyModifiedProperties();
		}

		private void InitializeStyles()
		{
			if (_tagPillStyle == null)
			{
				_tagPillStyle = new GUIStyle("HelpBox");
				_tagPillStyle.fontSize = 11;
				_tagPillStyle.alignment = TextAnchor.MiddleLeft;
				_tagPillStyle.padding = new RectOffset(6, 20, 3, 3);
				_tagPillStyle.margin = new RectOffset(0, 5, 0, 0);
			}

			if (_resultTagButtonStyle == null)
			{
				// A clickable button that looks like a tag label
				_resultTagButtonStyle = new GUIStyle("minibutton");
				_resultTagButtonStyle.fontSize = 10;
				_resultTagButtonStyle.alignment = TextAnchor.MiddleCenter;
				_resultTagButtonStyle.margin = new RectOffset(2, 2, 2, 2);
				_resultTagButtonStyle.fixedHeight = 18;
			}

			if (_typeButtonStyle == null)
			{
				_typeButtonStyle = new GUIStyle("minibutton");
				_typeButtonStyle.fontSize = 11;
				_typeButtonStyle.fontStyle = FontStyle.Bold;
				_typeButtonStyle.fixedHeight = 18;
				_typeButtonStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.4f, 0.7f, 1f) : new Color(0.1f, 0.3f, 0.8f);
			}
		}

		private void DrawSOHeader()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField($"Database Contains {_db.SOQueryEntities.Count} Entities", EditorStyles.boldLabel);
			long totalSize = CalculateTotalSize(_db.SOQueryEntities);
			EditorGUILayout.LabelField($"Total Memory: {FormatBytes(totalSize)}", EditorStyles.miniLabel);
			EditorGUILayout.EndVertical();
		}

		private void DrawSearchArea()
		{
			EditorGUILayout.LabelField("Query Playground", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical("box");

			// --- 1. Active Filters ---
			EditorGUILayout.BeginHorizontal();

			var currentTags = _searchString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
										   .Select(s => s.Trim()).ToList();

			foreach (var tag in currentTags.ToList())
			{
				if (GUILayout.Button($"{tag}  ×", _tagPillStyle, GUILayout.Height(20)))
				{
					RemoveTag(tag);
				}
			}

			GUILayout.FlexibleSpace();

			Color originalColor = GUI.backgroundColor;
			GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
			if (GUILayout.Button("+ Add Filter", GUILayout.Width(100), GUILayout.Height(20)))
			{
				var dropdown = new TagSearchDropdown(new AdvancedDropdownState(), _db, (selectedTag) =>
				{
					AddTag(selectedTag);
				});
				dropdown.Show(new Rect(Event.current.mousePosition, Vector2.zero));
			}
			GUI.backgroundColor = originalColor;

			EditorGUILayout.EndHorizontal();

			// --- 2. Filter Logic ---
			var allResults = FilterList(currentTags);
			int totalCount = allResults.Count;

			// --- 3. Pagination Logic ---
			int totalPages = Mathf.CeilToInt((float)totalCount / ITEMS_PER_PAGE);
			// Clamp current page to valid range (in case filter reduced count)
			if (_currentPage >= totalPages) _currentPage = Mathf.Max(0, totalPages - 1);

			// Get slice for current page
			var pageResults = allResults.Skip(_currentPage * ITEMS_PER_PAGE).Take(ITEMS_PER_PAGE).ToList();

			// --- 4. Stats & Pagination Controls ---
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

			// --- 5. Result List ---
			_showConfigs = EditorGUILayout.Foldout(_showConfigs, "Results Preview", true);
			if (_showConfigs)
			{
				float scrollHeight = Mathf.Min(400, pageResults.Count * 26 + 10);
				_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(scrollHeight));

				foreach (var item in pageResults)
				{
					DrawEntityLine(item);
				}

				if (pageResults.Count == 0)
				{
					EditorGUILayout.HelpBox("No items match your filter.", MessageType.Info);
				}

				EditorGUILayout.EndScrollView();
			}

			EditorGUILayout.EndVertical();
		}

		private void DrawEntityLine(ScriptableObject obj)
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

			// Object Field
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ObjectField(obj, typeof(ScriptableObject), false, GUILayout.Width(180));
			}

			if (obj is ISOQueryEntity entity)
			{
				// Memory Size
				long size = Profiler.GetRuntimeMemorySizeLong(obj);
				EditorGUILayout.LabelField(FormatBytes(size), EditorStyles.miniLabel, GUILayout.Width(45));

				// --- Clickable Type Pill ---
				string typeName = obj.GetType().Name;
				if (GUILayout.Button(typeName, _typeButtonStyle))
				{
					AddTag(typeName);
				}

				// --- Clickable Tag Pills ---
				foreach (var tag in entity.Tags.Take(6)) // Show max 6 tags
				{
					if (GUILayout.Button(tag, _resultTagButtonStyle))
					{
						AddTag(tag);
					}
				}

				if (entity.Tags.Count > 6)
				{
					GUILayout.Label($"+{entity.Tags.Count - 6}", EditorStyles.miniLabel);
				}
			}

			GUILayout.FlexibleSpace();
			EditorGUILayout.EndHorizontal();
		}

		// --- Logic Methods ---

		private void AddTag(string newTag)
		{
			var tags = _searchString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
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
			var tags = _searchString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
									.Select(t => t.Trim()).ToList();
			tags.Remove(tagToRemove);
			_searchString = string.Join(", ", tags);
			_currentPage = 0;
			Repaint();
		}

		private List<ScriptableObject> FilterList(List<string> searchTerms)
		{
			if (searchTerms == null || searchTerms.Count == 0)
				return _db.SOQueryEntities.Cast<ScriptableObject>().ToList();

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
					while (t != null && t != typeof(ScriptableObject))
					{
						if (t.Name.Equals(term, StringComparison.OrdinalIgnoreCase))
						{
							isType = true;
							break;
						}
						t = t.BaseType;
					}

					if (!hasTag && !isType)
					{
						match = false;
						break;
					}
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