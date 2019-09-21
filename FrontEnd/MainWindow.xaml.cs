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
		VisualStatement statement = new VisualStatement();
		public MainWindow()
		{
			InitializeComponent();
			Dashboard.Statements.Add(statement);
		}

		private void Grid_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
		{
			var cb = new ComboBox();
			MainPanel.Children.Add(cb);
		}
	}
}
