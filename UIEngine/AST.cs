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
	public abstract class Node
	{
		public string Name { get; set; }
		public string Header { get; set; }
		public ObjectNode Parent { get; internal set; }
		public string Description { get; set; }

		protected string _Preview = "...";
		protected abstract string Preview { get; set; }

		/// <summary>
		///		It defines the way that the object data should be interpreted as a preview
		/// </summary>
		public Func<object, string> PreviewExpression { get; protected set; } = o => o.ToString();
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
			var setter = propertyInfo.SetMethod;
			CanWrite = setter.IsPublic && (setter.GetCustomAttribute<Visible>()?.IsEnabled ?? true);
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
		public bool CanWrite { get; internal set; } = false;
		public bool IsLeaf => Properties.Count == 0;
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
		#region Object Data
		private PropertyInfo propertyInfo;
		public delegate void ObjectdataChangeDelegate(object data);
		public event ObjectdataChangeDelegate ObjectDataLoaded;
		private object _ObjectData;
		internal object ObjectData
		{
			get => _ObjectData;
			set
			{
				if (value != _ObjectData)
				{
					_ObjectData = value;
					ObjectDataLoaded(value);
				}
			}
		}
		/// <summary>
		///		Retrieves object value of the object node. 
		///		Returns null if cannot cast object to the given type
		///		<para>
		///			Only use it if necessary. 
		///		</para>
		/// </summary>
		/// <typeparam name="T">
		///		Typeof the specified object
		/// </typeparam>
		/// <returns>
		///		A reference to object of the object node. Strongly suggest not to modify it. 
		/// </returns>
		public T GetValue<T>()
		{
			if (ObjectData is T)
			{
				return (T)ObjectData;
			}
			else
			{
				return default;
			}
		}
		/// <summary>
		///		Set the object data
		/// </summary>
		/// <param name="value"></param>
		/// <returns>
		///		false if this property is read only 
		///		or there is an exception when setting property value
		/// </returns>
		public bool SetValue(object value)
		{
			if (CanWrite)
			{
				try
				{
					propertyInfo.SetValue(Parent.ObjectData, value);
				}
				catch
				{
					return false;
				}

				ObjectData = value;

				return true;
			}

			return false;
		}
		#endregion

		protected override string Preview
		{
			get => _Preview;
			set => _Preview = value;
		}

		private void LoadObject(PropertyInfo propertyInfo)
		{
			ObjectData = propertyInfo.GetValue(Parent?.ObjectData);
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
				data = ObjectData;
			}
			Methods = data.GetType().GetVisibleMethods()
				.Select(mi => new MethodNode(this, mi)).ToList();
		}

		public override string ToString() => Header;
	}

	public class MethodNode : Node
	{
		public MethodNode(ObjectNode parent, MethodInfo methodInfo)
		{
			Parent = parent;
			Body = methodInfo;
			var attr = methodInfo.GetCustomAttribute<Visible>();
			Header = attr.Header;
			Description = attr.Description;
			Signature = methodInfo.GetParameters().Select(p => new Parameter(p.ParameterType)).ToList();
			ReturnType = methodInfo.ReturnType;
		}

		public List<Parameter> Signature;
		public Type ReturnType { get; private set; }
		private MethodInfo Body;

		protected override string Preview
		{
			get => _Preview;
			set => _Preview = value;
		}

		public ObjectNode Invoke()
		{
			var objectData = Body.Invoke(
				Parent?.ObjectData, 
				Signature.Select(p => p.Data.ObjectData).ToArray()
			);
			
			return new ObjectNode(null, objectData, new Visible(Header, Description)) { CanWrite = false };
		}

		public bool SetParameter(ObjectNode argument, int index)
		{
			var param = Signature[index];
			if (param.Type.IsAssignableFrom(argument.ObjectData.GetType()))
			{
				param.Data = argument;
				return true;
			}
			return false;
		}

		public Dictionary<ObjectNode, bool> GetCandidates(int index)
		{
			throw new NotImplementedException();
		}

		public class Parameter
		{
			public Parameter(Type type, ObjectNode data = null)
			{
				Type = type;
				Data = data;
			}

			public Type Type { get; private set; }
			public ObjectNode Data { get; set; }
		}
	}

	public class LinqNode : Node
	{
		protected override string Preview
		{
			get => _Preview;
			set => _Preview = value;
		}
	}
}
