using System;

namespace NestedSO
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
    public class SOQueryTagsContainerAttribute : Attribute
    {
        // Extracts string constants or names from enum from target to display in Tags Selector
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public class SOQueryExcludeTypeAttribute : Attribute
    {
        // Types marked with this will NOT be added to the SOQuery index
    }
}