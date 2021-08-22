using System.Collections.Generic;
using UnityEngine;

public interface INestedSOCollection
{
	void AddAsset(ScriptableObject item);
	void RemoveAsset(ScriptableObject item);
	IReadOnlyList<ScriptableObject> GetRawItems();
}