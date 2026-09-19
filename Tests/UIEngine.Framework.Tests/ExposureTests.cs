using UIEngine.Core.Attributes;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class ExposureTests
{
    [Fact]
    public void DiscoveryIncludesOnlyExplicitlyExposedMembers()
    {
        var members = ReflectionExposure
            .Discover(typeof(_ExposureFixture))
            .Select(static member => member.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(_ExposureFixture.Children),
                nameof(_ExposureFixture.Run),
                nameof(_ExposureFixture.Summary),
                nameof(_ExposureFixture.VisibleValue),
            ],
            members);
        Assert.DoesNotContain(nameof(_ExposureFixture.HiddenAction), members);
        Assert.DoesNotContain(nameof(_ExposureFixture.HiddenValue), members);
    }

    private sealed class _ExposureFixture
    {
        private int _ExecutionCount;

        [Expose]
        public string VisibleValue { get; } = "visible";

        public string HiddenValue { get; } = "hidden";

        [Children]
        public IReadOnlyList<object> Children { get; } = Array.Empty<object>();

        [Summary]
        public string Summary => VisibleValue;

        [Action]
        public void Run() => _ExecutionCount++;

        public void HiddenAction() => _ExecutionCount--;
    }
}
