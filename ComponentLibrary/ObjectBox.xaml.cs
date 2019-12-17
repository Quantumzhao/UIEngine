using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using UIEngine;
using System;
using System.Collections;
using System.Windows.Input;

/* A base case is needed. i.e. Object box needs to behave normally even if the object data value is null. 
 * This is for Method Box and drag and drop functionality
 * Drag and drop functionality can be suspended for a little bit until I finish method box
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

			this.ContentChanged += (me, newNode) => Initialize();
        }

		public ObjectBox(Type type) : base()
		{
			if (type.IsAssignableFrom(typeof(int)))
			{

			}
		}

		private Point _StartPoint;
		private ObjectNode _ObjectNode = null;
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

		private void Initialize()
		{
			DataContext = ObjectNode;
			Child?.RemoveSelf();

			var data = ObjectNode?.GetObjectData<object>();
			if (data is int || data is string || data is double)
			{
				ToTextBox(data.GetType());
			}
			else if (data is bool)
			{
				ToCheckBox(data.GetType());
			}
			else if (data is IEnumerable)
			{
				throw new NotImplementedException();
			}
			else
			{
				ToDropBox(data.GetType());
			}

			var button = new Button();
			{
				button.Visibility = Visibility.Collapsed;
				button.Content = "Drag";
				button.MouseMove += ObjectBox_MouseMove;
				button.DragEnter += ObjectBox_DragEnter;
				button.Drop += ObjectBox_Drop;
				button.MouseLeftButtonDown += ObjectBox_MouseLeftButtonDown;
			}
			MainPanel.Children.Add(button);
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

		private void ToTextBox(Type type)
		{
			var textBox = new TextBox();
			{
				textBox.SetBinding(TextBox.TextProperty, "ObjectData");
				if (type.Equals(typeof(int)))
				{
					textBox.LostFocus += ChangeObjectData_Int;
				}
				else if (type.Equals(typeof(double)))
				{
					textBox.LostFocus += ChangeObjectData_Double;
				}
				else if (type.Equals(typeof(string)))
				{
					textBox.LostFocus += (ts, te) => ObjectNode.ObjectData = (ts as TextBox).Text;
				}
			}
			MainPanel.Children.Add(textBox);
		}
		private void ToCheckBox(Type type)
		{
			var checkbox = new CheckBox();
			checkbox.Content = ObjectNode.Header;
			// checkbox.IsChecked = (bool)data;
			MainPanel.Children.Add(checkbox);
		}
		private void ToDropBox(Type type)
		{
			var comboBox = new ComboBox();
			{
				//if (data == null)
				//{
				//	comboBox.IsEditable = true;
				//}

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
			_StartPoint = e.GetPosition(null);			
		}

		private void ObjectBox_MouseMove(object sender, MouseEventArgs e)
		{
			Vector diff = _StartPoint - e.GetPosition(null);
			if (e.LeftButton == MouseButtonState.Pressed &&
				(Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
				Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
			{
				DragDrop.DoDragDrop(this, new DataObject(typeof(ObjectNode), ObjectNode), DragDropEffects.Copy);
			}
		}

		private void ObjectBox_DragEnter(object sender, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(typeof(ObjectNode)))
			{
				e.Effects = DragDropEffects.None;
			}
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
