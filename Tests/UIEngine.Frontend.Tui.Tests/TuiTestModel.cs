using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace UIEngine.Frontend.Tui.Tests;

internal sealed class TuiTestModel
{
    private readonly TuiTestChild _Child = new();

    public int ChildAccessThreadId { get; private set; }

    [Expose]
    public string Name { get; set; } = "model";

    [Children]
    public TuiTestChild Child
    {
        get
        {
            ChildAccessThreadId = Environment.CurrentManagedThreadId;
            return _Child;
        }
    }

    public static UIEngineHost CreateHost(TuiTestModel? model = null)
    {
        TuiTestRoots.Model = model ?? new TuiTestModel();
        var host = new UIEngineHost();
        return host;
    }

    public static Guid RootId => TuiTestRoots.Id;

    public static LogicalPath Path(params string[] members)
    {
        var path = LogicalPath.Empty.Append(new RootLogicalPathSegment(RootId));
        foreach (var member in members)
        {
            if (StringComparer.Ordinal.Equals(member, "model"))
            {
                continue;
            }

            path = path.Append(member);
        }

        return path;
    }
}

internal static class TuiTestRoots
{
    [Root]
    public static TuiTestModel Model { get; set; } = new();

    public static Guid Id
    {
        get
        {
            foreach (var id in UIEngineHost.Instance.RootIds)
            {
                var path = LogicalPath.Empty.Append(new RootLogicalPathSegment(id));
                var names = path.ResolveNames();
                if (names.IsRight && StringComparer.Ordinal.Equals(
                    names.IfLeft(static error => throw new InvalidOperationException(error.Message))[0],
                    nameof(Model)))
                {
                    return id;
                }
            }

            throw new InvalidOperationException("The TUI test root was not discovered.");
        }
    }

    public static RootPathSegmentSnapshot Snapshot => new(Id);
}

internal sealed class TuiTestChild
{
    [Expose]
    public int Value { get; set; } = 7;
}
