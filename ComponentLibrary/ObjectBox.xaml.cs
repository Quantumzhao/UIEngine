﻿using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using UIEngine;
using System;
using System.Collections;
using System.Windows.Input;

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
		}

		private Point _StartPoint;

		public static readonly DependencyProperty ObjectNodeProperty = DependencyProperty.Register("ObjectNode", typeof(ObjectNode), typeof(ObjectBox));
		public ObjectNode ObjectNode 
		{ 
			get => GetValue(ObjectNodeProperty) as ObjectNode;
			set
			{
				if (ObjectNode != value)
				{
					SetValue(ObjectNodeProperty, value);
					Initialize();
				}
			}
		}

		public IBox Child { get; set; }

		//public event Action<object, ObjectNode> ObjectNodeChanged;
		public static event NewNodeSelectedHandler NewNodeSelected;
		public static event RemovedHandler Removed;

		private void Initialize()
		{
			// this.ContentChanged += (me, newNode) => Initialize();

			DataContext = ObjectNode;
			Child?.RemoveSelf();

			var type = ObjectNode.Type;
			if (type.IsSame(TypeSystem.Bool))
			{
				ToCheckBox();
			}
			else if (type.IsValueType)
			{
				ToTextBox(type);
			}
			else if (type.IsDerivedFrom(TypeSystem.Collection))
			{
				throw new NotImplementedException();
			}
			else
			{
				ToDropBox();
			}

			//var button = new Button();
			//{
			//	button.Visibility = Visibility.Collapsed;
			//	button.Content = "Drag";
			//	button.MouseMove += ObjectBox_MouseMove;
			//	button.DragEnter += ObjectBox_DragEnter;
			//	button.Drop += ObjectBox_Drop;
			//	button.MouseLeftButtonDown += ObjectBox_MouseLeftButtonDown;
			//}
			//MainPanel.Children.Add(button);
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
				textBox.SetBinding(TextBox.TextProperty, "ObjectData");
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
			// checkbox.IsChecked = (bool)data;
			MainPanel.Children.Add(checkbox);
		}
		private void ToDropBox()
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
