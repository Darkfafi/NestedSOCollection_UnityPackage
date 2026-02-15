using System;
using UnityEngine;

namespace NestedSO
{
	public abstract class SOQueryEntity : ScriptableObject, ISOQueryEntity
	{
		[field: SerializeField]
		public string Id
		{
			get; private set;
		} = Guid.NewGuid().ToString();

		[SerializeField]
		private SOQueryTags _tags = new SOQueryTags();

		public IReadOnlySOQueryTags Tags => _tags;
	}

	public interface ISOQueryEntity
	{
		public string Id
		{
			get;
		}

		public IReadOnlySOQueryTags Tags
		{
			get;
		}
	}
}
