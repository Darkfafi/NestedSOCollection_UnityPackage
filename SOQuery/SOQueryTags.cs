using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace NestedSO
{
	public interface IReadOnlySOQueryTags : IReadOnlyList<string>
	{
		public IReadOnlyList<string> Tags
		{
			get;
		}
	}

	[System.Serializable]
	public class SOQueryTags : IEnumerable<string>, IList<string>, IReadOnlySOQueryTags
	{
		[SerializeField]
		private List<string> _tags = new List<string>();

		public IReadOnlyList<string> Tags => _tags;

		public void Add(string tag) => _tags.Add(tag);
		public bool Remove(string tag) => _tags.Remove(tag);
		public bool Contains(string tag) => _tags.Contains(tag);
		public int Count => _tags.Count;

		public bool IsReadOnly => ((IList<string>)_tags).IsReadOnly;

		public string this[int index]
		{
			get => _tags[index];
			set => _tags[index] = value;
		}

		public void AddRange(IEnumerable<string> collection) => _tags.AddRange(collection);
		public int IndexOf(string item) => _tags.IndexOf(item);
		public void Insert(int index, string item) => _tags.Insert(index, item);
		public void RemoveAt(int index) => _tags.RemoveAt(index);
		public void Clear() => _tags.Clear();
		public void CopyTo(string[] array, int arrayIndex) => _tags.CopyTo(array, arrayIndex);

		public IEnumerator<string> GetEnumerator() => _tags.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => _tags.GetEnumerator();
	}

	[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Enum)]
	public class SOQueryTagsContainerAttribute : System.Attribute { }
}