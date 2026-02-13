using System.Collections.Generic;
using UnityEngine;

namespace NestedSO
{
    [CreateAssetMenu(fileName = "SOQueryDatabase", menuName = "NestedSO/SOQueryDatabase")]
    public class SOQueryDatabase : ScriptableObject
    {
        public List<SOQueryEntity> SOQueryEntities = new List<SOQueryEntity>();
    }
}
