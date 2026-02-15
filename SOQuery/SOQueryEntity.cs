using System;
using System.Collections.Generic;
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
		private List<string> _tags = new List<string>();

		public IReadOnlyList<string> Tags => _tags;
	}

	public interface ISOQueryEntity
	{
		public string Id
		{
			get;
		}

		public IReadOnlyList<string> Tags
		{
			get;
		}
	}
}
