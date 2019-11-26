using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UIEngine
{
	public delegate void NodeOperationsDelegate(Node node);

	public static class Dashboard
	{
		public static HashSet<Node> Roots { get; } = new HashSet<Node>();
		public static HashSet<ObjectNode> GetGlobalObjects()
			=> new HashSet<ObjectNode>(Roots.Where(n => n is ObjectNode).Select(n => n as ObjectNode));
		public static HashSet<MethodNode> GetGlobalMethods()
			=> new HashSet<MethodNode>(Roots.Where(n => n is MethodNode).Select(n => n as MethodNode));

		/// <summary>
		///		Put all static objects into global objects collection.
		///		<para>
		///			THIS METHOD MUST BE CALLED PRIOR TO ANY OTHER CALLS!
		///		</para>
		/// </summary>
		/// <param name="classes">
		///		The classes where the desired importing static objects are located
		/// </param>
		public static void ImportEntryObjects(params Type[] classes)
		{
			foreach (var type in classes)
			{
				// Load all static properties
				foreach (var property in type.GetVisibleProperties(staticOnly: true))
				{
					ObjectNode node = new ObjectNode(null, property);
					Roots.Add(node);
				}

				// Load all static methods
				foreach (var method in type.GetVisibleMethods(staticOnly: true))
				{
					MethodNode node = new MethodNode(null, method);
					Roots.Add(node);
				}
			}
		}
	}

	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
	public class Visible : Attribute
	{
		/// <summary>
		///		Initializing the visibility tag. Any member marked with "Visible" can be accessed via front end
		/// </summary>
		/// <param name="header">
		///		The name of the member that is to be displayed
		///		<para>
		///			The easiest way to do this: nameof([member name])
		///		</para>
		/// </param>
		/// <param name="description">
		///		Some description (optional)
		/// </param>
		public Visible(string header = "", string description = "")
		{
			Header = header;
			Description = description;
		}
		public string Header { get; set; }
		public string Description { get; set; }
		public bool IsEnabled { get; set; } = true;
		public Func<object, string> PreviewExpression { get; set; } = o => o.ToString();
	}

	public static class Misc
	{
		public static IEnumerable<PropertyInfo> GetVisibleProperties(this Type type, bool staticOnly = false)
		{
			return (
				staticOnly ? type.GetProperties(BindingFlags.Static | BindingFlags.Public) : type.GetProperties())
				.Where(p =>
				{
					var attr = p.GetCustomAttribute<Visible>();
					return attr != null && attr.IsEnabled;
				}
			);
		}

		public static IEnumerable<MethodInfo> GetVisibleMethods(this Type type, bool staticOnly = false)
		{
			return (
				staticOnly ? type.GetMethods(BindingFlags.Static | BindingFlags.Public) : type.GetMethods())
				.Where(m =>
				{
					var attr = m.GetCustomAttribute<Visible>();
					return attr != null && attr.IsEnabled;
				}
			);
		}

		public static List<object> ToObjectList(this IEnumerable collection)
		{
			var enumerator = collection.GetEnumerator();
			var list = new List<object>();
			while (enumerator.MoveNext())
			{
				list.Add(enumerator.Current);
			}
			return list;
		}
	}
}
