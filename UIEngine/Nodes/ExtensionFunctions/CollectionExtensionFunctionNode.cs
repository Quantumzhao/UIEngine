using UIEngine.Nodes.ExtensionFunctions;

namespace UIEngine.Nodes.ExtensionFunctions
{
	public abstract class CollectionExtensionFunctionNode : Node, IExtensionFunctionNode
	{
		private const string _INVALID_OPERATION_WARNING = "Extension function nodes do not have succession";

		public abstract ObjectNode Invoke();
		internal protected CollectionExtensionFunctionNode(CollectionNode collection)
		{
			SourceCollection = collection;
		}

		public CollectionNode SourceCollection { get; private set; }
		public virtual CollectionNode ReturnNode { get; }
		internal protected ObjectNode Enumerator { get; set; }

		internal abstract bool DoesSatisfySignature();
		internal override ObjectNode InstantiateSuccession()
		{
			Dashboard.RaiseWarningMessage(this, _INVALID_OPERATION_WARNING);
			return null;
		}
	}
}
