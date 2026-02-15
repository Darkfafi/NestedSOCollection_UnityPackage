using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NestedSO
{
	[System.Serializable]
	public abstract class NestedSOListBase : IList
	{
		public abstract object this[int index] { get; set; }

		public abstract bool IsFixedSize { get; }
		public abstract bool IsReadOnly { get; }
		public abstract int Count { get; }
		public abstract bool IsSynchronized { get; }
		public abstract object SyncRoot { get; }

		public abstract int Add(object value);
		public abstract void Clear();
		public abstract bool Contains(object value);
		public abstract void CopyTo(Array array, int index);
		public abstract IEnumerator GetEnumerator();
		public abstract int IndexOf(object value);
		public abstract void Insert(int index, object value);
		public abstract void Remove(object value);
		public abstract void RemoveAt(int index);
	}

	[System.Serializable]
	public class NestedSOList<T> : NestedSOListBase, IReadOnlyList<T>, IList<T> where T : ScriptableObject
	{
		public List<T> Items = new List<T>();
		private IList NonGenericItems => Items;

		public override object this[int index]
		{
			get => NonGenericItems[index];
			set => NonGenericItems[index] = value;
		}

		T IList<T>.this[int index]
		{
			get => Items[index];
			set => Items[index] = value;
		}

		public override bool IsFixedSize => NonGenericItems.IsFixedSize;

		public override bool IsReadOnly => NonGenericItems.IsReadOnly;

		public override int Count => Items.Count;

		public override bool IsSynchronized => NonGenericItems.IsSynchronized;

		public override object SyncRoot => NonGenericItems.SyncRoot;

		T IReadOnlyList<T>.this[int index] => Items[index];

		public void Add(T item)
		{
			Items.Add(item);
		}

		public override int Add(object value)
		{
			return NonGenericItems.Add(value);
		}

		public override void Clear()
		{
			Items.Clear();
		}

		public bool Contains(T item)
		{
			return Items.Contains(item);
		}

		public override bool Contains(object value)
		{
			return NonGenericItems.Contains(value);
		}

		public void CopyTo(T[] array, int arrayIndex)
		{
			Items.CopyTo(array, arrayIndex);
		}

		public override void CopyTo(Array array, int index)
		{
			NonGenericItems.CopyTo(array, index);
		}

		public override IEnumerator GetEnumerator()
		{
			return Items.GetEnumerator();
		}

		public int IndexOf(T item)
		{
			return Items.IndexOf(item);
		}

		public override int IndexOf(object value)
		{
			return NonGenericItems.IndexOf(value);
		}

		public void Insert(int index, T item)
		{
			Items.Insert(index, item);
		}

		public override void Insert(int index, object value)
		{
			NonGenericItems.Insert(index, value);
		}

		public bool Remove(T item)
		{
			return Items.Remove(item);
		}

		public override void Remove(object value)
		{
			NonGenericItems.Remove(value);
		}

		public override void RemoveAt(int index)
		{
			Items.RemoveAt(index);
		}

		IEnumerator<T> IEnumerable<T>.GetEnumerator()
		{
			return Items.GetEnumerator();
		}
	}
}