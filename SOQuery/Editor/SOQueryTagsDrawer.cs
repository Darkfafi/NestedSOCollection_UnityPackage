using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using System.Collections.Generic;
using System.Reflection;

namespace NestedSO.SOEditor
{
	[CustomPropertyDrawer(typeof(SOQueryTags))]
	public class SOQueryTagsDrawer : PropertyDrawer
	{
		private GUIStyle _tagPillStyle;
		private GUIStyle _typeTagStyle;
		private GUIStyle _runtimeTagStyle;
		private SOQueryDatabase _cachedDb;

		private SOQueryDatabase GetDatabase()
		{
			if (_cachedDb != null) return _cachedDb;
			string[] guids = AssetDatabase.FindAssets($"t:{nameof(SOQueryDatabase)}");
			if (guids.Length > 0)
				_cachedDb = AssetDatabase.LoadAssetAtPath<SOQueryDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
			return _cachedDb;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			SerializedProperty listProp = property.FindPropertyRelative("_tags");
			if (listProp == null)
			{
				EditorGUI.LabelField(position, "Error: _tags list not found in SOQueryTags");
				return;
			}

			InitializeStyles();
			EditorGUI.BeginProperty(position, label, property);

			// Draw Label
			Rect labelRect = new Rect(position.x, position.y, position.width, 18);
			EditorGUI.LabelField(labelRect, label);

			// Setup Layout
			float currentX = position.x;
			float currentY = position.y + 20; // Start below label
			float containerWidth = position.width;
			float rowHeight = 22;

			// --- Draw Editable Editor Tags ---
			for (int i = 0; i < listProp.arraySize; i++)
			{
				SerializedProperty element = listProp.GetArrayElementAtIndex(i);
				string tagValue = element.stringValue;

				float tagWidth = _tagPillStyle.CalcSize(new GUIContent($"{tagValue}  ×")).x;

				if (currentX + tagWidth > position.x + containerWidth)
				{
					currentX = position.x;
					currentY += rowHeight;
				}

				Rect tagRect = new Rect(currentX, currentY, tagWidth, 20);

				if (GUI.Button(tagRect, $"{tagValue}  ×", _tagPillStyle))
				{
					listProp.DeleteArrayElementAtIndex(i);
					property.serializedObject.ApplyModifiedProperties();
					break;
				}

				currentX += tagWidth + 4;
			}

			// --- Draw Add Button (+) ---
			float btnWidth = 24;
			if (currentX + btnWidth > position.x + containerWidth)
			{
				currentX = position.x;
				currentY += rowHeight;
			}

			Rect btnRect = new Rect(currentX, currentY, btnWidth, 20);
			if (GUI.Button(btnRect, "+", EditorStyles.miniButton))
			{
				ShowAddDropdown(listProp);
			}

			// --- Draw Read-Only Tags (Type & Runtime) ---
			var autoTags = GetAutoTags(property);
			if (autoTags.Count > 0)
			{
				currentX = position.x;
				currentY += rowHeight + 4;

				Rect iconRect = new Rect(currentX, currentY + 3, 16, 16);
				GUI.Label(iconRect, EditorGUIUtility.IconContent("d_FilterByLabel"), EditorStyles.miniLabel);
				currentX += 20;

				foreach (var (tag, style) in autoTags)
				{
					float tagW = style.CalcSize(new GUIContent(tag)).x;

					if (currentX + tagW > position.x + containerWidth)
					{
						currentX = position.x;
						currentY += rowHeight;
					}

					Rect r = new Rect(currentX, currentY, tagW, 18);

					// Draw Read-Only Label/Button
					if (Event.current.type == EventType.Repaint)
					{
						style.Draw(r, new GUIContent(tag), false, false, false, false);
					}

					currentX += tagW + 4;
				}
			}

			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (_tagPillStyle == null) InitializeStyles();

			SerializedProperty listProp = property.FindPropertyRelative("_tags");
			if (listProp == null) return 20;

			float width = EditorGUIUtility.currentViewWidth - 40;
			float height = 24;
			float currentX = 0;

			// Measure Editor Tags
			for (int i = 0; i < listProp.arraySize; i++)
			{
				string val = listProp.GetArrayElementAtIndex(i).stringValue;
				float tagW = _tagPillStyle.CalcSize(new GUIContent($"{val}  ×")).x;

				if (currentX + tagW > width)
				{
					currentX = 0;
					height += 22;
				}
				currentX += tagW + 4;
			}
			if (currentX + 24 > width) height += 22; // Space for (+) button

			// Measure Auto Tags (Type + Runtime)
			var autoTags = GetAutoTags(property);
			if (autoTags.Count > 0)
			{
				height += 26; // Spacing + New Line
				currentX = 20; // Indent for icon

				foreach (var (tag, style) in autoTags)
				{
					float tagW = style.CalcSize(new GUIContent(tag)).x;
					if (currentX + tagW > width)
					{
						currentX = 0;
						height += 22;
					}
					currentX += tagW + 4;
				}
			}

			return height + 10;
		}

