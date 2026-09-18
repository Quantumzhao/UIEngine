using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using UIEngine;
using UIEngine.Nodes;

namespace CLITestProject
{
	class Program
	{
		private static readonly Dictionary<Node, int> _CACHED_NODES = new Dictionary<Node, int>();
		private static int _Counter = 0;
		private static Node _CurrentNode;

		static void Main(string[] args)
		{
			DemographicModel.Init();
			Dashboard.ImportEntryObjects(typeof(DemographicModel));
			
			var flag = _ParseAndExecute("show");
			while (flag)
			{
				flag = _ParseAndExecute(Console.ReadLine());
			}
		}

		/* NAME							: name of current objectnode
		 * SHOW							: show members of current OBJECTNODE. If current is null, then show root members
		 * SHOW [No]					: show members of ID [NO]
		 * SHOW #[No]					: show members of ID [No]
		 *								  If current is a methodnode, then show its signature
		 * ASGN #[No1] #[No2]			: assign the value of ID [No2] to [No1]
		 * ASGN #[No] {lit}				: assign a literal to ID [No]
		 * PARA #[No1] #[No2] #[No3]	: assign the value of [No3] to parameter [No2] of the method node of ID [No1]
		 * PARA #[No1] #[No2] {lit}		: assign a literal to parameter [No2] of the method node of ID [No1]
		 * EXEC #[No1]					: execute the method of [No1], and assign the result a new ID
		 * FLTR #[No] {'a -> bool}		: Apply `Where<>` expression on the collection node of [No]
		 * INJC #[No] {'a -> 'b}		: Apply `Select<>` expression
		 * SORT #[No] {'a -> IComparable}: Sort the elements by a specified property
		 * FORE #[No] {'a -> 'b}		: Apply `ForEach<>` expression
		 * EXIT							: Exit
		 * {'a -> bool}: 
		 * expr ::= (AND expr expr)
		 *        | (OR expr expr)
		 *        | (NOT expr)
		 *        | (EQ expr expr)
		 *        | 
		 * e.g. 
		 * ASGN #1 #2			ASGN #1				ASGN 0.0
		 * ASGN #1 "Hello"		ASGN #1 2			ASGN #1 false
		 * PARA #1 #2 #3		PARA #1 #2 3		EXEC #1 */
		private static bool _ParseAndExecute(string input)
		{
			Queue<string> tokens = new Queue<string>(input.Split());
			var opcode = tokens.Dequeue().ToUpper();

			if (opcode == "NAME") _Name();
			else if (opcode == "SHOW") _Show(tokens);
			else if (opcode == "ASGN") _Asgn(tokens);
			else if (opcode == "EXEC") _Exec(tokens);
			else if (opcode == "EXIT") return false;
			else throw new NotImplementedException();

			return true;
		}

		private static void _TryAddToCachedNodes(Node node)
		{
			if (_CACHED_NODES.ContainsKey(node)) return;

			_CACHED_NODES.Add(node, _Counter);
			_Counter++;
		}

		private static void _Tabulate(ObservableCollection<ObjectNode> table)
		{
			for (int i = 0; i < table.Count; i++)
			{
				_TryAddToCachedNodes(table[i]);
				Console.Write(string.Format("{0,-20}", $"[{_CACHED_NODES[table[i]]}] {table[i].Header}"));
				Console.WriteLine();
			}
		}

		private static Node _GetByID(int id) => _CACHED_NODES.Single(p => p.Value == id).Key;

		private static void _PrintElements<T>(string caption, List<T> members) where T : Node
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(string.Format("{0,-12}", caption));
			for (int i = 0; i < members.Count; i++)
			{
				_TryAddToCachedNodes(members[i]);
				sb.Append(string.Format("{0,-20}", $"[{_CACHED_NODES[members[i]]}] {members[i].Header}"));
			}
			Console.WriteLine(sb.ToString());
		}

		private static bool _TryParse(string token, out object ret)
		{
			string rest = token.Substring(1);
			if (token.StartsWith('#'))
			{
				ret = int.Parse(rest);
				return true;
			}
			else if (token.StartsWith('\"'))
			{
				ret = rest.Substring(0, rest.Count() - 1);
			}
			else if (char.IsLetter(token[0]))
			{
				ret = bool.Parse(token);
			}
			else if (int.TryParse(token, out int result))
			{
				ret = result;
			}
			else
			{
				ret = double.Parse(token);
			}

			return false;
		}

		private static int _ParseToID(string token) => int.Parse(token.Substring(1));

		private static void _Name() => Console.WriteLine($"{_CurrentNode.Header}\n");

		private static void _Show(Queue<string> tokens)
		{
			if (_CurrentNode is ObjectNode || _CurrentNode == null)
			{
				var dstNode = _CurrentNode as ObjectNode;
				// if `SHOW` is followed by one parameter
				if (tokens.TryDequeue(out string token))
				{
					// modify that node instead of current objectnode
					if (!int.TryParse(token, out int id))
					{
						id = _ParseToID(token);
					}
					dstNode = _GetByID(id) as ObjectNode;
				}

				if (dstNode != null)
				{
					if (dstNode is CollectionNode) _Tabulate((dstNode as CollectionNode).Collection);

					if (!dstNode.IsPrimitiveType())
					{
						_PrintElements("Objects: ", dstNode.Properties.ToList());
						_PrintElements("Methods: ", dstNode.Methods.ToList());
					}
					else Console.WriteLine(dstNode.ObjectData + "\n");
				}
				else
				{
					_PrintElements("Objects: ", Dashboard.GetRootNodes<ObjectNode>().ToList());
					_PrintElements("Methods: ", Dashboard.GetRootNodes<MethodNode>().ToList());
				}
			}
			else if (_CurrentNode is MethodNode methodNode)
			{
				Console.Write($"{methodNode.ReturnNode.TypeName}{methodNode.Header}(");
				for (int i = 0; i < methodNode.Signatures.Count; i++)
				{
					var para = methodNode.Signatures[i];
					Console.Write($"{para.TypeName} {para.Header}");
					if (i != methodNode.Signatures.Count - 1) Console.Write(", ");
				}
				Console.WriteLine(")\n");
			}
		}

		private static void _Exec(Queue<string> tokens)
		{
			var ret = (_GetByID(_ParseToID(tokens.Dequeue())) as MethodNode).Invoke();
			_TryAddToCachedNodes(ret);
			_CurrentNode = ret;
			_ParseAndExecute("show");
		}

		private static void _Asgn(Queue<string> tokens)
		{
			ObjectNode firstNode = _CurrentNode as ObjectNode;
			// get the first parameter
			_TryParse(tokens.Dequeue(), out object firstValue);
			// convert the ID to an objectnode
			firstNode = _GetByID((int)firstValue) as ObjectNode;
			// the second token
			var token = tokens.Dequeue();
			// if it is an ID
			if (_TryParse(token, out object secondValue))
			{
				// At this stage, assume only objectnodes can be assigned to objectnodes
				ObjectNode secondNode = _GetByID((int)secondValue) as ObjectNode;
				firstNode.ObjectData = secondNode.ObjectData;
			}
			else firstNode.ObjectData = secondValue;
		}
	}
}
