using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace UIEngine
{
	public delegate void NodeOperations(Node node);

	internal class AST
	{
		public static IEnumerable<ObjectNode> Global = new HashSet<ObjectNode>();
		public Node Root;
		public Node current;

		public event NodeOperations Show;

		public void NavigateTo(Node node)
		{
			current = node;
			if (node is MethodNode)
			{

			}
			else
			{

			}

			//Show(node);
		}
	}

	public abstract class Node
	{
		public Node(string name)
		{
			Name = name;
		}

		public string Name;
		public ObjectNode Parent;
	}

	public class ObjectNode : Node
	{
		public ObjectNode(string name,
			IEnumerable<PropertyInfo> properties = null,
			IEnumerable<MethodInfo> methods = null) : base(name)
		{
			Properties = properties
				?.Where(pi => pi.GetCustomAttribute<Visible>() != null)
				?.Select(pi => new ObjectNode(pi.Name));
		}

		public IEnumerable<ObjectNode> Properties;
		public IEnumerable<MethodNode> Methods;

		public ObjectNode GetProperty(string name)
		{
			return Properties.Where(p => p.Name == name).FirstOrDefault();
		}

		public MethodNode GetMethod(string name)
		{
			return Methods.Where(m => m.Name == name).FirstOrDefault();
		}
	}

	public class MethodNode : Node
	{
		public MethodNode(MethodInfo methodInfo, ObjectNode parent) : base(methodInfo.Name)
		{
			Parent = parent;
			Body = methodInfo;
		}

		public List<ObjectNode> Parameters;
		private MethodInfo Body;

		public object Invoke()
		{
			return Body.Invoke(Parent, Parameters.ToArray());
		}

		public void PassInArg(object arg, int index)
		{
			throw new NotImplementedException();
		}
	}


	public static class Dashboard
	{
		public static IEnumerable<VisualObject> GlobalObjects;
	}

	public class VisualObject
	{
		/// <summary>
		///		And change the current visual object to the new object specified by its name
		/// </summary>
		/// <param name="name">
		///		Name of the object
		/// </param>
		/// <returns>
		///		The new object
		/// </returns>
		public VisualObject AccessProperty(string name)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		///		And change the current visual object to the return value of the method
		/// </summary>
		/// <param name="name">
		///		name of the method
		/// </param>
		/// <returns>
		///		The method and its info about signature and etc
		/// </returns>
		public VisualProcedure AcessMethod(string name)
		{
			throw new NotImplementedException();
		}
	}

	public class VisualProcedure
	{

	}

	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
	public class Visible : Attribute { }

	public static class Misc
	{
	}
}
