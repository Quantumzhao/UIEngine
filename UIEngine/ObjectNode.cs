using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace UIEngine
{
	/* object nodes should never be created or replaced via external assemblies.
	 * object nodes must always maintain a tree data structure.
	 * thus, if an object node wants to point to another object node(not talking about setting another node as child), 
	 * it should actually point to the object data wrapped by that node */
	public class ObjectNode : Node, INotifyPropertyChanged
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
				var objectNode = new ObjectNode(parent, propertyInfo.GetCustomAttribute<VisibleAttribute>());
				objectNode.SourceObjectInfo = new PropertyDomainModelRefInfo(propertyInfo);
				var setter = propertyInfo.SetMethod;
				objectNode.IsReadOnly = !(setter.IsPublic && (setter.GetCustomAttribute<VisibleAttribute>()?.IsEnabled ?? true));

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
		internal static ObjectNode Create(ObjectNode parent, object objectData, VisibleAttribute attribute)
		{
			var objectNode = new ObjectNode(parent, attribute);
			if (parent is CollectionNode)
			{
				objectNode.SourceObjectInfo = new OtherDomainModelRefInfo(objectData.GetType(),
					SourceReferenceType.Indexer);
			}
			else
			{
				objectNode.SourceObjectInfo = new OtherDomainModelRefInfo(objectData.GetType(),
					SourceReferenceType.ReturnValue);
			}
			objectNode._ObjectData = objectData;
			objectNode.TryGetDescriptiveInfo(objectData);
			objectNode.SetBinding(objectData);

			return objectNode;
		}
		/// <summary>
		///		Create from class template. Just a typed placeholder. e.g. parameter and in LINQ local variables
		/// </summary>
		/// <param name="type"></param>
		internal static ObjectNode Create(Type type, DescriptiveInfoAttribute descriptiveInfo)
		{
			var objectNode = new ObjectNode(null, descriptiveInfo);
			objectNode.SourceObjectInfo = new OtherDomainModelRefInfo(type, SourceReferenceType.parameter);
			return objectNode;
		}

		// The basic ctor
		protected ObjectNode(ObjectNode parent, DescriptiveInfoAttribute attribute)
		{
			Parent = parent;
			if (attribute != null)
			{
				if (attribute is VisibleAttribute visible)
				{
					this.AppendVisibleAttribute(visible);
					PreviewExpression = visible.PreviewExpression;
				}
				Name = attribute.Name;
				Header = attribute.Header;
				Description = attribute.Description;
			}
		}

		/* the next node in the syntax tree
		 * The succession should be an empty (but not null) node when first assigned. 
		 * It should be instantiated by InstantiateSuccession method */
		internal Node Succession { get; set; }
		public TypeSystem Type => SourceObjectInfo.ObjectDataType;
		public bool IsValueType => SourceObjectInfo.ObjectDataType.IsValueType;
		internal bool IsEmpty => _ObjectData == null;
		internal DomainModelRefInfo SourceObjectInfo { get; set; }
		public bool IsReadOnly { get; internal set; } = false;
		public bool IsLeaf => Properties?.Count == 0;
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
				if (IsReadOnly && value != _ObjectData)
				{
					_ObjectData = value;
					SetValueToSourceObject();
					InvokePropertyChanged(this, new PropertyChangedEventArgs(nameof(ObjectData)));
				}
			}
		}

		/// <summary>
		///		Retrieves object value of the object node. 
		///		Returns default value if cannot cast object to the given type
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
			if (SourceObjectInfo is PropertyDomainModelRefInfo propertyDomainModelRefInfo)
			{
				// if property info is null, then it's an abstract object, don't load object data
				if (propertyDomainModelRefInfo.PropertyInfo != null)
				{
					_ObjectData = propertyDomainModelRefInfo.PropertyInfo.GetValue(Parent?.ObjectData);
					//Preview = PreviewExpression?.Invoke(ObjectData);
				}

				TryGetDescriptiveInfo(_ObjectData);
				SetBinding(_ObjectData);
			}
		}
		protected virtual void SetValueToSourceObject()
		{
			switch (SourceObjectInfo.SourceReferenceType)
			{
				case SourceReferenceType.Property:
					((PropertyDomainModelRefInfo)SourceObjectInfo).PropertyInfo.SetValue(Parent?.ObjectData, ObjectData);
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
			if (_ObjectData == null)
			{
				LoadObjectData();
			}
			_Properties = SourceObjectInfo.ObjectDataType.ReflectedType.GetVisibleProperties(BindingFlags.Public | BindingFlags.Instance)
				.Select(pi => Create(this, pi)).ToList();
		}
		private void LoadMethods()
		{
			if (_ObjectData == null)
			{
				LoadObjectData();
			}
			_Methods = SourceObjectInfo.ObjectDataType.ReflectedType.GetVisibleMethods(BindingFlags.Public | BindingFlags.Instance)
				.Select(mi => MethodNode.Create(this, mi)).ToList();
		}

		internal ObjectNode FindDecendant(object objectData)
		{
			if (objectData.Equals(_ObjectData))
			{
				return this;
			}
			else if (_Properties != null)
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
		public void SetReferenceTo(ObjectNode objectNode)
		{
			ObjectData = objectNode.ObjectData;
		}

		internal override ObjectNode InstantiateSuccession()
		{
			// if succession is an object node, load and return it
			if (Succession is ObjectNode objectSuccesstion)
			{
				objectSuccesstion.LoadObjectData();
			}
			// if succession is a method node, just return the method node
			// if it has no succession, return this
			return Succession?.InstantiateSuccession() ?? this;
		}

		private void SetBinding(object objectData, INotifyPropertyChanged prevObject = null)
		{
			if (prevObject != objectData)
			{
				if (prevObject != null)
				{
					prevObject.PropertyChanged -= OnPropertyChanged;
				}
				if (objectData is INotifyPropertyChanged notifiable)
				{
					notifiable.PropertyChanged += OnPropertyChanged;
				}
			}

			void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
			{
				var dstProperty = Properties.FirstOrDefault(p => 
					((PropertyDomainModelRefInfo)p.SourceObjectInfo).PropertyName == e.PropertyName);
				if (dstProperty != null)
				{
					dstProperty.Refresh();
					dstProperty.InvokePropertyChanged(dstProperty, 
						new PropertyChangedEventArgs(nameof(ObjectData)));
				}
			}
		}

		private void TryGetDescriptiveInfo(object data)
		{
			if (!string.IsNullOrEmpty(this.Header))
			{
				return;
			}
			else if (data is IVisible visible)
			{
				this.Header = visible.Header;
				this.Name = visible.Name;
				this.Description = visible.Description;
			}
			else if (Misc.ObjectTable.TryGetValue(data, out DescriptiveInfoAttribute descriptiveInfoAttribute))
			{
				this.Header = descriptiveInfoAttribute.Header;
				this.Name = descriptiveInfoAttribute.Name;
				this.Description = descriptiveInfoAttribute.Description;
			}
			else
			{
				this.Header = data.ToString();
			}
		}
	}
}