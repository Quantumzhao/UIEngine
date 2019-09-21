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
		public static readonly List<VisualStatement> Statements = new List<VisualStatement>();
		public static IEnumerable<VisualObject> GlobalObjects = null;

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
			var globalObjects = new HashSet<VisualObject>();
			foreach (var type in classes)
			{
				foreach (var property in type.GetProperties().GetVisibleProperty())
				{
					Type propertyType = property.GetValue(null).GetType();
					ObjectNode node = new ObjectNode(
						property.Name,
						propertyType
							.GetProperties()
							.GetVisibleProperty(),
						propertyType
							.GetMethods()
					);
				}
			}
			GlobalObjects = globalObjects;
		}

		public static void AddStatement()
		{
			Statements.Add(new VisualStatement());
		}

		public static IEnumerable<VisualObject> GetGlobalObjects()
		{
			return GlobalObjects;
		}
	}

	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Class)]
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
	}

	public static class Misc
	{
		public static IEnumerable<PropertyInfo> GetVisibleProperty(this IEnumerable<PropertyInfo> properties)
		{
			return properties.Where(p => p.GetCustomAttribute<Visible>() != null);
		}

		public static IEnumerable<MethodInfo> GetVisibleMethod(this IEnumerable<MethodInfo> methods)
		{
			return methods.Where(p => p.GetCustomAttribute<Visible>() != null);
		}
	}
}
