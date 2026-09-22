namespace UIEngine.Core;

internal static class PathResolution
{
    public static async Task<InteractionResult<ResolvedPath>> ResolveAsync(
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
        host.RecordCanonicalPath(root.Handle, canonical);
        var handle = root.Handle;
        var described = await host.DescribeAsync(handle);
        if (!described.IsSuccess)
        {
            return InteractionResult.Failure<ResolvedPath>(described.Error!);
        }

        var descriptor = described.Value;
        if (path.Segments.Count == 1)
        {
            return InteractionResult.Success(new ResolvedPath(
                handle,
                descriptor,
                null,
                canonical,
                locations));
        }

        for (var index = 1; index < path.Segments.Count; index++)
        {
            var segment = path.Segments[index];
            var matches = descriptor.Members
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
            if (member is ValueDescriptor or ActionDescriptor)
            {
                if (segment.Selector is not null || index != path.Segments.Count - 1)
                {
                    return _InvalidTraversal(segment.Identifier);
                }

                canonical = canonical.Append(segment.Identifier);
                return InteractionResult.Success(new ResolvedPath(
                    handle,
                    descriptor,
                    member,
                    canonical,
                    locations));
            }

            if (member is ReferenceDescriptor reference)
            {
                if (segment.Selector is not null)
                {
                    return _InvalidTraversal(segment.Identifier);
                }

                var read = await reference.ReadAsync();
                if (!read.IsSuccess)
                {
                    return InteractionResult.Failure<ResolvedPath>(read.Error!);
                }

                if (read.Value is null)
                {
                    return InteractionResult.Failure<ResolvedPath>(
                        InteractionErrorCode.UNAVAILABLE,
                        $"Reference '{reference.Id}' is empty.");
                }

                handle = read.Value.Value;
                canonical = canonical.Append(segment.Identifier);
            }
            else if (member is CollectionDescriptor collection)
            {
                if (segment.Selector is null)
                {
                    if (index != path.Segments.Count - 1)
                    {
                        return _InvalidTraversal(segment.Identifier);
                    }

                    canonical = canonical.Append(segment.Identifier);
                    return InteractionResult.Success(new ResolvedPath(
                        handle,
                        descriptor,
                        collection,
                        canonical,
                        locations));
                }

                var selected = await collection.SelectAsync(segment.Selector);
                if (!selected.IsSuccess)
                {
                    return InteractionResult.Failure<ResolvedPath>(selected.Error!);
                }

                if (selected.Value.Count == 0)
                {
                    return InteractionResult.Failure<ResolvedPath>(
                        InteractionErrorCode.NOT_FOUND,
                        $"Selector on collection '{collection.Id}' matched no object.");
                }

                if (selected.Value.Count > 1)
                {
                    return InteractionResult.Failure<ResolvedPath>(
                        InteractionErrorCode.AMBIGUOUS,
                        $"Selector on collection '{collection.Id}' matched multiple objects.");
                }

                handle = selected.Value[0];
                canonical = canonical.Append(segment.Identifier, segment.Selector);
            }

            described = await host.DescribeAsync(handle);
            if (!described.IsSuccess)
            {
                return InteractionResult.Failure<ResolvedPath>(described.Error!);
            }

            descriptor = described.Value;
            locations.Add(new PathLocation(handle, canonical));
            host.RecordCanonicalPath(handle, canonical);
            if (index == path.Segments.Count - 1)
            {
                return InteractionResult.Success(new ResolvedPath(
                    handle,
                    descriptor,
                    null,
                    canonical,
                    locations));
            }
        }

        throw new InvalidOperationException("Path traversal ended without a result.");
    }

    private static InteractionResult<ResolvedPath> _InvalidTraversal(string memberId) =>
        InteractionResult.Failure<ResolvedPath>(
            InteractionErrorCode.INVALID_INPUT,
            $"Member '{memberId}' cannot be traversed in this path.");
}
