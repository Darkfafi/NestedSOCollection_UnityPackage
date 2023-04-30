#if UNITY_EDITOR
using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NestedSO.SOEditor
{
	public static class GenericMenuEditorUtils
	{
		public static GenericMenu CreateSOWindow(Type baseType, GenericMenu.MenuFunction2 SelectionCallback, bool showWindow = false)
		{
			Type[] types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).Where(x => baseType.IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface).ToArray();
			GenericMenu menu = new GenericMenu();
			StringBuilder pathString = new StringBuilder();
			for(int i = 0; i < types.Length; i++)
			{
				Type type = types[i];

				if(type != baseType && baseType != type.BaseType)
				{
					pathString.Append(type.IsGenericType ? type.GetGenericTypeDefinition().Name : type.BaseType.Name);
					pathString.Append("/");
				}

				menu.AddItem(new GUIContent($"{pathString}{type.Name}"), false, SelectionCallback, type);
				pathString.Clear();
			}

			if(showWindow)
			{
				menu.ShowAsContext();
			}

			return menu;
		}
	}
}
#endif