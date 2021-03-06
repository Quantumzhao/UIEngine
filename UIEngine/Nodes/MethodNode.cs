﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UIEngine.Core;

namespace UIEngine.Nodes
{
	public class MethodNode : Node
	{
		private const string _PARAM_EMPTY_ERROR = "Some parameters are empty";

		internal static MethodNode Create(ObjectNode parent, MethodInfo methodInfo)
		{
			var methodNode = new MethodNode();
			methodNode.Parent = parent;
			methodNode._Body = methodInfo;
			var attr = methodInfo.GetCustomAttribute<VisibleAttribute>();
			methodNode.Name = attr.Name;
			methodNode.Header = attr.Header;
			methodNode.Description = attr.Description;
			methodNode.Signatures = methodInfo.GetParameters()
				.Select(p => ObjectNode.Create(p.ParameterType, p.GetCustomAttribute<DescriptiveInfoAttribute>()))
				.ToList();
			methodNode.Succession = ObjectNode.Create(methodInfo.ReturnType, attr);
			if (parent != null)
			{
				parent.Succession = methodNode;
			}

			return methodNode;
		}

		public ObjectNode ReturnNode => Succession as ObjectNode;
		public List<ObjectNode> Signatures { get; set; }
		private MethodInfo _Body;

		protected override string Preview
		{
			get => _Preview;
			set => _Preview = value;
		}

		/// <summary>
		///		Invoke and store return object to return node
		/// </summary>
		/// <returns>return object</returns>
		public ObjectNode Invoke()
		{
			// check if any parameter is empty
			if (!Signatures.All(n => !n.IsEmpty))
			{
				Dashboard.RaiseWarningMessage(this, _PARAM_EMPTY_ERROR);
				throw new InvalidOperationException(_PARAM_EMPTY_ERROR);
			}
			var objectData = _Body.Invoke(
				Parent?.ObjectData,
				Signatures.Select(p => p.InstantiateSuccession().ObjectData).ToArray()
			);

			ReturnNode.ObjectData = objectData;

			return ReturnNode;
		}

		/// <summary>
		/// 	Checks if the parameter can be assigned by the argument
		/// </summary>
		/// <param name="index">
		/// 	the index of the parameter that is to be assigned
		/// </param>
		public bool CanAssignArgument(object argument, int index)
		{
			return CanAssignArgument(argument, Signatures[index]);
		}
		public bool CanAssignArgument(object argument, ObjectNode parameter)
		{
			return parameter.IsAssignableFrom(argument.GetType());
		}

		public bool SetParameter(object argument, ObjectNode parameter)
		{
			if (CanAssignArgument(argument, parameter))
			{
				parameter.ObjectData = argument;
				return true;
			}
			return false;
		}
		public bool SetParameter(object argument, int index)
		{
			return SetParameter(argument, Signatures[index]);
		}

		/// <summary>
		/// 	Get the candidate arguments for a specified parameter
		/// </summary>
		/// <param name="index">
		/// 	the index of the specified parameter
		/// </param>
		/// <returns>
		/// 	A dictionary of all the candidate root nodes. 
		/// 	Each node corresponds to a boolean value which indicates 
		/// 	whether the type of the node matches parameter type
		/// </returns>
		public Dictionary<Node, bool> GetCandidates(int index)
		{
			var candidates = new Dictionary<Node, bool>();
			foreach (var node in Dashboard.GetRootNodes<Node>())
			{
				candidates.Add(node, CanAssignArgument(node, index));
			}
			return candidates;
		}

		internal override ObjectNode InstantiateSuccession()
		{
			if (ReturnNode.SourceObjectInfo.ReflectedType.Equals(typeof(void)))
			{
				return ReturnNode;
			}
			else
			{
				return ReturnNode.InstantiateSuccession();
			}
		}
	}
}