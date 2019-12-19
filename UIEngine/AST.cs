﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
namespace UIEngine
{
	/* For current stage, UI Engine only supports int, double, string, bool, object and collection
	 */
	public abstract class Node
	{
		/// <summary>
		///		The unique identifier that is going to be used inside the engine
		/// </summary>
		public string Name { get; set; } = string.Empty;
		/// <summary>
		///		Name of this node that is to be shown to the users
		/// </summary>
		public string Header { get; set; } = string.Empty;
		public ObjectNode Parent { get; internal set; }
		public string Description { get; set; } = string.Empty;
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
		//public static ObjectNode CreateEmptyObjectNode(TypeSystem type) => Create(type.ReflectedType);

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
				return CollectionNode.Create(parent, propertyInfo);
			}
			else
			{
				var objectNode = new ObjectNode(parent, propertyInfo.GetCustomAttribute<Visible>());
				objectNode.SourceObjectInfo = new DomainModelReferenceInfo(propertyInfo, SourceReferenceType.Property);
				//var setter = propertyInfo.SetMethod;
				//CanWrite = setter.IsPublic && (setter.GetCustomAttribute<Visible>()?.IsEnabled ?? true);
				objectNode.Name = propertyInfo.Name;

				return objectNode;
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
			var objectNode = new ObjectNode(parent, attribute);
			if (parent is CollectionNode)
			{
				objectNode.SourceObjectInfo = new DomainModelReferenceInfo(objectData.GetType(), SourceReferenceType.Indexer);
			}
			else
			{
				objectNode.SourceObjectInfo = new DomainModelReferenceInfo(objectData.GetType(), SourceReferenceType.ReturnValue);
			}
			objectNode.Header = objectData.ToString();
			objectNode._ObjectData = objectData;

			return objectNode;
		}
		/// <summary>
		///		Create from class template. Just a typed placeholder. e.g. parameter and in LINQ local variables
		/// </summary>
		/// <param name="type"></param>
		internal static ObjectNode Create(Type type)
		{
			var objectNode = new ObjectNode(null, null);
			objectNode.SourceObjectInfo = new DomainModelReferenceInfo(type, SourceReferenceType.parameter);
			objectNode.IsEmpty = true;
			return objectNode;
		}

		// The basic ctor
		protected ObjectNode(ObjectNode parent, Visible attribute)
		{
			Parent = parent;
			if (attribute != null)
			{
				Header = attribute.Header;
				PreviewExpression = attribute.PreviewExpression;
				Description = attribute.Description;
				Name = attribute.Name;
			}
		}

