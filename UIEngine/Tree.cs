/* “这个世界上还有很多我不懂的东西。
 * 敬畏它们。
 * 比如高效的垃圾回收。”
 * ———我说的
 */
using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
namespace UIEngine
{
	// For current stage, UI Engine only supports int, double, string, bool, object and collection
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

		/// <summary>
		///		The currently selected node. 
		///		It can be (instance) method node or object node
		/// 	This property is for LINQ node
		/// </summary>

		public override string ToString() => Header;

		/// <summary>Transform the succession from a template to an instantiated node</summary>
		/// <returns>Object node is the tail node of the syntax tree</returns>
		internal abstract ObjectNode InstantiateSuccession();
	}

	/* object nodes should never be created or replaced via external assemblies.
	 * object nodes must always maintain a tree data structure.
	 * thus, if an object node wants to point to another object node(not talking about setting another node as child), 
	 * it should actually point to the object data wrapped by that node */
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
				objectNode.SourceObjectInfo = new DomainModelReferenceInfo(objectData.GetType(), 
					SourceReferenceType.Indexer);
			}
			else
			{
				objectNode.SourceObjectInfo = new DomainModelReferenceInfo(objectData.GetType(), 
					SourceReferenceType.ReturnValue);
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

		/* the next node in the syntax tree
		 * The succession should be an empty (but not null) node when first assigned. 
		 * It should be instantiated by InstantiateSuccession method */
		internal Node Succession { get; set; }
		public TypeSystem Type => SourceObjectInfo.ObjectDataType;
		public bool IsValueType => SourceObjectInfo.ObjectDataType.IsValueType;
		internal bool IsEmpty => _ObjectData == null;
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
		public delegate void ObjectdataChangeDelegate(object data);
		private object _ObjectData = null;
		public object ObjectData
		{
			get
			{
				if (_ObjectData == null)
				{
					LoadObjectData();
					if (Parent != null)
					{
						Parent.Succession = this;
					}
				}

				return _ObjectData;
			}
			set
			{
				if (CanWrite && value != _ObjectData)
				{
					_ObjectData = value;
					SetValueToSourceObject();
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
					//Preview = PreviewExpression?.Invoke(ObjectData);
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

		/// <summary>
		///		A syntactic sugar for making an object node point to another node
		/// </summary>
		/// <param name="objectNode"></param>
		internal void SetReferenceTo(ObjectNode objectNode)
		{
			ObjectData = objectNode.ObjectData;
		}

		internal override ObjectNode InstantiateSuccession()
		{
			// if succession is an object node, load and return it
			if (Succession is ObjectNode)
			{
				(Succession as ObjectNode).LoadObjectData();
			}
			// if succession is a method node, just return the method node
			// if it has no succession, return this
			return Succession?.InstantiateSuccession() ?? this;
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
			methodNode.Signatures = methodInfo.GetParameters()
				.Select(p => ObjectNode.Create(p.ParameterType)).ToList();
			methodNode.ReturnNode = ObjectNode.Create(methodInfo.ReturnType);
			if (parent != null)
			{
				parent.Succession = methodNode;
			}

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
			// check if any parameter is empty
			if (!Signatures.All(n => !n.IsEmpty))
			{
				var message = "Some parameters are empty";
				Dashboard.OnWarningMessageHappen(this, message);
				throw new InvalidOperationException(message);
			}
			var objectData = _Body.Invoke(
				Parent?.ObjectData,
				Signatures.Select(p => p.InstantiateSuccession().ObjectData).ToArray()
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

		internal override ObjectNode InstantiateSuccession()
		{
			if (ReturnNode.Type.ReflectedType.Equals(typeof(void)))
			{
				return ReturnNode;
			}
			else
			{
				return ReturnNode.InstantiateSuccession();
			}
		}
	}

	/// <summary>
	///		The collection that is used to generate.
	///		this node should not be an element of another collection. 
	///		Only <c>IList</c> is supported at current stage
	/// </summary>
	public class CollectionNode : ObjectNode
	{
		private const string _INVALID_OPERATION_WARNING = 
			"Collection node is not supported in a LINQ expression";
		public bool Is_2D { get; private set; }
		public bool DisplayPropertiesAsHeadings { get; set; } = false;
		public List<string> Headings { get; private set; } = new List<string>();
		public List<List<ObjectNode>> Elements { get; private set; } = new List<List<ObjectNode>>();
		public TypeSystem ElementType { get; private set; }

		#region LINQ Functionalities
		public ForEachNode ForEachExpression { get; private set; }
		public SelectNode SelectExpression { get; private set; }
		public SortNode SortExpression { get; private set; }
		public WhereNode WhereExpression { get; set; }
		#endregion

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
		/// <summary>
		///		For LINQ expression generated collection
		/// </summary>
		/// <param name="collection"></param>
		/// <returns></returns>
		internal static CollectionNode Create(List<ObjectNode> collection)
		{
			var collectionNode = new CollectionNode(typeof(List<ObjectNode>));
			foreach (var element in collection)
			{
				var list = new List<ObjectNode> { element };
				collectionNode.Elements.Add(list);
			}
			return collectionNode;
		}
		private CollectionNode(ObjectNode parent, PropertyInfo propertyInfo)
			: base(parent, propertyInfo.GetCustomAttribute<Visible>())
		{
			SourceObjectInfo = new DomainModelReferenceInfo(propertyInfo, SourceReferenceType.Property);
			Initialize();
		}
		private CollectionNode(Type type) : base(null, null)
		{
			SourceObjectInfo = new DomainModelReferenceInfo(type, SourceReferenceType.parameter);
			Initialize();
		}
		private void Initialize()
		{
			ElementType = TypeSystem.ToRestrictedType(SourceObjectInfo.ObjectDataType.ReflectedType.GenericTypeArguments[0]);
			ForEachExpression = ForEachNode.Create(this);
			SelectExpression = SelectNode.Create(this);
			SortExpression = SortNode.Create(this);
			WhereExpression = WhereNode.Create(this);
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
				Succession = Elements[row][column];
				return Succession as ObjectNode;
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

		internal override ObjectNode InstantiateSuccession()
		{
			Dashboard.OnWarningMessageHappen(this, _INVALID_OPERATION_WARNING);
			return null;
		}
	}

	// nested Linq expression should not be allowed. 
	// i.e. c0.Select(c1 => c1.Where(c2 => c2.p0).First());
	public abstract class LinqNode : Node
	{
		private const string _INVALID_OPERATION_WARNING = "LINQ nodes do not have succession";
		internal protected LinqNode(CollectionNode collection)
		{
			Collection = collection;
		}

		public CollectionNode Collection { get; private set; }
		internal protected ObjectNode Enumerator { get; set; }
		public CollectionNode ReturnCollectionNode { get; private set; }
		protected override string Preview { get => throw new NotImplementedException(); 
			set => throw new NotImplementedException(); }
		internal abstract CollectionNode Execute();
		internal abstract bool IsSatisfySignature { get; }
		internal override ObjectNode InstantiateSuccession()
		{
			Dashboard.OnWarningMessageHappen(this, _INVALID_OPERATION_WARNING);
			return null;
		}
		public abstract void AddPredicate(ObjectNode predicate);
	}

	public class ForEachNode : LinqNode
	{
		private const string _SINGLE_PREDICATE_WARNING = "For each node has only one predicate";
		public static ForEachNode Create(CollectionNode collection) => new ForEachNode(collection);
		private List<ObjectNode> _Predicate { get; } = new List<ObjectNode>();

		private ForEachNode(CollectionNode collection) : base(collection) 
		{
			// For each only has one predicate
			_Predicate.Add(ObjectNode.Create(collection.ElementType.ReflectedType));
		}

		internal override bool IsSatisfySignature => true;

		internal override CollectionNode Execute()
		{
			foreach (var list in Collection.Elements)
			{
				foreach (var enumerator in list)
				{
					_Predicate[0].SetReferenceTo(enumerator);
					_Predicate[0].InstantiateSuccession();
				}
			}

			return null;
		}

		public override void AddPredicate(ObjectNode predicate)
		{
			Dashboard.OnWarningMessageHappen(this, _SINGLE_PREDICATE_WARNING);
			throw new InvalidOperationException(_SINGLE_PREDICATE_WARNING);
		}
	} 

	public class WhereNode : LinqNode
	{
		private const string _INVALID_RETURN_TYPE = "Current expression does not qualify return type requirement";
		public static WhereNode Create(CollectionNode collection) => new WhereNode(collection);
		private WhereNode(CollectionNode collection) : base(collection) { }

		private static readonly Func<bool, bool, bool> And = (left, right) => left && right;
		private static readonly Func<bool, bool, bool> Or = (left, right) => left || right;
		private static readonly Func<bool, bool> Not = value => !value;

		internal override bool IsSatisfySignature => throw new NotImplementedException();

		/* The set of conditions that the where predicate describes.
		 * They are connected by logic operators, e.g. cond1 AND cond2 OR NOT cond3. 
		 * The initial (template) conditions are syntax trees of object nodes, once the root nodes (i.e. during execution) are assigned,
		 * the parser will give off their return value and replace themselves in the key value pairs*/
		private readonly Queue<KeyValuePair<int, object>> _Predicates = new Queue<KeyValuePair<int, object>>();
		/// <summary>
		///		Execute the predicate
		/// </summary>
		/// <returns>If the expression is invalid, it will give off a warning and just return the collection itself</returns>
		internal override CollectionNode Execute()
		{
			if (!IsSatisfySignature)
			{
				Dashboard.OnWarningMessageHappen(this, _INVALID_RETURN_TYPE);
				return Collection;
			}
			var ret = new List<ObjectNode>();
			foreach (var list in Collection.Elements)
			{
				foreach (var enumerator in list)
				{
					while (Parser.IsReadyToBeParsed(_Predicates.Peek()))
					{
						var pair = _Predicates.Dequeue();
						ObjectNode condition = pair.Value as ObjectNode;
						condition.SetReferenceTo(enumerator);
						bool result = (bool)condition.InstantiateSuccession().ObjectData;
						_Predicates.Enqueue(new KeyValuePair<int, object>(pair.Key, result));
					}
					if (Parser.Execute(_Predicates))
					{
						ret.Add(enumerator);
					}
				}
			}
			return CollectionNode.Create(ret);
		}

		public override void AddPredicate(ObjectNode predicate)
		{
			_Predicates.Enqueue(new KeyValuePair<int, object>(3, predicate));
		}

		public void AddOperator(LogicOperators logicOperator)
		{
			switch (logicOperator)
			{
				case LogicOperators.Add:
					_Predicates.Enqueue(new KeyValuePair<int, object>(0, And));
					break;
				case LogicOperators.Or:
					_Predicates.Enqueue(new KeyValuePair<int, object>(1, Or));
					break;
				case LogicOperators.Not:
					_Predicates.Enqueue(new KeyValuePair<int, object>(2, Not));
					break;
				default:
					break;
			}
		}

		public enum LogicOperators
		{
			Add = 0, 
			Or = 1, 
			Not = 2
			// "Condition" has an implicit value of 3
		}

		private static class Parser
		{
			internal static bool Execute(Queue<KeyValuePair<int, object>> conditions)
			{
				int i = 0;
				var expression = new FunctionBuilder();
				expression.AddVariable<bool>();
				throw new NotImplementedException();

			}

			internal static bool IsReadyToBeParsed(KeyValuePair<int, object> condition)
			{
				if (condition.Key != 3)
				{
					return true;
				}
				else
				{
					return condition.Value is bool;
				}
			}
		}
	}

	public class SelectNode : LinqNode
	{
		public static SelectNode Create(CollectionNode collection) => new SelectNode(collection);

		private SelectNode(CollectionNode collection) : base(collection) { }

		internal override bool IsSatisfySignature => throw new NotImplementedException();

		internal override CollectionNode Execute()
		{
			throw new NotImplementedException();
		}

		public override void AddPredicate(ObjectNode predicate)
		{
			throw new NotImplementedException();
		}
	}

	public class SortNode : LinqNode
	{
		public static SortNode Create(CollectionNode collection) => new SortNode(collection);

		private SortNode(CollectionNode collection) : base(collection) { }

		internal override bool IsSatisfySignature => throw new NotImplementedException();

		internal override CollectionNode Execute()
		{
			throw new NotImplementedException();
		}

		public override void AddPredicate(ObjectNode predicate)
		{
			throw new NotImplementedException();
		}
	}
}
