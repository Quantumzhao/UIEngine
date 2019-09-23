using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UIEngine;


namespace FrontEnd
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		Tree tree = new Tree();
		public MainWindow()
		{
			InitializeComponent();
			Dashboard.ImportEntryObjects(typeof(Dataset.Dataset));
			Dashboard.Trees.Add(tree);
		}

		private void Grid_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
		{
			var cb = new ComboBox
			{
				Margin = new Thickness(10, 0, 0, 0),
				ItemsSource = Dashboard.GlobalObjects
			};
			cb.SelectionChanged += Cb_SelectionChanged;
			MainPanel.Children.Add(cb);
		}

		private void Cb_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			var cb = new ComboBox
			{
				Margin = new Thickness(10, 0, 0, 0)
			};
			ObjectNode current = e.Source as ObjectNode;
			Dashboard.GetMembers(current, out List<ObjectNode> properties, out _);
			cb.ItemsSource = properties;
			cb.SelectionChanged += Cb_SelectionChanged;
			MainPanel.Children.Add(cb);
		}
	}
}
