using System;
using System.Windows;
using System.Windows.Controls;
using UIEngine;

namespace ComponentLibrary
{
	/// <summary>
	/// Interaction logic for MethodBox.xaml
	/// </summary>
	public partial class MethodBox : UserControl, IBox
	{
		public event RemovedHandler Removed;

		public MethodNode _MethodNode = null;
		public MethodNode MethodNode
		{
			get => _MethodNode;
			set
			{
				if (_MethodNode != value)
				{
					_MethodNode = value;
					_Initialize();
				}
			}
		}

		public IBox VisualChild { get; set; }

		public IBox Host { get; set; }
		public UIPanel ParentContainer { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

		public MethodBox()
		{
			InitializeComponent();
		}

		private void _Initialize()
		{
			for (int i = 0; i < MethodNode.Signatures.Count; i++)
			{
				ParaPanel.Children.Add(Utility.CreateBox(MethodNode.Signatures[i]) as UIElement);
				ParaPanel.Children.Add(new TextBlock() { Text = ", " });
			}
			if (ParaPanel.Children.Count > 0)
			{
				ParaPanel.Children.RemoveAt(ParaPanel.Children.Count - 1);
			}
		}

		private void ChangeArg_Int(object sender, ObjectNode arg)
		{
			if (int.TryParse((sender as TextBox).Text, out int result))
			{
				arg.ObjectData = result;
			}
			else
			{
				throw new ArgumentException("Invalid integer");
			}
		}

		private void ChangeArg_Double(object sender, ObjectNode arg)
		{
			if (double.TryParse((sender as TextBox).Text, out double result))
			{
				arg.ObjectData = result;
			}
			else
			{
				throw new ArgumentException("Invalid double");
			}
		}

		private void Execute_Click(object sender, RoutedEventArgs e)
		{
			var control = MainPanel.Children[MainPanel.Children.Count - 1];
			if (control is ObjectBox)
			{
				MainPanel.Children.Remove(control);
			}

			var objectBox = Utility.CreateBox(MethodNode.Invoke()) as ObjectBox;
			if (objectBox != null)
			{
				MainPanel.Children.Add(objectBox);
			}
		}

		public void RemoveSelf()
		{
			VisualChild?.RemoveSelf();
			Removed?.Invoke(this);
		}
	}
}
