using System;
using System.Collections;
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
	/// Interaction logic for CollectionBox.xaml
	/// </summary>
	public partial class CollectionBox : UserControl, IBox
	{
		public static event RemovedHandler Removed;
		public event Action<object, CollectionNode> ContentChanged;

		public CollectionBox()
		{
			InitializeComponent();
			ContentChanged += (me, newNode) => Initialize();
		}

		public IBox Child { get; set; }

		private CollectionNode _CollectionNode;
		public CollectionNode CollectionNode
		{
			get => _CollectionNode;
			set
			{
				CollectionNode = value;
				if (value != null)
				{
					ContentChanged?.Invoke(this, value);
				}
			}
		}

		private void Initialize()
		{
			if (!CollectionNode.Is_2D)
			{
				int columns = (CollectionNode.FormattedData[0] as ICollection).Count;
				for (int i = 1; i <= columns; i++)
				{
					var column = new DataGridTextColumn();
					column.Header = i;
					column.Binding = new Binding($"[{i.ToString()}]");
					MainDataGrid.Columns.Add(column);
				}
			}
			else if (CollectionNode.ObjectData is IDictionary)
			{

			}
			else
			{

			}
		}

		public void RemoveSelf()
		{
			Child?.RemoveSelf();
			Removed?.Invoke(this);
		}
	}
}
