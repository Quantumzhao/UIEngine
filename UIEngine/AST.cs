using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
namespace UIEngine
{
	public abstract class Node
	{
		/// <summary>
		///		The unique identifier that is going to be used inside the engine
		/// </summary>
		public string Name { get; set; }
		/// <summary>
		///		Name of this node that is to be shown to the users
		/// </summary>
		public string Header { get; set; }
		public ObjectNode Parent { get; internal set; }
		public string Description { get; set; }
		/// <summary>
		///		Type of the object inside object node
		/// </summary>
		protected string _Preview = "...";
		protected abstract string Preview { get; set; }

		/// <summary>
		///		It defines the way that the object data should be interpreted as a preview
		/// </summary>
		public Func<object, string> PreviewExpression { get; set; } = o => o.ToString();

		public override string ToString() => Header;
	}

	public class ObjectNode : Node
	{
		/// <summary>
		///		Create from property
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="propertyInfo"></param>
		/// <returns></returns>
		internal static ObjectNode Create(ObjectNode parent, PropertyInfo propertyInfo)
		{
			if (typeof(ICollection).IsAssignableFrom(propertyInfo.PropertyType))
			{
				return new CollectionNode(parent, propertyInfo);
			}
			else
			{
				return new ObjectNode(parent, propertyInfo);
			}
		}
		/// <summary>
		///		Create from anonymous object. i.e. elements in a collection or return value
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="objectData"></param>
		/// <param name="attribute"></param>
		/// <returns></returns>
		internal static ObjectNode Create(ObjectNode parent, object objectData, Visible attribute)
		{
			return new ObjectNode(parent, objectData, attribute);
		}

		/// <summary>
		///		A template for other ctors
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="attribute"></param>
		protected ObjectNode(ObjectNode parent, Visible attribute)
		{
			Parent = parent;
			PreviewExpression = attribute.PreviewExpression;
			Description = attribute.Description;
		}
		private ObjectNode(ObjectNode parent, PropertyInfo propertyInfo)
			: this(parent, propertyInfo.GetCustomAttribute<Visible>())
		{
			Header = propertyInfo.GetCustomAttribute<Visible>().Header;
			_SourceObjectInfo = new DomainModelReferenceInfo(propertyInfo, SourceReferenceType.Property);
			var setter = propertyInfo.SetMethod;
			_SourceObjectInfo.ObjectDataType = propertyInfo.PropertyType;
			//CanWrite = setter.IsPublic && (setter.GetCustomAttribute<Visible>()?.IsEnabled ?? true);
			Name = propertyInfo.Name;
		}
		private ObjectNode(ObjectNode parent, object objectData, Visible attribute)
			: this(parent, attribute)
		{
			if (parent is CollectionNode)
			{
				_SourceObjectInfo = new DomainModelReferenceInfo(null, SourceReferenceType.Indexer);
			}
			else
			{
				_SourceObjectInfo = new DomainModelReferenceInfo(null, SourceReferenceType.ReturnValue);
			}
			Name = "anonymous";
			Header = objectData.ToString();
			_ObjectData = objectData;
			_SourceObjectInfo.ObjectDataType = objectData.GetType();
		}
		internal readonly DomainModelReferenceInfo _SourceObjectInfo;
		public bool CanWrite { get; internal set; } = true;
		public bool IsLeaf => Properties.Count == 0;
		private List<ObjectNode> _Properties = null;
		public List<ObjectNode> Properties
		{
			get
			{
				if (_Properties == null)
				{
					LoadProperties();
				}
				return _Properties;
			}
		}

