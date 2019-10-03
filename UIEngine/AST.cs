using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
namespace UIEngine
{
	public class Tree
	{
		private static IEnumerable<Node> Global = new HashSet<Node>();
		public Node Root;
		public Node current;
	}

	public abstract class Node
	{
		public string Name;
		public ObjectNode Parent;
		public string Description;

		protected string preview = "...";
		protected abstract string Preview { get; set; }
	}

	public class ObjectNode : Node
	{
		private ObjectNode(ObjectNode parent, Visible attribute)
		{
			Parent = parent;
			PreviewExpression = attribute.PreviewExpression;
			Description = attribute.Description;
			ObjectDataLoaded += o => Preview = PreviewExpression?.Invoke(o);
		}
		internal ObjectNode(ObjectNode parent, PropertyInfo propertyInfo) 
			: this(parent, propertyInfo.GetCustomAttribute<Visible>())
		{
			Header = propertyInfo.GetCustomAttribute<Visible>().Header;
			this.propertyInfo = propertyInfo;
			Name = propertyInfo.Name;
		}
		// create object nodes from annonymous objects, like elements in a collection
		internal ObjectNode(ObjectNode parent, object objectData, Visible attribute) 
			: this(parent, attribute)
		{
			propertyInfo = null;
			Name = "N/A";
			Header = objectData.ToString();
			ObjectData = objectData;
		}

		private List<ObjectNode> _Properties = null;
		public List<ObjectNode> Properties
		{
			get
			{
				if (_Properties == null)
				{
					LoadProperties(ObjectData);
				}
				return _Properties;
			}
			set => _Properties = value;
		}

		private List<MethodNode> _Methods = null;
		public List<MethodNode> Methods
		{
			get
			{
				if (_Methods == null)
				{
					LoadMethods(ObjectData);
				}
				return _Methods;
			}
			set => _Methods = value;
		}
		public string Header { get; set; }
		#region Object Data
		private PropertyInfo propertyInfo;
		public delegate void ObjectdataChangeDelegate(object data);
		public event ObjectdataChangeDelegate ObjectDataLoaded;
		internal object ObjectData { get; set; }
		#endregion

		/// <summary>
		///		It defines the way that the object data should be interpreted as a preview
		/// </summary>
		public Func<object, string> PreviewExpression { get; private set; } = o => o.ToString();
		protected override string Preview
		{
			get => preview;
			set => preview = value;
		}

		private void LoadObject(PropertyInfo propertyInfo)
		{
			ObjectData = propertyInfo.GetValue(Parent?.ObjectData);
			ObjectDataLoaded(ObjectData);
		}

		private void LoadProperties(object data)
		{
			if (data == null)
			{
				LoadObject(propertyInfo);
				data = ObjectData;
			}

			if (data is IEnumerable<object>)
			{
				Properties = (data as IEnumerable<object>)
					.Select(o => new ObjectNode(
						this, o, propertyInfo.GetCustomAttribute<Visible>()
					)).ToList();
			}
			else
			{
				Properties = data.GetType().GetVisibleProperties()
					.Select(pi => new ObjectNode(this, pi)).ToList();
			}
		}
		private void LoadMethods(object data)
		{
			if (data == null)
			{
				LoadObject(propertyInfo);
			}
			Methods = data.GetType().GetVisibleMethods()
				.Select(mi => new MethodNode(this, mi)).ToList();
		}

		public override string ToString()
		{
			return Header;
		}
	}

	public class MethodNode : Node
	{
		public MethodNode(ObjectNode parent, MethodInfo methodInfo)
		{
		}

		public List<ObjectNode> Parameters;
		private MethodInfo Body;

		protected override string Preview { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

		public object Invoke()
		{
			return Body.Invoke(Parent, Parameters.ToArray());
		}

		public void PassInArg(object arg, int index)
		{
			throw new NotImplementedException();
		}
	}

	public class LinqNode : Node
	{
		protected override string Preview
		{
			get => preview;
			set => preview = value;
		}
	}
}
