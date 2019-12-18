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
	/// Interaction logic for MethodBox.xaml
	/// </summary>
	public partial class MethodBox : UserControl, IBox
	{
		public static event RemovedHandler Removed;

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

		public IBox Child { get; set; }

		public MethodBox()
		{
			InitializeComponent();
		}

		private void _Initialize()
		{
			for (int i = 0; i < MethodNode.Signatures.Count; i++)
			{
				//if (arg.Type.IsAssignableFrom(typeof(int)) ||
				//	arg.Type.IsAssignableFrom(typeof(double)) ||
				//	arg.Type.IsAssignableFrom(typeof(string)))
				//{
				//	var textBox = new TextBox();
				//	{
				//		textBox.MinWidth = 50;
				//		textBox.DataContext = arg;
				//		if (arg.Type.IsAssignableFrom(typeof(int)))
				//		{
				//			textBox.TextChanged += (sender, e) => ChangeArg_Int(sender, arg);
				//		}
				//		else if (arg.Type.IsAssignableFrom(typeof(double)))
				//		{
				//			textBox.TextChanged += (sender, e) => ChangeArg_Double(sender, arg);
				//		}
				//		else if (arg.Type.IsAssignableFrom(typeof(string)))
				//		{
				//			textBox.TextChanged += (ts, te) => arg.Data = (ts as TextBox).Text;
				//		}
				//		textBox.TextChanged += (sender, e) =>
				//		{
				//			if (MethodNode.Signature.TrueForAll(s => s.Data != null))
				//			{
				//				Execute.IsEnabled = true;
				//			}
				//		};
				//	}
				//	ParaPanel.Children.Add(textBox);
				//}
				//else
				//{
				//	throw new NotImplementedException();
				//}
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
			Child?.RemoveSelf();
			Removed?.Invoke(this);
		}
	}
}
