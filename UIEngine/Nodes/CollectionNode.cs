using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using UIEngine.Core;

namespace UIEngine.Nodes
{
	/// <summary>
	///		The collection that is used to generate.
	///		This implementation solely relies on <see cref="IEnumerable{T}"/>, 
	///		Therefore it does not support multi-dimensional data structure
	/// </summary>
	public class CollectionNode : ObjectNode, INotifyCollectionChanged, INotifyPropertyChanged, IEnumerable<ObjectNode>, IEnumerable
	{
		private const string _INVALID_OPERATION_WARNING =
			"Collection node is not supported in a LINQ expression";
		private const string _NO_SUCH_NODE =
			"The specified node does not exist";

		public event NotifyCollectionChangedEventHandler CollectionChanged;

		private ObservableCollection<ObjectNode> _Collection = null;
		internal static List<Type> NotEnumerables { get; } = new List<Type> { typeof(string) };
		/// <summary>
		///		Unless you know what you are doing, otherwise don't use this property
		/// </summary>
		public ObservableCollection<ObjectNode> Collection
		{
			get
			{
				if (_Collection == null)
				{
					this.LoadObjectData();
				}
				return _Collection;
			}
		}
		internal Type ElementType { get; private set; }
		public int Count
		{
			get
			{
				if (_Collection == null)
				{
					this.LoadObjectData();
				}
				return Collection.Count;
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
		private CollectionNode(Type type) : base(null)
		{
			SourceObjectInfo = new OtherDomainModelRefInfo(type, SourceReferenceType.parameter);
			Initialize();
		}
		private void Initialize()
		{
			ElementType = SourceObjectInfo.ReflectedType.GenericTypeArguments[0];
			// disable for now
			//ForEachExpression = ForEachNode.Create(this);
			//SelectExpression = SelectNode.Create(this);
			//SortExpression = SortNode.Create(this);
			//WhereExpression = WhereNode.Create(this);
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
						var objectNode = ObjectNode.Create(this, e.NewItems[0]);
						Add(objectNode);
						break;

					case NotifyCollectionChangedAction.Remove:
						Remove(e.OldItems[0]);
						break;

					case NotifyCollectionChangedAction.Replace:
					case NotifyCollectionChangedAction.Reset:
						throw new NotImplementedException();

					case NotifyCollectionChangedAction.Move:
						Collection.Clear();
						break;

					default:
						break;
				}
			}
		}

		private void LoadFormattedData()
		{
			_Collection = new ObservableCollection<ObjectNode>();

			foreach (var objectData in ObjectData as IEnumerable)
			{
				Collection.Add(ObjectNode.Create(this, objectData));
			}
		}

		// assume the collection object is not an element of another collection
		internal void SetValueToSourceCollectionElement(object oldValue, object newValue)
		{
			throw new NotImplementedException();
		}

		internal void ForEach(Action<ObjectNode> operation)
		{
			foreach (var item in Collection)
			{
				operation(item);
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
			Collection.Add(objectNode);
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
				NotifyCollectionChangedAction.Add, objectNode));
		}

		public void Remove(ObjectNode objectNode)
		{
			Collection.Remove(objectNode);
			CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
				NotifyCollectionChangedAction.Remove, objectNode));
		}
		private void Remove(object NodeWithObjectData)
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

		public IEnumerator<ObjectNode> GetEnumerator() => Collection.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
	}
}