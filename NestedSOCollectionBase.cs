using System.Collections.Generic;
using UnityEngine;
namespace NestedSO
{
	public abstract class NestedSOCollectionBase<NestedSOItemT> : ScriptableObject, INestedSOCollection
	where NestedSOItemT : ScriptableObject
	{
		#region Editor Variables

		[SerializeField, HideInInspector]
		private List<NestedSOItemT> _nestedSOItems = new List<NestedSOItemT>();

		#endregion

		#region Public Methods

		public void AddAsset(ScriptableObject item)
		{
			if (item is NestedSOItemT castedItem)
			{
				_nestedSOItems.Add(castedItem);
			}
		}

		public void RemoveAsset(ScriptableObject item)
		{
			if (item is NestedSOItemT castedItem)
			{
				_nestedSOItems.Remove(castedItem);
			}
		}

		public IReadOnlyList<ScriptableObject> GetRawItems() => _nestedSOItems;
		public IReadOnlyList<NestedSOItemT> GetItems() => _nestedSOItems;

		#endregion
	}
}