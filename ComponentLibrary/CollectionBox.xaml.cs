﻿using System;
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

		public IBox VisualChild { get; set; }

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

		public IBox Host
		{
			get => this;
			set => throw new InvalidOperationException();
		}

		public static readonly DependencyProperty ParentContainerProperty
			= DependencyProperty.Register(nameof(ParentContainer), typeof(UIPanel), typeof(CollectionBox));
		public UIPanel ParentContainer
		{
			get => GetValue(ParentContainerProperty) as UIPanel;
			set => SetValue(ParentContainerProperty, value);
		}

		private void Initialize()
		{
			if (!CollectionNode.Is_2D)
			{
				var column = new DataGridTemplateColumn();
				column.Header = "Elements";
				column.CellTemplate = FindResource("Template") as DataTemplate;
				MainDataGrid.Columns.Add(column);
				MainDataGrid.ItemsSource = _CollectionNode;
			}
			else if (CollectionNode.GetObjectData<object>() is IDictionary)
			{
				throw new NotImplementedException();
			}
			else
			{
				throw new NotImplementedException();
			}
		}

		public void RemoveSelf()
		{
			VisualChild?.RemoveSelf();
			Removed?.Invoke(this);
		}
	}
}
