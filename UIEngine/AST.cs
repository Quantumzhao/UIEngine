using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace UIEngine
{
	internal class Tree
	{
		private static IEnumerable<Node> Global = new HashSet<Node>();
		public Node Root;
		public Node current;

		public event NodeOperationsDelegate Show;

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
				?.GetVisibleProperty()
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
}
