#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace NestedSO.SOEditor
{
    public static class NestedSOAssetUtils
    {
        public static void DestroyAsset(ScriptableObject asset)
        {
            if (asset == null) return;

            if (asset is NestedSOCollectionBase collectionBase)
            {
                var items = collectionBase.GetRawItems();
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    DestroyAsset(items[i]); 
                }
            }

            var fields = asset.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                if (typeof(NestedSOListBase).IsAssignableFrom(field.FieldType))
                {
                    var listWrapper = field.GetValue(asset);
                    if (listWrapper == null) continue;

                    // Access the generic 'Items' list
                    var itemsField = field.FieldType.GetField("Items");
                    if (itemsField != null)
                    {
                        var list = itemsField.GetValue(listWrapper) as IList;
                        if (list != null)
                        {
                            for (int i = list.Count - 1; i >= 0; i--)
                            {
                                DestroyAsset(list[i] as ScriptableObject);
                            }
                        }
                    }
                }
            }
            
            AssetDatabase.RemoveObjectFromAsset(asset);
            Object.DestroyImmediate(asset, true);
        }
    }
}
#endif