		public TypeSystem Type => SourceObjectInfo.ObjectDataType;
		public bool IsValueType => SourceObjectInfo.ObjectDataType.IsValueType;
		internal bool IsEmpty { get; set; } = false;
		internal DomainModelReferenceInfo SourceObjectInfo { get; set; }
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
		private object _ObjectData = null;
		public object ObjectData
		{
			get
			{
				if (_ObjectData == null)
				{
					LoadObjectData();
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
		public T GetObjectData<T>() => (T)ObjectData;

		#endregion

		protected override string Preview
		{
			get => _Preview;
			set => _Preview = value;
		}

		protected virtual void LoadObjectData()
		{
			if (SourceObjectInfo.SourceReferenceType == SourceReferenceType.Property)
			{
				// if property info is null, then it's an abstract object, don't load object data
				if (SourceObjectInfo.PropertyInfo != null)
				{
					_ObjectData = SourceObjectInfo.PropertyInfo.GetValue(Parent?.ObjectData);
					// Preview = PreviewExpression?.Invoke(ObjectData);
				}
			}
		}
		protected virtual void SetValueToSourceObject()
		{
			switch (SourceObjectInfo.SourceReferenceType)
			{
				case SourceReferenceType.Property:
					SourceObjectInfo.PropertyInfo.SetValue(Parent.ObjectData, ObjectData);
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

				case SourceReferenceType.parameter:
					break;

				default:
					return;
			}
		}

		private void LoadProperties()
		{
			if (ObjectData is IEnumerable<object>)
			{
				_Properties = (ObjectData as IEnumerable<object>)
					.Select(o => Create(
						this, o, SourceObjectInfo.PropertyInfo.GetCustomAttribute<Visible>()
					)).ToList();
			}
			else
			{
				_Properties = ObjectData.GetType().GetVisibleProperties()
					.Select(pi => Create(this, pi)).ToList();
			}
		}
		private void LoadMethods()
		{
			if (ObjectData == null)
			{
				LoadObjectData();
			}
			_Methods = SourceObjectInfo.ObjectDataType.ReflectedType.GetVisibleMethods()
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
			var methodNode = new MethodNode();
			methodNode.Parent = parent;
			methodNode._Body = methodInfo;
			var attr = methodInfo.GetCustomAttribute<Visible>();
			methodNode.Name = methodNode.Name;
			methodNode.Header = attr.Header;
			methodNode.Description = attr.Description;
			methodNode.Signatures = methodInfo.GetParameters().Select(p => ObjectNode.Create(p.ParameterType)).ToList();
			methodNode.ReturnNode = ObjectNode.Create(methodInfo.ReturnType);

			return methodNode;
		}

		public ObjectNode ReturnNode { get; private set; }
		public List<ObjectNode> Signatures { get; set; }
		private MethodInfo _Body;

		protected override string Preview
		{
			get => _Preview;
			set => _Preview = value;
		}

		/// <summary>
		///		Invoke and store return object to return node
		/// </summary>
		/// <returns>return object</returns>
		public ObjectNode Invoke()
		{
			var objectData = _Body.Invoke(
				Parent?.ObjectData,
				Signatures.Select(p => p.ObjectData).ToArray()
			);

			ReturnNode.ObjectData = objectData;

			return ReturnNode;
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
		public bool CanAssignArgument(object argument, ObjectNode parameter)
		{
			return parameter.SourceObjectInfo.ObjectDataType.IsAssignableFrom(argument.GetType());
		}

		public bool SetParameter(object argument, ObjectNode parameter)
		{
			if (CanAssignArgument(argument, parameter))
			{
				parameter.ObjectData = argument;
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
	}

	/// <summary>
	///		The collection that is used to generate this node should not be an element of another collection
	/// </summary>
	public class CollectionNode : ObjectNode
	{
		public bool Is_2D { get; private set; }
		public bool DisplayPropertiesAsHeadings { get; set; } = false;
		public List<string> Headings { get; private set; } = new List<string>();
		public List<List<ObjectNode>> Elements { get; private set; } = new List<List<ObjectNode>>();

		/// <summary>
		///		2D data structure functionalities will be implemented later
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="propertyInfo"></param>
		/// <returns></returns>
		internal static new CollectionNode Create(ObjectNode parent, PropertyInfo propertyInfo)
		{
			return new CollectionNode(parent, propertyInfo);
		}
		internal static new CollectionNode Create(Type type)
		{
			return new CollectionNode(type);
		}
		private CollectionNode(ObjectNode parent, PropertyInfo propertyInfo)
			: base(parent, propertyInfo.GetCustomAttribute<Visible>())
		{
			SourceObjectInfo = new DomainModelReferenceInfo(propertyInfo, SourceReferenceType.Property);
		}
		private CollectionNode(Type type) : base(null, null)
		{
			IsEmpty = true;
			SourceObjectInfo = new DomainModelReferenceInfo(type, SourceReferenceType.parameter);
		}

		protected override void LoadObjectData()
		{
			base.LoadObjectData();
			LoadFormattedData();
		}

		private void LoadFormattedData()
		{
			var preFormattedData = (ObjectData as ICollection).ToObjectList();
			var formattedData = new List<object>();
			var list = new List<ObjectNode>();
			// If it is a dictionary
			if (SourceObjectInfo.ObjectDataType.IsDerivedFrom(typeof(IDictionary)))
			{
				Headings.Add("Key");
				Headings.Add("Value");
				Is_2D = true;
				foreach (var key in (ObjectData as IDictionary).Keys)
				{
					list.Add(Create(this, key, new Visible(Header, Description)));
					Elements.Add(list);
				}
				var enumerator = Elements.GetEnumerator();
				enumerator.MoveNext();
				foreach (var value in (ObjectData as IDictionary).Values)
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
					formattedData.Add((element as ICollection).ToObjectList());
				}
				foreach (var row in formattedData)
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
				foreach (var objectData in ObjectData as ICollection)
				{
					var row = new List<ObjectNode>();
					row.Add(ObjectNode.Create(this, objectData, new Visible(Header, Description)));
					Elements.Add(row);
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
			get
			{
				if (Elements.Count == 0)
				{
					LoadObjectData();
				}
				return Elements[row][column];
			}
		}
		public List<ObjectNode> this[int row]
		{
			get
			{
				if (Elements.Count == 0)
				{
					LoadObjectData();
				}
				return Elements[row];
			}
		}
	}

	public class LinqNode
	{
	}
}
