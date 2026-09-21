using System.Reflection;
using System.Xml.Linq;
using UIEngine.Core;
using UIEngine.Frontend.Tui;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using Xunit;

namespace UIEngine.Frontend.Tui.Tests;

[Collection("Terminal application")]
public sealed class TuiBoundaryTests
{
    [Fact]
    public void WorkspaceUsesCopiedOptionsAndLeavesCallerOwnedHostAlive()
    {
        using var host = new UIEngineHost();
        var options = new TuiFrontendOptions
        {
            ApplicationTitle = "Test application",
            InitialPath = LogicalPath.Root.Append("world"),
            CollectionWindowSize = 12,
        };

        using var workspace = TuiFrontend.CreateWorkspace(host, options);

        Assert.NotSame(options, workspace.Session.Options);
        Assert.Equal(options, workspace.Session.Options);
        Assert.False(workspace.Session.IsDisposed);
        Assert.False(host.IsDisposed);

        workspace.Dispose();

        Assert.True(workspace.Session.IsDisposed);
        Assert.False(host.IsDisposed);
    }

    [Fact]
    public void WorkspaceRejectsInvalidConfigurationAndDisposedHosts()
    {
        using var host = new UIEngineHost();

        Assert.Throws<ArgumentException>(() => TuiFrontend.CreateWorkspace(
            host,
            new TuiFrontendOptions { ApplicationTitle = " " }));
        Assert.Throws<ArgumentOutOfRangeException>(() => TuiFrontend.CreateWorkspace(
            host,
            new TuiFrontendOptions { CollectionWindowSize = 0 }));

        host.Dispose();

        Assert.Throws<ObjectDisposedException>(() => TuiFrontend.CreateWorkspace(host));
    }

    [Fact]
    public async Task FullscreenRunnerHonorsCancellationAndLeavesCallerOwnedHostAlive()
    {
        var backend = new InMemoryTerminalBackend(new TerminalSize(40, 10));
        using var terminal = Terminal.Open(backend, new TerminalOptions(), force: true);
        using var host = new UIEngineHost();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await TuiFrontend.RunAsync(host, cancellationToken: cancellation.Token);

        Assert.False(host.IsDisposed);
    }

    [Fact]
    public void ProjectReferencesPreserveFrontendAndDomainBoundaries()
    {
        var coreReferences = typeof(UIEngineHost).Assembly.GetReferencedAssemblies();
        var tuiAssembly = typeof(TuiFrontend).Assembly;
        var tuiReferences = tuiAssembly.GetReferencedAssemblies();

        Assert.DoesNotContain(coreReferences, static reference =>
            reference.Name is not null &&
            (reference.Name == "Tui" || reference.Name.StartsWith("XenoAtom.", StringComparison.Ordinal)));
        Assert.Null(tuiAssembly.EntryPoint);
        Assert.Contains(tuiReferences, static reference => reference.Name == "Core");
        Assert.Contains(tuiReferences, static reference => reference.Name == "XenoAtom.Terminal.UI");
        Assert.DoesNotContain(tuiReferences, static reference => reference.Name == "CyclicDomain");

        var repositoryRoot = _FindRepositoryRoot();
        var tuiProject = XDocument.Load(Path.Combine(
            repositoryRoot,
            "Frontend",
            "Tui",
            "Tui.csproj"));
        var tuiPackages = tuiProject.Descendants("PackageReference")
            .Select(static element => (
                Name: element.Attribute("Include")?.Value ??
                    throw new InvalidOperationException("A package reference is missing Include."),
                Version: element.Attribute("Version")?.Value ??
                    throw new InvalidOperationException("A package reference is missing Version.")))
            .ToArray();
        var tuiProjectReferences = tuiProject.Descendants("ProjectReference")
            .Select(static element => element.Attribute("Include")?.Value ??
                throw new InvalidOperationException("A project reference is missing Include."))
            .ToArray();

        Assert.Equal([("XenoAtom.Terminal.UI", "3.9.0")], tuiPackages);
        Assert.Equal(["../../Core/Core.csproj"], tuiProjectReferences);

        var consumerProject = XDocument.Load(Path.Combine(
            repositoryRoot,
            "Examples",
            "CyclicWorld.Tui",
            "CyclicWorld.Tui.csproj"));
        var outputType = consumerProject.Descendants("OutputType").Single().Value;
        var projectReferences = consumerProject.Descendants("ProjectReference")
            .Select(static element => element.Attribute("Include")?.Value ??
                throw new InvalidOperationException("A project reference is missing Include."))
            .ToArray();

        Assert.Equal("Exe", outputType);
        Assert.Contains("../../Core/Core.csproj", projectReferences);
        Assert.Contains("../../Frontend/Tui/Tui.csproj", projectReferences);
        Assert.Contains("../CyclicDomain/CyclicDomain.csproj", projectReferences);
    }

    private static string _FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UIEngine.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
