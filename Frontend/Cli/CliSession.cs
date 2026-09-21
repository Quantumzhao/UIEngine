using System.Globalization;
using UIEngine.Core;

namespace UIEngine.Frontend.Cli;

/// <summary>Runs a serial command session over the concrete UIEngine runtime.</summary>
internal sealed class CliSession : IDisposable
{
    private const int DEFAULT_COLLECTION_LIMIT = 20;

    private static readonly string[] _COMMANDS =
    [
        "ls",
        "cd",
        "inspect",
        "get",
        "set",
        "call",
        "watch",
        "exit",
    ];

    private readonly UIEngineHost _Host;
    private readonly TextWriter _Output;
    private readonly HashSet<ObservationSubscription> _Subscriptions = [];
    private List<_LocationBinding> _Locations = [];
    private bool _Disposed;

    public CliSession(UIEngineHost host, TextWriter output)
    {
        _Host = host;
        _Output = output;
    }

    internal ObjectHandle? CurrentHandle => _Locations.Count == 0
        ? null
        : _Locations[^1].LastResolvedHandle;

    internal string CurrentPath => _Locations.Count == 0 ? "/" : _Locations[^1].Path.ToString();

    internal async Task<bool> ExecuteAsync(
        string line,
        CancellationToken cancellationToken = default)
    {
        if (_Disposed)
        {
            _WriteFailure(InteractionErrorCode.DISPOSED, "The CLI session has been disposed.");
            return false;
        }

        var tokenResult = CliTokenizer.Tokenize(line);
        if (!tokenResult.IsSuccess)
        {
            _WriteFailure(tokenResult.Error!);
            return true;
        }

        var tokens = tokenResult.Value;
        if (tokens.Count == 0)
        {
            return true;
        }

        switch (tokens[0].ToUpperInvariant())
        {
            case "LS":
                await _ListAsync(tokens, cancellationToken);
                return true;
            case "CD":
                await _ChangeDirectoryAsync(tokens, cancellationToken);
                return true;
            case "INSPECT":
                await _InspectAsync(tokens, cancellationToken);
                return true;
            case "GET":
                await _GetAsync(tokens, cancellationToken);
                return true;
            case "SET":
                await _SetAsync(tokens, cancellationToken);
                return true;
            case "CALL":
                await _CallAsync(tokens, cancellationToken);
                return true;
            case "WATCH":
                await _WatchAsync(tokens, cancellationToken);
                return true;
            case "EXIT":
                if (!_HasArity(tokens, 1, "exit"))
                {
                    return true;
                }

                _Output.WriteLine("bye");
                Dispose();
                return false;
            default:
                _WriteFailure(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Unknown command '{tokens[0]}'.");
                return true;
        }
    }

    internal async Task<IReadOnlyList<string>> GetCompletionsAsync(
        string text,
        int caret,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)caret, (uint)text.Length);

        var tokenStart = caret;
        while (tokenStart > 0 && !char.IsWhiteSpace(text[tokenStart - 1]))
        {
            tokenStart--;
        }

        var completedTokens = text[..tokenStart]
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (completedTokens.Length == 0)
        {
            return _COMMANDS;
        }

