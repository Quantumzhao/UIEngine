using System.Globalization;
using UIEngine.Core;

namespace UIEngine.Frontend.Cli;

/// <summary>Runs an interactive command session over frontend-neutral UIEngine descriptors.</summary>
internal sealed class CliSession : IAsyncDisposable
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

    private readonly object _Gate = new();
    private readonly UIEngineHost _Host;
    private readonly TextWriter _Output;
    private readonly HashSet<IObservationSubscription> _Subscriptions = [];
    private List<_LocationBinding> _Locations = [];
    private int _IsDisposed;

    public CliSession(UIEngineHost host, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(output);
        _Host = host;
        _Output = output;
    }

    internal ObjectHandle? CurrentHandle => _Locations.Count == 0
        ? null
        : _Locations[^1].LastResolvedHandle;

    internal string CurrentPath => _Locations.Count == 0 ? "/" : _Locations[^1].Path.ToString();

    internal async ValueTask<bool> ExecuteAsync(
        string line,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _IsDisposed) != 0)
        {
            _WriteFailure(InteractionErrorCode.HOST_DISPOSED, "The CLI session has been disposed.");
            return false;
        }

        var tokenResult = CliTokenizer.Tokenize(line);
        if (!tokenResult.IsSuccess)
        {
            _WriteFailure(tokenResult.Error);
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
                await _ListAsync(tokens, cancellationToken).ConfigureAwait(false);
                return true;
            case "CD":
                await _ChangeDirectoryAsync(tokens, cancellationToken).ConfigureAwait(false);
                return true;
            case "INSPECT":
                await _InspectAsync(tokens, cancellationToken).ConfigureAwait(false);
                return true;
            case "GET":
                await _GetAsync(tokens, cancellationToken).ConfigureAwait(false);
                return true;
            case "SET":
                await _SetAsync(tokens, cancellationToken).ConfigureAwait(false);
                return true;
            case "CALL":
                await _CallAsync(tokens, cancellationToken).ConfigureAwait(false);
                return true;
            case "WATCH":
                await _WatchAsync(tokens, cancellationToken).ConfigureAwait(false);
                return true;
            case "EXIT":
                if (!_HasArity(tokens, 1, "exit"))
                {
                    return true;
                }

                _Output.WriteLine("bye");
                await DisposeAsync().ConfigureAwait(false);
                return false;
            default:
                _WriteFailure(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Unknown command '{tokens[0]}'.");
                return true;
        }
    }

    internal async ValueTask<IReadOnlyList<string>> GetCompletionsAsync(
        string text,
        int caret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
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
                await _GetNavigationCompletionsAsync(cancellationToken).ConfigureAwait(false),
            "LS" when completedTokens.Length == 1 =>
                await _GetCollectionCompletionsAsync(cancellationToken).ConfigureAwait(false),
            "GET" when completedTokens.Length == 1 =>
                await _GetValueCompletionsAsync(writableOnly: false, cancellationToken)
                    .ConfigureAwait(false),
            "SET" when completedTokens.Length == 1 =>
                await _GetValueCompletionsAsync(writableOnly: true, cancellationToken)
                    .ConfigureAwait(false),
            "CALL" => await _GetActionCompletionsAsync(completedTokens, cancellationToken)
                .ConfigureAwait(false),
            "WATCH" when completedTokens.Length == 1 =>
                await _GetWatchCompletionsAsync(cancellationToken).ConfigureAwait(false),
            _ => Array.Empty<string>(),
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _IsDisposed, 1) != 0)
        {
            return;
        }

        IObservationSubscription[] subscriptions;
        lock (_Gate)
        {
            subscriptions = _Subscriptions.ToArray();
            _Subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
        }

        await _Host.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask _ListAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (tokens.Count > 1)
        {
            await _ListCollectionAsync(tokens, cancellationToken).ConfigureAwait(false);
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

        var descriptorResult = await _GetCurrentDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (!descriptorResult.IsSuccess)
        {
            _WriteFailure(descriptorResult.Error);
            return;
        }

        var descriptor = descriptorResult.Value;
        foreach (var value in descriptor.Values.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            _Output.WriteLine($"value {value.Id}");
        }

        foreach (var reference in descriptor.References.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            _Output.WriteLine($"reference {reference.Id}");
        }

        foreach (var collection in descriptor.Collections.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            _Output.WriteLine($"collection {collection.Id}");
        }

        foreach (var action in descriptor.Actions.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            _Output.WriteLine($"action {action.Id}");
        }
    }

    private async ValueTask _ListCollectionAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        var options = _ParseCollectionOptions(tokens);
        if (!options.IsSuccess)
        {
            _WriteFailure(options.Error);
            return;
        }

        var binding = await _ResolveMemberAsync(
            tokens[1],
            DescriptorKind.COLLECTION,
            cancellationToken).ConfigureAwait(false);
        if (!binding.IsSuccess)
        {
            _WriteFailure(binding.Error);
            return;
        }

        var collection = (ICollectionDescriptor)binding.Value.MemberDescriptor;
        var request = _CreateCollectionRequest(collection, options.Value);
        if (!request.IsSuccess)
        {
            _WriteFailure(request.Error);
            return;
        }

        var read = await collection.ReadAsync(request.Value, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess)
        {
            _WriteFailure(read.Error);
            return;
        }

        var result = read.Value;
        _Output.WriteLine(
            $"collection {collection.Id} mode={result.Mode} offset={result.Offset} " +
            $"count={result.Entries.Count} total={_FormatValue(result.TotalCount)} " +
            $"hasMore={result.HasMore} continuation={_FormatValue(result.ContinuationToken)}");
        foreach (var entry in result.Entries)
        {
            var key = entry.Key is null ? string.Empty : $" key={_FormatValue(entry.Key.Value)}";
            var content = entry.Kind switch
            {
                CollectionEntryKind.NULL => "null",
                CollectionEntryKind.SCALAR => $"value={_FormatValue(entry.ScalarValue)}",
                CollectionEntryKind.REFERENCE =>
                    $"reference={entry.Reference!.Value.Identity.RuntimeId:D} " +
                    $"identity={_FormatValue(entry.DomainIdentity)}",
                _ => throw new InvalidOperationException("Unknown collection entry kind."),
            };
            _Output.WriteLine($"[{entry.Position}]{key} {content}");
        }
    }

    private async ValueTask _ChangeDirectoryAsync(
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

        var resolution = await _Host.Paths.ResolveAsync(tokens[1], cancellationToken).ConfigureAwait(false);
        if (!resolution.IsResolved)
        {
            _WriteFailure(resolution.Error);
            return;
        }

        if (resolution.Target is not null && resolution.Target.Kind != DescriptorKind.INSTANCE)
        {
            _WriteFailure(
                InteractionErrorCode.INVALID_PATH,
                "The path identifies a member rather than a navigable object.");
            return;
        }

        var captured = await _CaptureLocationsAsync(resolution.Locations, cancellationToken)
            .ConfigureAwait(false);
        if (!captured.IsSuccess)
        {
            _WriteFailure(captured.Error);
            return;
        }

        _Locations = captured.Value;
        _Output.WriteLine(CurrentPath);
    }

    private async ValueTask _InspectAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (!_HasArity(tokens, 1, "inspect"))
        {
            return;
        }

        var descriptorResult = await _GetCurrentDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (!descriptorResult.IsSuccess)
        {
            _WriteFailure(descriptorResult.Error);
            return;
        }

        var descriptor = descriptorResult.Value;
        _Output.WriteLine($"path: {CurrentPath}");
        _Output.WriteLine("availability: available");
        _Output.WriteLine($"type: {descriptor.TypeName}");
        _Output.WriteLine($"runtime-identity: {descriptor.Identity.RuntimeId:D}");
        _Output.WriteLine($"domain-identity: {_FormatValue(descriptor.DomainIdentity)}");
        _Output.WriteLine($"summary: {descriptor.Summary ?? "null"}");

        foreach (var value in descriptor.Values.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            var options = value.Options.Count == 0
                ? "none"
                : string.Join('|', value.Options.Select(option => _FormatValue(option.Value)));
            var range = _FormatRange(value.Range);
            var rules = _FormatRules(value.ValidationRules);
            _Output.WriteLine(
                $"value {value.Id} type={value.ValueType.Name} read={value.CanRead} write={value.CanWrite} " +
                $"nullable={value.IsNullable} range={range} options={options} rules={rules} " +
                $"unit={_FormatValue(value.Unit)} tags={_FormatTags(value.Tags)}");
        }

        foreach (var reference in descriptor.References.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            _Output.WriteLine($"reference {reference.Id} type={reference.ReferenceType.Name}");
        }

        foreach (var collection in descriptor.Collections.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            _Output.WriteLine(
                $"collection {collection.Id} element={collection.ElementType.Name} " +
                $"key={collection.KeyType?.Name ?? "none"} capabilities={collection.Capabilities}");
        }

        foreach (var action in descriptor.Actions.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            var parameters = string.Join(
                ", ",
                action.Parameters.Select(parameter =>
                    $"{parameter.Id}:{parameter.ParameterType.Name}" +
                    $" required={parameter.IsRequired}" +
                    $" nullable={parameter.IsNullable}" +
                    $" default={(parameter.HasDefaultValue ? _FormatValue(parameter.DefaultValue) : "absent")}" +
                    $" range={_FormatRange(parameter.Range)}" +
                    $" options={(parameter.Options.Count == 0 ? "none" : string.Join('|', parameter.Options.Select(option => _FormatValue(option.Value))))}" +
                    $" rules={_FormatRules(parameter.ValidationRules)}"));
            _Output.WriteLine(
                $"action {action.Id}({parameters}) result={action.ResultType?.Name ?? "void"} " +
                $"async={action.IsAsynchronous} cancellation={action.SupportsCancellation} " +
                $"progress={action.ProgressType?.Name ?? "none"} risk={action.Risk} " +
                $"confirm={action.RequiresConfirmation}");
        }
    }

    private async ValueTask _GetAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (!_HasArity(tokens, 2, "get <member>"))
        {
            return;
        }

        var binding = await _ResolveMemberAsync(tokens[1], DescriptorKind.VALUE, cancellationToken)
            .ConfigureAwait(false);
        if (!binding.IsSuccess)
        {
            _WriteFailure(binding.Error);
            return;
        }

        var value = (IValueDescriptor)binding.Value.MemberDescriptor;
        var read = await value.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess)
        {
            _WriteFailure(read.Error);
            return;
        }

        _Output.WriteLine($"{value.Id} = {_FormatValue(read.Value)}");
    }

    private async ValueTask _SetAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (!_HasArity(tokens, 3, "set <member> <value>"))
        {
            return;
        }

        var binding = await _ResolveMemberAsync(tokens[1], DescriptorKind.VALUE, cancellationToken)
            .ConfigureAwait(false);
        if (!binding.IsSuccess)
        {
            _WriteFailure(binding.Error);
            return;
        }

        var value = (IValueDescriptor)binding.Value.MemberDescriptor;
        var write = await value.WriteAsync(tokens[2], cancellationToken).ConfigureAwait(false);
        if (!write.IsSuccess)
        {
            _WriteFailure(write.Error);
            return;
        }

        _Output.WriteLine($"{value.Id} = {_FormatValue(write.Value)}");
    }

    private async ValueTask _CallAsync(
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
            _WriteFailure(arguments.Error);
            return;
        }

        var binding = await _ResolveMemberAsync(tokens[1], DescriptorKind.ACTION, cancellationToken)
            .ConfigureAwait(false);
        if (!binding.IsSuccess)
        {
            _WriteFailure(binding.Error);
            return;
        }

        var action = (IActionDescriptor)binding.Value.MemberDescriptor;
        var invocationStart = await action.InvokeAsync(arguments.Value, cancellationToken).ConfigureAwait(false);
        if (!invocationStart.IsSuccess)
        {
            _WriteFailure(invocationStart.Error);
            return;
        }

        var invocation = invocationStart.Value;
        using var cancellationRegistration = action.SupportsCancellation
            ? cancellationToken.Register(
                static state => ((IActionInvocation)state!).RequestCancellation(),
                invocation)
            : default;
        await foreach (var progress in invocation.ReadProgressAsync(CancellationToken.None)
            .ConfigureAwait(false))
        {
            _Output.WriteLine(
                $"progress {action.Id} token={progress.OrderingToken} " +
                $"value={_FormatValue(progress.Value)}");
        }

        var completion = await invocation.Completion.ConfigureAwait(false);
        if (!completion.IsSuccess)
        {
            _WriteFailure(completion.Error);
            return;
        }

        _Output.WriteLine($"{action.Id} => {_FormatValue(completion.Value)}");
    }

    private async ValueTask _WatchAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (!_HasArity(tokens, 2, "watch <member|collection>"))
        {
            return;
        }

        var descriptor = await _GetCurrentDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (!descriptor.IsSuccess)
        {
            _WriteFailure(descriptor.Error);
            return;
        }

        var kind = _FindObservableKind(descriptor.Value, tokens[1]);
        if (kind is null)
        {
            _WriteFailure(
                InteractionErrorCode.TARGET_MISSING,
                $"Observable member '{tokens[1]}' was not found at '{CurrentPath}'.");
            return;
        }

        var binding = await _ResolveMemberAsync(tokens[1], kind.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!binding.IsSuccess)
        {
            _WriteFailure(binding.Error);
            return;
        }

        var observed = await _Host.ObserveAsync(
            binding.Value.OwnerHandle,
            new ObservationRequest { MemberId = tokens[1] },
            cancellationToken).ConfigureAwait(false);
        if (!observed.IsSuccess)
        {
            _WriteFailure(observed.Error);
            return;
        }

        var subscription = observed.Value;
        lock (_Gate)
        {
            _Subscriptions.Add(subscription);
        }

        _Output.WriteLine($"watching {tokens[1]} (cancel to stop)");
        try
        {
            await foreach (var change in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
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
            lock (_Gate)
            {
                _Subscriptions.Remove(subscription);
            }

            await subscription.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<IReadOnlyList<string>> _GetNavigationCompletionsAsync(
        CancellationToken cancellationToken)
    {
        var completions = new HashSet<string>(StringComparer.Ordinal)
        {
            "/",
            "..",
        };

        foreach (var root in _Host.Roots)
        {
            completions.Add(LogicalPath.Root.Append(root.Identifier).ToString());
        }

        if (CurrentHandle is not null)
        {
            var descriptorResult = await _GetCurrentDescriptorAsync(cancellationToken)
                .ConfigureAwait(false);
            if (descriptorResult.IsSuccess)
            {
                var current = _Locations[^1].Path;
                foreach (var reference in descriptorResult.Value.References)
                {
                    completions.Add(current.Append(reference.Id).ToString());
                }

                foreach (var collection in descriptorResult.Value.Collections)
                {
                    completions.Add(current.Append(collection.Id).ToString());
                }
            }
        }

        return completions.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
    }

    private async ValueTask<IReadOnlyList<string>> _GetValueCompletionsAsync(
        bool writableOnly,
        CancellationToken cancellationToken)
    {
        var descriptorResult = await _GetCurrentDescriptorAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!descriptorResult.IsSuccess)
        {
            return Array.Empty<string>();
        }

        return descriptorResult.Value.Values
            .Where(value => !writableOnly || value.CanWrite)
            .Select(static value => value.Id)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private async ValueTask<IReadOnlyList<string>> _GetCollectionCompletionsAsync(
        CancellationToken cancellationToken)
    {
        var descriptor = await _GetCurrentDescriptorAsync(cancellationToken).ConfigureAwait(false);
        return descriptor.IsSuccess
            ? descriptor.Value.Collections
                .Select(static collection => collection.Id)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    private async ValueTask<IReadOnlyList<string>> _GetWatchCompletionsAsync(
        CancellationToken cancellationToken)
    {
        var descriptor = await _GetCurrentDescriptorAsync(cancellationToken).ConfigureAwait(false);
        return descriptor.IsSuccess
            ? descriptor.Value.Values.Cast<IMemberDescriptor>()
                .Concat(descriptor.Value.References)
                .Concat(descriptor.Value.Collections)
                .Select(static member => member.Id)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    private async ValueTask<IReadOnlyList<string>> _GetActionCompletionsAsync(
        string[] completedTokens,
        CancellationToken cancellationToken)
    {
        var descriptorResult = await _GetCurrentDescriptorAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!descriptorResult.IsSuccess)
        {
            return Array.Empty<string>();
        }

        if (completedTokens.Length == 1)
        {
            return descriptorResult.Value.Actions
                .Select(static action => action.Id)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        var action = descriptorResult.Value.Actions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, completedTokens[1], StringComparison.Ordinal));
        if (action is null)
        {
            return Array.Empty<string>();
        }

        var suppliedParameters = completedTokens
            .Skip(2)
            .Select(static token => token.Split('=', 2)[0])
            .ToHashSet(StringComparer.Ordinal);
        return action.Parameters
            .Where(parameter => !suppliedParameters.Contains(parameter.Id))
            .Select(static parameter => $"{parameter.Id}=")
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private async ValueTask<InteractionResult<IObjectDescriptor>> _GetCurrentDescriptorAsync(
        CancellationToken cancellationToken)
    {
        if (_Locations.Count == 0)
        {
            return InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "No object is selected. Use 'cd /<root>' first.");
        }

        var location = _Locations[^1];
        var resolution = await _Host.Paths.ResolveAsync(location.Path, cancellationToken)
            .ConfigureAwait(false);
        if (!resolution.IsResolved)
        {
            return InteractionResult.Failure<IObjectDescriptor>(
                resolution.Error!.Code,
                resolution.Error.Message,
                resolution.Error.Issues);
        }

        if (resolution.Target is not { Kind: DescriptorKind.INSTANCE } target)
        {
            return InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.INVALID_PATH,
                "The current path no longer identifies a navigable object.");
        }

        if (location.DomainIdentity is not null &&
            !StringComparer.Ordinal.Equals(
                location.DomainIdentity,
                target.OwnerDescriptor.DomainIdentity?.Value))
        {
            return InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.TARGET_MISSING,
                "The current path resolved to a different domain identity.");
        }

        if (!StringComparer.Ordinal.Equals(location.TypeName, target.OwnerDescriptor.TypeName))
        {
            return InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.TYPE_MISMATCH,
                "The current path resolved to an incompatible object type.");
        }

        _Locations[^1] = location with
        {
            Path = resolution.CanonicalPath!,
            LastResolvedHandle = target.OwnerHandle,
        };
        return InteractionResult.Success(target.OwnerDescriptor);
    }

    private async ValueTask<InteractionResult<ResolvedBindingTarget>> _ResolveMemberAsync(
        string memberId,
        DescriptorKind kind,
        CancellationToken cancellationToken)
    {
        if (_Locations.Count == 0)
        {
            return InteractionResult.Failure<ResolvedBindingTarget>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "No object is selected. Use 'cd /<root>' first.");
        }

        var location = _Locations[^1];
        var resolved = await _Host.Bindings.ResolveAsync(new BindingReference(
            location.DomainIdentity,
            location.Path.ToString(),
            memberId,
            kind,
            ExpectedTypeName: null,
            location.DomainIdentity is null
                ? BindingFallbackPolicy.PATH_ONLY
                : BindingFallbackPolicy.DOMAIN_IDENTITY_THEN_PATH), cancellationToken).ConfigureAwait(false);
        if (!resolved.IsResolved)
        {
            return InteractionResult.Failure<ResolvedBindingTarget>(
                resolved.Error!.Code,
                resolved.Error.Message,
                resolved.Error.Issues);
        }

        _Locations[^1] = location with
        {
            Path = resolved.CanonicalPath ?? location.Path,
            LastResolvedHandle = resolved.Target!.OwnerHandle,
        };
        return InteractionResult.Success(resolved.Target);
    }

    private async ValueTask<InteractionResult<List<_LocationBinding>>> _CaptureLocationsAsync(
        IReadOnlyList<LogicalPathLocation> locations,
        CancellationToken cancellationToken)
    {
        var captured = new List<_LocationBinding>(locations.Count);
        foreach (var location in locations)
        {
            var described = await _Host.DescribeAsync(location.Handle, cancellationToken)
                .ConfigureAwait(false);
            if (!described.IsSuccess)
            {
                return InteractionResult.Failure<List<_LocationBinding>>(
                    described.Error!.Code,
                    described.Error.Message,
                    described.Error.Issues);
            }

            captured.Add(new _LocationBinding(
                location.Path,
                described.Value.DomainIdentity?.Value,
                described.Value.TypeName,
                location.Handle));
        }

        return InteractionResult.Success(captured);
    }

    private InteractionResult<CollectionReadRequest> _CreateCollectionRequest(
        ICollectionDescriptor collection,
        _CollectionOptions options)
    {
        var limit = options.Limit ?? Math.Min(
            DEFAULT_COLLECTION_LIMIT,
            _Host.Configuration.CollectionLimits.MaxPageSize);
        if (collection.Capabilities.HasFlag(CollectionCapabilities.PAGING))
        {
            return InteractionResult.Success(CollectionReadRequest.Page(options.Offset, limit));
        }

        if (collection.Capabilities.HasFlag(CollectionCapabilities.VIRTUALIZED_RANGE))
        {
            return InteractionResult.Success(CollectionReadRequest.Range(options.Offset, limit));
        }

        if (collection.Capabilities.HasFlag(CollectionCapabilities.FINITE_SNAPSHOT))
        {
            if (options.Offset != 0)
            {
                return InteractionResult.Failure<CollectionReadRequest>(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Collection '{collection.Id}' supports snapshots only; offset must be zero.");
            }

            var snapshotLimit = options.Limit ?? Math.Min(
                DEFAULT_COLLECTION_LIMIT,
                _Host.Configuration.CollectionLimits.MaxSnapshotSize);
            return InteractionResult.Success(CollectionReadRequest.Snapshot(snapshotLimit));
        }

        return InteractionResult.Failure<CollectionReadRequest>(
            InteractionErrorCode.COLLECTION_ACCESS_UNSUPPORTED,
            $"Collection '{collection.Id}' has no bounded access mode.");
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

    private static DescriptorKind? _FindObservableKind(IObjectDescriptor descriptor, string memberId)
    {
        if (descriptor.Values.Any(member => StringComparer.Ordinal.Equals(member.Id, memberId)))
        {
            return DescriptorKind.VALUE;
        }

        if (descriptor.References.Any(member => StringComparer.Ordinal.Equals(member.Id, memberId)))
        {
            return DescriptorKind.REFERENCE;
        }

        return descriptor.Collections.Any(member => StringComparer.Ordinal.Equals(member.Id, memberId))
            ? DescriptorKind.COLLECTION
            : null;
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

    private void _WriteFailure(InteractionError? error)
    {
        if (error is null)
        {
            _WriteFailure(InteractionErrorCode.INVOCATION_FAILED, "An unspecified error occurred.");
            return;
        }

        _WriteFailure(error.Code, error.Message);
        foreach (var issue in error.Issues)
        {
            _Output.WriteLine(
                $"issue {issue.Code} target={issue.Target} id={issue.TargetId}: {issue.Message}");
        }
    }

    private void _WriteFailure(InteractionErrorCode code, string message) =>
        _Output.WriteLine($"error {code}: {message}");

    private static string _FormatObservationValue(ObservationValue value) =>
        value.IsSupplied ? _FormatValue(value.Value) : "unspecified";

    private static string _FormatRange(ValueRange? range) => range is null
        ? "none"
        : $"{_FormatValue(range.Minimum)}..{_FormatValue(range.Maximum)}";

    private static string _FormatRules(IReadOnlyList<ValidationRuleDescriptor> rules) =>
        rules.Count == 0 ? "none" : string.Join('|', rules.Select(static rule => rule.Kind));

    private static string _FormatTags(IReadOnlyList<string> tags) =>
        tags.Count == 0 ? "none" : string.Join('|', tags);

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
