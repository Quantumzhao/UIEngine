using System;
using System.Windows;
using System.Windows.Controls;
using UIEngine;

namespace ComponentLibrary
{
	public delegate void CreatedHandler(IBox sender, IBox newBox);
	public delegate void NewNodeSelectedHandler(ObjectBox sender, Node newSelection);
	public delegate void RemovedHandler(IBox sender);

	public static class Utility
	{
		internal static IBox CreateBox(Node node, UIPanel panel = null)
		{
			IBox box;
			if (node is CollectionNode)
			{
				box = new CollectionBox() { CollectionNode = node as CollectionNode };
			}
			else if (node is ObjectNode)
			{
				box = new ObjectBox();
				box.Host = box;
				(box as ObjectBox).ObjectNode = node as ObjectNode;
				if (panel != null)
				{
					(box as ObjectBox).NewNodeSelected += (me, newNode) =>
					{
						me.Host.VisualChild?.RemoveSelf();
						me.Host.VisualChild = CreateBox(newNode, panel);
						panel.AddNewBox(me.Host.VisualChild);
					};
					(box as ObjectBox).Removed += me => panel.RemoveOldBox(me);
				}
			}
			else
			{
				box = new MethodBox() { MethodNode = node as MethodNode };
				if (panel != null)
				{
					(box as MethodBox).Removed += me => panel.RemoveOldBox(me);
				}
			}
			return box;
		}
	}

	public interface IBox
	{
		IBox Host { get; set; }
		IBox VisualChild { get; set; }
		void RemoveSelf();
	}
}