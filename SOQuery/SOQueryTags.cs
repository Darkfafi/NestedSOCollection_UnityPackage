using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace NestedSO
{
	public interface IReadOnlySOQueryTags : IReadOnlyCollection<string>
	{
	}

	[Serializable]
	public class SOQueryTags : IEnumerable<string>, IReadOnlySOQueryTags
	{
		[SerializeField]
		private List<string> _tags = new List<string>();

		[NonSerialized]
		private HashSet<string> _runtimeTags;

		private HashSet<string> RuntimeTags
		{
			get
			{
				if (_runtimeTags == null) _runtimeTags = new HashSet<string>();
				return _runtimeTags;
			}
		}

		public int Count => _tags.Count + RuntimeTags.Count;

		public IEnumerator<string> GetEnumerator()
		{
			// Yield Editor tags
			foreach (var t in _tags) yield return t;

			// Yield Runtime tags
			foreach (var t in RuntimeTags) yield return t;
		}

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		// --- Query Logic ---

		public bool Contains(string tag)
		{
			return _tags.Contains(tag) || RuntimeTags.Contains(tag);
		}

		// --- Serialized Tags ---

		public void Add(string tag)
		{
			if (!_tags.Contains(tag)) _tags.Add(tag);
		}

		public bool Remove(string tag) => _tags.Remove(tag);

		public void AddRange(IEnumerable<string> collection)
		{
			// Add to the persistent editor list
			foreach (var item in collection)
			{
				if (!_tags.Contains(item)) _tags.Add(item);
			}
		}

		public void Clear() => _tags.Clear();

		// --- Runtime Tags ---

		public void AddRuntime(string tag)
		{
			if (!string.IsNullOrEmpty(tag)) RuntimeTags.Add(tag);
		}

		public void RemoveRuntime(string tag) => RuntimeTags.Remove(tag);

		public void ClearRuntime() => RuntimeTags.Clear();

		// --- Constructors ---

		public SOQueryTags() { }

		public SOQueryTags(params string[] tags)
		{
			if (tags != null)
			{
				_tags.AddRange(tags);
			}
		}
	}
}