using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace UIEngine
{
	/// <summary>
	///		The collection that is used to generate.
	///		this node should not be an element of another collection. 
	///		Only <c>IList</c> is supported at current stage
	/// </summary>
	public class CollectionNode : ObjectNode, INotifyCollectionChanged, INotifyPropertyChanged, IEnumerable<ObjectNode>
	{
		private const string _INVALID_OPERATION_WARNING =
			"Collection node is not supported in a LINQ expression";
		private const string _DIMENSION_ERROR =
			"Applying 1D operations to 2D collections or v.v. ";
		private const string _NO_SUCH_NODE =
			"The specified node does not exist";

		public event NotifyCollectionChangedEventHandler CollectionChanged;

		public bool Is_2D { get; private set; }
		public bool DisplayPropertiesAsHeadings { get; set; } = false;
		// This is for dictionary and 2D collections
		public List<string> Headings { get; private set; } = new List<string>();

		private ObservableCollection<ObservableCollection<ObjectNode>> _Collection2D = null;

		/// <summary>
		///		Unless you know what you are doing, otherwise don't use this property
		/// </summary>
		public ObservableCollection<ObservableCollection<ObjectNode>> Collection2D
		{
			get
			{
				if (!Is_2D)
				{
					throw new InvalidOperationException(_DIMENSION_ERROR);
				}
				else
				{
					if (_Collection2D == null)
					{
						this.LoadObjectData();
					}
					return _Collection2D;
				}				
			}
		}

		private ObservableCollection<ObjectNode> _Collection = null;
		/// <summary>
		///		Unless you know what you are doing, otherwise don't use this property
		/// </summary>
		public ObservableCollection<ObjectNode> Collection
		{
			get
			{
				if (Is_2D)
				{
					throw new InvalidOperationException(_DIMENSION_ERROR);
				}
				else
				{
					if (_Collection == null)
					{
						this.LoadObjectData();
					}
					return _Collection;
				}				
			}
		}
		public TypeSystem ElementType { get; private set; }
		/// <summary>
		///		If the collection is 2D, returns the count for its rows. 
		///		If it's 1D, returns the count for its elements. 
		/// </summary>
		public int Count
		{
			get
			{
				this.LoadObjectData();
				return Is_2D ? Collection2D.Count : Collection.Count;
			}
		}

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
		///		For LINQ expression generated collection. 
		///		Supports only 1D collection in current phase
		/// </summary>
		/// <param name="collection"></param>
		/// <returns></returns>
		internal static CollectionNode Create(ObservableCollection<ObjectNode> collection)
		{
			var collectionNode = new CollectionNode(typeof(ObservableCollection<ObjectNode>));
			foreach (var element in collection)
			{
				collectionNode.Collection.Add(element);
			}
			return collectionNode;
		}
		private CollectionNode(ObjectNode parent, PropertyInfo propertyInfo)
			: base(parent, propertyInfo.GetCustomAttribute<VisibleAttribute>())
		{
			SourceObjectInfo = new PropertyDomainModelRefInfo(propertyInfo);
			Initialize();
		}
		private CollectionNode(Type type) : base(null, null)
		{
			SourceObjectInfo = new OtherDomainModelRefInfo(type, SourceReferenceType.parameter);
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
				Is_2D = false;
				//_Collection = new ObservableCollection<ObjectNode>();
			}
			// if it is a 2D collection, or things other than list ...
			// will be changed in the future
			else
			{
				Is_2D = true;
				//_Collection2D = new ObservableCollection<ObservableCollection<ObjectNode>>();
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
				notifiable.CollectionChanged += OnCollectionChanged;
			}

			void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
			{
				switch (e.Action)
				{
					case NotifyCollectionChangedAction.Add:
						var objectNode = ObjectNode.Create(this, e.NewItems[0], 
							new VisibleAttribute(Header, Description));
						Add(objectNode);
						break;

					case NotifyCollectionChangedAction.Remove:
						Remove(e.OldItems[0]);
						break;

					case NotifyCollectionChangedAction.Replace:
					case NotifyCollectionChangedAction.Reset:
					case NotifyCollectionChangedAction.Move:
						throw new NotImplementedException();
					default:
						break;
				}
			}
		}

		private void LoadFormattedData()
		{
			var preFormattedData = (ObjectData as ICollection).ToObjectList();
			var formattedData = new List<object>();
			var list = new ObservableCollection<ObjectNode>();
			// If it is a dictionary
			if (SourceObjectInfo.ObjectDataType.IsDerivedFrom(typeof(IDictionary)))
			{
				Headings.Add("Key");
				Headings.Add("Value");
				foreach (var key in (ObjectData as IDictionary).Keys)
				{
					list.Add(Create(this, key, new VisibleAttribute(Header, Description)));
					Collection2D.Add(list);
				}
				var enumerator = Collection2D.GetEnumerator();
				enumerator.MoveNext();
				foreach (var value in (ObjectData as IDictionary).Values)
				{
					enumerator.Current.Add(ObjectNode.Create(this, value, new VisibleAttribute(Header, Description)));
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
				_Collection2D = new ObservableCollection<ObservableCollection<ObjectNode>>();
				foreach (var row in formattedData)
				{
					var elementRow = new ObservableCollection<ObjectNode>();
					foreach (var column in row as ICollection)
					{
						elementRow.Add(ObjectNode.Create(this, column, new VisibleAttribute(Header, Description)));
					}
					Collection2D.Add(elementRow);
				}
			}
			// If it's a one dimensional list (or set, stack ...)
			else
			{
				_Collection = new ObservableCollection<ObjectNode>();
				foreach (var objectData in ObjectData as ICollection)
				{
					Collection.Add(ObjectNode.Create(this, objectData, new VisibleAttribute(Header, Description)));
				}
			}
		}

		// assume the collection object is not an element of another collection
		internal void SetValueToSourceCollectionElement(object oldValue, object newValue)
		{
			throw new NotImplementedException();
		}

		internal void ForEach(Action<ObjectNode> operation)
		{
			if (Is_2D)
			{
				foreach (var row in Collection2D)
				{
					foreach (var item in row)
					{
						operation(item);
					}
				}
			}
			else
			{
				foreach (var item in Collection)
				{
					operation(item);
				}
			}
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
		public ObjectNode this[int index]
		{
			get
			{
				if (Collection.Count == 0)
				{
					LoadObjectData();
				}
				Succession = Collection[index];
				return Succession as ObjectNode;
			}
		}

		internal override ObjectNode InstantiateSuccession()
		{
			Dashboard.RaiseWarningMessage(this, _INVALID_OPERATION_WARNING);
			return null;
		}

		public void Add(ObjectNode objectNode)
		{
			if (Is_2D)
			{
				throw new NotImplementedException();
			}
			else
			{
				Collection.Add(objectNode);
				CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
					NotifyCollectionChangedAction.Add, objectNode));
			}
		}

		public void Remove(ObjectNode objectNode)
		{
			if (Is_2D)
			{
				throw new NotImplementedException();
			}
			else
			{
				Collection.Remove(objectNode);
				CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
					NotifyCollectionChangedAction.Remove, objectNode));
			}
		}
		private void Remove(object NodeWithObjectData)
		{
			if (Is_2D)
			{
				throw new NotImplementedException();
			}
			else
			{
				try
				{
					var objNode = Collection.Single(node => node.ObjectData == NodeWithObjectData);
					CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
						NotifyCollectionChangedAction.Remove, objNode));
				}
				catch (InvalidOperationException)
				{
					Dashboard.RaiseWarningMessage(this, _NO_SUCH_NODE);
				}
			}
		}

		public IEnumerator<ObjectNode> GetEnumerator()
		{
			if (Is_2D)
			{
				throw new NotImplementedException();
			}
			else
			{
				return Collection.GetEnumerator();
			}
		}

		IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
	}
}