		// --- Helpers ---

		// Returns list of (TagName, StyleToUse)
		private List<(string tag, GUIStyle style)> GetAutoTags(SerializedProperty property)
		{
			var results = new List<(string, GUIStyle)>();
			var targetObj = property.serializedObject.targetObject;

			if (targetObj == null) return results;

			// Type Tags (Blue)
			System.Type t = targetObj.GetType();
			while (t != null && t != typeof(ScriptableObject))
			{
				// Check Exclusion Attribute (Requires SOQuery reference)
				if (!SOQueryDatabase.IsTypeExcluded(t))
				{
					results.Add((t.Name, _typeTagStyle));
				}
				t = t.BaseType;
			}

			// Runtime Tags (Orange)
			// We need Reflection to get the private _runtimeTags hashset from the SOQueryTags instance
			try
			{
				// Get the SOQueryTags instance from the field
				object tagsInstance = fieldInfo.GetValue(targetObj);
				if (tagsInstance != null)
				{
					// Access private field '_runtimeTags'
					var runtimeField = tagsInstance.GetType().GetField("_runtimeTags", BindingFlags.NonPublic | BindingFlags.Instance);
					if (runtimeField != null)
					{
						var runtimeSet = runtimeField.GetValue(tagsInstance) as HashSet<string>;
						if (runtimeSet != null)
						{
							foreach (var tag in runtimeSet)
							{
								results.Add((tag, _runtimeTagStyle));
							}
						}
					}
				}
			}
			catch
			{
				// Reflection might fail on intricate nesting, fail silently for drawer
			}

			return results;
		}

		private void ShowAddDropdown(SerializedProperty listProp)
		{
			var db = GetDatabase();
			if (db == null) return;

			HashSet<string> allTags = SOQueryDatabaseEditor.GetSOQueryEntityTags(db);

			// Filter out applied tags
			for (int i = 0; i < listProp.arraySize; i++)
			{
				allTags.Remove(listProp.GetArrayElementAtIndex(i).stringValue);
			}

			var dropdown = new TagSearchDropdown(new AdvancedDropdownState(), allTags, (selected) =>
			{
				listProp.serializedObject.Update();
				int index = listProp.arraySize;
				listProp.InsertArrayElementAtIndex(index);
				listProp.GetArrayElementAtIndex(index).stringValue = selected;
				listProp.serializedObject.ApplyModifiedProperties();
			});

			dropdown.Show(new Rect(Event.current.mousePosition, Vector2.zero));
		}

		private void InitializeStyles()
		{
			if (_tagPillStyle == null)
			{
				_tagPillStyle = new GUIStyle(EditorStyles.helpBox);
				_tagPillStyle.fontSize = 11;
				_tagPillStyle.alignment = TextAnchor.MiddleLeft;
				_tagPillStyle.padding = new RectOffset(6, 6, 3, 3);
				_tagPillStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.9f) : Color.black;
			}

			// Blue-ish for Types
			if (_typeTagStyle == null)
			{
				_typeTagStyle = new GUIStyle(EditorStyles.miniButton);
				_typeTagStyle.fontSize = 10;
				_typeTagStyle.fontStyle = FontStyle.Bold;
				_typeTagStyle.fixedHeight = 18;
				_typeTagStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.4f, 0.7f, 1f) : new Color(0.1f, 0.3f, 0.8f);
			}

			// Orange-ish for Runtime
			if (_runtimeTagStyle == null)
			{
				_runtimeTagStyle = new GUIStyle(EditorStyles.miniButton);
				_runtimeTagStyle.fontSize = 10;
				_runtimeTagStyle.fixedHeight = 18;
				_runtimeTagStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1f, 0.8f, 0.4f) : new Color(0.8f, 0.5f, 0.1f);
			}
		}
	}
}