using System.Globalization;
using LanguageExt;
using UIEngine.Core;
using static LanguageExt.Prelude;

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
        "exit",
    ];

    private readonly UIEngineHost _Host;
    private readonly TextWriter _Output;
    private List<_LocationBinding> _Locations = [];
    private bool _Disposed;

    public CliSession(UIEngineHost host, TextWriter output)
    {
        _Host = host;
        _Output = output;
    }

    internal Guid? CurrentHandle => _Locations.Count == 0
        ? null
        : _Locations[^1].LastResolvedHandle;

    internal string CurrentPath => _Locations.Count == 0 ? "/" : _Locations[^1].Path.ToString();

    internal async Task<bool> ExecuteAsync(string line)
    {
        if (_Disposed)
        {
            _WriteFailure(InteractionErrorCode.DISPOSED, "The CLI session has been disposed.");
            return false;
        }

        var tokenResult = CliTokenizer.Tokenize(line);
        if (!tokenResult.IsRight)
        {
            _WriteFailure((InteractionError)tokenResult);
            return true;
        }

        var tokens = tokenResult.IfLeft(static _ => System.Array.Empty<string>());
        if (tokens.Count == 0)
        {
            return true;
        }

        switch (tokens[0].ToUpperInvariant())
        {
            case "LS":
                _List(tokens);
                return true;
            case "CD":
                _ChangeDirectory(tokens);
                return true;
            case "INSPECT":
                _Inspect(tokens);
                return true;
            case "GET":
                _Get(tokens);
                return true;
            case "SET":
                _Set(tokens);
                return true;
            case "CALL":
                await _CallAsync(tokens);
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

    internal IReadOnlyList<string> GetCompletions(
        string text,
        int caret)
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
                _GetNavigationCompletions(),
            "LS" when completedTokens.Length == 1 =>
                _GetCollectionCompletions(),
            "GET" when completedTokens.Length == 1 =>
                _GetValueCompletions(false),
            "SET" when completedTokens.Length == 1 =>
                _GetValueCompletions(true),
            "CALL" => _GetActionCompletions(completedTokens),
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
        _Host.Dispose();
    }

    private void _List(IReadOnlyList<string> tokens)
    {
        if (tokens.Count > 1)
        {
            _ListCollection(tokens);
            return;
        }

        if (_Locations.Count == 0)
        {
            foreach (var root in _Host.Roots.OrderBy(static root => root.Name, StringComparer.Ordinal))
            {
                _Output.WriteLine($"root {root.Name}");
            }

            return;
        }

        var resolved = _GetCurrentObjectNode();
        if (!resolved.IsRight)
        {
            _WriteFailure((InteractionError)resolved);
            return;
        }

        foreach (var member in ((IObjectNode)(BaseNode)resolved).Members
                     .OrderBy(_GetNodeOrder)
                     .ThenBy(static member => member.Name, StringComparer.Ordinal))
        {
            _Output.WriteLine($"{_GetNodeKind(member)} {member.Name}");
        }
    }

    private void _ListCollection(IReadOnlyList<string> tokens)
    {
        var options = _ParseCollectionOptions(tokens);
        if (!options.IsRight)
        {
            _WriteFailure((InteractionError)options);
            return;
        }

        var resolved = _ResolveMember(
            tokens[1],
            static member => member is ICollectionNode,
            "collection");
        if (!resolved.IsRight)
        {
            _WriteFailure((InteractionError)resolved);
            return;
        }

        var collection = (ICollectionNode)(BaseNode)resolved;
        var collectionOptions = (_CollectionOptions)options;
        var read = collection.ReadEntries(
            collectionOptions.Offset,
            collectionOptions.Limit ?? DEFAULT_COLLECTION_LIMIT);
        if (!read.IsRight)
        {
            _WriteFailure((InteractionError)read);
            return;
        }

        var slice = (CollectionSlice)read;
        _Output.WriteLine(
            $"collection {((BaseNode)resolved).Name} offset={slice.Offset} count={slice.Entries.Count} " +
            $"total={_FormatValue(slice.TotalCount)} hasMore={slice.HasMore}");
        foreach (var entry in slice.Entries)
        {
            var key = entry.Key is null ? string.Empty : $" key={_FormatValue(entry.Key)}";
            var content = entry switch
            {
                NullCollectionEntry => "null",
                ScalarCollectionEntry scalar => $"value={_FormatValue(scalar.Value)}",
                ReferenceCollectionEntry reference =>
                    $"reference={reference.Handle:D}",
                _ => throw new InvalidOperationException("Unknown collection entry type."),
            };
            _Output.WriteLine($"[{entry.Position}]{key} {content}");
        }
    }

    private void _ChangeDirectory(IReadOnlyList<string> tokens)
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

        var path = CliLogicalPathParser.Parse(tokens[1]);
        if (!path.IsRight)
        {
            _WriteFailure((InteractionError)path);
            return;
        }

        var resolution = _Host.ResolvePath((LogicalPath)path);
        if (!resolution.IsRight)
        {
            _WriteFailure((InteractionError)resolution);
            return;
        }

        var resolvedPath = (ResolvedPath)resolution;
        if (!resolvedPath.IsObject)
        {
            _WriteFailure(
                InteractionErrorCode.INVALID_INPUT,
                "The path identifies a member rather than a navigable object.");
            return;
        }

        var captured = _CaptureLocations(resolvedPath.ResolutionChain);
        if (!captured.IsRight)
        {
            _WriteFailure((InteractionError)captured);
            return;
        }

        _Locations = (List<_LocationBinding>)captured;
        _Output.WriteLine(CurrentPath);
    }

    private void _Inspect(IReadOnlyList<string> tokens)
    {
        if (!_HasArity(tokens, 1, "inspect"))
        {
            return;
        }

        var resolved = _GetCurrentObjectNode();
        if (!resolved.IsRight)
        {
            _WriteFailure((InteractionError)resolved);
            return;
        }

        var node = (BaseNode)resolved;
        var objectNode = (IObjectNode)node;
        _Output.WriteLine($"path: {CurrentPath}");
        _Output.WriteLine($"type: {node.ValueType.FullName ?? node.ValueType.Name}");
        _Output.WriteLine($"handle: {objectNode.Handle:D}");
        _Output.WriteLine($"summary: {objectNode.Summary ?? "null"}");

        foreach (var member in objectNode.Members.OrderBy(static member => member.Name, StringComparer.Ordinal))
        {
            switch (member)
            {
                case IMethodNode method:
                    var parameters = string.Join(
                        ", ",
                        method.Parameters.Select(parameter =>
                            $"{parameter.Name}:{parameter.ParameterType.Name}" +
                            $" required={parameter.IsRequired}" +
                            $" nullable={parameter.IsNullable}" +
                            $" default={(parameter.HasDefaultValue ? _FormatValue(parameter.DefaultValue) : "absent")}" +
                            $" range={_FormatRange(parameter.Range)}" +
                            $" options={(parameter.Options.Count == 0 ? "none" : string.Join('|', parameter.Options.Select(static option => _FormatValue(option.Value))))}"));
                    _Output.WriteLine(
                        $"action {member.Name}({parameters}) result={method.ResultType?.Name ?? "void"} " +
                        $"async={method.IsAsynchronous} status={method.Status?.ToString() ?? "never-run"}");
                    break;
                case ICollectionNode collection:
                    _Output.WriteLine(
                        $"collection {member.Name} element={collection.ElementType.Name} " +
                        $"key={collection.KeyType?.Name ?? "none"}");
                    break;
                case IReferenceNode reference:
                    _Output.WriteLine($"reference {member.Name} type={reference.ReferenceType.Name}");
                    break;
                default:
                    var writable = member as IWritableValueNode;
                    var valueOptions = member is IEnumNode enumNode
                        ? enumNode.Options
                        : writable?.Options ?? [];
                    var options = valueOptions.Count == 0
                        ? "none"
                        : string.Join('|', valueOptions.Select(static option => _FormatValue(option.Value)));
                    _Output.WriteLine(
                        $"value {member.Name} type={member.ValueType.Name} " +
                        $"read={member is IReadableValueNode} " +
                        $"write={writable is not null} nullable={member is INullableValueNode} " +
                        $"range={_FormatRange(writable?.Range)} options={options}");
                    break;
            }
        }
    }

    private void _Get(IReadOnlyList<string> tokens)
    {
        if (!_HasArity(tokens, 2, "get <member>"))
        {
            return;
        }

        var resolved = _ResolveMember(
            tokens[1],
            static member => member is IReadableValueNode,
            "readable value");
        if (!resolved.IsRight)
        {
            _WriteFailure((InteractionError)resolved);
            return;
        }

        var resolvedNode = (BaseNode)resolved;
        var read = ((IReadableValueNode)resolvedNode).ReadValue();
        if (!read.IsRight)
        {
            _WriteFailure((InteractionError)read);
            return;
        }

        var value = ((Option<object>)read).IfNoneUnsafe((object?)null);
        _Output.WriteLine($"{resolvedNode.Name} = {_FormatValue(value)}");
    }

    private void _Set(IReadOnlyList<string> tokens)
    {
        if (!_HasArity(tokens, 3, "set <member> <value>"))
        {
            return;
        }

        var resolved = _ResolveMember(
            tokens[1],
            static member => member is IWritableValueNode,
            "writable value");
        if (!resolved.IsRight)
        {
            _WriteFailure((InteractionError)resolved);
            return;
        }

        var resolvedNode = (BaseNode)resolved;
        var write = ((IWritableValueNode)resolvedNode).WriteValue(tokens[2]);
        if (!write.IsRight)
        {
            _WriteFailure((InteractionError)write);
            return;
        }

        var value = ((Option<object>)write).IfNoneUnsafe((object?)null);
        _Output.WriteLine($"{resolvedNode.Name} = {_FormatValue(value)}");
    }

    private async Task _CallAsync(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            _WriteFailure(InteractionErrorCode.INVALID_INPUT, "Usage: call <action> name=value ...");
            return;
        }

        var arguments = _ParseArguments(tokens);
        if (!arguments.IsRight)
        {
            _WriteFailure((InteractionError)arguments);
            return;
        }

        var resolved = _ResolveMember(
            tokens[1],
            static member => member is IMethodNode,
            "action");
        if (!resolved.IsRight)
        {
            _WriteFailure((InteractionError)resolved);
            return;
        }

        var resolvedNode = (BaseNode)resolved;
        var action = (IMethodNode)resolvedNode;
        var parsedArguments = arguments.IfLeft(
            static _ => new Dictionary<string, object?>());
        var started = action.Invoke(parsedArguments);
        if (!started.IsRight)
        {
            _WriteFailure((InteractionError)started);
            return;
        }

        var result = await action.ResultTask!;

        if (!result.IsRight)
        {
            _WriteFailure((InteractionError)result);
            return;
        }

        var value = ((Option<object>)result).IfNoneUnsafe((object?)null);
        _Output.WriteLine($"{resolvedNode.Name} => {_FormatValue(value)}");
    }

    private string[] _GetNavigationCompletions()
    {
        var completions = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            "/",
            "..",
        };
        foreach (var root in _Host.Roots)
        {
            completions.Add(LogicalPath.Root.Append(root.Name).ToString());
        }

        if (CurrentHandle is not null)
        {
            var resolved = _GetCurrentObjectNode();
            if (resolved.IsRight)
            {
                var current = _Locations[^1].Path;
                foreach (var member in ((IObjectNode)(BaseNode)resolved).Members
                             .Where(static member => member is IReferenceNode or ICollectionNode))
                {
                    completions.Add(current.Append(member.Name).ToString());
                }
            }
        }

        return completions.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
    }

    private string[] _GetValueCompletions(bool writableOnly)
    {
        var resolved = _GetCurrentObjectNode();
        return resolved.IsRight
            ? ((IObjectNode)(BaseNode)resolved).Members
                .Where(_IsValueNode)
                .Where(value => !writableOnly || value is IWritableValueNode)
                .Select(static value => value.Name)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    private string[] _GetCollectionCompletions()
    {
        var resolved = _GetCurrentObjectNode();
        return resolved.IsRight
            ? ((IObjectNode)(BaseNode)resolved).Members
                .Where(static member => member is ICollectionNode)
                .Select(static collection => collection.Name)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    private string[] _GetActionCompletions(
        string[] completedTokens)
    {
        var resolved = _GetCurrentObjectNode();
        if (!resolved.IsRight)
        {
            return [];
        }

        var actions = ((IObjectNode)(BaseNode)resolved).Members
            .Where(static member => member is IMethodNode);
        if (completedTokens.Length == 1)
        {
            return actions.Select(static action => action.Name)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        var action = actions.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Name, completedTokens[1]));
        if (action is null)
        {
            return [];
        }

        var supplied = completedTokens.Skip(2)
            .Select(static token => token.Split('=', 2)[0])
            .ToHashSet(StringComparer.Ordinal);
        return ((IMethodNode)action).Parameters
            .Where(parameter => !supplied.Contains(parameter.Name))
            .Select(static parameter => $"{parameter.Name}=")
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private Either<InteractionError, BaseNode> _GetCurrentObjectNode()
    {
        if (_Locations.Count == 0)
        {
            return Left(new InteractionError(
                InteractionErrorCode.UNAVAILABLE,
                "No object is selected. Use 'cd /<root>' first."));
        }

        var location = _Locations[^1];
        var resolution = _Host.ResolvePath(location.Path);
        if (!resolution.IsRight)
        {
            return _Failure<BaseNode>((InteractionError)resolution);
        }

        var resolvedPath = (ResolvedPath)resolution;
        if (resolvedPath.Node is not IObjectNode objectNode)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "The current path no longer identifies a navigable object."));
        }

        var typeName = resolvedPath.Node.ValueType.FullName ?? resolvedPath.Node.ValueType.Name;
        if (!StringComparer.Ordinal.Equals(location.TypeName, typeName))
        {
            return Left(new InteractionError(
                InteractionErrorCode.TYPE_MISMATCH,
                "The current path resolved to an incompatible object type."));
        }

        _Locations[^1] = location with
        {
            Path = resolvedPath.CanonicalPath,
            LastResolvedHandle = objectNode.Handle,
        };
        return Right(resolvedPath.Node);
    }

    private Either<InteractionError, BaseNode> _ResolveMember(
        string memberName,
        Func<BaseNode, bool> matchesExpectedFacet,
        string expectedKind)
    {
        var resolved = _GetCurrentObjectNode();
        if (!resolved.IsRight)
        {
            return resolved;
        }

        var members = ((IObjectNode)(BaseNode)resolved).Members
            .Where(member => StringComparer.Ordinal.Equals(member.Name, memberName))
            .ToArray();
        if (members.Length == 0)
        {
            return Left(new InteractionError(
                InteractionErrorCode.NOT_FOUND,
                $"Member '{memberName}' was not found."));
        }

        if (members.Length > 1)
        {
            return Left(new InteractionError(
                InteractionErrorCode.AMBIGUOUS,
                $"Member '{memberName}' is ambiguous."));
        }

        return matchesExpectedFacet(members[0])
            ? Right(members[0])
            : Left(new InteractionError(
                InteractionErrorCode.TYPE_MISMATCH,
                $"Member '{memberName}' is not a {expectedKind}."));
    }

    private static Either<InteractionError, List<_LocationBinding>> _CaptureLocations(
        IReadOnlyList<ResolvedNode> resolutionChain)
    {
        var captured = new List<_LocationBinding>(resolutionChain.Count);
        foreach (var resolved in resolutionChain)
        {
            if (resolved.Node is not IObjectNode objectNode)
            {
                continue;
            }

            captured.Add(new _LocationBinding(
                resolved.CanonicalPath,
                resolved.Node.ValueType.FullName ?? resolved.Node.ValueType.Name,
                objectNode.Handle));
        }

        return Right(captured);
    }

    private static Either<InteractionError, _CollectionOptions> _ParseCollectionOptions(
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "Usage: ls <collection> [offset=<n>] [limit=<n>]"));
        }

        long offset = 0;
        int? limit = null;
        var supplied = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        for (var index = 2; index < tokens.Count; index++)
        {
            var pair = tokens[index].Split('=', 2);
            if (pair.Length != 2 || !supplied.Add(pair[0]))
            {
                return Left(new InteractionError(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Collection option '{tokens[index]}' is malformed or duplicated."));
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
                    return Left(new InteractionError(
                        InteractionErrorCode.INVALID_INPUT,
                        $"Unknown or invalid collection option '{tokens[index]}'."));
            }
        }

        return Right(new _CollectionOptions(offset, limit));
    }

    private static Either<InteractionError, IReadOnlyDictionary<string, object?>> _ParseArguments(
        IReadOnlyList<string> tokens)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 2; index < tokens.Count; index++)
        {
            var separator = tokens[index].IndexOf('=');
            if (separator <= 0)
            {
                return Left(new InteractionError(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Argument '{tokens[index]}' must use name=value syntax."));
            }

            var name = tokens[index][..separator];
            var value = tokens[index][(separator + 1)..];
            if (!arguments.TryAdd(name, value))
            {
                return Left(new InteractionError(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Argument '{name}' was supplied more than once."));
            }
        }

        return Right((IReadOnlyDictionary<string, object?>)arguments);
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

    private static Either<InteractionError, T> _Failure<T>(InteractionError error) =>
        Left(error);

    private void _WriteFailure(InteractionError error)
    {
        _WriteFailure(error.Code, error.Message);
        foreach (var issue in error.Issues)
        {
            _Output.WriteLine($"issue {issue.Code} name={issue.TargetName}: {issue.Message}");
        }
    }

    private void _WriteFailure(InteractionErrorCode code, string message) =>
        _Output.WriteLine($"error {code}: {message}");

    private static bool _IsValueNode(BaseNode node) =>
        node is IReadableValueNode or IWritableValueNode;

    private static int _GetNodeOrder(BaseNode node) => node switch
    {
        IMethodNode => 3,
        ICollectionNode => 2,
        IReferenceNode => 1,
        _ => 0,
    };

    private static string _GetNodeKind(BaseNode node) => node switch
    {
        IMethodNode => "action",
        ICollectionNode => "collection",
        IReferenceNode => "reference",
        _ => "value",
    };

    private static string _FormatRange(IValueRange? range) => range is null
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
        string TypeName,
        Guid LastResolvedHandle);

    private readonly record struct _CollectionOptions(long Offset, int? Limit);
}
