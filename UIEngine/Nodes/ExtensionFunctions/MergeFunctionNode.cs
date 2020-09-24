using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace UIEngine.Nodes.ExtensionFunctions
{
	public class MergeFunctionNode : CollectionExtensionFunctionNode
	{
		internal MergeFunctionNode(CollectionNode collection) : base(collection) { }
		protected override string Preview { get; set; }

		public ObservableCollection<CollectionNode> MergeSources { get; } = new ObservableCollection<CollectionNode>();

		public override ObjectNode Invoke()
		{
			throw new NotImplementedException();
		}

		// need rework
		internal override bool DoesSatisfySignature() => MergeSources.All(c => c.Count == SourceCollection.Count);
	}
}
