using System.Globalization;
using UIEngine.Core;

namespace UIEngine.Frontend.Cli;

/// <summary>Runs an interactive command session over frontend-neutral UIEngine descriptors.</summary>
internal sealed class CliSession
{
    private static readonly string[] _COMMANDS =
    [
        "ls",
        "cd",
        "inspect",
        "get",
        "set",
        "call",
        "exit",
    ];

    private readonly UIEngineHost _Host;
    private readonly TextWriter _Output;
    private List<LogicalPathLocation> _Locations = [];

    public CliSession(UIEngineHost host, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(output);
        _Host = host;
        _Output = output;
    }

    internal ObjectHandle? CurrentHandle => _Locations.Count == 0 ? null : _Locations[^1].Handle;

    internal string CurrentPath => _Locations.Count == 0 ? "/" : _Locations[^1].Path.ToString();

    internal async ValueTask<bool> ExecuteAsync(
        string line,
        CancellationToken cancellationToken = default)
    {
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
            case "EXIT":
                if (!_HasArity(tokens, 1, "exit"))
                {
                    return true;
                }

                _Output.WriteLine("bye");
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
            "GET" when completedTokens.Length == 1 =>
                await _GetValueCompletionsAsync(writableOnly: false, cancellationToken)
                    .ConfigureAwait(false),
            "SET" when completedTokens.Length == 1 =>
                await _GetValueCompletionsAsync(writableOnly: true, cancellationToken)
                    .ConfigureAwait(false),
            "CALL" => await _GetActionCompletionsAsync(completedTokens, cancellationToken)
                .ConfigureAwait(false),
            _ => Array.Empty<string>(),
        };
    }

    private async ValueTask _ListAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (!_HasArity(tokens, 1, "ls"))
        {
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

        _Locations = resolution.Locations.ToList();
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
        _Output.WriteLine($"type: {descriptor.TypeName}");
        _Output.WriteLine($"identity: {descriptor.Identity.RuntimeId:D}");
        _Output.WriteLine($"summary: {descriptor.Summary ?? "null"}");

        foreach (var value in descriptor.Values.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            var options = value.Options.Count == 0
                ? "none"
                : string.Join('|', value.Options.Select(option => _FormatValue(option.Value)));
            var range = value.Range is null
                ? "none"
                : $"{_FormatValue(value.Range.Minimum)}..{_FormatValue(value.Range.Maximum)}";
            _Output.WriteLine(
                $"value {value.Id} type={value.ValueType.Name} read={value.CanRead} write={value.CanWrite} " +
                $"nullable={value.IsNullable} range={range} options={options}");
        }

        foreach (var reference in descriptor.References.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            _Output.WriteLine($"reference {reference.Id} type={reference.ReferenceType.Name}");
        }

        foreach (var collection in descriptor.Collections.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            _Output.WriteLine($"collection {collection.Id} element={collection.ElementType.Name}");
        }

        foreach (var action in descriptor.Actions.OrderBy(static member => member.Id, StringComparer.Ordinal))
        {
            var parameters = string.Join(
                ", ",
                action.Parameters.Select(static parameter =>
                    $"{parameter.Id}:{parameter.ParameterType.Name}" +
                    $"?={parameter.IsNullable}" +
                    (parameter.HasDefaultValue
                        ? $" default={_FormatValue(parameter.DefaultValue)}"
                        : string.Empty)));
            _Output.WriteLine(
                $"action {action.Id}({parameters}) risk={action.Risk} confirm={action.RequiresConfirmation}");
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

        var descriptorResult = await _GetCurrentDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (!descriptorResult.IsSuccess)
        {
            _WriteFailure(descriptorResult.Error);
            return;
        }

        var value = descriptorResult.Value.Values.FirstOrDefault(member =>
            string.Equals(member.Id, tokens[1], StringComparison.Ordinal));
        if (value is null)
        {
            _WriteFailure(
                InteractionErrorCode.TARGET_MISSING,
                $"Value '{tokens[1]}' was not found at '{CurrentPath}'.");
            return;
        }

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

        var descriptorResult = await _GetCurrentDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (!descriptorResult.IsSuccess)
        {
            _WriteFailure(descriptorResult.Error);
            return;
        }

        var value = descriptorResult.Value.Values.FirstOrDefault(member =>
            string.Equals(member.Id, tokens[1], StringComparison.Ordinal));
        if (value is null)
        {
            _WriteFailure(
                InteractionErrorCode.TARGET_MISSING,
                $"Value '{tokens[1]}' was not found at '{CurrentPath}'.");
            return;
        }

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

        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 2; index < tokens.Count; index++)
        {
            var separator = tokens[index].IndexOf('=');
            if (separator <= 0)
            {
                _WriteFailure(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Argument '{tokens[index]}' must use name=value syntax.");
                return;
            }

            var name = tokens[index][..separator];
            var value = tokens[index][(separator + 1)..];
            if (!arguments.TryAdd(name, value))
            {
                _WriteFailure(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Argument '{name}' was supplied more than once.");
                return;
            }
        }

        var descriptorResult = await _GetCurrentDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (!descriptorResult.IsSuccess)
        {
            _WriteFailure(descriptorResult.Error);
            return;
        }

        var action = descriptorResult.Value.Actions.FirstOrDefault(member =>
            string.Equals(member.Id, tokens[1], StringComparison.Ordinal));
        if (action is null)
        {
            _WriteFailure(
                InteractionErrorCode.TARGET_MISSING,
                $"Action '{tokens[1]}' was not found at '{CurrentPath}'.");
            return;
        }

        var invocationStart = await action.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (!invocationStart.IsSuccess)
        {
            _WriteFailure(invocationStart.Error);
            return;
        }

        var completion = await invocationStart.Value.Completion.ConfigureAwait(false);
        if (!completion.IsSuccess)
        {
            _WriteFailure(completion.Error);
            return;
        }

        _Output.WriteLine($"{action.Id} => {_FormatValue(completion.Value)}");
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
                foreach (var reference in descriptorResult.Value.References)
                {
                    var current = LogicalPath.Parse(CurrentPath).Value;
                    completions.Add(current.Append(reference.Id).ToString());
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

        var resolution = await _Host.Paths.ResolveAsync(_Locations[^1].Path, cancellationToken)
            .ConfigureAwait(false);
        if (!resolution.IsResolved)
        {
            return InteractionResult.Failure<IObjectDescriptor>(
                resolution.Error!.Code,
                resolution.Error.Message);
        }

        if (resolution.Target is not { Kind: DescriptorKind.INSTANCE } target)
        {
            return InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.INVALID_PATH,
                "The current path no longer identifies a navigable object.");
        }

        _Locations = resolution.Locations.ToList();
        return InteractionResult.Success(target.OwnerDescriptor);
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

    private static string _FormatValue(object? value) => value switch
    {
        null => "null",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

}
