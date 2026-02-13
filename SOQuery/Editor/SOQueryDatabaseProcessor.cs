using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NestedSO.SOEditor
{
    public class SOQueryDatabaseProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        // Runs automatically before you build the game
        public void OnPreprocessBuild(BuildReport report)
        {
            UpdateDatabase();
        }

        // You can also run this manually from the top toolbar
        [MenuItem("Tools/NestedSO/Update SOQueryDatabase")]
        public static void UpdateDatabase()
        {
            // 1. Find the SOQueryDatabase
            string[] dbGuids = AssetDatabase.FindAssets($"t:{nameof(SOQueryDatabase)}");
            if (dbGuids.Length == 0)
            {
                Debug.LogError($"No {nameof(SOQueryDatabase)} found in the project! Please create one.");
                return;
            }

            SOQueryDatabase db = AssetDatabase.LoadAssetAtPath<SOQueryDatabase>(AssetDatabase.GUIDToAssetPath(dbGuids[0]));
            db.SOQueryEntities.Clear();
            ForEachScriptableObjects<SOQueryEntity>(entity =>
            {
                if (entity != null)
                {
                    db.SOQueryEntities.Add(entity);
                }
            });

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"Config Database updated with {db.SOQueryEntities.Count} configs.");
        }



        public static void ForEachScriptableObjects<T>(Action<T> method) where T : ScriptableObject
        {
            List<T> returnValue = new List<T>();
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(ScriptableObject)}");
            for (int i = 0, c = guids.Length; i < c; i++)
            {
                string guid = guids[i];
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                CallMethod(so);
            }

            void CallMethod(ScriptableObject _so)
            {
                if (_so is T castedSO)
                {
                    method(castedSO);
                }

                if (_so is NestedSOCollectionBase nestedSOCollection)
                {
                    foreach (var x in nestedSOCollection.GetRawItems())
                    {
                        CallMethod(x);
                    }
                }
            }
        }
    }
}