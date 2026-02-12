using System.Collections.Generic;
using UnityEngine;

namespace NestedSO
{
    [System.Serializable]
    public class NestedSOListBase { }
    
    [System.Serializable]
    public class NestedSOList<T> : NestedSOListBase where T : ScriptableObject
    {
        public List<T> Items = new List<T>();
    }
}
