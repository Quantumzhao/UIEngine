using System;
using System.Collections.Generic;
using System.Text;

namespace UIEngine
{
	public class FlattenedClass
	{
		public List<object> Properties { get; } = new List<object>();
		public List<Delegate> Methods { get; } = new List<Delegate>();
	}
}
