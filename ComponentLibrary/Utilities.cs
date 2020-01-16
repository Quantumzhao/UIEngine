using System;
using System.Windows;
using System.Windows.Controls;
using UIEngine;

namespace ComponentLibrary
{
	public delegate void CreatedHandler(IBox sender, IBox newBox);
	public delegate void NewNodeSelectedHandler(ObjectBox sender, Node newSelection);
	public delegate void RemovedHandler(IBox sender);

	public static class Utilities
	{
		internal static IBox CreateBox(Node node, UIPanel panel = null)
		{
			IBox box;
			if (node is CollectionNode)
			{
				box = new CollectionBox()
				{
					CollectionNode = node as CollectionNode,
					ParentContainer = panel
				};
			}
			else if (node is ObjectNode)
			{
				box = new ObjectBox();
				box.Host = box;
				(box as ObjectBox).ObjectNode = node as ObjectNode;
				if (panel != null)
				{
					(box as ObjectBox).ParentContainer = panel;
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
		UIPanel ParentContainer { get; set; }
		IBox Host { get; set; }
		IBox VisualChild { get; set; }
		void RemoveSelf();
	}
}