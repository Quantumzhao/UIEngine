using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using UIEngine;

namespace CLITestProject
{
	class Program
	{
		private static readonly Dictionary<Node, int> _CachedNodes = new Dictionary<Node, int>();
		private static int _Counter = 0;

		private static Node _CurrentNode;

		static void Main(string[] args)
		{
			DemographicModel.Init();
			Dashboard.ImportEntryObjects(typeof(DemographicModel));
			
			var flag = ParseAndExecute("show");
			while (flag)
			{
				flag = ParseAndExecute(Console.ReadLine());
			}
		}

		/* NAME							: name of current objectnode
		 * SHOW							: show members of current OBJECTNODE. If current is null, then show root members
		 * SHOW [NO]					: show members of ID [NO]
		 * SHOW #[No]					: show members of ID [No]
		 *								  If current is a methodnode, then show its signature
		 * ASGN #[No]					: assign the value of ID [No] to current objectnode
		 * ASGN #[No1] #[No2]			: assign the value of ID [No2] to [No1]
		 * ASGN #[No] {lit}				: assign a literal to ID [No]
		 * ASGN {lit}					: assign a literal to current objectnode
		 * PARA #[No1] #[No2] #[No3]	: assign the value of [No3] to parameter [No2] of the method node of ID [No1]
		 * PARA #[No1] #[No2] {lit}		: assign a literal to parameter [No2] of the method node of ID [No1]
		 * EXEC #[No1]					: execute the method of [No1], and assign the result a new ID
		 * EXIT							: Exit
		 * e.g. 
		 * ASGN #1 #2			ASGN #1				ASGN 0.0
		 * ASGN #1 "Hello"		ASGN #1 2			ASGN #1 false
		 * PARA #1 #2 #3		PARA #1 #2 3		EXEC #1 */
		private static bool ParseAndExecute(string input)
		{
			Queue<string> tokens = new Queue<string>(input.Split());
			var opcode = tokens.Dequeue().ToUpper();

			if (opcode == "NAME")
			{
				Console.WriteLine($"{_CurrentNode.Header}\n");
			}
			else if (opcode == "SHOW")
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
							id = ParseToID(token);
						}
						dstNode = GetByID(id) as ObjectNode;
					}

					if (dstNode != null)
					{
						if (dstNode is CollectionNode)
						{
							Tabulate((dstNode as CollectionNode).Collection);
						}

						if (!dstNode.IsValueType)
						{
							PrintElements("Objects: ", dstNode.Properties.ToList());
							PrintElements("Methods: ", dstNode.Methods.ToList());
						}
						else
						{
							Console.WriteLine(dstNode.ObjectData + "\n");
						}
					}
					else
					{
						PrintElements("Objects: ", Dashboard.GetRootObjectNodes().ToList());
						PrintElements("Methods: ", Dashboard.GetRootMethodNodes().ToList());
					}
				}
				else if (_CurrentNode is MethodNode methodNode)
				{
					Console.Write($"{methodNode.ReturnNode.Type.RestrictedType}{methodNode.Header}(");
					for (int i = 0; i < methodNode.Signatures.Count; i++)
					{
						var para = methodNode.Signatures[i];
						Console.Write($"{para.Type.RestrictedType} {para.Header}");
						if (i != methodNode.Signatures.Count - 1)
						{
							Console.Write(", ");
						}
					}
					Console.WriteLine(")\n");
				}
			}
			//else if (opcode == "ACSS")
			//{
			//	_CurrentNode = GetByID(ParseToID(tokens.Dequeue()));
			//}
			else if (opcode == "ASGN" && _CurrentNode is ObjectNode currentNode)
			{
				ObjectNode firstNode = currentNode;
				// if the first parameter is an ID
				if (TryParse(tokens.Dequeue(), out object firstValue))
				{
					// convert the ID to an objectnode
					firstNode = GetByID((int)firstValue) as ObjectNode;

					// if there is a second parameter
					if (tokens.TryDequeue(out string token))
					{
						// if it is an ID
						if (TryParse(token, out object secondValue))
						{
							// At this stage, assume only objectnodes can be assigned to objectnodes
							ObjectNode secondNode = GetByID((int)secondValue) as ObjectNode;
							firstNode.ObjectData = secondNode.ObjectData;
						}
						else
						{
							firstNode.ObjectData = secondValue;
						}
					}
				}
				// it follows the `SHOW {lit}` syntax
				else
				{
					firstNode.ObjectData = firstValue;
				}
			}
			else if (opcode == "EXEC")
			{
				var methodNode = GetByID(ParseToID(tokens.Dequeue())) as MethodNode;
				var ret = methodNode.Invoke();
				TryAddToCachedNodes(ret);
				_CurrentNode = ret;
				ParseAndExecute("show");
			}
			else if (opcode == "PARA")
			{

			}
			else if (opcode == "EXIT")
			{
				return false;
			}
			else throw new NotImplementedException();
			return true;
		}

		private static void TryAddToCachedNodes(Node node)
		{
			if (_CachedNodes.ContainsKey(node))
			{
				return;
			}

			_CachedNodes.Add(node, _Counter);
			_Counter++;
		}

		private static void Tabulate(ObservableCollection<ObjectNode> table)
		{
			for (int i = 0; i < table.Count; i++)
			{
				TryAddToCachedNodes(table[i]);
				Console.Write(string.Format("{0,-20}", $"[{_CachedNodes[table[i]]}] {table[i].Header}"));
				Console.WriteLine();
			}
		}

		private static Node GetByID(int id) => _CachedNodes.Single(p => p.Value == id).Key;

		private static void PrintElements<T>(string caption, List<T> members) where T : Node
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(string.Format("{0,-12}", caption));
			for (int i = 0; i < members.Count; i++)
			{
				TryAddToCachedNodes(members[i]);
				sb.Append(string.Format("{0,-20}", $"[{_CachedNodes[members[i]]}] {members[i].Header}"));
			}
			Console.WriteLine(sb.ToString());
		}

		private static bool TryParse(string token, out object ret)
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

		private static int ParseToID(string token) => int.Parse(token.Substring(1));
	}
}
