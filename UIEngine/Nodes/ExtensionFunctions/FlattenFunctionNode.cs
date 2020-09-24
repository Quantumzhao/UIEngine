using System;
using System.Collections.Generic;
using System.Text;

namespace UIEngine.Nodes.ExtensionFunctions
{
	public class FlattenFunctionNode : CollectionExtensionFunctionNode
	{
		public FlattenFunctionNode(CollectionNode collectionNode) : base(collectionNode) { }
		protected override string Preview { get; set; }

		/// <summary>
		///		Stores the specified object nodes
		/// </summary>
		public readonly List<ObjectNode> SubNodes = new List<ObjectNode>();
		internal readonly List<string> Path = new List<string>();

		public void AddSubNode(ObjectNode objectNode)
		{
			SubNodes.Add(objectNode);
		}

		public override ObjectNode Invoke() 
		{
			throw new NotImplementedException();
		}

		internal override ObjectNode InstantiateSuccession()
		{
			throw new NotImplementedException();
		}

		internal override bool DoesSatisfySignature()
		{
			throw new NotImplementedException();
		}
	}
}
