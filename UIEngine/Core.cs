using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UIEngine
{
	public delegate void NodeOperationsHandler(Node node);
	public delegate void WarningMessageHandler(Node source, string message);
	[Obsolete]
	public delegate void NotifySelfChangedHandler(Node sender, NotifySelfChangedEventArgs e);

	[Obsolete]
	public interface INotifySelfChanged
	{
		event NotifySelfChangedHandler OnSelfChanged;
	}

	public interface IVisible
	{
		/// <summary>
		///		The unique name that is used to locate and find a node. 
		///		It is optional, but the node cannot be found if it is left blank
		/// </summary>
		string Name { get; }
		string Description { get; }
		/// <summary>
		///		The actual name that will be seen by the users
		/// </summary>
		string Header { get; }
	}

	public static class Dashboard
	{
		public static HashSet<Node> Roots { get; } = new HashSet<Node>();
		public static HashSet<ObjectNode> GetRootObjectNodes()
			=> new HashSet<ObjectNode>(Roots.Where(n => n is ObjectNode).Select(n => n as ObjectNode));
		public static HashSet<MethodNode> GetRootMethodNodes()
			=> new HashSet<MethodNode>(Roots.Where(n => n is MethodNode).Select(n => n as MethodNode));
		public static event WarningMessageHandler WarningMessagePublished;

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

		/// <summary>
		///		Not yet implemented
		/// </summary>
		public static void RefreshAll()
		{
			foreach (var node in GetRootObjectNodes())
			{
				node.Refresh();
			}
		}

		/// <summary>
		///		Not yet implemented
		/// </summary>
		public static void NotifyPropertyChanged(object sender, string propertyName, object newValue)
		{
			ObjectNode objectNode = Find(sender);
			if (objectNode != null)
			{
				objectNode.Properties.FirstOrDefault(n => ((PropertyDomainModelRefInfo)n.SourceObjectInfo)
					.PropertyName == propertyName).ObjectData = newValue;
			}
		}

		/// <summary>
		///		Not yet implemented
		/// </summary>
		public static ObjectNode Find(object objectData)
		{
			foreach (var objectNode in GetRootObjectNodes())
			{
				var ret = objectNode.FindDecendant(objectData);
				if (ret != null)
				{
					return ret;
				}
			}

			return null;
		}

		internal static void RaiseWarningMessage(Node source, string message)
		{
			WarningMessagePublished?.Invoke(source, message);
		}

		/// <summary>
		///		Appends descriptive info to the designated object
		/// </summary>
		/// <typeparam name="T">Accepts only reference types. </typeparam>
		/// <param name="target"> the target object </param>
		/// <param name="visibleAttribute"> descriptive info in the form of <c>Visible</c> attribute </param>
		/// <example><code>object.AppendVisibleAttribute(new Visible(""))</code></example>
		public static T AppendVisibleAttribute<T>(this T target, VisibleAttribute visibleAttribute)
			where T : class
		{
			Misc.ObjectTable.Add(target, visibleAttribute);
			return target;
		}
	}

	public abstract class DescriptiveInfoAttribute : Attribute
	{
		public DescriptiveInfoAttribute(string name, string header, string description)
		{
			Name = name;
			Header = header;
			Description = description;
		}

		/// <summary>
		///		The unique name that is used to locate and find a node. 
		///		It is optional, but the node cannot be found if it is left blank
		/// </summary>
		public string Name { get; }
		public string Header { get; set; }
		public string Description { get; set; }
	}

	/* There are 3 ways in providing descriptive information, which are:
	 * 1. Conditional weak table, i.e. appended pseudo-attribute
	 *    This is best for objects dynamically added into a collection
	 * 2. IVisible interface
	 *    This is the best way of storing/accessing this information, 
	 *    but is limited to user defined types
	 * 3. Visible Attribute
	 *    This is an alternative way for properties with non user defined types
	 * They are with priority order: 1 > 2 > 3 */
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
	public class VisibleAttribute : DescriptiveInfoAttribute
	{
		/// <summary>
		///		Initializing the visibility tag. 
		///		Any member marked with "Visible" can be accessed via front end
		/// </summary>
		/// <param name="name">
		///		Name of this property. If <paramref name="header"/> is left blank, 
		///		<paramref name="name"/> will be the default string for <paramref name="header"/>
		/// </param>
		/// <param name="header">
		///		Name of the member. It has to be the exact name of the member, i.e. <c>nameof(member)</c>
		/// </param>
		/// <param name="description">
		///		Some descriptions (optional)
		/// </param>
		public VisibleAttribute(string header, string description = "", string name = "")
			: base(name, header, description) { }
		public bool IsEnabled { get; set; } = true;
		public Func<object, string> PreviewExpression { get; set; } = o => o.ToString();
	}

	[AttributeUsage(AttributeTargets.GenericParameter | AttributeTargets.Parameter)]
	public class ParamInfo : DescriptiveInfoAttribute
	{
		public ParamInfo(string header, string description = "", string name = "")
			: base(name, header, description) { }
	}

	public static class Misc
	{
		public static readonly ConditionalWeakTable<object, VisibleAttribute> ObjectTable 
			= new ConditionalWeakTable<object, VisibleAttribute>();

		internal static IEnumerable<PropertyInfo> GetVisibleProperties(this Type type, BindingFlags flags)
		{
			return type.GetProperties(flags).Where(p =>
			{
				var attr = p.GetCustomAttribute<VisibleAttribute>();
				return attr != null && attr.IsEnabled;
			});
		}

		internal static IEnumerable<MethodInfo> GetVisibleMethods(this Type type, BindingFlags flags)
		{
			return type.GetMethods(flags).Where(m =>
			{
				var attr = m.GetCustomAttribute<VisibleAttribute>();
				return attr != null && attr.IsEnabled;
			});
		}

		internal static List<object> ToObjectList(this ICollection collection)
		{
			var array = new object[collection.Count];
			collection.CopyTo(array, 0);
			return array.ToList();
		}

		public static TypeSystem ToValidType(this Type type) => TypeSystem.ToRestrictedType(type);

		internal static Variable ToVariable(this object value)
		{
			return new Variable(value);
		}
	}

	public abstract class DomainModelRefInfo 
	{
		internal DomainModelRefInfo(Type type, SourceReferenceType sourceReferenceType)
		{
			SourceReferenceType = sourceReferenceType;
			ObjectDataType = type.ToValidType();
		}

		internal readonly SourceReferenceType SourceReferenceType;
		internal readonly TypeSystem ObjectDataType;
	}
	public class OtherDomainModelRefInfo : DomainModelRefInfo
	{
		internal OtherDomainModelRefInfo(Type type, SourceReferenceType sourceReferenceType) 
			: base(type, sourceReferenceType) { }
	}
	public class PropertyDomainModelRefInfo : DomainModelRefInfo
	{
		public readonly string PropertyName;
		public readonly PropertyInfo PropertyInfo;

		public PropertyDomainModelRefInfo(PropertyInfo info) 
			: base(info.PropertyType, SourceReferenceType.Property)
		{
			PropertyInfo = info;
			PropertyName = PropertyInfo.Name;
		}
	}
	internal enum SourceReferenceType
	{
		Property,
		Indexer,
		ReturnValue,
		parameter
	}

	public class TypeSystem
	{
		public static TypeSystem ToRestrictedType(Type type)
		{
			if (type.Equals(typeof(bool)))
			{
				return Bool;
			}
			else if (type.Equals(typeof(string)))
			{
				return String;
			}
			else if (type.Equals(typeof(double)))
			{
				return Double;
			}
			else if (type.Equals(typeof(int)))
			{
				return Int;
			}
			else if (typeof(ICollection).IsAssignableFrom(type))
			{
				return new TypeSystem(type, Types.Collection);
			}
			else if (typeof(Enum).IsAssignableFrom(type))
			{
				return new EnumType(type);
			}
			else if (typeof(object).IsAssignableFrom(type))
			{
				return new TypeSystem(type, Types.Object);
			}
			else
			{
				throw new ArgumentException("Not a valid type");
			}
		}
		protected TypeSystem(Type type, Types restrictedType)
		{
			ReflectedType = type;
			RestrictedType = restrictedType;
		}
		protected TypeSystem(Types restrictedType)
		{
			RestrictedType = restrictedType;
			switch (restrictedType)
			{
				case Types.Bool:
					ReflectedType = typeof(bool);
					break;
				case Types.Collection:
					ReflectedType = typeof(ICollection);
					break;
				case Types.Double:
					ReflectedType = typeof(double);
					break;
				case Types.Int:
					ReflectedType = typeof(int);
					break;
				case Types.String:
					ReflectedType = typeof(string);
					break;
				case Types.Object:
					ReflectedType = typeof(object);
					break;
				case Types.Enum:
					ReflectedType = typeof(Enum);
					break;
			}
		}

		public static readonly TypeSystem Bool			= new TypeSystem(Types.Bool);
		/// <summary>
		///		A shortcut for <c>ICollection</c>. To wrap a <c>System.Type</c>, use <c>ToRestrictedType()</c>
		/// </summary>
		public static readonly TypeSystem Collection	= new TypeSystem(Types.Collection);
		public static readonly TypeSystem Double		= new TypeSystem(Types.Double);
		public static readonly TypeSystem Int			= new TypeSystem(Types.Int);
		public static readonly TypeSystem String		= new TypeSystem(Types.String);
		public static readonly TypeSystem Object		= new TypeSystem(Types.Object);
		public static readonly EnumType   Enum			= new EnumType  (typeof(Enum));

		internal readonly Type ReflectedType;
		public readonly Types RestrictedType;

		public bool IsSame(TypeSystem type)
		{
			if ((RestrictedType & (Types.Collection | Types.Object | Types.Enum)) != 0)
			{
				return this.ReflectedType.Equals(type.ReflectedType);
			}
			else
			{
				return this.Equals(type);
			}
		}

		public bool IsDerivedFrom(Type type)
		{
			if ((RestrictedType & (Types.Collection | Types.Object | Types.Enum)) != 0)
			{
				return type.IsAssignableFrom(ReflectedType);
			}
			else
			{
				return ReflectedType.Equals(type);
			}
		}
		public bool IsDerivedFrom(TypeSystem type) => IsDerivedFrom(type.ReflectedType);
		public bool IsAssignableFrom(Type type) => ReflectedType.IsAssignableFrom(type);
		/// <summary>
		///		Enum is not considered a value type by this context
		/// </summary>
		public bool IsValueType => (RestrictedType & (Types.Int | Types.Double | Types.Bool | Types.String)) != 0;
		public bool IsEnum => IsDerivedFrom(TypeSystem.Enum);
		public enum Types
		{
			Bool = 1,
			Double = 2,
			Int = 4,
			String = 8,
			Collection = 16,
			Object = 32,
			Enum = 64
		}

		public class EnumType : TypeSystem
		{
			internal EnumType(Type type) : base(type, Types.Enum)
			{
				IsMultiSelect = type.GetCustomAttribute<FlagsAttribute>() != null;
				if (!type.Equals(typeof(System.Enum)))
				{
					Candidates = new ReadOnlyCollection<string>(System.Enum.GetNames(type));
				}
				else
				{
					Candidates = new ReadOnlyCollection<string>(new List<string>());
				}
			}

			public readonly ReadOnlyCollection<string> Candidates;
			public readonly bool IsMultiSelect;
		}
	}

	internal class Expression
	{
		private readonly List<Expression> _Arguments = new List<Expression>();
		internal virtual Delegate Body { get; set; }
		internal int MaxArguments { get; set; }

		internal bool AddArgument(Expression argument)
		{
			if (_Arguments.Count < MaxArguments)
			{
				_Arguments.Add(argument);
				return true;
			}
			else
			{
				return false;
			}
		}

		internal virtual object Invoke()
		{
			return Body.DynamicInvoke(_Arguments.Select(a => a.Invoke()).ToArray());
		}
	}

	internal class Variable : Expression
	{
		internal Variable(object value)
		{
			Value = value;
		}

		internal object Value { get; set; }
		internal override Delegate Body => throw new InvalidOperationException();

		internal override object Invoke() => Value;
	}

	[Obsolete]
	public class NotifySelfChangedEventArgs : EventArgs
	{
		public readonly object newObjectData;
	}

	/// <summary>
	///		The purpose of this class is to wrap up a struct into a reference type 
	///		for <c>AppendVisibleAttribute</c>
	///		<para> please take extra caution when handling it, since it it immutable, and any assignment will remove its visible attribute. </para>
	/// </summary>
	/// <typeparam name="T">
	///		T must be a struct. There's nno need to wrap up a reference type into a reference type
	///	</typeparam>
	public class W<T> where T : struct
	{
		public readonly T Value;

		public W(T value) => Value = value;

		public static implicit operator T(W<T> wrappedStruct) => wrappedStruct.Value;
		public static implicit operator W<T>(T value) => new W<T>(value);

		public override string ToString() => Value.ToString();
	}
}
