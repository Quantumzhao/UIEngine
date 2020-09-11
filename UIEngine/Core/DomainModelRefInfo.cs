using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace UIEngine.Core
{
	public abstract class DomainModelRefInfo
	{
		internal DomainModelRefInfo(Type type, SourceReferenceType sourceReferenceType)
		{
			SourceReferenceType = sourceReferenceType;
			ReflectedType = type;
		}

		internal readonly SourceReferenceType SourceReferenceType;
		//internal readonly TypeSystem ObjectDataType;
		internal readonly Type ReflectedType;
	}
	public class OtherDomainModelRefInfo : DomainModelRefInfo
	{
		internal OtherDomainModelRefInfo(Type type, SourceReferenceType sourceReferenceType)
			: base(type, sourceReferenceType) { }
	}
	public class PropertyDomainModelRefInfo : DomainModelRefInfo
	{
		public readonly string PropertyName;
		public readonly PropertyInfo PropertyInfo;

		public PropertyDomainModelRefInfo(PropertyInfo info)
			: base(info.PropertyType, SourceReferenceType.Property)
		{
			PropertyInfo = info;
			PropertyName = PropertyInfo.Name;
		}
	}
	internal enum SourceReferenceType
	{
		Property,
		Enumerator,
		ReturnValue,
		parameter
	}
}