		private List<MethodNode> _Methods = null;
		public List<MethodNode> Methods
		{
			get
			{
				if (_Methods == null)
				{
					LoadMethods();
				}
				return _Methods;
			}
		}
		#region Object Data
		// private PropertyInfo propertyInfo;
		public delegate void ObjectdataChangeDelegate(object data);
		// public event ObjectdataChangeDelegate ObjectDataLoaded;
		private object _ObjectData;
		public object ObjectData
		{
			get
			{
				if (_ObjectData == null)
				{
					LoadObjectData(_SourceObjectInfo.PropertyInfo);
				}

				return _ObjectData;
			}
			set
			{
				if (CanWrite && value != _ObjectData)
				{
					_ObjectData = value;
					SetValueToSourceObject();
					// ObjectDataLoaded(value);
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
		public T GetObjectData<T>()
		{
			if (typeof(T).IsAssignableFrom(_SourceObjectInfo.ObjectDataType))
			{
				return (T)ObjectData;
			}
			else
			{
				throw new InvalidOperationException("Type Error");
			}
		}

		#endregion

		protected override string Preview
		{
			get => _Preview;
			set => _Preview = value;
		}

		private void LoadObjectData(object objectData)
		{
			_ObjectData = objectData;
		}
		protected virtual void LoadObjectData()
		{
			if (_SourceObjectInfo.SourceReferenceType == SourceReferenceType.Property)
			{
				_ObjectData = _SourceObjectInfo.PropertyInfo.GetValue(Parent?.ObjectData);
				// Preview = PreviewExpression?.Invoke(ObjectData);
			}
		}
		protected virtual void SetValueToSourceObject()
		{
			switch (_SourceObjectInfo.SourceReferenceType)
			{
				case SourceReferenceType.Property:
					_SourceObjectInfo.PropertyInfo.SetValue(Parent.ObjectData, ObjectData);
					break;

				case SourceReferenceType.Indexer:					
					if (Parent.ObjectData is IList)
					{
						var collection = Parent.ObjectData as IList;
						collection[collection.IndexOf(ObjectData)] = ObjectData;
					}
					else
					{
						throw new NotImplementedException();
					}
					break;

				case SourceReferenceType.ReturnValue:
					throw new InvalidOperationException("return value is read only");

				default:
					return;
			}
			_SourceObjectInfo.PropertyInfo.SetValue(Parent.ObjectData, ObjectData);
		}

		private void LoadProperties()
		{
			if (ObjectData is IEnumerable<object>)
			{
				_Properties = (ObjectData as IEnumerable<object>)
					.Select(o => new ObjectNode(
						this, o, _SourceObjectInfo.PropertyInfo.GetCustomAttribute<Visible>()
					)).ToList();
			}
			else
			{
				_Properties = ObjectData.GetType().GetVisibleProperties()
					.Select(pi => new ObjectNode(this, pi)).ToList();
			}
		}
		private void LoadMethods()
		{
			if (ObjectData == null)
			{
				LoadObjectData(_SourceObjectInfo.PropertyInfo);
			}
			_Methods = _SourceObjectInfo.ObjectDataType.GetVisibleMethods()
				.Select(mi => MethodNode.Create(this, mi)).ToList();
		}

		internal ObjectNode FindDecendant(object objectData)
		{
			if (objectData.Equals(_ObjectData))
			{
				return this;
			}
			else if (_Properties.Count != 0)
			{
				foreach (var property in _Properties)
				{
					var ret = property.FindDecendant(objectData);
					if (ret != null)
					{
						return ret;
					}
				}

				return null;
			}
			else
			{
				return null;
			}
		}

		internal void Refresh()
		{
			LoadObjectData();
			LoadProperties();
		}
	}

	public class MethodNode : Node
	{
		internal static MethodNode Create(ObjectNode parent, MethodInfo methodInfo)
		{
			return new MethodNode(parent, methodInfo);
		}

		private MethodNode(ObjectNode parent, MethodInfo methodInfo)
		{
			Parent = parent;
			_Body = methodInfo;
			var attr = methodInfo.GetCustomAttribute<Visible>();
			Header = attr.Header;
			Description = attr.Description;
			Signatures = methodInfo.GetParameters().Select(p => new Parameter(p.ParameterType)).ToList();
		}

		public Type ReturnType => _Body.ReturnType;
		public List<Parameter> Signatures { get; set; }
		private MethodInfo _Body;

		protected override string Preview
		{
			get => _Preview;
			set => _Preview = value;
		}

		public ObjectNode Invoke()
		{
			var objectData = _Body.Invoke(
				Parent?.ObjectData,
				Signatures.Select(p => p.Data).ToArray()
			);

			return ObjectNode.Create(null, objectData, new Visible(Header, Description));
		}

		/// <summary>
		/// 	Checks if the parameter can be assigned by the argument
		/// </summary>
		/// <param name="index">
		/// 	the index of the parameter that is to be assigned
		/// </param>
		public bool CanAssignArgument(object argument, int index)
		{
			return CanAssignArgument(argument, Signatures[index]);
		}
		public bool CanAssignArgument(object argument, Parameter parameter)
		{
			return parameter.Type.IsAssignableFrom(argument.GetType());
		}

		public bool SetParameter(object argument, Parameter parameter)
		{
			if (CanAssignArgument(argument, parameter))
			{
				parameter.Data = argument;
				return true;
			}
			return false;
		}
		public bool SetParameter(object argument, int index)
		{
			return SetParameter(argument, Signatures[index]);
		}

		/// <summary>
		/// 	Get the candidate arguments for a specified parameter
		/// </summary>
		/// <param name="index">
		/// 	the index of the specified parameter
		/// </param>
		/// <returns>
		/// 	A dictionary of all the candidate root nodes. 
		/// 	Each node corresponds to a boolean value which indicates 
		/// 	whether the type of the node matches parameter type
		/// </returns>
		public Dictionary<Node, bool> GetCandidates(int index)
		{
			var candidates = new Dictionary<Node, bool>();
			foreach (var node in Dashboard.Roots)
			{
				candidates.Add(node, CanAssignArgument(node, index));
			}
			return candidates;
		}

		public class Parameter
		{
			public Parameter(Type type, object data = null)
			{
				Type = type;
				Data = data;
			}

			public Type Type { get; private set; }

			private object _Data = null;
			public object Data
			{
				get => _Data;
				set
				{
					if (value != null)
					{
						if (Type.IsAssignableFrom(value.GetType()))
						{
							_Data = value;
						}
						else
						{
							throw new ArgumentException();
						}
					}
				}
			}
		}
	}

	//public class ValueNode : ObjectNode
	//{
	//	internal ValueNode(ObjectNode parent, PropertyInfo propertyInfo)
	//		: base(parent, propertyInfo) { }
	//}

	/// <summary>
	///		The collection that is used to generate this node should not be an element of another collection
	/// </summary>
	public class CollectionNode : ObjectNode
	{
		public bool Is_2D { get; private set; }
		public bool DisplayPropertiesAsHeadings { get; set; } = false;
		public List<string> Headings { get; private set; } = new List<string>();
		public List<object> FormattedData { get; private set; } = new List<object>();
		public List<List<ObjectNode>> Elements { get; private set; } = new List<List<ObjectNode>>();

		internal CollectionNode(ObjectNode parent, PropertyInfo propertyInfo)
			: base(parent, propertyInfo.GetCustomAttribute<Visible>()) { }

		protected override void LoadObjectData()
		{
			base.LoadObjectData();
			LoadFormattedData(ObjectData);
		}

		private void LoadFormattedData(object data)
		{
			if (data == null)
			{
				data = ObjectData;
			}

			var preFormattedData = (data as ICollection).ToObjectList();
			var list = new List<ObjectNode>();
			// If it is a dictionary
			if (data.GetType().IsAssignableFrom(typeof(IDictionary)))
			{
				Headings.Add("Key");
				Headings.Add("Value");
				Is_2D = true;
				foreach (var key in (data as IDictionary).Keys)
				{
					list.Add(Create(this, key, new Visible(Header, Description)));
					Elements.Add(list);
				}
				var enumerator = Elements.GetEnumerator();
				enumerator.MoveNext();
				foreach (var value in (data as IDictionary).Values)
				{
					enumerator.Current.Add(Create(this, value, new Visible(Header, Description)));
					enumerator.MoveNext();
				}
			}
			// If it's a two dimensional data structure
			else if (preFormattedData[0] is ICollection)
			{
				Is_2D = true;
				foreach (var element in preFormattedData)
				{
					FormattedData.Add((element as ICollection).ToObjectList());
				}
				foreach (var row in FormattedData)
				{
					var elementRow = new List<ObjectNode>();
					foreach (var column in row as ICollection)
					{
						elementRow.Add(Create(this, column, new Visible(Header, Description)));
					}
					Elements.Add(elementRow);
				}
			}
			// If it's a one dimensional list (or set, stack ...)
			else
			{
				Is_2D = false;
				foreach (var objectData in data as ICollection)
				{
					list.Add(ObjectNode.Create(this, objectData, new Visible(Header, Description)));
					Elements.Add(list);
				}
			}
		}

		// assume the collection object is not an element of another collection
		internal void SetValueToSourceCollectionElement(object oldValue, object newValue)
		{
			throw new NotImplementedException();
		}

		public ObjectNode this[int row, int column]
		{
			get => Elements[row][column];
		}
	}

	public class LinqNode
	{
	}
}
