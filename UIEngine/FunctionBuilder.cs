using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomFunctionBuilder
{
	internal class FunctionBuilder
	{
		private readonly Dictionary<string, object> _Variables = new Dictionary<string, object>();

		private readonly LinkedList<KeyValuePair<string, object>> _ExecutionSequence =
			new LinkedList<KeyValuePair<string, object>>();

		/// <summary>
		///		Add one local variable to the wrapped function
		/// </summary>
		/// <param name="name"> Name of the parameter</param>
		/// <param name="data"> The object data of the parameter</param>
		/// <typeparam name="T"></typeparam>
		public bool AddVariable(string name, object data)
		{
			if (VariablesCount < MaximumVariablesCount)
			{
				if (_Variables.ContainsKey(name))
				{
					_Variables[name] = data;
				}
				else
				{
					_Variables.Add(name, data);
				}				
				return true;
			}
			else
			{
				return false;
			}
		}

		/// <summary>
		///		Add one subprocedure or function to the wrapped function
		/// </summary>
		/// <param name="name">
		///		<para>The name of the function</para>
		///		<para>
		///			NOTE: If the function has a return value, 
		///			the return value will be stored inside the 
		///			<para>Func</para> typed instance, 
		///			and its name is the same as the function. 
		///		</para>
		/// </param>
		/// <param name="function"></param>
		/// <param name="isAtBeginnning">
		///		Indicates whether the new function call is prior to other existing ones
		///	</param>
		public void AddFunction(string name, Delegate function, bool isAtBeginnning = false)
			=> AddFunction(name, (object)function, isAtBeginnning);
		public void AddFunction(string name, FunctionBuilder function, bool isAtBeginnning = false)
			=> AddFunction(name, (object)function, isAtBeginnning);
		private void AddFunction(string name, object function, bool isAtBeginnning)
		{
			var pair = new KeyValuePair<string, object>(name, function);
			if (isAtBeginnning) _ExecutionSequence.AddFirst(pair);
			else _ExecutionSequence.AddLast(pair);
		}

		public int VariablesCount => _Variables.Count;
		public int MaximumVariablesCount { get; set; } = int.MaxValue;

		/// <summary>
		///		To invoke the wrapped function
		/// </summary>
		/// <returns>
		///		If the wrapped function is void type, then return null
		///		Otherwise a meaningful return value
		/// </returns>
		public object Invoke()
		{
			KeyValuePair<string, object> tempResult = new KeyValuePair<string, object>();

			while (_ExecutionSequence.Count != 0)
			{
				var functionPair = _ExecutionSequence.First.Value;
				_ExecutionSequence.RemoveFirst();
				tempResult = new KeyValuePair<string, object>
				(
					functionPair.Key,
					functionPair
						.Value
						.GetType()
						.GetMethod("Invoke")
						.Invoke(functionPair.Value, new object[] { })
				);

				if (tempResult.Value != null)
					_Variables.Add(tempResult.Key, tempResult.Value);

			}

			return tempResult.Value;
		}

		/// <summary>
		///		The Indexer. This version is a shortcut of <c>GetTempVariable<c/>. 
		/// </summary>
		/// <param name="name">The name of the <c>tempVariable</c>, which is used to find it</param>
		/// <returns>The requested variable</returns>
		public object this[string name]
		{
			get => _Variables[name];
			set => _Variables[name] = value;
		}
	}

	internal abstract class Expression
	{

	}

	internal class BinaryExpression<TLeft, TRight, TOut> : Expression
	{
		internal TLeft Left { get; set; }
		internal TRight Right { get; set; }
	}

	internal class UnaryExpression<TIn, TOut> : Expression
	{
		internal TIn In { get; set; }
	}
}
