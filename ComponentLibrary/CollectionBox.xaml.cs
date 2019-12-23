using System.Collections;
using System.Windows;
using System.Windows.Controls;
using UIEngine;

namespace ComponentLibrary
{
	/// <summary>
	/// Interaction logic for CollectionBox.xaml
	/// </summary>
	public partial class CollectionBox : UserControl, IBox
	{
		public static event RemovedHandler Removed;

		public CollectionBox()
		{
			InitializeComponent();
		}

		public IBox Child { get; set; }

		private CollectionNode _CollectionNode;
		public CollectionNode CollectionNode
		{
			get => _CollectionNode;
			set
			{
				_CollectionNode = value;
				if (value != null)
				{
					Initialize();
				}
			}
		}

		private void Initialize()
		{
			if (!CollectionNode.Is_2D)
			{
				int columns = (CollectionNode[0] as ICollection).Count;
				for (int i = 1; i <= columns; i++)
				{
					var column = new DataGridTemplateColumn();
					column.Header = i;
					column.CellTemplate = FindResource("Template") as DataTemplate;
					MainDataGrid.Columns.Add(column);
				}
				MainDataGrid.ItemsSource = _CollectionNode.Elements;
			}
			else if (CollectionNode.GetObjectData<object>() is IDictionary)
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
