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
			ObjectBox.SelectionChanged += (me, newNode) =>
			{
				me.Child?.RemoveSelf();
				me.Child = newNode;
			};
			ObjectBox.ObjectBoxCreated += (me, newBox) =>
			{
				MainPanel.Children.Add(newBox);
			};
			ObjectBox.SelfDestroyed += me =>
			{
				MainPanel.Children.Remove(me as ObjectBox);
			};
			MethodBox.SelfDestroyed += me =>
			{
				MainPanel.Children.Remove(me as MethodBox);
			};
		}

		private void Start_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			var selection = e.AddedItems[0];
			MainPanel.Children.Clear();
			if (selection is ObjectNode)
			{
				var objectBox = new ObjectBox();
				objectBox.ObjectNode = selection as ObjectNode;

				MainPanel.Children.Add(objectBox);
			}
			else
			{
				var methodBox = new MethodBox();
				methodBox.MethodNode = selection as MethodNode;

				MainPanel.Children.Add(methodBox);
			}
		}
	}
}
