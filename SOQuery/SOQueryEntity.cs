using System.Collections.Generic;
using UnityEngine;

namespace NestedSO
{
    public abstract class SOQueryEntity : ScriptableObject
    {
        [Tooltip("Tags used for querying this config at runtime.")]
        public List<string> Tags = new List<string>();
    }
}
