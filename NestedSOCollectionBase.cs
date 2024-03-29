﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NestedSO
{
	public abstract class NestedSOCollectionBase<NestedSOItemT> : NestedSOCollectionBase, IReadOnlyList<NestedSOItemT>
		where NestedSOItemT : ScriptableObject
	{
		#region Editor Variables

		[SerializeField, HideInInspector]
		private List<NestedSOItemT> _nestedSOItems = new List<NestedSOItemT>();

		#endregion

		#region Properties

		public int Count => _nestedSOItems.Count;

		#endregion

		#region Internal Methods

		internal override void _AddAsset(ScriptableObject item)
		{
			if(item is NestedSOItemT castedItem)
			{
				_nestedSOItems.Add(castedItem);
			}
		}

		internal override void _RemoveAsset(ScriptableObject item)
		{
			if(item is NestedSOItemT castedItem)
			{
				_nestedSOItems.Remove(castedItem);
			}
		}

		internal override bool _HasAsset(ScriptableObject item)
		{
			if(item is NestedSOItemT castedItem)
			{
				return _nestedSOItems.Contains(castedItem);
			}
			return false;
		}

		internal override void _MarkAsAddedAsset(ScriptableObject item)
		{
			if(item is NestedSOItemT castedAsset)
			{
				OnAddedAsset(castedAsset);
			}
		}

		internal override void _MarkAsRemovedAsset(ScriptableObject item)
		{
			if(item is NestedSOItemT castedAsset)
			{
				OnRemovedAsset(castedAsset);
			}
		}

		#endregion

		#region Public Methods

		public void ForEach(Action<NestedSOItemT> action)
		{
			for(int i = 0, c = _nestedSOItems.Count; i < c; i++)
			{
				action(_nestedSOItems[i]);
			}
		}

		public void ForEachReversed(Action<NestedSOItemT> action)
		{
			for(int i = _nestedSOItems.Count - 1; i >= 0; i--)
			{
				action(_nestedSOItems[i]);
			}
		}

		public bool TryGetItem(Predicate<NestedSOItemT> predicate, out NestedSOItemT item)
		{
			for(int i = 0, c = _nestedSOItems.Count; i < c; i++)
			{
				item = _nestedSOItems[i];
				if(predicate(item))
				{
					return true;
				}
			}
			item = null;
			return false;
		}

		public bool TryGetItem<T>(out T item) where T : NestedSOItemT
		{
			for(int i = 0, c = _nestedSOItems.Count; i < c; i++)
			{
				NestedSOItemT baseItem = _nestedSOItems[i];
				if(baseItem is T castedItem)
				{
					item = castedItem;
					return true;
				}
			}

			item = null;
			return false;
		}

		public bool TryGetItem<T>(Predicate<T> predicate, out T item) where T : NestedSOItemT
		{
			for(int i = 0, c = _nestedSOItems.Count; i < c; i++)
			{
				NestedSOItemT baseItem = _nestedSOItems[i];
				if(baseItem is T castedItem && predicate(castedItem))
				{
					item = castedItem;
					return true;
				}
			}

			item = null;
			return false;
		}

		public List<T> GetItems<T>() where T : NestedSOItemT
		{
			List<T> castedItems = new List<T>();
			for(int i = 0, c = _nestedSOItems.Count; i < c; i++)
			{
				var item = _nestedSOItems[i];
				if(item is T castedItem)
				{
					castedItems.Add(castedItem);
				}
			}
			return castedItems;
		}

		public List<T> GetItems<T>(Predicate<T> predicate) where T : NestedSOItemT
		{
			List<T> castedItems = new List<T>();
			for(int i = 0, c = _nestedSOItems.Count; i < c; i++)
			{
				var item = _nestedSOItems[i];
				if(item is T castedItem && predicate(castedItem))
				{
					castedItems.Add(castedItem);
				}
			}
			return castedItems;
		}

		public override IReadOnlyList<ScriptableObject> GetRawItems() => _nestedSOItems;
		public IReadOnlyList<NestedSOItemT> GetItems() => _nestedSOItems;
		public NestedSOItemT this[int index] => _nestedSOItems[index];
		public IEnumerator<NestedSOItemT> GetEnumerator() => _nestedSOItems.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => _nestedSOItems.GetEnumerator();

		#endregion

		#region Protected Methods

		protected virtual void OnAddedAsset(NestedSOItemT asset)
		{
		
		}

		protected virtual void OnRemovedAsset(NestedSOItemT asset)
		{

		}

		#endregion
	}

	public abstract class NestedSOCollectionBase : ScriptableObject
	{
		public abstract IReadOnlyList<ScriptableObject> GetRawItems();
		internal abstract void _MarkAsAddedAsset(ScriptableObject item);
		internal abstract void _AddAsset(ScriptableObject item);

		internal abstract void _MarkAsRemovedAsset(ScriptableObject item);
		internal abstract void _RemoveAsset(ScriptableObject item);
		internal abstract bool _HasAsset(ScriptableObject item);
	}
}
