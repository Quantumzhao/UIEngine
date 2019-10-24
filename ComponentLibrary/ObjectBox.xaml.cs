using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using UIEngine;
using System;
using System.Collections;
namespace ComponentLibrary
{
	/// <summary>
	/// Interaction logic for UserControl1.xaml
	/// </summary>
	public partial class ObjectBox : UserControl
    {
		public ObjectNode ObjectNode { get; set; }

        public ObjectBox()
        {
            InitializeComponent();
        }

		private void UserControl_Initialized(object sender, EventArgs e)
		{
			DataContext = ObjectNode;

			if (ObjectNode == null)
			{
				return;
			}

			var data = ObjectNode.ObjectData;
			if (data is int || data is string || data is double)
			{
				var textbox = new TextBox();
				{
					textbox.SetBinding(TextBox.TextProperty, "ObjectData");
					if (data is int)
					{
						textbox.LostFocus += ChangeObjectData_Int;
					}
					else if (data is double)
					{
						textbox.LostFocus += ChangeObjectData_Double;
					}
					else if (data is string)
					{
						textbox.LostFocus += (ts, te) => ObjectNode.SetValue((ts as TextBox).Text);
					}
				}
				MainGrid.Children.Add(textbox);
			}
			else if (data is IEnumerable)
			{
				throw new NotImplementedException();
			}
			else
			{
				var comboBox = new ComboBox();
				{
					comboBox.ItemsSource = ObjectNode.Properties;
					throw new NotImplementedException();
				}
			}
		}

		private void ChangeObjectData_Int(object sender, RoutedEventArgs e)
		{
			if (int.TryParse((sender as TextBox).Text, out int result))
			{
				ObjectNode.SetValue(result);
			}
			else
			{
				throw new ArgumentException("Invalid integer");
			}
		}

		private void ChangeObjectData_Double(object sender, RoutedEventArgs e)
		{
			if (double.TryParse((sender as TextBox).Text, out double result))
			{
				ObjectNode.SetValue(result);
			}
			else
			{
				throw new ArgumentException("Invalid double");
			}
		}
	}
}
