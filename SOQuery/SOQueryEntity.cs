using System;
using UnityEngine;

namespace NestedSO
{
	[SOQueryExcludeType]
	public abstract class SOQueryEntity : ScriptableObject, ISOQueryEntity
	{
		[field: SerializeField]
		public string Id
		{
			get; private set;
		} = Guid.NewGuid().ToString();

		[SerializeField]
		protected SOQueryTags _tags = new SOQueryTags();

		public IReadOnlySOQueryTags Tags => _tags;

		public virtual void OnEnable()
		{
			_tags.ClearRuntime();
			SyncTags(_tags);
		}
		public virtual void OnValidate()
		{
			_tags.ClearRuntime();
			SyncTags(_tags);
		}

#if UNITY_EDITOR
		public void EDITOR_SetTags(SOQueryTags tags)
		{
			_tags.Clear();
			if(tags != null)
			{
				foreach (var tag in tags)
				{
					_tags.Add(tag);
				}
			}
		}

		public void EDITOR_SetId(string id)
		{
			Id = id;
		}
#endif

		protected abstract void SyncTags(SOQueryTags tags);
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
