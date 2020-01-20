using FirstFloor.ModernUI.Windows.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ComponentLibrary;
using UIEngine;
using System.Collections.ObjectModel;

namespace Demo
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : ModernWindow
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
			DemographicModel.Init();
			Dashboard.ImportEntryObjects(typeof(DemographicModel));
			Source = DemographicModel.Model.People;
			DataGrid.ItemsSource = Source;
		}

		public ObservableCollection<Person> Source { get; set; }
		private void Button_Click(object sender, RoutedEventArgs e)
		{
			DemographicModel.TimeElapse();
		}
	}
}
