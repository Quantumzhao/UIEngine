using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UIEngine.Core;
using UIEngine.Nodes;

namespace UIEngine
{
	public static class Dashboard
	{
		public static event WarningMessageHandler WarningMessagePublished;

		internal static HashSet<Node> Roots { get; } = new HashSet<Node>();
		public static IEnumerable<T> GetRootNodes<T>() where T : Node
			=> Roots.Where(n => n is T).Select(n => n as T);

		/// <summary>
		///		Get a root node by its name
		/// </summary>
		/// <typeparam name="T"><see cref="ObjectNode"/>, <see cref="CollectionNode"/> or <see cref="MethodNode"/></typeparam>
		/// <param name="name"></param>
		/// <returns>Null if not found</returns>
		public static T GetRootNode<T>(string name) where T : Node
		{
			return Roots.SingleOrDefault(n => n.Name == name) as T;
		}

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
				foreach (var property in
					type.GetVisibleProperties(BindingFlags.Public | BindingFlags.Static))
				{
					ObjectNode node = ObjectNode.Create(null, property);
					Roots.Add(node);
				}

				// Load all static methods
				foreach (var method in type.GetVisibleMethods(BindingFlags.Public | BindingFlags.Static))
				{
					MethodNode node = MethodNode.Create(null, method);
					Roots.Add(node);
				}
			}
		}

		///// <summary>
		/////		Not yet implemented
		///// </summary>
		//public static void RefreshAll()
		//{
		//	foreach (var node in GetRootObjectNodes())
		//	{
		//		node.Refresh();
		//	}
		//}

		///// <summary>
		/////		Not yet implemented
		///// </summary>
		//public static void NotifyPropertyChanged(object sender, string propertyName, object newValue)
		//{
		//	ObjectNode objectNode = Find(sender);
		//	if (objectNode != null)
		//	{
		//		objectNode.Properties.FirstOrDefault(n => ((PropertyDomainModelRefInfo)n.SourceObjectInfo)
		//			.PropertyName == propertyName).ObjectData = newValue;
		//	}
		//}

		///// <summary>
		/////		Not yet implemented
		///// </summary>
		//public static ObjectNode Find(object objectData)
		//{
		//	foreach (var objectNode in GetRootObjectNodes())
		//	{
		//		var ret = objectNode.FindDecendant(objectData);
		//		if (ret != null)
		//		{
		//			return ret;
		//		}
		//	}

		//	return null;
		//}

		internal static void RaiseWarningMessage(Node source, string message)
		{
			WarningMessagePublished?.Invoke(source, message);
		}

		/// <summary>
		///		Appends descriptive info to the designated object
		/// </summary>
		/// <typeparam name="T">Accepts only reference types. </typeparam>
		/// <param name="target"> the target object </param>
		/// <param name="descriptiveInfoAttribute"> descriptive info in the form of <c>Visible</c> attribute </param>
		/// <example><code>object.AppendVisibleAttribute(new Visible(""))</code></example>
		public static T AppendVisibleAttribute<T>(this T target, DescriptiveInfoAttribute descriptiveInfoAttribute)
			where T : class
		{
			Misc.ObjectTable.Add(target, descriptiveInfoAttribute);
			return target;
		}

		public static void SetAndRaiseIfPropertyChanged<T, V>(this T src, ref V propertyField, V value, 
			[CallerMemberName] string propertyName = null) where T : INotifyPropertyChanged
		{
			throw new NotImplementedException();
		}

		/// <summary>
		///		Maps selected (recursive) properties into a new object. For example: 
		///		<code>{"name":"string","member":{"id":0}} => {"name":"string","id":0}</code>
		/// </summary>
		/// <remarks>Does not support methods</remarks>
		/// <example><code>{"name":"string","member":{"id":0}} => {"name":"string","id":0}</code></example>
		/// <typeparam name="T">Type of the class that is about to be converted</typeparam>
		/// <param name="src">An onject of the class</param>
		/// <param name="mappings">paths to properties</param>
		/// <returns>a new object</returns>
		internal static FlattenedClass<T> ToFlattenedClass<T>(this T src, IEnumerable<Func<T, object>> mappings)
		{
			var ret = new FlattenedClass<T>();

			foreach (var mapping in mappings)
			{
				ret.Properties.Add(new PseudoProperty<T>(mapping, src));
			}

			return ret;
		}
	}
}
