﻿using System.Collections.Generic;
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
			Dashboard.ImportEntryObjects(typeof(Dataset.Dataset));
		}

		private void Cb_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			var cb = new ComboBox
			{
				Margin = new Thickness(10, 0, 0, 0)
			};
			ObjectNode current = cb.SelectedItem as ObjectNode;
			cb.ItemsSource = current.Properties;
			cb.SelectionChanged += Cb_SelectionChanged;
			MainPanel.Children.Add(cb);
		}

		private void Add_Click(object sender, RoutedEventArgs e)
		{
			var cb = new ComboBox
			{
				Margin = new Thickness(10, 0, 0, 0),
				ItemsSource = Dashboard.GlobalObjects
			};
			//cb.SelectionChanged += Cb_SelectionChanged;
			MainPanel.Children.Add(cb);
		}
	}
}
