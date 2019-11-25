using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UIEngine;
using ComponentLibrary;
using System.Linq;

namespace FrontEnd
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();
			Dataset.Dataset.Init();
			Dashboard.ImportEntryObjects(typeof(Dataset.Dataset), typeof(Dataset.Person));
		}

		private void Add_Click(object sender, RoutedEventArgs e)
		{
			MainPanel.Children.Add(new UIPanel());
		}
	}
}
