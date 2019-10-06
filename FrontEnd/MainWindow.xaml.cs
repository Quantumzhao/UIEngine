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
		public MainWindow()
		{
			InitializeComponent();
			Dataset.Dataset.Init();
			Dashboard.ImportEntryObjects(typeof(Dataset.Dataset));
		}

		private void Cb_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			var selectedItem = e.AddedItems[0] as ObjectNode;
			if (selectedItem.Properties.Count != 0)
			{
				var cb = new ComboBox
				{
					Margin = new Thickness(10, 0, 0, 0)
				};
				cb.ItemsSource = selectedItem.Properties;
				cb.SelectionChanged += Cb_SelectionChanged;
				MainPanel.Children.Add(cb);
			}
			else
			{
				var tb = new TextBox();
				tb.Margin = new Thickness(10, 0, 0, 0);
				tb.Text = selectedItem.GetValue<object>().ToString();
				tb.LostFocus += (tbs, tbe) => selectedItem.SetValue(tb.Text);
				MainPanel.Children.Add(tb);
			}
		}

		private void Add_Click(object sender, RoutedEventArgs e)
		{
			var cb = new ComboBox
			{
				Margin = new Thickness(10, 0, 0, 0),
				ItemsSource = Dashboard.GetGlobalObjects()
			};
			
			cb.SelectionChanged += Cb_SelectionChanged;
			MainPanel.Children.Add(cb);
		}
	}
}
