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
            StringComparer.Ordinal.Equals(candidate.Identifier, rootSegment.Identifier));
        if (root is null)
        {
            return InteractionResult.Failure<ResolvedPath>(
                InteractionErrorCode.NOT_FOUND,
                $"Root '{rootSegment.Identifier}' was not found.");
        }

        var canonical = LogicalPath.Root.Append(root.Identifier);
        var locations = new List<PathLocation> { new(root.Handle, canonical) };
        var current = host.CreateObjectNode(root.Handle, root.Identifier);
        if (!current.IsSuccess)
        {
            return InteractionResult.Failure<ResolvedPath>(current.Error!);
        }

        if (path.Segments.Count == 1)
        {
            return InteractionResult.Success(new ResolvedPath(
                current.Value,
                canonical,
                locations));
        }

        for (var index = 1; index < path.Segments.Count; index++)
        {
            var segment = path.Segments[index];
            var matches = current.Value.Members
                .Where(member => StringComparer.Ordinal.Equals(member.Id, segment.Identifier))
                .ToArray();
            if (matches.Length == 0)
            {
                return InteractionResult.Failure<ResolvedPath>(
                    InteractionErrorCode.NOT_FOUND,
                    $"Member '{segment.Identifier}' was not found at '{canonical}'.");
            }

            if (matches.Length > 1)
            {
                return InteractionResult.Failure<ResolvedPath>(
                    InteractionErrorCode.AMBIGUOUS,
                    $"Member '{segment.Identifier}' is ambiguous at '{canonical}'.");
            }

            var member = matches[0];
            if (member is IReferenceNode reference)
            {
                if (segment.Selector is not null)
                {
                    return _InvalidTraversal(segment.Identifier);
                }

                var read = reference.ReadReference();
                if (!read.IsSuccess)
                {
                    return InteractionResult.Failure<ResolvedPath>(read.Error!);
                }

                if (read.Value is null)
                {
                    return InteractionResult.Failure<ResolvedPath>(
                        InteractionErrorCode.UNAVAILABLE,
                        $"Reference '{member.Id}' is empty.");
                }

                canonical = canonical.Append(segment.Identifier);
                var resolved = host.CreateObjectNode(read.Value.Value, member.Id);
                if (!resolved.IsSuccess)
                {
                    return InteractionResult.Failure<ResolvedPath>(resolved.Error!);
                }

                current = resolved;
                locations.Add(new PathLocation(read.Value.Value, canonical));
                if (index == path.Segments.Count - 1)
                {
                    return InteractionResult.Success(new ResolvedPath(
                        current.Value,
                        canonical,
                        locations));
                }

                continue;
            }

            if (member is LiveCollectionNode collection)
            {
                if (segment.Selector is null)
                {
                    if (index != path.Segments.Count - 1)
                    {
                        return _InvalidTraversal(segment.Identifier);
                    }

                    canonical = canonical.Append(segment.Identifier);
                    return InteractionResult.Success(new ResolvedPath(
                        member,
                        canonical,
                        locations));
                }

                var selected = collection.Select(segment.Selector);
                if (!selected.IsSuccess)
                {
                    return InteractionResult.Failure<ResolvedPath>(selected.Error!);
                }

                if (selected.Value.Count == 0)
                {
                    return InteractionResult.Failure<ResolvedPath>(
                        InteractionErrorCode.NOT_FOUND,
                        $"Selector on collection '{member.Id}' matched no object.");
                }

                if (selected.Value.Count > 1)
                {
                    return InteractionResult.Failure<ResolvedPath>(
                        InteractionErrorCode.AMBIGUOUS,
                        $"Selector on collection '{member.Id}' matched multiple objects.");
                }

                var handle = selected.Value[0];
                canonical = canonical.Append(segment.Identifier, segment.Selector);
                var resolved = host.CreateObjectNode(handle, member.Id);
                if (!resolved.IsSuccess)
                {
                    return InteractionResult.Failure<ResolvedPath>(resolved.Error!);
                }

                current = resolved;
                locations.Add(new PathLocation(handle, canonical));
                if (index == path.Segments.Count - 1)
                {
                    return InteractionResult.Success(new ResolvedPath(
                        current.Value,
                        canonical,
                        locations));
                }

                continue;
            }

            if (segment.Selector is not null || index != path.Segments.Count - 1)
            {
                return _InvalidTraversal(segment.Identifier);
            }

            canonical = canonical.Append(segment.Identifier);
            return InteractionResult.Success(new ResolvedPath(member, canonical, locations));
        }

        throw new InvalidOperationException("Path traversal ended without a result.");
    }

    private static InteractionResult<ResolvedPath> _InvalidTraversal(string memberId) =>
        InteractionResult.Failure<ResolvedPath>(
            InteractionErrorCode.INVALID_INPUT,
            $"Member '{memberId}' cannot be traversed in this path.");
}
