using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using UIEngine.Core;

namespace UIEngine.Nodes
{
	public class FilterFunctionNode : ExtensionFunctionNode
	{
		private const string _INVALID_RETURN_TYPE = "Current expression does not qualify return type requirement";
		public static FilterFunctionNode Create(CollectionNode collection) 
			=> new FilterFunctionNode(collection);
		private FilterFunctionNode(CollectionNode collection) : base(collection) 
			=> Predicate = new Val(Enumerator);

		public Expr Predicate { get; }
		public CollectionNode ReturnNode => Succession as CollectionNode;
		protected override string Preview { get; set; }

		/// <summary>
		///		Execute the predicate
		/// </summary>
		/// <returns>If the expression is invalid, it will give off a warning and just return the collection itself</returns>
		public override ObjectNode Invoke()
		{
			if (!DoesSatisfySignature())
			{
				Dashboard.RaiseWarningMessage(this, _INVALID_RETURN_TYPE);
				return SourceCollection;
			}
			
			return CollectionNode.Create(new ObservableCollection<ObjectNode>(
				SourceCollection.Where(e => 
				{
					Enumerator.SetReferenceTo(e);
					return Predicate.Eval();
				})));
		}

		internal override ObjectNode InstantiateSuccession()
		{
			return ReturnNode.InstantiateSuccession();
		}

		public static readonly Func<ObjectNode, ObjectNode, bool> And = 
			(left, right) => (bool)left.ObjectData && (bool)right.ObjectData;
		public static readonly Func<ObjectNode, ObjectNode, bool> Or = 
			(left, right) => (bool)left.ObjectData || (bool)right.ObjectData;
		public static readonly Func<ObjectNode, bool> Not = 
			value => !(bool)value.ObjectData;
		public static readonly Func<ObjectNode, ObjectNode, bool> IsEqual =
			(left, right) => (bool)left.ObjectData == (bool)right.ObjectData;

		internal override bool DoesSatisfySignature() => throw new NotImplementedException();
	}

	/// <summary><code>
	/// type Expr = 
	///		| NegExpr of Expr
	///		| AndExpr of Expr * Expr
	///		| OrExpr of Expr * Expr
	///		| BoolEqExpr of Expr * Expr
	///		| ObjEqExpr of Val * Val
	///		| Val of ObjectNode
	/// </code></summary>
	/// <typeparam name="T">type of the root element. 
	/// i.e. the <see cref="T"/> in <see cref="IEnumerable{T}"/></typeparam>
	public abstract class Expr
	{
		internal Expr(ObjectNode env) => Env = env;

		internal ObjectNode Env { get; set; }

		public abstract bool Eval();
	}

	public abstract class BinExpr : Expr
	{
		internal BinExpr(ObjectNode env) : base(env)
		{
			Left = new Val(env);
			Right = new Val(env);
		}
		public Expr Left { get; internal set; }
		public Expr Right { get; internal set; }

		/// <summary>
		///		Apply one of the arguments
		/// </summary>
		/// <param name="type"></param>
		/// <param name="target"><see cref="Left"/> or <see cref="Right"/> property </param>
		public void Assign(ExprTypes type, ref Expr target)
		{
			switch (type)
			{
				case ExprTypes.And:
					target = new AndExpr(Env);
					break;

				case ExprTypes.Or:
					target = new OrExpr(Env);
					break;

				case ExprTypes.Not:
					target = new NegExpr(Env);
					break;

				case ExprTypes.Equal:
					target = new EqExpr(Env);
					break;
			}
		}
	}

	public class AndExpr : BinExpr
	{
		internal AndExpr(ObjectNode env) : base(env) { }

		public override bool Eval() => Left.Eval() && Right.Eval();
	}

	public class OrExpr : BinExpr
	{
		internal OrExpr(ObjectNode env) : base(env) { }

		public override bool Eval() => Left.Eval() || Right.Eval();
	}

	public class EqExpr : BinExpr
	{
		internal EqExpr(ObjectNode env) : base(env) { }

		public override bool Eval() => 
			(Left as Val).Value.ObjectData == (Right as Val).Value.ObjectData;
	}

	public class NegExpr : Expr
	{
		internal NegExpr(ObjectNode env) : base(env) => Value = new Val(env);

		public Expr Value { get; internal set; }

		public override bool Eval() => !Value.Eval();
	}

	public class Val : Expr
	{
		internal Val(ObjectNode env) : base(env) { }

		/// <summary>
		///		Set to the 
		/// </summary>
		public ObjectNode Value { get; set; }

		public override bool Eval() => (bool)Value.ObjectData;
	}

	public enum ExprTypes
	{
		And, 
		Or, 
		Not, 
		Equal
	}
}
