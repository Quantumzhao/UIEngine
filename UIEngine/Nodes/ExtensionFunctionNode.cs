namespace UIEngine.Nodes
{
	public abstract class ExtensionFunctionNode : Node
	{
		private const string _INVALID_OPERATION_WARNING = "Extension function nodes do not have succession";

		public abstract ObjectNode Invoke();
		internal protected ExtensionFunctionNode(CollectionNode collection)
		{
			SourceCollection = collection;
		}

		public CollectionNode SourceCollection { get; private set; }
		internal protected ObjectNode Enumerator { get; set; }

		internal abstract bool DoesSatisfySignature();
		internal override ObjectNode InstantiateSuccession()
		{
			Dashboard.RaiseWarningMessage(this, _INVALID_OPERATION_WARNING);
			return null;
		}
	}
}
