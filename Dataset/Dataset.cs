using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UIEngine;

namespace Dataset
{
	internal class JsonHelper : IDisposable
	{
		string filepath;
		JObject jObject;

		public JsonHelper(string path)
		{
			filepath = path;
			using (StreamReader reader = new StreamReader(filepath))
			{
				string json = reader.ReadToEnd();
				jObject = JObject.Parse(json);
			}
		}

		public void Dispose() => File.WriteAllText(filepath, jObject.ToString());

		public JToken GetProperty(string name) => jObject.Property(name).Value;

		public void SetProperty<T>(string name, JToken value) => jObject.Property(name).Value = value;

		public void AddProperty(string name, JToken value) => jObject.Add(name, value);

		public IEnumerable<JProperty> ListProperties() => jObject.Properties();

		public bool Has(string propertyName) => jObject.Properties().Where(p => p.Name == propertyName).Count() != 0;
	}

	[Visible(nameof(Dataset))]
	public static class Dataset
	{
		[Visible(nameof(People))]
		public static List<Person> People { get; } = new List<Person>();
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
						Country = "GB",
						State = "Portmouth"
					}
				}
			);
		}
	}

	public class Person
	{
		[Visible(nameof(Name))]
		public string Name { get; set; }
		[Visible(nameof(Phone))]
		public string Phone { get; set; }
		[Visible(nameof(Address))]
		public Address Address { get; set; }
	}

	public class Address
	{
		[Visible(nameof(Country))]
		public string Country { get; set; }
		[Visible(nameof(State))]
		public string State { get; set; }
	}
}
