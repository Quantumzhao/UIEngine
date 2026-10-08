using UIEngine.Core;
using UIEngine.Core.Attributes;

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
        var host = new UIEngineHost();
        host.SetRoot("model", model ?? new TuiTestModel());
        return host;
    }

    public static LogicalPath Path(params string[] members)
    {
        var path = LogicalPath.Root;
        foreach (var member in members)
        {
            path = path.Append(member);
        }

        return path;
    }
}

internal sealed class TuiTestChild
{
    [Expose]
    public int Value { get; set; } = 7;
}
