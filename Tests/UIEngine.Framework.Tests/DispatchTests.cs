using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class SynchronousInteractionTests
{
    [Fact]
    public void LiveOperationsAreSynchronousAndAllowReentrantAccess()
    {
        using var host = new UIEngineHost();
        var model = new _ReentrantModel();
        host.SetRoot("model", model);
        model.Attach(host);
        var resolved = host.ResolveRootNode("model");
        var valueNode = Assert.Single(((IObjectNode)resolved.RightValue().Node).Members);
        Assert.True(valueNode is IReadableValueNode);
        var value = (IReadableValueNode)valueNode;

        var read = value.ReadValue();

        Assert.True(read.IsRight);
        Assert.Equal(17, read.RightValue());
        Assert.True(model.ReentrantDescribeSucceeded);
    }

    private sealed class _ReentrantModel
    {
        private UIEngineHost? _Host;
        public bool ReentrantDescribeSucceeded { get; private set; }

        [Expose]
        public int Value
        {
            get
            {
                ReentrantDescribeSucceeded = _Host!.ResolveRootNode("model").IsRight;
                return 17;
            }
        }

        public void Attach(UIEngineHost host)
        {
            _Host = host;
        }
    }

}
