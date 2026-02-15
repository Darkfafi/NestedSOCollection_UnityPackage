using UnityEditor.IMGUI.Controls;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

namespace NestedSO.SOEditor
{
	public class TagSearchDropdown : AdvancedDropdown
	{
		private Action<string> _onTagSelected;
		private HashSet<string> _allTags;

		public TagSearchDropdown(AdvancedDropdownState state, IEnumerable<string> allTags, Action<string> onTagSelected) : base(state)
		{
			_onTagSelected = onTagSelected;
			_allTags = new HashSet<string>(allTags);
			minimumSize = new Vector2(250, 300);
		}

		protected override AdvancedDropdownItem BuildRoot()
		{
			var root = new AdvancedDropdownItem("Tags");

			if (_allTags.Count == 0)
			{
				root.AddChild(new AdvancedDropdownItem("(No Tags Found in DB)"));
				return root;
			}

			foreach (var tag in _allTags.OrderBy(x => x))
			{
				root.AddChild(new AdvancedDropdownItem(tag));
			}

			return root;
		}

		protected override void ItemSelected(AdvancedDropdownItem item)
		{
			if (item.name != "(No Tags Found in DB)")
			{
				_onTagSelected?.Invoke(item.name);
			}
		}
	}
}