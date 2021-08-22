using System.Collections.Generic;
using UnityEngine;

namespace NestedSO
{
	public interface INestedSOCollection
	{
		void AddAsset(ScriptableObject item);
		void RemoveAsset(ScriptableObject item);
		IReadOnlyList<ScriptableObject> GetRawItems();
	}
}