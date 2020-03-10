using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UIEngine;

namespace TestWindow
{
	public class TestClass
	{
		[Visible(nameof(TestStaticString))]
		public static string TestStaticString { get; set; } = "Hello";

		[Visible(nameof(TestCollection))]
		public static ObservableCollection<DataPiece> TestCollection { get; set; }
			= new ObservableCollection<DataPiece>() 
			{ 
				new DataPiece() 
				{ 
					InstanceString = "s", 
					InstanceInt = 1 
				} 
			};

		[Visible(nameof(AddData))]
		public static void AddData(string s, int i)
		{
			TestCollection.Add(new DataPiece() { InstanceString = s, InstanceInt = i });
		}

		[Visible(nameof(AddCompleteData))]
		public static void AddCompleteData(DataPiece d)
		{
			TestCollection.Add(d);
		}
	}

	public class DataPiece
	{
		[Visible(nameof(InstanceString))]
		public string InstanceString { get; set; } = "World";

		[Visible(nameof(InstanceInt))]
		public int InstanceInt { get; set; } = 42;
	}
}
