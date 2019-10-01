using System;
using System.Collections.Generic;
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

		public event NodeOperationsDelegate Show;
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
		internal ObjectNode(ObjectNode parent, PropertyInfo propertyInfo)
		{
			Parent = parent;
			this.propertyInfo = propertyInfo;
			Name = propertyInfo.Name;
			var attr = propertyInfo.GetCustomAttribute<Visible>();
			Header = attr.Header;
			Description = attr.Description;

			ObjectDataLoaded += o => Preview = PreviewExpression?.Invoke(o);
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
		public string Header;
		#region Object Data
		private PropertyInfo propertyInfo;
		public delegate void ObjectdataChangeDelegate(object data);
		public event ObjectdataChangeDelegate ObjectDataLoaded;
		internal object ObjectData { get; set; }
		#endregion

		/// <summary>
		///		It defines the way that the object data should be interpreted as a preview
		/// </summary>
		public Func<object, string> PreviewExpression { get; set; } = o => o.ToString();
		protected override string Preview
		{
			get => preview;
			set => preview = value;
		}

		private async void LoadObject(PropertyInfo propertyInfo)
		{
			await Task.Run(() =>
			{
				ObjectData = propertyInfo.GetValue(Parent?.ObjectData);
				ObjectDataLoaded(ObjectData);
			});
		}

		private void LoadProperties(object data)
		{
			if (data == null)
			{
				LoadObject(propertyInfo);
			}
			Properties = data.GetType().GetVisibleProperties()
				.Select(pi => new ObjectNode(this, pi)).ToList();
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
}
