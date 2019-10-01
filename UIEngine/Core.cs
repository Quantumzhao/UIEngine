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
		public static HashSet<ObjectNode> GlobalObjects = new HashSet<ObjectNode>();

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
				foreach (var property in type.GetVisibleProperties())
				{
					var attr = property.GetCustomAttribute<Visible>();
					ObjectNode node = new ObjectNode(null, property);
					GlobalObjects.Add(node);
				}
			}
		}

		public static void AddTree()
		{
			Trees.Add(new Tree());
		}

		public static IEnumerable<ObjectNode> GetGlobalObjects()
		{
			return GlobalObjects;
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
		public Func<object, string> PreviewExpression { get; set; } = o => o.ToString();
	}

	public static class Misc
	{
		public static IEnumerable<PropertyInfo> GetVisibleProperties(this Type type)
		{
			return type.GetProperties().Where( p =>
			{
				var attr = p.GetCustomAttribute<Visible>();
				return attr != null && attr.IsEnabled;
			});
		}

		public static IEnumerable<MethodInfo> GetVisibleMethods(this Type type)
		{
			return type.GetMethods().Where(m =>
			{
				var attr = m.GetCustomAttribute<Visible>();
				return attr != null && attr.IsEnabled;
			});
		}
	}
}
