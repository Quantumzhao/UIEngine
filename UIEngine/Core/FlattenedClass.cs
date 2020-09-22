using System;
using System.Collections.Generic;
using System.Text;

namespace UIEngine.Core
{
	/// <summary>
	///		A special class in UIEngine. It will not be interpreted as a tree of nodes
	///		Note: Deprecated
	/// </summary>
	/// <typeparam name="T">The class that has been flattened</typeparam>
	public class FlattenedClass<T>
	{
		internal FlattenedClass() { }
		public List<PseudoProperty<T>> Properties { get; } = new List<PseudoProperty<T>>();
	}

	/// <summary>
	/// 
	/// </summary>
	/// <typeparam name="T">Topmost owner of the property</typeparam>
	public class PseudoProperty<T> : IVisible
	{
		internal PseudoProperty(Func<T, object> accessor, T owner)
		{
			Accessor = accessor;
			Owner = owner;
		}

		public string Name => throw new NotImplementedException();
		public string Description => throw new NotImplementedException();
		public string Header => throw new NotImplementedException();

		internal Func<T, object> Accessor { get; }
		internal T Owner { get; }
	}
}
