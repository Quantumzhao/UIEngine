using System;
using System.Windows;
using System.Windows.Controls;
using UIEngine;

namespace ComponentLibrary
{
	public static class Utility
	{
	}

	public interface IBox
	{
		IBox Child { get; set; }
		void RemoveChild();
	}
}