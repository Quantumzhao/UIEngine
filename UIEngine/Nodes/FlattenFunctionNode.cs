using System;
using System.Collections.Generic;
using System.Text;

namespace UIEngine.Nodes
{
	public class FlattenFunctionNode : ExtensionFunctionNode
	{
		protected override string Preview { get; set; }
		/// <summary>
		///		Node templates that WILL be instantiated in the resultant FlattenedClass object. 
		///		It's more of a manifest
		/// </summary>
		internal readonly List<ObjectNode> SubNodes = new List<ObjectNode>();

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
	}
}
