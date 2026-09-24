namespace UIEngine.Core;

internal static class PathResolution
{
    public static InteractionResult<ResolvedPath> Resolve(
        UIEngineHost host,
        LogicalPath path)
    {
        var segments = path.Segments;
        if (segments.Count == 0)
        {
            return InteractionResult.Failure<ResolvedPath>(
                InteractionErrorCode.INVALID_INPUT,
                "The root list is not a navigable object.");
        }

        if (segments[0] is not MemberLogicalPathSegment rootSegment)
        {
            return InteractionResult.Failure<ResolvedPath>(
                InteractionErrorCode.INVALID_INPUT,
                "The first path segment must identify a registered root.");
        }

        var root = host.Roots.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Name, rootSegment.Name));
        if (root is null)
        {
            return InteractionResult.Failure<ResolvedPath>(
                InteractionErrorCode.NOT_FOUND,
                $"Root '{rootSegment.Name}' was not found.");
        }

        var canonical = LogicalPath.Root.Append(root.Name);
        var rootNode = host.CreateObjectNode(root.Handle, root.Name);
        if (!rootNode.IsSuccess)
        {
            return InteractionResult.Failure<ResolvedPath>(rootNode.Error!);
        }

        BaseNode current = rootNode.Value;
        var resolutionChain = new List<ResolvedNode>
        {
            new(canonical, current),
        };

        for (var index = 1; index < segments.Count; index++)
        {
            var segment = segments[index];
            InteractionResult<BaseNode> resolved = segment switch
            {
                MemberLogicalPathSegment member => _ResolveMember(current, member),
                ListLogicalPathSegment list => _ResolveListElement(host, current, list),
                DictLogicalPathSegment dictionary =>
                    _ResolveDictionaryValue(host, current, dictionary),
                _ => InteractionResult.Failure<BaseNode>(
                    InteractionErrorCode.UNSUPPORTED,
                    "The logical path contains an unsupported segment."),
            };
            if (!resolved.IsSuccess)
            {
                return InteractionResult.Failure<ResolvedPath>(resolved.Error!);
            }

            canonical = canonical.Append(segment);
            current = resolved.Value;
            resolutionChain.Add(new ResolvedNode(canonical, current));
        }

        return InteractionResult.Success(new ResolvedPath(resolutionChain));
    }

    private static InteractionResult<BaseNode> _ResolveMember(
        BaseNode current,
        MemberLogicalPathSegment segment)
    {
        if (current is not IObjectNode objectNode)
        {
            return _InvalidTraversal(segment.Name);
        }

        var matches = objectNode.Members
            .Where(member => StringComparer.Ordinal.Equals(member.Name, segment.Name))
            .ToArray();
        if (matches.Length == 0)
        {
            return InteractionResult.Failure<BaseNode>(
                InteractionErrorCode.NOT_FOUND,
                $"Member '{segment.Name}' was not found.");
        }

        if (matches.Length > 1)
        {
            return InteractionResult.Failure<BaseNode>(
                InteractionErrorCode.AMBIGUOUS,
                $"Member '{segment.Name}' is ambiguous.");
        }

        if (matches[0] is not LiveReferenceNode reference)
        {
            return InteractionResult.Success(matches[0]);
        }

        var target = reference.ResolveTarget();
        return target.IsSuccess
            ? InteractionResult.Success<BaseNode>(target.Value)
            : InteractionResult.Failure<BaseNode>(target.Error!);
    }

    private static InteractionResult<BaseNode> _ResolveListElement(
        UIEngineHost host,
        BaseNode current,
        ListLogicalPathSegment segment)
    {
        if (current is not LiveCollectionNode collection)
        {
            return _InvalidTraversal($"index {segment.Index}");
        }

        return _ResolveSelectedObject(host, collection, collection.Select(segment));
    }

    private static InteractionResult<BaseNode> _ResolveDictionaryValue(
        UIEngineHost host,
        BaseNode current,
        DictLogicalPathSegment segment)
    {
        if (current is not LiveCollectionNode collection)
        {
            return _InvalidTraversal($"key '{segment.Key}'");
        }

        return _ResolveSelectedObject(host, collection, collection.Select(segment));
    }

    private static InteractionResult<BaseNode> _ResolveSelectedObject(
        UIEngineHost host,
        LiveCollectionNode collection,
        InteractionResult<IReadOnlyList<Guid>> selected)
    {
        if (!selected.IsSuccess)
        {
            return InteractionResult.Failure<BaseNode>(selected.Error!);
        }

        if (selected.Value.Count == 0)
        {
            return InteractionResult.Failure<BaseNode>(
                InteractionErrorCode.NOT_FOUND,
                $"Selection on collection '{collection.Name}' matched no object.");
        }

        if (selected.Value.Count > 1)
        {
            return InteractionResult.Failure<BaseNode>(
                InteractionErrorCode.AMBIGUOUS,
                $"Selection on collection '{collection.Name}' matched multiple objects.");
        }

        var resolved = host.CreateObjectNode(selected.Value[0], collection.Name);
        return resolved.IsSuccess
            ? InteractionResult.Success<BaseNode>(resolved.Value)
            : InteractionResult.Failure<BaseNode>(resolved.Error!);
    }

    private static InteractionResult<BaseNode> _InvalidTraversal(string description) =>
        InteractionResult.Failure<BaseNode>(
            InteractionErrorCode.INVALID_INPUT,
            $"The {description} path segment cannot be applied at this location.");
}
