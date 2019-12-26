using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using UIEngine;

namespace ComponentLibrary
{
	/// <summary>
	/// Interaction logic for UIPanel.xaml
	/// </summary>
	public partial class UIPanel : UserControl
	{
		public HashSet<Node> Roots { get; set; }

		public UIPanel()
		{
			InitializeComponent();
		}

		private void UserControl_Loaded(object sender, RoutedEventArgs e)
		{
			Start.DataContext = this;
			Roots = Dashboard.Roots;			
		}

		private void Start_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			MainPanel.Children.Clear();
			AddNewBox(Utility.CreateBox(e.AddedItems[0] as Node, this));
		}

		internal void AddNewBox(IBox newBox)
		{
			MainPanel.Children.Add(newBox as UIElement);
		}

		internal void RemoveOldBox(IBox oldBox)
		{
			MainPanel.Children.Remove(oldBox as ObjectBox);
		}
	}
}
