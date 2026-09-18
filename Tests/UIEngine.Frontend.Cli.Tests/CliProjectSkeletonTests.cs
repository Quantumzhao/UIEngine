using System.Reflection;
using Xunit;

namespace UIEngine.Frontend.Cli.Tests;

public sealed class CliProjectSkeletonTests
{
    [Fact]
    public void CliAssemblyHasAnEntryPoint()
    {
        var assembly = Assembly.Load(new AssemblyName("UIEngine.Frontend.Cli"));

        Assert.NotNull(assembly.EntryPoint);
    }
}
