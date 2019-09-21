using System;
using System.Collections.Generic;
using System.Text;

namespace UIEngine
{
	public abstract class VisualElement
	{
		public string Name = "";
		public VisualElement Parent;
	}

	public class VisualObject : VisualElement
	{
		public VisualObject(VisualObject parent, ObjectNode node)
		{
			Parent = parent;
		}

		public Dictionary<string, VisualObject> Properties = new Dictionary<string, VisualObject>();
		public Dictionary<string, VisualExpression> Methods = new Dictionary<string, VisualExpression>();

		/// <summary>
		///		And change the current visual object to the new object specified by its name
		/// </summary>
		/// <param name="name">
		///		Name of the object
		/// </param>
		/// <returns>
		///		The new object
		/// </returns>
		public VisualObject AccessProperty(string name)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		///		And change the current visual object to the return value of the method
		/// </summary>
		/// <param name="name">
		///		name of the method
		/// </param>
		/// <returns>
		///		The method and its info about signature and etc
		/// </returns>
		public VisualExpression AcessMethod(string name)
		{
			throw new NotImplementedException();
		}
	}

	public class VisualExpression : VisualElement
	{
		public List<ParameterTuple> Parameters = new List<ParameterTuple>();

		public object Execute()
		{
			throw new NotImplementedException();
		}

		public class ParameterTuple
		{
			public string Name;
			public Type Type;
			public object Value;
			public VisualExpression BindingExpression;
		}
	}

	public class VisualStatement
	{
		public VisualObject Root;
		public VisualElement Current;
		public void SetCurrent(VisualElement visualElement)
		{
			Current = visualElement;
		}
	}
}
