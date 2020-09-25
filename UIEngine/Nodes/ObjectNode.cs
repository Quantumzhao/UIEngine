using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using UIEngine;
using UIEngine.Core;

namespace UIEngine.Nodes
{
	/* object nodes should never be created or replaced via external assemblies.
	 * object nodes must always maintain a tree data structure.
	 * thus, if an object node wants to point to another object node(not referring setting another node as child), 
	 * it should actually point to the object data wrapped by that node */
	public class ObjectNode : Node, INotifyPropertyChanged
	{
		private const string _ILLEGAL_CTRL_STATE = "This node doesn't accept input, therefore it is always disabled. ";
		private const string _ENUM_ASSERTION_FAILURE = "This node is not an Enum type";

		/// <summary>
		///		Create from property
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="propertyInfo"></param>
		/// <returns></returns>
		internal static ObjectNode Create(ObjectNode parent, PropertyInfo propertyInfo)
		{
			if (typeof(IEnumerable).IsAssignableFrom(propertyInfo.PropertyType) && !CollectionNode.NotEnumerables.Contains(propertyInfo.PropertyType))
			{
				return CollectionNode.Create(parent, propertyInfo);
			}
			else
			{
				var objectNode = new ObjectNode(parent, propertyInfo.GetCustomAttribute<VisibleAttribute>());
				objectNode.SourceObjectInfo = new PropertyDomainModelRefInfo(propertyInfo);
				var setter = propertyInfo.SetMethod;
				objectNode.IsReadOnly = setter == null || !setter.IsPublic || !(setter.GetCustomAttribute<VisibleAttribute>()?.IsFeatureEnabled ?? true);
				objectNode._IsEnabled = !objectNode.IsReadOnly;

				return objectNode;
			}
		}
		/// <summary>
		///		Create from anonymous object. i.e. elements in a collection
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="objectData"></param>
		/// <returns></returns>
		internal static ObjectNode Create(ObjectNode parent, object objectData)
		{
			var objectNode = new ObjectNode(parent);
			if (parent is CollectionNode)
			{
				objectNode.SourceObjectInfo = new OtherDomainModelRefInfo(objectData.GetType(),
					SourceReferenceType.Enumerator);
			}
			// questionable, consider revision
			else
			// parent is null, in the case of return value
			{
				objectNode.SourceObjectInfo = new OtherDomainModelRefInfo(objectData.GetType(),
					SourceReferenceType.ReturnValue);
			}
			objectNode._ObjectData = objectData;
			objectNode.TryGetDescriptiveInfo();
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
		protected ObjectNode(ObjectNode parent, DescriptiveInfoAttribute attribute = null)
		{
			Parent = parent;
			TryGetDescriptiveInfo(attribute);
		}

		internal bool IsEmpty => _ObjectData == null;
		internal DomainModelRefInfo SourceObjectInfo { get; set; }
		// IsReadOnly => !IsEnabled
		public bool IsReadOnly { get; private set; }
		public bool IsLeaf => Properties?.Count == 0;

		private bool _IsEnabled;
		/// <summary>
		///		By default, it is the same value as <see cref="IsReadOnly"/>. 
		///		When it is set to true, the object node can be read and written. 
		///		When false, the object node is still accessible but becomes read only, 
		///		similar to conventional user controls
		/// </summary>
		public bool IsEnabled 
		{ 
			get => _IsEnabled;
			set
			{
				if (!IsReadOnly)
				{
					_IsEnabled = value;
				}
				else
				{
					Dashboard.RaiseWarningMessage(this, _ILLEGAL_CTRL_STATE);
				}
			}
		}
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
				if (!IsReadOnly)
				{
					if (value != _ObjectData)
					{
						_ObjectData = value;
						SetValueToSourceObject();
						InvokePropertyChanged(this, new PropertyChangedEventArgs(nameof(ObjectData)));
					}
				}
				else
				{
					throw new InvalidOperationException();
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

		/// <summary>
		///		Finds the property with the specified name. 
		/// </summary>
		/// <remarks>Properties with no names attached cannot be found with this function</remarks>
		/// <param name="name">property name</param>
		/// <returns>null if not found</returns>
		public virtual ObjectNode this[string name] => Properties.SingleOrDefault(p => p.Name == name);

		public string TypeName => SourceObjectInfo.ReflectedType.ToString();

		#region Type related utilities
		/// <summary>
		///		A primitive type means, int, float, byte
		///		and their derivatives, bool and string, char, decimal 
		/// </summary>
		/// <returns>true if it is a primitive type </returns>
		public bool IsPrimitiveType() =>
			IsNumeric() ||
			IsBoolean() ||
			IsText();

		/// <summary>
		///		States if it is int, byte and their derivatives
		/// </summary>
		public bool IsDiscreteNumeric() =>
			IsTypeOf<int>() ||
			IsTypeOf<ushort>() ||
			IsTypeOf<uint>() ||
			IsTypeOf<ulong>() ||
			IsTypeOf<short>() ||
			IsTypeOf<long>() ||
			IsTypeOf<sbyte>() ||
			IsTypeOf<byte>();

		/// <summary>
		///		States if it is a number
		/// </summary>
		public bool IsNumeric() =>
			IsDiscreteNumeric()||
			IsTypeOf<float>()  ||
			IsTypeOf<double>() ||
			IsTypeOf<decimal>();

		/// <summary>
		///		States if it is bool
		/// </summary>
		public bool IsBoolean() =>
			IsTypeOf<bool>();

		/// <summary>
		///		States if it is string or char
		/// </summary>
		public bool IsText() =>
			IsTypeOf<string>() ||
			IsTypeOf<char>();

		/// <summary>
		///		States if the node is of <see cref="Enum"/> type. 
		///		Can also state if the <see cref="Enum"/> is marked with <see cref="FlagsAttribute"/>
		/// </summary>
		/// <param name="mode"></param>
		/// <returns>
		///		true if it is an <see cref="Enum"/>, or it is an <see cref="Enum"/> 
		///		with <see cref="FlagsAttribute"/>, if specified
		///	</returns>
		///	<remarks>
		///		Throws <see cref="ArgumentException"/> if it is not an <see cref="Enum"/> 
		///		but specifies the selection mode
		///	</remarks>
		/// <exception cref="ArgumentException"/>
		public bool IsEnum(SelectionMode? mode = null)
		{
			var isEnum = IsTypeOf<Enum>();
			if (mode == null)
			{
				return isEnum;
			}
			else if (!isEnum)
			{
				throw new ArgumentException(_ENUM_ASSERTION_FAILURE);
			}
			else
			{
				var isMS = SourceObjectInfo.ReflectedType.GetCustomAttribute(typeof(FlagsAttribute)) == null;
				return mode == SelectionMode.MultiSelect && isMS;
			}
		}

		/// <summary>
		///		Use this to state if <see cref="ObjectData"/> is type of <see cref="T"/> to avoid loading it. 
		/// </summary>
		/// <typeparam name="T">The comparing type</typeparam>
		/// <returns>true if the <see cref="ObjectData"/> is type of <see cref="T"/></returns>
		public bool IsTypeOf<T>() => SourceObjectInfo.ReflectedType == typeof(T);
		/// <summary>
		///		Use this to state if <see cref="ObjectData"/> is derived from <see cref="T"/> to avoid loading it. 
		/// </summary>
		/// <typeparam name="T">The comparing type</typeparam>
		/// <returns>true if the <see cref="ObjectData"/> is derived from <see cref="T"/></returns>
		public bool IsDerivedFrom<T>() => typeof(T).IsAssignableFrom(SourceObjectInfo.ReflectedType);
		public bool IsAssignableFrom<T>() => IsAssignableFrom(typeof(T));
		internal bool IsAssignableFrom(Type type) => SourceObjectInfo.ReflectedType.IsAssignableFrom(type);
		#endregion

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

				TryGetDescriptiveInfo();
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

				case SourceReferenceType.Enumerator:
					if (Parent.ObjectData is IList)
					{
						var collection = Parent.ObjectData as IList;
						collection[collection.IndexOf(ObjectData)] = ObjectData;
					}
					else if (Parent.ObjectData is IEnumerable<object> enumerable)
					{
						throw new InvalidOperationException("readonly");
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
			_Properties = SourceObjectInfo.ReflectedType.GetVisibleProperties(BindingFlags.Public | BindingFlags.Instance)
				.Select(pi => Create(this, pi)).ToList();
		}
		private void LoadMethods()
		{
			if (_ObjectData == null)
			{
				LoadObjectData();
			}
			_Methods = SourceObjectInfo.ReflectedType.GetVisibleMethods(BindingFlags.Public | BindingFlags.Instance)
				.Select(mi => MethodNode.Create(this, mi)).ToList();
		}

		[Obsolete]
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

			/* Great, now I have no idea what it means */

			// if succession is a method node, just return the method node
			// if it has no succession, return this
			return Succession?.InstantiateSuccession() ?? this;
		}

		// magic, I don't want to touch it any more
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

		// This method is for accessing info via compile-time attributes
		private void TryGetDescriptiveInfo(DescriptiveInfoAttribute attribute)
		{
			if (attribute != null)
			{
				Name = attribute.Name;
				Header = attribute.Header;
				Description = attribute.Description;
				if (attribute is VisibleAttribute visible)
				{
					PreviewExpression = visible.PreviewExpression;
					IsEnabled = visible.IsControlEnabled;
				}
			}
			else
			{
				Name = Misc.GenerateNameForObjectNode();
			}
		}
		// This is for accessing run-time info (i.e. interfaces and object table)
		private void TryGetDescriptiveInfo()
		{
			if (_ObjectData == null)
			{
				return;
			}
			else if (_ObjectData is IVisible visible)
			{
				this.Header = visible.Header;
				this.Name = visible.Name;
				this.Description = visible.Description;
			}
			else if (Misc.ObjectTable.TryGetValue(_ObjectData, out DescriptiveInfoAttribute descriptiveInfoAttribute))
			{
				this.Header = descriptiveInfoAttribute.Header;
				this.Name = descriptiveInfoAttribute.Name;
				this.Description = descriptiveInfoAttribute.Description;
				this.IsEnabled = (descriptiveInfoAttribute as VisibleAttribute).IsControlEnabled;
			}
			else if (string.IsNullOrEmpty(Header))
			{
				this.Header = _ObjectData.ToString();
				this.Name = Misc.GenerateNameForObjectNode();
			}
		}
	}
}