using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace UIEngine
{
	public delegate void NodeOperationsDelegate(Node node);

	public static class Dashboard
	{
		public static readonly List<Tree> Trees = new List<Tree>();
		public static HashSet<ObjectNode> GlobalObjects = null;

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
			var globalObjects = new HashSet<ObjectNode>();
			foreach (var type in classes)
			{
				// Load all static properties
				foreach (var property in type.GetProperties().GetVisibleProperties())
				{
					var attr = property.GetCustomAttribute<Visible>();
					ObjectNode node = new ObjectNode(
						property.Name,
						attr.Header,
						attr.Description
					);
				}
			}
			GlobalObjects = globalObjects;
		}

		public static void AddTree()
		{
			Trees.Add(new Tree());
		}

		public static IEnumerable<ObjectNode> GetGlobalObjects()
		{
			return GlobalObjects;
		}

		public static void GetMembers(ObjectNode node, 
			out List<ObjectNode> properties, out List<MethodNode> methods)
		{
			if (node.Properties == null || node.Methods == null)
			{
				node.Select();
			}
			properties = node.Properties;
			methods = node.Methods;
		}

		public static List<ObjectNode> GetCandidates(MethodNode method, int index)
		{
			throw new NotImplementedException();
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
		public Visible(string header, string description = "")
		{
			Header = header;
			Description = description;
		}
		public string Header { get; set; }
		public string Description { get; set; }
		public bool IsEnabled { get; set; } = true;
	}

	public static class Misc
	{
		public static IEnumerable<PropertyInfo> GetVisibleProperties(this IEnumerable<PropertyInfo> properties)
		{
			return properties.Where( p =>
			{
				var attr = p.GetCustomAttribute<Visible>();
				return attr != null && attr.IsEnabled;
			});
		}

		public static IEnumerable<MethodInfo> GetVisibleMethod(this IEnumerable<MethodInfo> methods)
		{
			return methods.Where(m =>
			{
				var attr = m.GetCustomAttribute<Visible>();
				return attr != null && attr.IsEnabled;
			});
		}
	}
}
