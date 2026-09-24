namespace UIEngine.Core;

internal static class PathResolution
{
    public static InteractionResult<ResolvedPath> Resolve(
        UIEngineHost host,
        LogicalPath path)
    {
        if (path.Segments.Count == 0)
        {
            return InteractionResult.Failure<ResolvedPath>(
                InteractionErrorCode.INVALID_INPUT,
                "The root list is not a navigable object.");
        }

        var rootSegment = path.Segments[0];
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

        var resolutionChain = new List<ResolvedNode>
        {
            new(canonical, rootNode.Value),
        };
        IObjectNode current = rootNode.Value;

        if (path.Segments.Count == 1)
        {
            return InteractionResult.Success(new ResolvedPath(resolutionChain));
        }

        for (var index = 1; index < path.Segments.Count; index++)
        {
            var segment = path.Segments[index];
            var matches = current.Members
                .Where(member => StringComparer.Ordinal.Equals(member.Name, segment.Name))
                .ToArray();
            if (matches.Length == 0)
            {
                return InteractionResult.Failure<ResolvedPath>(
                    InteractionErrorCode.NOT_FOUND,
                    $"Member '{segment.Name}' was not found at '{canonical}'.");
            }

            if (matches.Length > 1)
            {
                return InteractionResult.Failure<ResolvedPath>(
                    InteractionErrorCode.AMBIGUOUS,
                    $"Member '{segment.Name}' is ambiguous at '{canonical}'.");
            }

            var member = matches[0];
            if (member is LiveReferenceNode reference)
            {
                if (segment is not MemberLogicalPathSegment)
                {
                    return _InvalidTraversal(segment.Name);
                }

                canonical = canonical.Append(segment.Name);
                var resolved = reference.ResolveTarget();
                if (!resolved.IsSuccess)
                {
                    return InteractionResult.Failure<ResolvedPath>(resolved.Error!);
                }

                resolutionChain.Add(new ResolvedNode(canonical, resolved.Value));
                if (index == path.Segments.Count - 1)
                {
                    return InteractionResult.Success(new ResolvedPath(resolutionChain));
                }

                current = resolved.Value;
                continue;
            }

            if (member is LiveCollectionNode collection)
            {
                if (segment is MemberLogicalPathSegment)
                {
                    if (index != path.Segments.Count - 1)
                    {
                        return _InvalidTraversal(segment.Name);
                    }

                    canonical = canonical.Append(segment.Name);
                    resolutionChain.Add(new ResolvedNode(canonical, member));
                    return InteractionResult.Success(new ResolvedPath(resolutionChain));
                }

                var collectionPath = canonical.Append(segment.Name);
                resolutionChain.Add(new ResolvedNode(collectionPath, member));

                var selected = segment switch
                {
                    ListLogicalPathSegment list => collection.Select(list),
                    DictLogicalPathSegment dictionary => collection.Select(dictionary),
                    _ => throw new InvalidOperationException(
                        "Unknown logical path segment type."),
                };
                if (!selected.IsSuccess)
                {
                    return InteractionResult.Failure<ResolvedPath>(selected.Error!);
                }

                if (selected.Value.Count == 0)
                {
                    return InteractionResult.Failure<ResolvedPath>(
                        InteractionErrorCode.NOT_FOUND,
                        $"Selector on collection '{member.Name}' matched no object.");
                }

                if (selected.Value.Count > 1)
                {
                    return InteractionResult.Failure<ResolvedPath>(
                        InteractionErrorCode.AMBIGUOUS,
                        $"Selector on collection '{member.Name}' matched multiple objects.");
                }

                var handle = selected.Value[0];
                canonical = canonical.Append(segment);
                var resolved = host.CreateObjectNode(handle, member.Name);
                if (!resolved.IsSuccess)
                {
                    return InteractionResult.Failure<ResolvedPath>(resolved.Error!);
                }

                resolutionChain.Add(new ResolvedNode(canonical, resolved.Value));
                if (index == path.Segments.Count - 1)
                {
                    return InteractionResult.Success(new ResolvedPath(resolutionChain));
                }

                current = resolved.Value;
                continue;
            }

            if (segment is not MemberLogicalPathSegment || index != path.Segments.Count - 1)
            {
                return _InvalidTraversal(segment.Name);
            }

            canonical = canonical.Append(segment.Name);
            resolutionChain.Add(new ResolvedNode(canonical, member));
            return InteractionResult.Success(new ResolvedPath(resolutionChain));
        }

        throw new InvalidOperationException("Path traversal ended without a result.");
    }

    private static InteractionResult<ResolvedPath> _InvalidTraversal(string memberName) =>
        InteractionResult.Failure<ResolvedPath>(
            InteractionErrorCode.INVALID_INPUT,
            $"Member '{memberName}' cannot be traversed in this path.");
}
