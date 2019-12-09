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
	public partial class ObjectBox : UserControl, IBox
    {
		private ObjectNode _ObjectNode;
		public ObjectNode ObjectNode
		{
			get => _ObjectNode;
			set
			{
				_ObjectNode = value;
				if (value != null)
				{
					ContentChanged?.Invoke(this, value);
				}
			}
		}

		public IBox Child { get; set; }

		public event Action<object, ObjectNode> ContentChanged;
		public static event NewNodeSelectedHandler NewNodeSelected;
		public static event RemovedHandler Removed;

        public ObjectBox()
        {
            InitializeComponent();
			
			ContentChanged += (me, newNode) => Initialize();
        }

		private void Initialize()
		{
			DataContext = ObjectNode;

			var data = ObjectNode?.GetValue<object>();
			if (data is int || data is string || data is double)
			{
				var textBox = new TextBox();
				{
					textBox.SetBinding(TextBox.TextProperty, "ObjectData");
					if (data is int)
					{
						textBox.LostFocus += ChangeObjectData_Int;
					}
					else if (data is double)
					{
						textBox.LostFocus += ChangeObjectData_Double;
					}
					else if (data is string)
					{
						textBox.LostFocus += (ts, te) => ObjectNode.SetValue((ts as TextBox).Text);
					}
				}
				MainGrid.Children.Add(textBox);
			}
			else if (data is bool)
			{
				var checkbox = new CheckBox();
				checkbox.Content = ObjectNode.Header;
				checkbox.IsChecked = (bool)data;
				MainGrid.Children.Add(checkbox);
			}
			else if (data is IEnumerable)
			{
				throw new NotImplementedException();
			}
			else
			{
				var comboBox = new ComboBox();
				{
					if (data == null)
					{
						comboBox.IsEditable = true;
					}

					comboBox.ItemsSource = ObjectNode.Properties;
					comboBox.SelectionChanged += (sender, e) =>
					{						
						NewNodeSelected?.Invoke(this, e.AddedItems[0] as Node);
					};
				}
				MainGrid.Children.Add(comboBox);
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

		public void RemoveSelf()
		{
			Child?.RemoveSelf();
			Removed?.Invoke(this);
		}
	}
}
