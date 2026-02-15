using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using System.Collections.Generic;

namespace NestedSO.SOEditor
{
	[CustomPropertyDrawer(typeof(SOQueryTags))]
	public class SOQueryTagsDrawer : PropertyDrawer
	{
		private GUIStyle _tagPillStyle;
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
			// Find the inner list: "_tags"
			SerializedProperty listProp = property.FindPropertyRelative("_tags");
			if (listProp == null)
			{
				EditorGUI.LabelField(position, "Error: _tags list not found in SOQueryTags");
				return;
			}

			InitializeStyles();
			EditorGUI.BeginProperty(position, label, property);

			// 1. Draw Label
			Rect labelRect = new Rect(position.x, position.y, position.width, 18);
			EditorGUI.LabelField(labelRect, label);

			// 2. Setup Layout
			float currentX = position.x;
			float currentY = position.y + 20; // Start below label
			float containerWidth = position.width;
			float rowHeight = 22;

			// 3. Draw Existing Tags
			for (int i = 0; i < listProp.arraySize; i++)
			{
				SerializedProperty element = listProp.GetArrayElementAtIndex(i);
				string tagValue = element.stringValue;

				// Calc width: Text + " x" padding
				float tagWidth = _tagPillStyle.CalcSize(new GUIContent($"{tagValue}  ×")).x;

				// Check for line break
				if (currentX + tagWidth > position.x + containerWidth)
				{
					currentX = position.x;
					currentY += rowHeight;
				}

				Rect tagRect = new Rect(currentX, currentY, tagWidth, 20);

				// Draw Clickable Pill
				if (GUI.Button(tagRect, $"{tagValue}  ×", _tagPillStyle))
				{
					listProp.DeleteArrayElementAtIndex(i);
					property.serializedObject.ApplyModifiedProperties(); // Save immediately
					break;
				}

				currentX += tagWidth + 4; // Spacing
			}

			// 4. Draw Add Button (+)
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

			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (_tagPillStyle == null) InitializeStyles();

			SerializedProperty listProp = property.FindPropertyRelative("_tags");
			if (listProp == null) return 20;

			float width = EditorGUIUtility.currentViewWidth - 40; // Approx inspector width
			float height = 24; // Initial label height
			float currentX = 0;

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

			if (currentX + 24 > width) height += 22;

			return height + 10; // Bottom padding
		}

		private void ShowAddDropdown(SerializedProperty listProp)
		{
			var db = GetDatabase();
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
		}
	}
}