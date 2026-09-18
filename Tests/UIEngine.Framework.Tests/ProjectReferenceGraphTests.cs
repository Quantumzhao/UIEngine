using System.Xml.Linq;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class ProjectReferenceGraphTests
{
    private const string LegacyRuntimeProject = "UIEngine/UIEngine.csproj";

    [Fact]
    public void ProjectReferencesFollowIntendedDependencyDirection()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expectedReferences = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Framework/UIEngine.Frontend.Cli/UIEngine.Frontend.Cli.csproj"] =
            [
                "Framework/UIEngine.Framework/UIEngine.Framework.csproj",
            ],
            ["Framework/UIEngine.Framework/UIEngine.Framework.csproj"] = Array.Empty<string>(),
            ["Samples/UIEngine.Samples.CyclicDomain/UIEngine.Samples.CyclicDomain.csproj"] =
            [
                "Framework/UIEngine.Framework/UIEngine.Framework.csproj",
            ],
        };

        var implementationProjects = Directory
            .EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(path => NormalizePath(Path.GetRelativePath(repositoryRoot, path)))
            .Where(path =>
                path.StartsWith("Framework/", StringComparison.Ordinal) ||
                path.StartsWith("Samples/", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expectedReferences.Keys.OrderBy(static path => path, StringComparer.Ordinal),
            implementationProjects);

        foreach (var project in expectedReferences)
        {
            var actualReferences = ReadProjectReferences(repositoryRoot, project.Key);
            var expected = project.Value.OrderBy(static path => path, StringComparer.Ordinal);

            Assert.Equal(expected, actualReferences);
            Assert.DoesNotContain(LegacyRuntimeProject, actualReferences);
        }
    }

    private static string FindRepositoryRoot()
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

    private static string[] ReadProjectReferences(string repositoryRoot, string projectPath)
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
            .Select(reference => NormalizePath(Path.GetRelativePath(repositoryRoot, reference)))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
