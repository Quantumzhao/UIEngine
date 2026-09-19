using System.Xml.Linq;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class ProjectReferenceGraphTests
{
    private const string _LEGACY_RUNTIME_PROJECT = "UIEngine/UIEngine.csproj";

    [Fact]
    public void ProjectReferencesFollowIntendedDependencyDirection()
    {
        var repositoryRoot = _FindRepositoryRoot();
        var expectedReferences = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Frontend/Cli/Cli.csproj"] =
            [
                "Core/Core.csproj",
                "Examples/CyclicDomain/CyclicDomain.csproj",
            ],
            ["Core/Core.csproj"] = Array.Empty<string>(),
            ["Examples/CyclicDomain/CyclicDomain.csproj"] =
            [
                "Core/Core.csproj",
            ],
        };

        var implementationProjects = Directory
            .EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(path => _NormalizePath(Path.GetRelativePath(repositoryRoot, path)))
            .Where(path =>
                path.StartsWith("Core/", StringComparison.Ordinal) ||
                path.StartsWith("Examples/", StringComparison.Ordinal) ||
                path.StartsWith("Frontend/", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expectedReferences.Keys.OrderBy(static path => path, StringComparer.Ordinal),
            implementationProjects);

        foreach (var project in expectedReferences)
        {
            var actualReferences = _ReadProjectReferences(repositoryRoot, project.Key);
            var expected = project.Value.OrderBy(static path => path, StringComparer.Ordinal);

            Assert.Equal(expected, actualReferences);
            Assert.DoesNotContain(_LEGACY_RUNTIME_PROJECT, actualReferences);
        }
    }

    private static string _FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UIEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static string[] _ReadProjectReferences(string repositoryRoot, string projectPath)
    {
        var projectFile = Path.Combine(repositoryRoot, projectPath);
        var projectDirectory = Path.GetDirectoryName(projectFile)
            ?? throw new InvalidOperationException($"Could not resolve the directory for {projectPath}.");
        var document = XDocument.Load(projectFile);

        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(reference => Path.GetFullPath(Path.Combine(projectDirectory, reference)))
            .Select(reference => _NormalizePath(Path.GetRelativePath(repositoryRoot, reference)))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string _NormalizePath(string path) => path.Replace('\\', '/');
}