        return completedTokens[0].ToUpperInvariant() switch
        {
            "CD" when completedTokens.Length == 1 =>
                await _GetNavigationCompletionsAsync(cancellationToken),
            "LS" when completedTokens.Length == 1 =>
                await _GetCollectionCompletionsAsync(cancellationToken),
            "GET" when completedTokens.Length == 1 =>
                await _GetValueCompletionsAsync(false, cancellationToken),
            "SET" when completedTokens.Length == 1 =>
                await _GetValueCompletionsAsync(true, cancellationToken),
            "CALL" => await _GetActionCompletionsAsync(completedTokens, cancellationToken),
            "WATCH" when completedTokens.Length == 1 =>
                await _GetWatchCompletionsAsync(cancellationToken),
            _ => [],
        };
    }

    public void Dispose()
    {
        if (_Disposed)
        {
            return;
        }

        _Disposed = true;
        foreach (var subscription in _Subscriptions.ToArray())
        {
            subscription.Dispose();
        }

        _Subscriptions.Clear();
        _Host.Dispose();
    }

    private async Task _ListAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (tokens.Count > 1)
        {
            await _ListCollectionAsync(tokens, cancellationToken);
            return;
        }

        if (_Locations.Count == 0)
        {
            foreach (var root in _Host.Roots.OrderBy(static root => root.Identifier, StringComparer.Ordinal))
            {
                _Output.WriteLine($"root {root.Identifier}");
            }

            return;
        }

        var described = await _GetCurrentDescriptorAsync(cancellationToken);
        if (!described.IsSuccess)
        {
            _WriteFailure(described.Error!);
            return;
        }

        foreach (var member in described.Value.Members
                     .OrderBy(static member => member.Kind)
                     .ThenBy(static member => member.Id, StringComparer.Ordinal))
        {
            _Output.WriteLine($"{member.Kind.ToString().ToLowerInvariant()} {member.Id}");
        }
    }

    private async Task _ListCollectionAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        var options = _ParseCollectionOptions(tokens);
        if (!options.IsSuccess)
        {
            _WriteFailure(options.Error!);
            return;
        }

        var binding = await _ResolveMemberAsync(tokens[1], MemberKind.COLLECTION, cancellationToken);
        if (!binding.IsSuccess)
        {
            _WriteFailure(binding.Error!);
            return;
        }

        var collection = (CollectionDescriptor)binding.Value.Member;
        var read = await collection.ReadAsync(
            options.Value.Offset,
            options.Value.Limit ?? DEFAULT_COLLECTION_LIMIT,
            cancellationToken);
        if (!read.IsSuccess)
        {
            _WriteFailure(read.Error!);
            return;
        }

        var slice = read.Value;
        _Output.WriteLine(
            $"collection {collection.Id} offset={slice.Offset} count={slice.Entries.Count} " +
            $"total={_FormatValue(slice.TotalCount)} hasMore={slice.HasMore}");
        foreach (var entry in slice.Entries)
        {
            var key = entry.Key is null ? string.Empty : $" key={_FormatValue(entry.Key)}";
            var content = entry switch
            {
                NullCollectionEntry => "null",
                ScalarCollectionEntry scalar => $"value={_FormatValue(scalar.Value)}",
                ReferenceCollectionEntry reference =>
                    $"reference={reference.Handle.Id:D} identity={_FormatValue(reference.DomainIdentity)}",
                _ => throw new InvalidOperationException("Unknown collection entry type."),
            };
            _Output.WriteLine($"[{entry.Position}]{key} {content}");
        }
    }

    private async Task _ChangeDirectoryAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (!_HasArity(tokens, 2, "cd <absolute-path|..>"))
        {
            return;
        }

        if (tokens[1] == "..")
        {
            if (_Locations.Count == 0)
            {
                _WriteFailure(InteractionErrorCode.INVALID_INPUT, "Already at the root list.");
                return;
            }

            _Locations.RemoveAt(_Locations.Count - 1);
            _Output.WriteLine(CurrentPath);
            return;
        }

        var resolution = await _Host.ResolvePathAsync(tokens[1], cancellationToken);
        if (!resolution.IsSuccess)
        {
            _WriteFailure(resolution.Error!);
            return;
        }

        if (!resolution.Value.IsObject)
        {
            _WriteFailure(
                InteractionErrorCode.INVALID_INPUT,
                "The path identifies a member rather than a navigable object.");
            return;
        }

        var captured = await _CaptureLocationsAsync(resolution.Value.Locations, cancellationToken);
        if (!captured.IsSuccess)
        {
            _WriteFailure(captured.Error!);
            return;
        }

        _Locations = captured.Value;
        _Output.WriteLine(CurrentPath);
    }

    private async Task _InspectAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (!_HasArity(tokens, 1, "inspect"))
        {
            return;
        }

        var described = await _GetCurrentDescriptorAsync(cancellationToken);
        if (!described.IsSuccess)
        {
            _WriteFailure(described.Error!);
            return;
        }

        var descriptor = described.Value;
        _Output.WriteLine($"path: {CurrentPath}");
        _Output.WriteLine($"type: {descriptor.TypeName}");
        _Output.WriteLine($"handle: {descriptor.Handle.Id:D}");
        _Output.WriteLine($"domain-identity: {_FormatValue(descriptor.DomainIdentity)}");
        _Output.WriteLine($"summary: {descriptor.Summary ?? "null"}");

        foreach (var member in descriptor.Members.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            switch (member)
            {
                case ValueDescriptor value:
                    var options = value.Options.Count == 0
                        ? "none"
                        : string.Join('|', value.Options.Select(static option => _FormatValue(option.Value)));
                    _Output.WriteLine(
                        $"value {value.Id} type={value.ValueType.Name} read={value.CanRead} " +
                        $"write={value.CanWrite} nullable={value.IsNullable} " +
                        $"range={_FormatRange(value.Range)} options={options}");
                    break;
                case ReferenceDescriptor reference:
                    _Output.WriteLine($"reference {reference.Id} type={reference.ReferenceType.Name}");
                    break;
                case CollectionDescriptor collection:
                    _Output.WriteLine(
                        $"collection {collection.Id} element={collection.ElementType.Name} " +
                        $"key={collection.KeyType?.Name ?? "none"}");
                    break;
                case ActionDescriptor action:
                    var parameters = string.Join(
                        ", ",
                        action.Parameters.Select(parameter =>
                            $"{parameter.Id}:{parameter.ParameterType.Name}" +
                            $" required={parameter.IsRequired}" +
                            $" nullable={parameter.IsNullable}" +
                            $" default={(parameter.HasDefaultValue ? _FormatValue(parameter.DefaultValue) : "absent")}" +
                            $" range={_FormatRange(parameter.Range)}" +
                            $" options={(parameter.Options.Count == 0 ? "none" : string.Join('|', parameter.Options.Select(static option => _FormatValue(option.Value))))}"));
                    _Output.WriteLine(
                        $"action {action.Id}({parameters}) result={action.ResultType?.Name ?? "void"} " +
                        $"async={action.IsAsynchronous} cancellation={action.SupportsCancellation} " +
                        $"progress={action.ProgressType?.Name ?? "none"}");
                    break;
            }
        }
    }

    private async Task _GetAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (!_HasArity(tokens, 2, "get <member>"))
        {
            return;
        }

        var binding = await _ResolveMemberAsync(tokens[1], MemberKind.VALUE, cancellationToken);
        if (!binding.IsSuccess)
        {
            _WriteFailure(binding.Error!);
            return;
        }

        var value = (ValueDescriptor)binding.Value.Member;
        var read = await value.ReadAsync(cancellationToken);
        if (!read.IsSuccess)
        {
            _WriteFailure(read.Error!);
            return;
        }

        _Output.WriteLine($"{value.Id} = {_FormatValue(read.Value)}");
    }

    private async Task _SetAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (!_HasArity(tokens, 3, "set <member> <value>"))
        {
            return;
        }

        var binding = await _ResolveMemberAsync(tokens[1], MemberKind.VALUE, cancellationToken);
        if (!binding.IsSuccess)
        {
            _WriteFailure(binding.Error!);
            return;
        }

        var value = (ValueDescriptor)binding.Value.Member;
        var write = await value.WriteAsync(tokens[2], cancellationToken);
        if (!write.IsSuccess)
        {
            _WriteFailure(write.Error!);
            return;
        }

        _Output.WriteLine($"{value.Id} = {_FormatValue(write.Value)}");
    }

    private async Task _CallAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (tokens.Count < 2)
        {
            _WriteFailure(InteractionErrorCode.INVALID_INPUT, "Usage: call <action> name=value ...");
            return;
        }

        var arguments = _ParseArguments(tokens);
        if (!arguments.IsSuccess)
        {
            _WriteFailure(arguments.Error!);
            return;
        }

        var binding = await _ResolveMemberAsync(tokens[1], MemberKind.ACTION, cancellationToken);
        if (!binding.IsSuccess)
        {
            _WriteFailure(binding.Error!);
            return;
        }

        var action = (ActionDescriptor)binding.Value.Member;
        var started = await action.InvokeAsync(arguments.Value, cancellationToken);
        if (!started.IsSuccess)
        {
            _WriteFailure(started.Error!);
            return;
        }

        var invocation = started.Value;
        using var registration = action.SupportsCancellation
            ? cancellationToken.Register(static state => ((ActionInvocation)state!).Cancel(), invocation)
            : default;
        await foreach (var progress in invocation.ReadProgressAsync(CancellationToken.None))
        {
            _Output.WriteLine(
                $"progress {action.Id} token={progress.OrderingToken} " +
                $"value={_FormatValue(progress.Value)}");
        }

        var completion = await invocation.Completion;
        if (!completion.IsSuccess)
        {
            _WriteFailure(completion.Error!);
            return;
        }

        _Output.WriteLine($"{action.Id} => {_FormatValue(completion.Value)}");
    }

    private async Task _WatchAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (!_HasArity(tokens, 2, "watch <member|collection>"))
        {
            return;
        }

        var descriptor = await _GetCurrentDescriptorAsync(cancellationToken);
        if (!descriptor.IsSuccess)
        {
            _WriteFailure(descriptor.Error!);
            return;
        }

        var member = descriptor.Value.Members.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, tokens[1]));
        if (member is null || member is ActionDescriptor)
        {
            _WriteFailure(
                InteractionErrorCode.NOT_FOUND,
                $"Observable member '{tokens[1]}' was not found at '{CurrentPath}'.");
            return;
        }

        var observed = await _Host.ObserveAsync(
            descriptor.Value.Handle,
            tokens[1],
            cancellationToken: cancellationToken);
        if (!observed.IsSuccess)
        {
            _WriteFailure(observed.Error!);
            return;
        }

        var subscription = observed.Value;
        _Subscriptions.Add(subscription);
        _Output.WriteLine($"watching {tokens[1]} (cancel to stop)");
        try
        {
            await foreach (var change in subscription.ReadAllAsync(cancellationToken))
            {
                _Output.WriteLine(
                    $"change {change.OrderingToken} kind={change.Kind} " +
                    $"member={change.MemberId ?? "*"} old={_FormatObservationValue(change.OldValue)} " +
                    $"new={_FormatObservationValue(change.NewValue)} " +
                    $"oldIndex={_FormatValue(change.OldIndex)} newIndex={_FormatValue(change.NewIndex)} " +
                    $"dropped={change.DroppedChangeCount}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _Output.WriteLine("watch cancelled");
        }
        finally
        {
            _Subscriptions.Remove(subscription);
            subscription.Dispose();
        }
    }

    private async Task<IReadOnlyList<string>> _GetNavigationCompletionsAsync(
        CancellationToken cancellationToken)
    {
        var completions = new HashSet<string>(StringComparer.Ordinal) { "/", ".." };
        foreach (var root in _Host.Roots)
        {
            completions.Add(LogicalPath.Root.Append(root.Identifier).ToString());
        }

        if (CurrentHandle is not null)
        {
            var descriptor = await _GetCurrentDescriptorAsync(cancellationToken);
            if (descriptor.IsSuccess)
            {
                var current = _Locations[^1].Path;
                foreach (var member in descriptor.Value.Members
                             .Where(static member => member is ReferenceDescriptor or CollectionDescriptor))
                {
                    completions.Add(current.Append(member.Id).ToString());
                }
            }
        }

        return completions.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
    }

    private async Task<IReadOnlyList<string>> _GetValueCompletionsAsync(
        bool writableOnly,
        CancellationToken cancellationToken)
    {
        var descriptor = await _GetCurrentDescriptorAsync(cancellationToken);
        return descriptor.IsSuccess
            ? descriptor.Value.Members.OfType<ValueDescriptor>()
                .Where(value => !writableOnly || value.CanWrite)
                .Select(static value => value.Id)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    private async Task<IReadOnlyList<string>> _GetCollectionCompletionsAsync(
        CancellationToken cancellationToken)
    {
        var descriptor = await _GetCurrentDescriptorAsync(cancellationToken);
        return descriptor.IsSuccess
            ? descriptor.Value.Members.OfType<CollectionDescriptor>()
                .Select(static collection => collection.Id)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    private async Task<IReadOnlyList<string>> _GetWatchCompletionsAsync(
        CancellationToken cancellationToken)
    {
        var descriptor = await _GetCurrentDescriptorAsync(cancellationToken);
        return descriptor.IsSuccess
            ? descriptor.Value.Members
                .Where(static member => member is not ActionDescriptor)
                .Select(static member => member.Id)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    private async Task<IReadOnlyList<string>> _GetActionCompletionsAsync(
        string[] completedTokens,
        CancellationToken cancellationToken)
    {
        var descriptor = await _GetCurrentDescriptorAsync(cancellationToken);
        if (!descriptor.IsSuccess)
        {
            return [];
        }

        var actions = descriptor.Value.Members.OfType<ActionDescriptor>();
        if (completedTokens.Length == 1)
        {
            return actions.Select(static action => action.Id)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        var action = actions.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, completedTokens[1]));
        if (action is null)
        {
            return [];
        }

        var supplied = completedTokens.Skip(2)
            .Select(static token => token.Split('=', 2)[0])
            .ToHashSet(StringComparer.Ordinal);
        return action.Parameters
            .Where(parameter => !supplied.Contains(parameter.Id))
            .Select(static parameter => $"{parameter.Id}=")
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<InteractionResult<ObjectDescriptor>> _GetCurrentDescriptorAsync(
        CancellationToken cancellationToken)
    {
        if (_Locations.Count == 0)
        {
            return InteractionResult.Failure<ObjectDescriptor>(
                InteractionErrorCode.UNAVAILABLE,
                "No object is selected. Use 'cd /<root>' first.");
        }

        var location = _Locations[^1];
        var resolution = await _Host.ResolvePathAsync(location.Path, cancellationToken);
        if (!resolution.IsSuccess)
        {
            return _Failure<ObjectDescriptor>(resolution.Error!);
        }

        if (!resolution.Value.IsObject)
        {
            return InteractionResult.Failure<ObjectDescriptor>(
                InteractionErrorCode.INVALID_INPUT,
                "The current path no longer identifies a navigable object.");
        }

        var descriptor = resolution.Value.OwnerDescriptor;
        if (location.DomainIdentity is not null &&
            !StringComparer.Ordinal.Equals(location.DomainIdentity, descriptor.DomainIdentity?.Value))
        {
            return InteractionResult.Failure<ObjectDescriptor>(
                InteractionErrorCode.NOT_FOUND,
                "The current path resolved to a different domain identity.");
        }

        if (!StringComparer.Ordinal.Equals(location.TypeName, descriptor.TypeName))
        {
            return InteractionResult.Failure<ObjectDescriptor>(
                InteractionErrorCode.TYPE_MISMATCH,
                "The current path resolved to an incompatible object type.");
        }

        _Locations[^1] = location with
        {
            Path = resolution.Value.CanonicalPath,
            LastResolvedHandle = descriptor.Handle,
        };
        return InteractionResult.Success(descriptor);
    }

    private async Task<InteractionResult<ResolvedBinding>> _ResolveMemberAsync(
        string memberId,
        MemberKind kind,
        CancellationToken cancellationToken)
    {
        if (_Locations.Count == 0)
        {
            return InteractionResult.Failure<ResolvedBinding>(
                InteractionErrorCode.UNAVAILABLE,
                "No object is selected. Use 'cd /<root>' first.");
        }

        var location = _Locations[^1];
        var resolved = await _Host.ResolveBindingAsync(
            new BindingReference(
                location.Path.ToString(),
                memberId,
                kind,
                location.DomainIdentity),
            cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved;
        }

        _Locations[^1] = location with
        {
            Path = resolved.Value.CanonicalPath,
            LastResolvedHandle = resolved.Value.OwnerHandle,
        };
        return resolved;
    }

    private async Task<InteractionResult<List<_LocationBinding>>> _CaptureLocationsAsync(
        IReadOnlyList<PathLocation> locations,
        CancellationToken cancellationToken)
    {
        var captured = new List<_LocationBinding>(locations.Count);
        foreach (var location in locations)
        {
            var described = await _Host.DescribeAsync(location.Handle, cancellationToken);
            if (!described.IsSuccess)
            {
                return _Failure<List<_LocationBinding>>(described.Error!);
            }

            captured.Add(new _LocationBinding(
                location.Path,
                described.Value.DomainIdentity?.Value,
                described.Value.TypeName,
                location.Handle));
        }

        return InteractionResult.Success(captured);
    }

    private static InteractionResult<_CollectionOptions> _ParseCollectionOptions(
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return InteractionResult.Failure<_CollectionOptions>(
                InteractionErrorCode.INVALID_INPUT,
                "Usage: ls <collection> [offset=<n>] [limit=<n>]");
        }

        long offset = 0;
        int? limit = null;
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 2; index < tokens.Count; index++)
        {
            var pair = tokens[index].Split('=', 2);
            if (pair.Length != 2 || !supplied.Add(pair[0]))
            {
                return InteractionResult.Failure<_CollectionOptions>(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Collection option '{tokens[index]}' is malformed or duplicated.");
            }

            switch (pair[0])
            {
                case "offset" when long.TryParse(
                    pair[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedOffset):
                    offset = parsedOffset;
                    break;
                case "limit" when int.TryParse(
                    pair[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedLimit) && parsedLimit > 0:
                    limit = parsedLimit;
                    break;
                default:
                    return InteractionResult.Failure<_CollectionOptions>(
                        InteractionErrorCode.INVALID_INPUT,
                        $"Unknown or invalid collection option '{tokens[index]}'.");
            }
        }

        return InteractionResult.Success(new _CollectionOptions(offset, limit));
    }

    private static InteractionResult<IReadOnlyDictionary<string, object?>> _ParseArguments(
        IReadOnlyList<string> tokens)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 2; index < tokens.Count; index++)
        {
            var separator = tokens[index].IndexOf('=');
            if (separator <= 0)
            {
                return InteractionResult.Failure<IReadOnlyDictionary<string, object?>>(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Argument '{tokens[index]}' must use name=value syntax.");
            }

            var name = tokens[index][..separator];
            var value = tokens[index][(separator + 1)..];
            if (!arguments.TryAdd(name, value))
            {
                return InteractionResult.Failure<IReadOnlyDictionary<string, object?>>(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Argument '{name}' was supplied more than once.");
            }
        }

        return InteractionResult.Success<IReadOnlyDictionary<string, object?>>(arguments);
    }

    private bool _HasArity(IReadOnlyList<string> tokens, int expected, string usage)
    {
        if (tokens.Count == expected)
        {
            return true;
        }

        _WriteFailure(InteractionErrorCode.INVALID_INPUT, $"Usage: {usage}");
        return false;
    }

    private static InteractionResult<T> _Failure<T>(InteractionError error) =>
        InteractionResult.Failure<T>(error.Code, error.Message, error.Issues);

    private void _WriteFailure(InteractionError error)
    {
        _WriteFailure(error.Code, error.Message);
        foreach (var issue in error.Issues)
        {
            _Output.WriteLine($"issue {issue.Code} id={issue.TargetId}: {issue.Message}");
        }
    }

    private void _WriteFailure(InteractionErrorCode code, string message) =>
        _Output.WriteLine($"error {code}: {message}");

    private static string _FormatObservationValue(ObservationValue value) =>
        value.IsSupplied ? _FormatValue(value.Value) : "unspecified";

    private static string _FormatRange(ValueRange? range) => range is null
        ? "none"
        : $"{_FormatValue(range.Minimum)}..{_FormatValue(range.Maximum)}";

    private static string _FormatValue(object? value) => value switch
    {
        null => "null",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private sealed record _LocationBinding(
        LogicalPath Path,
        string? DomainIdentity,
        string TypeName,
        ObjectHandle LastResolvedHandle);

    private readonly record struct _CollectionOptions(long Offset, int? Limit);
}
