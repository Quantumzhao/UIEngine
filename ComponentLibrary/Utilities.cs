using System;
using System.Windows;
using System.Windows.Controls;
using UIEngine;

namespace ComponentLibrary
{
	public delegate void ObjectBoxCreatedDelegate(ObjectBox sender, ObjectBox newObjectBox);
	public delegate void SelectionChangedDelegate(ObjectBox sender, IBox newSelection);
	public delegate void SelfDestroyedDelegate(IBox sender);

	public static class Utility
	{

	}

	public interface IBox
	{
		IBox Child { get; set; }
		void RemoveSelf();
	}
}