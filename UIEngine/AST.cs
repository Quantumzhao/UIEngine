using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace UIEngine
{
	public class Tree
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
		public Node(string name, string description)
		{
			Name = name;
			Description = description;
		}

		public string Name;
		public ObjectNode Parent;
		public string Description;

		public abstract void Load(object objectData);
		public abstract void Select();
	}

	public class ObjectNode : Node
	{
		internal ObjectNode(string name, string header, string description)
			: base(name, description)
		{
			Header = header;
		}

		public List<ObjectNode> Properties;
		public List<MethodNode> Methods;
		public string Header;
		internal object ObjectData;

		public ObjectNode GetProperty(string name)
		{
			var property = Properties.Where(p => p.Name == name).FirstOrDefault();
			// Load and fill the blank property with actual data from property info
			if (property.Properties == null)
			{
				property.Load(ObjectData.GetType().GetProperty(name).GetValue(ObjectData));
			}
			return property;
		}

		public MethodNode GetMethod(string name)
		{
			return Methods.Where(m => m.Name == name).FirstOrDefault();
		}

		public override void Load(object objectData)
		{
			Properties = objectData.GetType().GetProperties().GetVisibleProperties()
				.Select(pi =>
				{
					var attr = pi.GetCustomAttribute<Visible>();
					return new ObjectNode(pi.Name, attr.Header, attr.Description) { Parent = this };
				})
				.ToList();
		}

		/// <summary>
		///		Call this method when it is being selected. 
		///		<para>
		///			Make itself acquiring its context and prepare 
		///			for navigating to its properties and methods
		///		</para>
		/// </summary>
		public override void Select()
		{
			Load(
				Parent
				.ObjectData
				.GetType()
				.GetProperty(Name)
				.GetValue(Parent.ObjectData)
			);
		}
	}

	public class MethodNode : Node
	{
		public MethodNode(string name, string description) : base(name, description)
		{
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

		public override void Load(object objectData)
		{
			throw new NotImplementedException();
		}

		public override void Select()
		{
			throw new NotImplementedException();
		}
	}
}
