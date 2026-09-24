using System.Xml.Linq;
using UIEngine.Core;
using UIEngine.Frontend.Tui;
using Xunit;

namespace UIEngine.Frontend.Tui.Tests;

[Collection("Terminal application")]
public sealed class TuiBoundaryTests
{
    [Fact]
    public void WorkspaceUsesCopiedOptionsAndLeavesCallerOwnedHostAlive()
    {
        using var host = new UIEngineHost();
        host.SetRoot("world", new object());
        var options = new TuiFrontendOptions
        {
            ApplicationTitle = "Test application",
            InitialPath = LogicalPath.Root.Append("world"),
            CollectionWindowSize = 12,
        };

        using var workspace = new TuiWorkspace(host, options);

        Assert.Equal(options, workspace.Options);
        Assert.True(host.ResolveRootNode("world").IsRight);

        workspace.Dispose();

        Assert.True(host.ResolveRootNode("world").IsRight);
    }

    [Fact]
    public void FullscreenRunnerExposesOnlyHostAndOptions()
    {
        var run = Assert.Single(typeof(TuiFrontend).GetMethods(), static method =>
            method.Name == nameof(TuiFrontend.RunAsync));

        Assert.Equal([typeof(UIEngineHost), typeof(TuiFrontendOptions)],
            run.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void WorkspaceIsTheOnlyPublicFrontendLifetime()
    {
        var workspaceType = typeof(TuiWorkspace);

        Assert.Null(workspaceType.GetProperty("Session"));
        Assert.DoesNotContain(workspaceType.Assembly.GetTypes(), static type =>
            type.Name is "TuiSession" or "TuiIntent" or "QueuedTuiIntent" or
                "ITuiPresentationDispatcher");
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
