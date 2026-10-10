using System.Xml.Linq;
using UIEngine.Core;
using UIEngine.Frontend.Tui;
using Xunit;

namespace UIEngine.Frontend.Tui.Tests;

[Collection("Terminal application")]
public sealed class TuiBoundaryTests
{
    [Fact]
    public void FrontendOwnsWorkspaceButNotHostAndCopiesOptions()
    {
        using var host = TuiTestModel.CreateHost();
        var options = new TuiFrontendOptions
        {
            ApplicationTitle = "Test application",
            Startup = new AddNavigator(TuiTestModel.Path("model")),
            CollectionWindowSize = 12,
        };
        var tui = TuiFrontend.CreateWorkspace(options);
        var coreWorkspace = tui.Workspace;

        Assert.NotSame(options, tui.Options);
        Assert.Equal(options, tui.Options);
        Assert.Null(typeof(UIEngineWorkspace).GetProperty("Host"));
        Assert.NotEqual(Guid.Empty, Assert.Single(coreWorkspace.Navigators).Id);
        Assert.Null(typeof(Navigator).GetProperty("Name"));

        tui.Dispose();
        tui.Dispose();

        Assert.Empty(coreWorkspace.Navigators);
        Assert.True(host.ResolveRootNode(TuiTestModel.RootId).IsRight);
    }

    [Fact]
    public void PublicEntryPointsAcceptOnlyOptions()
    {
        var creates = typeof(TuiFrontend).GetMethods()
            .Where(static method => method.Name == nameof(TuiFrontend.CreateWorkspace))
            .ToArray();
        var runs = typeof(TuiFrontend).GetMethods()
            .Where(static method => method.Name == nameof(TuiFrontend.RunAsync))
            .ToArray();

        var create = Assert.Single(creates);
        var run = Assert.Single(runs);
        Assert.Equal(
            [typeof(TuiFrontendOptions)],
            create.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.Equal(
            [typeof(TuiFrontendOptions)],
            run.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.Null(typeof(TuiFrontendOptions).GetProperty("InitialPath"));
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
        var tuiProject = XDocument.Load(Path.Combine(repositoryRoot, "Frontend", "Tui", "Tui.csproj"));
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
