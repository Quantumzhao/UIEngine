using System;
using System.Windows;
using System.Windows.Controls;
using UIEngine	;

namespace ComponentLibrary
{
	public delegate void CreatedHandler(IBox sender, IBox newBox);
	public delegate void NewNodeSelectedHandler(ObjectBox sender, Node newSelection);
	public delegate void RemovedHandler(IBox sender);

	public static class Utility
	{
		internal static IBox CreateBox(Node node)
		{
			IBox box;
			if (node is CollectionNode)
			{
				box = new CollectionBox() { CollectionNode = node as CollectionNode };
			}
			else if (node is ObjectNode)
			{
				box = new ObjectBox();
				(box as ObjectBox).SetValue(ObjectBox.ObjectNodeProperty, node);
			}
			else
			{
				box = new MethodBox() { MethodNode = node as MethodNode };
			}
			return box;
		}
	}

	public interface IBox
	{
		IBox Child { get; set; }
		void RemoveSelf();
	}
}