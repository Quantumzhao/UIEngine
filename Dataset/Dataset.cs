#define TEST_COLLECTION

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UIEngine;
using System.ComponentModel;

namespace Dataset
{
	internal class JsonHelper : IDisposable
	{
		readonly string _Filepath;
		readonly JObject _JObject;

		public JsonHelper(string path)
		{
			_Filepath = path;
			using (StreamReader reader = new StreamReader(_Filepath))
			{
				string json = reader.ReadToEnd();
				_JObject = JObject.Parse(json);
			}
		}

		public void Dispose() => File.WriteAllText(_Filepath, _JObject.ToString());

		public JToken GetProperty(string name) => _JObject.Property(name).Value;

		public void SetProperty<T>(string name, JToken value) => _JObject.Property(name).Value = value;

		public void AddProperty(string name, JToken value) => _JObject.Add(name, value);

		public IEnumerable<JProperty> ListProperties() => _JObject.Properties();

		public bool Has(string propertyName) => _JObject.Properties().Where(p => p.Name == propertyName).Count() != 0;
	}

	public static class Dataset
	{
#if !TEST_COLLECTION
		[Visible("Alice")]
		public static Person Alice { get; set; }
		public static void Init()
		{
			Alice = new Person()
			{
				Name = "Alice",
				Phone = "12345678",
				Address = new Address()
				{
					Country = "US",
					State = "Alaska"
				}
			};
		}
#else
		[Visible(nameof(People))]
		public static List<Person> People { get; set; } = new List<Person>();

		public static void Init()
		{
			People.Add(
				new Person()
				{
					Name = "Alice",
					Phone = "12345678",
					Address = new Address()
					{
						Country = "US",
						State = "Alaska"
					}
				}
			);
			People.Add(
				new Person()
				{
					Name = "Bob",
					Phone = "87654321",
					Address = new Address()
					{
						Country = "UK",
						State = "Portmouth"
					}
				}
			);
		}
#endif
	}

	public class Person : INotifyPropertyChanged
	{
#if !TEST_COLLECTION
		[Visible(nameof(Get))]
		public static Person Get(string name)
		{
			return name == "Alice" ? Dataset.Alice : null;
		}
#endif
		[Visible(nameof(Add), Header = "Add")]
		public static int Add([ParamInfo("num1")]int num1, [ParamInfo("num2")]int num2)
		{
			return num1 + num2;
		}

		private string _Name;

		public event PropertyChangedEventHandler PropertyChanged;

		[Visible(nameof(Name))]
		public string Name
		{
			get => _Name;
			set
			{
				_Name = value;
				//Dashboard.NotifyPropertyChanged(this, nameof(Name), value);
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
			}
		}

		[Visible(nameof(Phone))]
		public string Phone 
		{ 
			get; 
			set; 
		}

		[Visible(nameof(Address))]
		public Address Address { get; set; }

		public override string ToString()
		{
			return Name;
		}
	}

	public class Address
	{
		[Visible("Country")]
		public string Country { get; set; }

		[Visible("State")]
		public string State { get; set; }
	}
}
