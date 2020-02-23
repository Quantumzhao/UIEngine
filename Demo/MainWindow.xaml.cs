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
using System.ComponentModel;

namespace Demo
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : ModernWindow, INotifyPropertyChanged
	{
		private bool _Test = true;
		public bool Test
		{
			get => _Test;
			set
			{
				_Test = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Test)));
			}
		}

		public MainWindow()
		{
			InitializeComponent();
			//IsTestLayout.DataContext = this;
		}

		public event PropertyChangedEventHandler PropertyChanged;

		private void Add_Click(object sender, RoutedEventArgs e)
		{
			MainPanel.Children.Add(new UIPanel());
			Test = false;
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			DemographicModel.Init();
			Dashboard.ImportEntryObjects(typeof(DemographicModel));
			Source = DemographicModel.Model.People;
			DataGrid.ItemsSource = Source;
		}

		private ObservableCollection<Person> _Source;
		public ObservableCollection<Person> Source
		{
			get => _Source;
			set
			{
				_Source = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Source)));
			}
		}
		private void Button_Click(object sender, RoutedEventArgs e)
		{
			DemographicModel.Model.StartSimulation();
		}
	}
}
