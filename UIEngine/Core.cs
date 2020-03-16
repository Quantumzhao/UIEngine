using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UIEngine
{
	public delegate void NodeOperationsHandler(Node node);
	public delegate void WarningMessageHandler(Node source, string message);
	public delegate void NotifySelfChangedHandler(Node sender, NotifySelfChangedEventArgs e);

	public interface INotifySelfChanged
	{
		event NotifySelfChangedHandler OnSelfChanged;
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
				foreach (var property in type.GetVisibleProperties(BindingFlags.Public | BindingFlags.Static))
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

		public static void RefreshAll()
		{
			foreach (var node in GetRootObjectNodes())
			{
				node.Refresh();
			}
		}

		public static void NotifyPropertyChanged(object sender, string propertyName, object newValue)
		{
			ObjectNode objectNode = Find(sender);
			if (objectNode != null)
			{
				objectNode.Properties.FirstOrDefault(n => n.Name == propertyName).ObjectData = newValue;
			}
		}

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

		public static string Test() => "New";
	}

	public abstract class DescriptiveInfo : Attribute
	{
		public DescriptiveInfo(string name, string header, string description)
		{
			Header = header == "" ? name : header;
			Description = description;
			Name = name;
		}

		public string Header { get; set; }
		public string Description { get; set; }
		public string Name { get; set; }
	}

	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
	public class Visible : DescriptiveInfo
	{
		/// <summary>
		///		Initializing the visibility tag. Any member marked with "Visible" can be accessed via front end
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
		public Visible(string name, string header = "", string description = "")
			: base(name, header, description) { }
		public bool IsEnabled { get; set; } = true;
		public Func<object, string> PreviewExpression { get; set; } = o => o.ToString();
	}

	[AttributeUsage(AttributeTargets.GenericParameter | AttributeTargets.Parameter)]
	public class ParamInfo : DescriptiveInfo
	{
		public ParamInfo(string name, string header = "", string description = "")
			: base(name, header, description) { }
	}

	public static class Misc
	{
		public static IEnumerable<PropertyInfo> GetVisibleProperties(this Type type, BindingFlags flags)
		{
			return type.GetProperties(flags).Where(p =>
			{
				var attr = p.GetCustomAttribute<Visible>();
				return attr != null && attr.IsEnabled;
			});
		}

		public static IEnumerable<MethodInfo> GetVisibleMethods(this Type type, BindingFlags flags)
		{
			return type.GetMethods(flags).Where(m =>
			{
				var attr = m.GetCustomAttribute<Visible>();
				return attr != null && attr.IsEnabled;
			});
		}

		public static List<object> ToObjectList(this ICollection collection)
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

	/* If the target object is retrieved from property, then property info is not null. 
	 * vice versa */
	internal class DomainModelReferenceInfo
	{
		public DomainModelReferenceInfo(PropertyInfo propertyInfo, SourceReferenceType sourceReferenceType)
			: this(propertyInfo.PropertyType, sourceReferenceType)
		{
			PropertyInfo = propertyInfo;
		}
		public DomainModelReferenceInfo(Type type, SourceReferenceType sourceReferenceType)
		{
			SourceReferenceType = sourceReferenceType;
			ObjectDataType = type.ToValidType();
		}

		public readonly PropertyInfo PropertyInfo;
		public readonly SourceReferenceType SourceReferenceType;
		public readonly TypeSystem ObjectDataType;
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
				return new TypeSystem(type);
			}
			else if (typeof(Enum).IsAssignableFrom(type))
			{
				return new TypeSystem(type);
			}
			else if (typeof(object).IsAssignableFrom(type))
			{
				return new TypeSystem(type);
			}
			else
			{
				throw new ArgumentException("Not a valid type");
			}
		}
		private TypeSystem(Type type)
		{
			ReflectedType = type;
			if (type.IsAssignableFrom(typeof(ICollection)))
			{
				RestrictedType = Types.Collection;
			}
			else
			{
				RestrictedType = Types.Object;
			}
		}
		private TypeSystem(Types restrictedType)
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
		public static readonly TypeSystem Enum			= new TypeSystem(Types.Enum);

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

	public class NotifySelfChangedEventArgs : EventArgs
	{
		public readonly object newObjectData;
	}
}
