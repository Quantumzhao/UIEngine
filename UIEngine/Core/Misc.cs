using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UIEngine.Nodes;

namespace UIEngine.Core
{
	public delegate void WarningMessageHandler(Node source, string message);
	//public delegate void NotifySelfChangedHandler(Node sender, NotifySelfChangedEventArgs e);

	/// <summary>
	///		This is an alternative for <see cref="VisibleAttribute"/>. 
	///		It is lightweight, and can be used to mark up objects 
	///		that don't work well with attributes, such as objects in a collection. 
	///		<para>
	///			For complete functionalities, 
	///			use <see cref="Dashboard.AppendVisibleAttribute{T}(T, DescriptiveInfoAttribute)"/> instead
	///		</para>
	/// </summary>
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


	public static class Misc
	{
		internal static readonly ConditionalWeakTable<object, DescriptiveInfoAttribute> ObjectTable 
			= new ConditionalWeakTable<object, DescriptiveInfoAttribute>();

		internal static IEnumerable<PropertyInfo> GetVisibleProperties(this Type type, BindingFlags flags)
		{
			return type.GetProperties(flags).Where(p =>
			{
				var attr = p.GetCustomAttribute<VisibleAttribute>();
				return attr != null && attr.IsFeatureEnabled;
			});
		}

		internal static IEnumerable<MethodInfo> GetVisibleMethods(this Type type, BindingFlags flags)
		{
			return type.GetMethods(flags).Where(m =>
			{
				var attr = m.GetCustomAttribute<VisibleAttribute>();
				return attr != null && attr.IsFeatureEnabled;
			});
		}

		internal static Variable ToVariable(this object value)
		{
			return new Variable(value);
		}
	}


	public enum SelectionMode
	{
		SingleSelect, 
		MultiSelect
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

	/// <summary>
	///		The purpose of this class is to wrap up a struct into a reference type 
	///		for <see cref="Dashboard.AppendVisibleAttribute{T}(T, DescriptiveInfoAttribute)"/>
	///		<para> please take extra caution when handling it, since it is immutable, and any assignment will remove its visible attribute. </para>
	/// </summary>
	/// <typeparam name="T">
	///		T must be a struct. There's no need to wrap up a reference type into a reference type
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
