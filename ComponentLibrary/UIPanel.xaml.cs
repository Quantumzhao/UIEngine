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
			var selection = e.AddedItems[0];
			if (selection is ObjectNode)
			{
				var objectBox = new ObjectBox();
				objectBox.ObjectNode = selection as ObjectNode;
				ObjectBox.SelectionChanged += (me, newNode) =>
				{
					int index = MainPanel.Children.IndexOf(me);
					while (index + 1 < MainPanel.Children.Count)
					{
						MainPanel.Children.RemoveAt(index + 1);
					}
				};
				ObjectBox.ObjectBoxCreated += (me, newBox) =>
				{
					MainPanel.Children.Add(newBox);
				};

				MainPanel.Children.Add(objectBox);
			}
			else
			{
				throw new NotImplementedException();
			}
		}
	}
}
