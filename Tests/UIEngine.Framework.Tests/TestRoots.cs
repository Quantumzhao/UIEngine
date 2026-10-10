using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace UIEngine.Framework.Tests;

internal static class TestRoots
{
    [Root]
    public static object model = new();

    [Root]
    public static object economy = new();

    [Root]
    public static object world = new();

    [Root]
    public static object catalog = new();

    [Root]
    public static int scalar = 7;

    [Root]
    public static IReadOnlyList<int> items = [1, 2, 3];

    [Root]
    [Action]
    public static int run(int value) => value + 1;

    public static Guid Set(string name, object value)
    {
        switch (name)
        {
            case "model":
                model = value;
                break;
            case "economy":
                economy = value;
                break;
            case "world":
                world = value;
                break;
            case "catalog":
                catalog = value;
                break;
            default:
                throw new ArgumentException($"Unknown test root '{name}'.", nameof(name));
        }

        return Id(name);
    }

    public static Guid Id(string name)
    {
        foreach (var id in UIEngineHost.Instance.RootIds)
        {
            var path = LogicalPath.Empty.Append(new RootLogicalPathSegment(id));
            var names = path.ResolveNames();
            if (names.IsRight && StringComparer.Ordinal.Equals(
                names.IfLeft(static error => throw new InvalidOperationException(error.Message))[0],
                name))
            {
                return id;
            }
        }

        throw new InvalidOperationException($"Test root '{name}' was not discovered.");
    }

    public static RootLogicalPathSegment Segment(string name) => new(Id(name));

    public static RootPathSegmentSnapshot Snapshot(string name) => new(Id(name));
}
