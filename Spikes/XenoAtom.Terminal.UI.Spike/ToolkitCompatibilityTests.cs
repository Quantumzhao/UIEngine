using UIEngine.Core;
using UIEngine.Core.Attributes;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Hosting;
using XenoAtom.Terminal.UI.Templating;
using Xunit;

namespace UIEngine.Spikes.XenoAtomTerminalUi;

public sealed class ToolkitCompatibilityTests
{
    [Fact]
    public async Task FullscreenHostSupportsInputFocusResizeDispatcherAndDeterministicExit()
    {
        var backend = new InMemoryTerminalBackend(new TerminalSize(80, 25));
        using var terminal = Terminal.Open(backend, new TerminalOptions(), force: true);
        var status = new State<string>("Waiting", "status");
        var first = new Button("First");
        var second = new Button("Second");
        var root = new VStack(first, second, new TextBlock(() => status.Value));
        var clicked = false;
        var resizedWidth = 0;
        var backgroundThreadId = 0;
        var dispatcherThreadId = 0;
        var phase = 0;

        second.ClickRouted += (_, _) => clicked = true;

        await terminal.Instance.RunAsync(
            root,
            async context =>
            {
                switch (phase)
                {
                    case 0:
                        Assert.Same(first, context.App.FocusedElement);
                        backgroundThreadId = await Task.Run(async () =>
                        {
                            var threadId = Environment.CurrentManagedThreadId;
                            await context.App.Dispatcher.InvokeAsync(() =>
                            {
                                dispatcherThreadId = Environment.CurrentManagedThreadId;
                                status.Value = "Marshalled";
                            });
                            return threadId;
                        });
                        backend.SetSize(new TerminalSize(40, 12), true);
                        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Tab });
                        phase = 1;
                        return TerminalLoopResult.Continue;
                    case 1 when ReferenceEquals(context.App.FocusedElement, second):
                        resizedWidth = root.Bounds.Width;
                        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
                        phase = 2;
                        return TerminalLoopResult.Continue;
                    case 2 when clicked:
                        return TerminalLoopResult.Stop;
                    default:
                        return TerminalLoopResult.Continue;
                }
            },
            new TerminalRunOptions(),
            default).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(clicked);
        Assert.Equal(40, resizedWidth);
        Assert.NotEqual(backgroundThreadId, dispatcherThreadId);
        Assert.Contains("Marshalled", backend.GetOutText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VisualCanBeComposedAndHostedInsideAnExistingTerminalApp()
    {
        var backend = new InMemoryTerminalBackend(new TerminalSize(60, 15));
        using var terminal = Terminal.Open(backend, new TerminalOptions(), force: true);
        var workspace = new VStack(new TextBlock("Embedded workspace"));
        var hostVisual = new VStack(new TextBlock("Consumer shell"), workspace);
        await using var app = new TerminalApp(hostVisual, terminal.Instance, new TerminalAppOptions
        {
            HostKind = TerminalHostKind.Fullscreen,
        });

        app.Post(app.Stop);
        await app.RunAsync(default).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(hostVisual, app.ContentRoot);
        Assert.Same(hostVisual, workspace.Parent);
    }

    [Fact]
    public async Task BoundedCoreWindowsCanReplaceListVisualsWithoutEnumeratingTheCollection()
    {
        var source = new _CountingCollection();
        using var host = new UIEngineHost(new UIEngineHostOptions { MaxCollectionItems = 8 });
        host.SetRoot("model", source);
        var resolved = host.ResolveRootNode("model");
        var collection = Assert.IsAssignableFrom<ICollectionNode>(Assert.Single(
            ((IObjectNode)((ResolvedNode)resolved).Node).Members));
        var list = new ListBox<string>();
        var templateProbe = new _TemplateProbe();
        list.ItemTemplate = new DataTemplate<string>(
            _TemplateProbe.Display,
            null,
            templateProbe.TryUpdate,
            null);
        var enumerationCounts = new List<int>();
        var phase = 0;
        var backend = new InMemoryTerminalBackend(new TerminalSize(40, 10));
        using var terminal = Terminal.Open(backend, new TerminalOptions(), force: true);

        await terminal.Instance.RunAsync(
            list,
            async _ =>
            {
                switch (phase)
                {
                    case 0:
                        _ReplaceItems(list, (CollectionSlice)collection.ReadEntries(0, 3));
                        enumerationCounts.Add(source.MoveNextCount);
                        phase = 1;
                        return TerminalLoopResult.Continue;
                    case 1:
                        _UpdateItems(list, (CollectionSlice)collection.ReadEntries(3, 3));
                        enumerationCounts.Add(source.MoveNextCount);
                        phase = 2;
                        return TerminalLoopResult.Continue;
                    default:
                        return TerminalLoopResult.Stop;
                }
            },
            new TerminalRunOptions(),
            default).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([4, 11], enumerationCounts);
        Assert.Equal(["3", "4", "5"], list.Items);
        Assert.True(templateProbe.UpdateCount > 0);
        Assert.True(source.MoveNextCount < _CountingCollection.ItemCount);
    }

    private static void _ReplaceItems(ListBox<string> list, CollectionSlice slice)
    {
        list.Items.Clear();
        foreach (var entry in slice.Entries.Cast<ScalarCollectionEntry>())
        {
            list.Items.Add(entry.Value.ToString()!);
        }
    }

    private static void _UpdateItems(ListBox<string> list, CollectionSlice slice)
    {
        Assert.Equal(list.Items.Count, slice.Entries.Count);
        for (var index = 0; index < slice.Entries.Count; index++)
        {
            list.Items[index] = ((ScalarCollectionEntry)slice.Entries[index]).Value.ToString()!;
        }
    }

    private sealed class _TemplateProbe
    {
        public int UpdateCount { get; private set; }

        public static TextBlock Display(
            DataTemplateValue<string> value,
            in DataTemplateContext _) => new TextBlock(value.GetValue());

        public bool TryUpdate(
            Visual visual,
            DataTemplateValue<string> value,
            in DataTemplateContext _)
        {
            if (visual is not TextBlock text)
            {
                return false;
            }

            text.Text = value.GetValue();
            UpdateCount++;
            return true;
        }
    }

    private sealed class _CountingCollection
    {
        public const int ItemCount = 10_000;

        public int MoveNextCount { get; private set; }

        [Children]
        public IEnumerable<int> Items => _Enumerate();

        private IEnumerable<int> _Enumerate()
        {
            for (var index = 0; index < ItemCount; index++)
            {
                MoveNextCount++;
                yield return index;
            }
        }
    }
}
