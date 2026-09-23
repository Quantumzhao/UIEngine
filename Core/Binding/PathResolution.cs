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
        var locations = new List<PathLocation> { new(root.Handle, canonical) };
        var current = host.CreateObjectNode(root.Handle, root.Name);
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
            if (member is IReferenceNode reference)
            {
                if (segment.Selector is not null)
                {
                    return _InvalidTraversal(segment.Name);
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
                        $"Reference '{member.Name}' is empty.");
                }

                canonical = canonical.Append(segment.Name);
                var resolved = host.CreateObjectNode(read.Value.Value, member.Name);
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
                        return _InvalidTraversal(segment.Name);
                    }

                    canonical = canonical.Append(segment.Name);
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
                        $"Selector on collection '{member.Name}' matched no object.");
                }

                if (selected.Value.Count > 1)
                {
                    return InteractionResult.Failure<ResolvedPath>(
                        InteractionErrorCode.AMBIGUOUS,
                        $"Selector on collection '{member.Name}' matched multiple objects.");
                }

                var handle = selected.Value[0];
                canonical = canonical.Append(segment.Name, segment.Selector);
                var resolved = host.CreateObjectNode(handle, member.Name);
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
                return _InvalidTraversal(segment.Name);
            }

            canonical = canonical.Append(segment.Name);
            return InteractionResult.Success(new ResolvedPath(member, canonical, locations));
        }

        throw new InvalidOperationException("Path traversal ended without a result.");
    }

    private static InteractionResult<ResolvedPath> _InvalidTraversal(string memberName) =>
        InteractionResult.Failure<ResolvedPath>(
            InteractionErrorCode.INVALID_INPUT,
            $"Member '{memberName}' cannot be traversed in this path.");
}
