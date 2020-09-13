﻿using System;
using System.Collections.Generic;
using System.Text;

namespace UIEngine.Nodes
{
	public abstract class ExtensionFunctionNode : Node
	{
		public ObjectNode ReturnNode { get; set; }
		public abstract ObjectNode Invoke();
	}
}
