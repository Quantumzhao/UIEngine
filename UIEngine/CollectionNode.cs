using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;

namespace UIEngine
{
	/// <summary>
	///		The collection that is used to generate.
	///		this node should not be an element of another collection. 
	///		Only <c>IList</c> is supported at current stage
	/// </summary>
	public class CollectionNode : ObjectNode, INotifyCollectionChanged
	{
		private const string _INVALID_OPERATION_WARNING =
			"Collection node is not supported in a LINQ expression";
		private const string _DIMENSION_ERROR =
			"Applying 1D operations to 2D collections or v.v. ";

		public event NotifyCollectionChangedEventHandler CollectionChanged;

		public bool Is_2D => Collection2D == null;
		public bool DisplayPropertiesAsHeadings { get; set; } = false;
		public List<string> Headings { get; private set; } = new List<string>();

		private List<List<ObjectNode>> _Collection2D = null;

		/// <summary>
		///		Unless you know what you are doing, otherwise don't use this property
		/// </summary>
		public List<List<ObjectNode>> Collection2D
		{
			get
			{
				if (_Collection2D == null)
				{
					throw new InvalidOperationException(_DIMENSION_ERROR);
				}
				else
				{
					return _Collection2D;
				}				
			}
			private set => _Collection2D = value;
		}

		private List<ObjectNode> _Collection = null;
		/// <summary>
		///		Unless you know what you are doing, otherwise don't use this property
		/// </summary>
		public List<ObjectNode> Collection
		{
			get
			{
				if (_Collection == null)
				{
					throw new InvalidOperationException(_DIMENSION_ERROR);
				}
				else
				{
					return _Collection;
				}				
			}
			private set => _Collection = value;
		}
		public TypeSystem ElementType { get; private set; }
		public int Count => Is_2D ? Collection2D.Count : Collection.Count;

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
		internal static CollectionNode Create(Type type)
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
				collectionNode.Collection2D.Add(list);
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

			// If it is a 1D collection
			if (SourceObjectInfo.ObjectDataType.IsDerivedFrom(typeof(IList)))
			{
				_Collection = new List<ObjectNode>();
			}
			// if it is a 2D collection, or things other than list ...
			// will be changed in the future
			else
			{
				_Collection2D = new List<List<ObjectNode>>();
			}
		}

		protected override void LoadObjectData()
		{
			base.LoadObjectData();
			SetBinding();
			LoadFormattedData();
		}

		private void SetBinding()
		{
			if (ObjectData is INotifyCollectionChanged notifiable)
			{
				notifiable.CollectionChanged += this.CollectionChanged;
			}
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
				foreach (var key in (ObjectData as IDictionary).Keys)
				{
					list.Add(Create(this, key, new Visible(Header, Description)));
					Collection2D.Add(list);
				}
				var enumerator = Collection2D.GetEnumerator();
				enumerator.MoveNext();
				foreach (var value in (ObjectData as IDictionary).Values)
				{
					enumerator.Current.Add(Create(this, value, new Visible(Header, Description)));
					enumerator.MoveNext();
				}
			}
			// If it's a two dimensional data structure
			else if (Is_2D)
			{
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
					Collection2D.Add(elementRow);
				}
			}
			// If it's a one dimensional list (or set, stack ...)
			else
			{
				foreach (var objectData in ObjectData as ICollection)
				{
					var row = new List<ObjectNode>();
					row.Add(ObjectNode.Create(this, objectData, new Visible(Header, Description)));
					Collection2D.Add(row);
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
				if (Collection2D.Count == 0)
				{
					LoadObjectData();
				}
				Succession = Collection2D[row][column];
				return Succession as ObjectNode;
			}
		}
		public List<ObjectNode> this[int row]
		{
			get
			{
				if (Collection2D.Count == 0)
				{
					LoadObjectData();
				}
				return Collection2D[row];
			}
		}

		internal override ObjectNode InstantiateSuccession()
		{
			Dashboard.OnWarningMessageHappen(this, _INVALID_OPERATION_WARNING);
			return null;
		}
	}
}