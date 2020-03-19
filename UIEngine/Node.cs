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
using System.ComponentModel;
using System.Collections.Specialized;
using System.Collections.ObjectModel;

namespace UIEngine
{
	// For current stage, UI Engine only supports int, double, string, bool, object and collection
	public abstract class Node : INotifyPropertyChanged
	{
		/// <summary>
		///		The name of the property if the object node is created out of a property. 
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

		public event PropertyChangedEventHandler PropertyChanged;

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
		/// <returns>Object node is the leaf of the syntax tree</returns>
		internal abstract ObjectNode InstantiateSuccession();

		protected void InvokePropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			PropertyChanged?.Invoke(sender, e);
		}
	}




	// nested Linq expression should not be allowed. 
	// i.e. c0.Select(c1 => c1.Where(c2 => c2.p0).First());
	public abstract class LinqNode : Node
	{
		private const string _INVALID_OPERATION_WARNING = "LINQ nodes do not have succession";
		internal protected LinqNode(CollectionNode collection)
		{
			SourceCollection = collection;
		}

		public CollectionNode SourceCollection { get; private set; }
		internal protected ObjectNode Enumerator { get; set; }
		public CollectionNode ReturnCollectionNode { get; private set; }
		protected override string Preview { get => throw new NotImplementedException(); 
			set => throw new NotImplementedException(); }
		internal abstract CollectionNode Execute();
		internal abstract bool IsSatisfySignature { get; }
		internal override ObjectNode InstantiateSuccession()
		{
			Dashboard.RaiseWarningMessage(this, _INVALID_OPERATION_WARNING);
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
			_Predicate.Add(ObjectNode.Create(collection.ElementType.ReflectedType, new VisibleAttribute(collection.Name)));
		}

		internal override bool IsSatisfySignature => true;

		internal override CollectionNode Execute()
		{
			foreach (var list in SourceCollection.Collection2D)
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
			Dashboard.RaiseWarningMessage(this, _SINGLE_PREDICATE_WARNING);
			throw new InvalidOperationException(_SINGLE_PREDICATE_WARNING);
		}
	} 

	public class WhereNode : LinqNode
	{
		private const string _INVALID_RETURN_TYPE = "Current expression does not qualify return type requirement";
		public static WhereNode Create(CollectionNode collection) => new WhereNode(collection);
		private WhereNode(CollectionNode collection) : base(collection) { }

		//private static readonly Func<bool, bool, bool> And = (left, right) => left && right;
		//private static readonly Func<bool, bool, bool> Or = (left, right) => left || right;
		//private static readonly Func<bool, bool> Not = value => !value;

		internal override bool IsSatisfySignature => throw new NotImplementedException();

		/* The set of conditions that the where predicate describes.
		 * They are connected by logic operators, e.g. cond1 AND cond2 OR NOT cond3. 
		 * The initial (template) conditions are syntax trees of object nodes, once the root nodes (i.e. during execution) are assigned,
		 * the execution will give off and replace with the return values 
		 * and then pass the boolean expression to the parser to get the final result */
		private readonly Queue<object> _Predicates = new Queue<object>();
		/// <summary>
		///		Execute the predicate
		/// </summary>
		/// <returns>If the expression is invalid, it will give off a warning and just return the collection itself</returns>
		internal override CollectionNode Execute()
		{
			if (!IsSatisfySignature)
			{
				Dashboard.RaiseWarningMessage(this, _INVALID_RETURN_TYPE);
				return SourceCollection;
			}
			var ret = new ObservableCollection<ObjectNode>();
			SourceCollection.ForEach(enumerator => {
				while (!(_Predicates.Peek() is bool))
				{
					if (_Predicates.Peek() is LogicOperators)
					{
						continue;
					}
					else
					{
						// executing and replacing a single condition with its return value
						ObjectNode condition = _Predicates.Dequeue() as ObjectNode;
						condition.SetReferenceTo(enumerator);
						_Predicates.Enqueue((bool)condition.InstantiateSuccession().ObjectData);
					}
				}

				if (Parser.Execute(_Predicates))
				{
					ret.Add(enumerator);
				}
			});
			return CollectionNode.Create(ret);
		}

		public override void AddPredicate(ObjectNode predicate) => _Predicates.Enqueue(predicate);

		public void AddOperator(LogicOperators logicOperator)
		{
			_Predicates.Enqueue(logicOperator);
		}

		public enum LogicOperators
		{
			And = 0,
			Or = 1, 
			Not = 2
		}

		private static class Parser
		{
			internal static bool Execute(Queue<object> tokens)
			{
				Expression tree = new Expression();
				while (tokens.Count != 0)
				{
					var token = tokens.Dequeue();
					if (token is LogicOperators operand)
					{
						switch (operand)
						{
							case LogicOperators.And:
								tree.Body = new Func<bool, bool, bool>((v1, v2) => v1 && v2);
								tree.MaxArguments = 2;
								break;

							case LogicOperators.Or:
								tree.Body = new Func<bool, bool, bool>((v1, v2) => v1 || v2);
								tree.MaxArguments = 2;
								break;

							case LogicOperators.Not:
								tree.Body = new Func<bool, bool>(v => !v);
								tree.MaxArguments = 1;
								break;

							default:
								break;
						}
					}
					else
					{
						if (!tree.AddArgument(token.ToVariable()))
						{
							var temp = tree;
							tree = new Expression();
							tree.AddArgument(temp);
						}
					}
				}

				return (bool)tree.Invoke();
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
