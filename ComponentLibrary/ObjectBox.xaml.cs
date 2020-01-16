using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows;
using System;
using UIEngine;

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

		public static readonly DependencyProperty ObjectNodeProperty
			= DependencyProperty.Register(nameof(ObjectNode), typeof(ObjectNode), typeof(ObjectBox), new PropertyMetadata(Initialize));
		public ObjectNode ObjectNode 
		{ 
			get => GetValue(ObjectNodeProperty) as ObjectNode;
			set => SetValue(ObjectNodeProperty, value);
		}

		public IBox VisualChild { get; set; }

		public static readonly DependencyProperty HostProperty
			= DependencyProperty.Register(nameof(Host), typeof(IBox), typeof(ObjectBox));
		public IBox Host
		{
			get => GetValue(HostProperty) as IBox;
			set => SetValue(HostProperty, value);
		}

		public static readonly DependencyProperty ParentContainerProperty
			= DependencyProperty.Register(nameof(ParentContainer), typeof(UIPanel), typeof(ObjectBox), new PropertyMetadata(InitializeHost));
		public UIPanel ParentContainer
		{
			get => GetValue(ParentContainerProperty) as UIPanel;
			set => SetValue(ParentContainerProperty, value);
		}


		public event NewNodeSelectedHandler NewNodeSelected;
		public event RemovedHandler Removed;

		private static void Initialize(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			ObjectBox box = d as ObjectBox;

			box.VisualChild?.RemoveSelf();
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
			else if (type.IsEnum)
			{
				box.ToRadioButtons();
			}
			else
			{
				box.ToDropBox();
			}

			var label = new Label();
			{
				label.Content = "Drag";
				label.Drop += box.ObjectBox_Drop;
				label.MouseLeftButtonDown += box.ObjectBox_MouseLeftButtonDown;
			}
			box.MainPanel.Children.Add(label);
		}
		private static void InitializeHost(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			ObjectBox box = d as ObjectBox;
			UIPanel panel = e.NewValue as UIPanel;

			box.NewNodeSelected += (me, newNode) =>
			{
				me.Host.VisualChild?.RemoveSelf();
				me.Host.VisualChild = Utilities.CreateBox(newNode, panel);
				panel.AddNewBox(me.Host.VisualChild);
			};
			box.Removed += me => panel.RemoveOldBox(me);
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
		private void ToRadioButtons()
		{

		}

		public void RemoveSelf()
		{
			VisualChild?.RemoveSelf();
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
