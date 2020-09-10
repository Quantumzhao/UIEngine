using System;
using System.Collections.Generic;
using System.Text;

namespace UIEngine
{
	public abstract class DescriptiveInfoAttribute : Attribute
	{
		protected DescriptiveInfoAttribute(string name, string header, string description)
		{
			Name = name ?? header;
			Header = header;
			Description = description;
		}

		/// <summary>
		///		The unique name that is used to locate and find a node. 
		///		It is optional, but the node cannot be found if it is left blank
		/// </summary>
		public string Name { get; }
		public string Header { get; set; }
		public string Description { get; set; }
	}

	/* There are 3 ways in providing descriptive information, which are:
	 * 1. Conditional weak table, i.e. appended pseudo-attribute
	 *    This is best for objects dynamically added into a collection
	 * 2. IVisible interface
	 *    This is the best way of storing/accessing this information, 
	 *    but is limited to user defined types
	 * 3. Visible Attribute
	 *    This is an alternative way for properties with non user defined types
	 * They are with priority order: 1 > 2 > 3 */
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
	public sealed class VisibleAttribute : DescriptiveInfoAttribute
	{
		/// <summary>
		///		Initializing the visibility tag. 
		///		Any member marked with "Visible" can be accessed via front end
		/// </summary>
		/// <param name="name">
		///		Name of this property. If <paramref name="header"/> is left blank, 
		///		<paramref name="name"/> will be the default string for <paramref name="header"/>
		/// </param>
		/// <param name="header">
		///		Name of the member. It has to be the exact name of the member, i.e. <c>nameof(member)</c>
		/// </param>
		/// <param name="description">
		///		Some descriptions (optional)
		/// </param>
		public VisibleAttribute(string header, string description = "", string name = null)
			: base(name, header, description) { }

		/// <summary>
		///		Indicates whether this feature is enabled
		/// </summary>
		public bool IsFeatureEnabled { get; set; } = true;
		/// <summary>
		///		Indicates whether the control generated from the object node that is marked by this is enabled initially
		/// </summary>
		public bool IsControlEnabled { get; set; } = true;
		public Func<object, string> PreviewExpression { get; set; } = o => o.ToString();
	}

	[AttributeUsage(AttributeTargets.GenericParameter | AttributeTargets.Parameter)]
	public sealed class ParamInfoAttribute : DescriptiveInfoAttribute
	{
		public ParamInfoAttribute(string header, string description = "", string name = "")
			: base(name, header, description) { }
	}
}
