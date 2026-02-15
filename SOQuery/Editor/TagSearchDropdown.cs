using UnityEngine;
using UnityEditor.IMGUI.Controls;
using System.Collections.Generic;
using System.Linq;
using System;

namespace NestedSO.SOEditor
{
	public class TagSearchDropdown : AdvancedDropdown
	{
		private SOQueryDatabase _db;
		private Action<string> _onTagSelected;

		public TagSearchDropdown(AdvancedDropdownState state, SOQueryDatabase db, Action<string> onTagSelected) : base(state)
		{
			_db = db;
			_onTagSelected = onTagSelected;
			// Set the size of the popup window
			minimumSize = new Vector2(250, 300);
		}

		protected override AdvancedDropdownItem BuildRoot()
		{
			var root = new AdvancedDropdownItem("Tags");

			// 1. Collect all unique tags and types
			HashSet<string> allItems = new HashSet<string>();

			foreach (var obj in _db.SOQueryEntities)
			{
				if (obj is not ISOQueryEntity entity) continue;

				// Add Manual Tags
				foreach (var t in entity.Tags) allItems.Add(t);

				// Add Type Names (and base types)
				Type tType = obj.GetType();
				while (tType != null && tType != typeof(ScriptableObject))
				{
					allItems.Add(tType.Name);
					tType = tType.BaseType;
				}
			}

			// 2. Sort and Add to Dropdown
			foreach (var item in allItems.OrderBy(x => x))
			{
				root.AddChild(new AdvancedDropdownItem(item));
			}

			return root;
		}

		protected override void ItemSelected(AdvancedDropdownItem item)
		{
			_onTagSelected?.Invoke(item.name);
		}
	}
}