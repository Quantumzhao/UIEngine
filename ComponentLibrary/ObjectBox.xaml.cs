using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows;
using UIEngine;
using System;

/* Drag and drop functionality can be suspended for a little bit until I finish method box
 */
namespace ComponentLibrary
{
	/// <summary>
	/// Interaction logic for UserControl1.xaml
	/// </summary>
	public partial class ObjectBox : UserControl, IBox
	{
		public ObjectBox()
		{
			InitializeComponent();
			AllowDrop = true;
			VerticalAlignment = VerticalAlignment.Top;
		}

		public static readonly DependencyProperty ObjectNodeProperty = DependencyProperty.Register("ObjectNode", typeof(ObjectNode), typeof(ObjectBox), new PropertyMetadata(Initialize));
		public ObjectNode ObjectNode 
		{ 
			get => GetValue(ObjectNodeProperty) as ObjectNode;
			set => SetValue(ObjectNodeProperty, value);
		}

		public IBox Child { get; set; }

		//public event Action<object, ObjectNode> ObjectNodeChanged;
		public static event NewNodeSelectedHandler NewNodeSelected;
		public static event RemovedHandler Removed;

		private static void Initialize(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			ObjectBox box = d as ObjectBox;

			box.Child?.RemoveSelf();
			box.MainPanel.Children.Clear();

			var type = box.ObjectNode.Type;
			if (type.IsSame(TypeSystem.Bool))
			{
				box.ToCheckBox();
			}
			else if (type.IsValueType)
			{
				box.ToTextBox(type);
			}
			else
			{
				box.ToDropBox();
			}

			var label = new Label();
			{
				label.Content = "Drag";
				//button.MouseMove += box.ObjectBox_MouseMove;
				//button.DragEnter += box.ObjectBox_DragEnter;
				label.Drop += box.ObjectBox_Drop;
				label.MouseLeftButtonDown += box.ObjectBox_MouseLeftButtonDown;
			}
			box.MainPanel.Children.Add(label);
		}

		private void ChangeObjectData_Int(object sender, RoutedEventArgs e)
		{
			if (int.TryParse((sender as TextBox).Text, out int result))
			{
				ObjectNode.ObjectData = result;
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
				ObjectNode.ObjectData = result;
			}
			else
			{
				throw new ArgumentException("Invalid double");
			}
		}

		private void ToTextBox(TypeSystem type)
		{
			var textBox = new TextBox();
			{
				textBox.SetBinding(TextBox.TextProperty, new Binding("ObjectData") { Source = ObjectNode });
				if (type.IsSame(TypeSystem.Int))
				{
					textBox.LostFocus += ChangeObjectData_Int;
				}
				else if (type.IsSame(TypeSystem.Double))
				{
					textBox.LostFocus += ChangeObjectData_Double;
				}
				else if (type.IsSame(TypeSystem.String))
				{
					textBox.LostFocus += (ts, te) => ObjectNode.ObjectData = (ts as TextBox).Text;
				}
			}
			MainPanel.Children.Add(textBox);
		}
		private void ToCheckBox()
		{
			var checkbox = new CheckBox();
			checkbox.Content = ObjectNode.Header;
			//checkbox.IsChecked = (bool)data;
			MainPanel.Children.Add(checkbox);
		}
		private void ToDropBox()
		{
			var comboBox = new ComboBox();
			{
				comboBox.ItemsSource = ObjectNode.Properties;
				comboBox.SelectionChanged += (sender, e) =>
				{
					NewNodeSelected?.Invoke(this, e.AddedItems[0] as Node);
				};
			}
			MainPanel.Children.Add(comboBox);
		}

		public void RemoveSelf()
		{
			Child?.RemoveSelf();
			Removed?.Invoke(this);
		}

		private void ObjectBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			ObjectBox box = this;
			DragDrop.DoDragDrop(box, box.ObjectNode, DragDropEffects.Link);
		}

		private void ObjectBox_Drop(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(typeof(ObjectNode)))
			{
				ObjectNode = e.Data.GetData(typeof(ObjectNode)) as ObjectNode;
			}
		}
	}
}
