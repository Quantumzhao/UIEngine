using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UIEngine;
using ComponentLibrary;
using System.Linq;

namespace TestWindow
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();
		}

		private void Add_Click(object sender, RoutedEventArgs e)
		{
			MainPanel.Children.Add(new UIPanel());
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			Dashboard.ImportEntryObjects(typeof(TestClass));
		}
	}
}
