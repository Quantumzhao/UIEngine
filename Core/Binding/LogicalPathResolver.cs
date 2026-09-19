using System.Globalization;

namespace UIEngine.Core;

/// <summary>Resolves absolute logical paths through semantic descriptors.</summary>
public sealed class LogicalPathResolver
{
    private readonly UIEngineHost _Host;

    internal LogicalPathResolver(UIEngineHost host)
    {
        _Host = host;
    }

    public ValueTask<LogicalPathResolution> ResolveAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var parsed = LogicalPath.Parse(path);
        return parsed.IsSuccess
            ? ResolveAsync(parsed.Value, cancellationToken)
            : ValueTask.FromResult(_Failure(
                BindingResolutionState.INVALID_PATH,
                parsed.Error!,
                null,
                []));
    }

    public async ValueTask<LogicalPathResolution> ResolveAsync(
        LogicalPath path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_Host.IsDisposed)
        {
            return _Failure(
                BindingResolutionState.TEMPORARILY_UNAVAILABLE,
                new InteractionError(InteractionErrorCode.HOST_DISPOSED, "The UIEngine host has been disposed."),
                path,
                []);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return _Failure(
                BindingResolutionState.TEMPORARILY_UNAVAILABLE,
                new InteractionError(InteractionErrorCode.CANCELLED, "Logical-path resolution was cancelled."),
                path,
                []);
        }

        if (path.Segments.Count == 0)
        {
            return _Resolved(path, null, []);
        }

        var rootSegment = path.Segments[0];
        var root = _Host.Roots.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Identifier, rootSegment.Identifier));
        if (root is null)
        {
            return _Failure(
                BindingResolutionState.TARGET_MISSING,
                new InteractionError(
                    InteractionErrorCode.TARGET_MISSING,
                    $"Root '{rootSegment.Identifier}' was not found."),
                path,
                []);
        }

        var canonicalPath = LogicalPath.Root.Append(root.Identifier);
        var locations = new List<LogicalPathLocation>
        {
            new(root.Handle, canonicalPath),
        };
        _Host.RecordCanonicalPath(root.Handle, canonicalPath);

        var currentHandle = root.Handle;
        var descriptorResult = await _Host.DescribeAsync(currentHandle, cancellationToken).ConfigureAwait(false);
        if (!descriptorResult.IsSuccess)
        {
            return _FromFailure(descriptorResult.Error!, path, locations);
        }

        var currentDescriptor = descriptorResult.Value;
        if (path.Segments.Count == 1)
        {
            return _Resolved(
                canonicalPath,
                new LogicalPathTarget(
                    DescriptorKind.INSTANCE,
                    currentHandle,
                    currentDescriptor,
                    null),
                locations);
        }

        for (var segmentIndex = 1; segmentIndex < path.Segments.Count; segmentIndex++)
        {
            var segment = path.Segments[segmentIndex];
            var matches = _FindMembers(currentDescriptor, segment.Identifier);
            if (matches.Count == 0)
            {
                return _Failure(
                    BindingResolutionState.TARGET_MISSING,
                    new InteractionError(
                        InteractionErrorCode.TARGET_MISSING,
                        $"Member '{segment.Identifier}' was not found at '{canonicalPath}'."),
                    path,
                    locations);
            }

            if (matches.Count > 1)
            {
                return _Failure(
                    BindingResolutionState.AMBIGUOUS,
                    new InteractionError(
                        InteractionErrorCode.AMBIGUOUS_TARGET,
                        $"Member '{segment.Identifier}' is ambiguous at '{canonicalPath}'."),
                    path,
                    locations);
            }

            var (kind, member) = matches[0];
            if (kind is DescriptorKind.VALUE or DescriptorKind.ACTION)
            {
                if (segment.Selector is not null || segmentIndex != path.Segments.Count - 1)
                {
                    return _InvalidTraversal(path, locations, segment.Identifier);
                }

                canonicalPath = canonicalPath.Append(segment.Identifier);
                return _Resolved(
                    canonicalPath,
                    new LogicalPathTarget(kind, currentHandle, currentDescriptor, member),
                    locations);
            }

            if (kind == DescriptorKind.REFERENCE)
            {
                if (segment.Selector is not null)
                {
                    return _InvalidTraversal(path, locations, segment.Identifier);
                }

                var reference = (IReferenceDescriptor)member;
                var read = await reference.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (!read.IsSuccess)
                {
                    return _FromFailure(read.Error!, path, locations);
                }

                if (read.Value is null)
                {
                    return _Failure(
                        BindingResolutionState.TEMPORARILY_UNAVAILABLE,
                        new InteractionError(
                            InteractionErrorCode.TARGET_UNAVAILABLE,
                            $"Reference '{reference.Id}' is empty."),
                        path,
                        locations);
                }

                currentHandle = read.Value.Value;
                canonicalPath = canonicalPath.Append(segment.Identifier);
            }
            else
            {
                var collection = (ICollectionDescriptor)member;
                var selector = segment.Selector;
                if (selector is null &&
                    segmentIndex + 1 < path.Segments.Count &&
                    path.Segments[segmentIndex + 1].Selector is null &&
                    int.TryParse(
                        path.Segments[segmentIndex + 1].Identifier,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var legacyIndex))
                {
                    selector = CollectionSelector.AtIndex(legacyIndex);
                    segmentIndex++;
                }

                if (selector is null)
                {
                    if (segmentIndex != path.Segments.Count - 1)
                    {
                        return _Failure(
                            BindingResolutionState.INVALID_PATH,
                            new InteractionError(
                                InteractionErrorCode.INVALID_PATH,
                                $"Collection '{segment.Identifier}' requires a selector before traversal can continue."),
                            path,
                            locations);
                    }

                    canonicalPath = canonicalPath.Append(segment.Identifier);
                    return _Resolved(
                        canonicalPath,
                        new LogicalPathTarget(
                            DescriptorKind.COLLECTION,
                            currentHandle,
                            currentDescriptor,
                            member),
                        locations);
                }

                var selected = await _SelectAsync(collection, selector, cancellationToken).ConfigureAwait(false);
                if (!selected.IsSuccess)
                {
                    return _FromFailure(selected.Error!, path, locations);
                }

                if (selected.Value.Count == 0)
                {
                    return _Failure(
                        BindingResolutionState.TARGET_MISSING,
                        new InteractionError(
                            InteractionErrorCode.TARGET_MISSING,
                            $"Selector on collection '{segment.Identifier}' did not match an element."),
                        path,
                        locations);
                }

                if (selected.Value.Count > 1)
                {
                    return _Failure(
                        BindingResolutionState.AMBIGUOUS,
                        new InteractionError(
                            InteractionErrorCode.AMBIGUOUS_TARGET,
                            $"Selector on collection '{segment.Identifier}' matched multiple elements."),
                        path,
                        locations);
                }

                currentHandle = selected.Value[0];
                canonicalPath = canonicalPath.Append(segment.Identifier, selector);
            }

            descriptorResult = await _Host.DescribeAsync(currentHandle, cancellationToken).ConfigureAwait(false);
            if (!descriptorResult.IsSuccess)
            {
                return _FromFailure(descriptorResult.Error!, path, locations);
            }

            currentDescriptor = descriptorResult.Value;
            locations.Add(new LogicalPathLocation(currentHandle, canonicalPath));
            _Host.RecordCanonicalPath(currentHandle, canonicalPath);

            if (segmentIndex == path.Segments.Count - 1)
            {
                return _Resolved(
                    canonicalPath,
                    new LogicalPathTarget(
                        DescriptorKind.INSTANCE,
                        currentHandle,
                        currentDescriptor,
                        null),
                    locations);
            }
        }

        throw new InvalidOperationException("Logical-path traversal ended without a result.");
    }

    private async ValueTask<InteractionResult<IReadOnlyList<ObjectHandle>>> _SelectAsync(
        ICollectionDescriptor collection,
        CollectionSelector selector,
        CancellationToken cancellationToken)
    {
        if (selector.Kind == CollectionSelectorKind.KEY)
        {
            return collection is ICollectionPathSelector keyed
                ? await keyed.SelectByKeyAsync(selector.Value, cancellationToken).ConfigureAwait(false)
                : InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                    InteractionErrorCode.TARGET_MISSING,
                    $"Collection '{collection.Id}' does not support keyed selection.");
        }

        var snapshot = await collection.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.IsSuccess)
        {
            return snapshot;
        }

        if (selector.Kind == CollectionSelectorKind.INDEX)
        {
            var index = int.Parse(selector.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            return (uint)index < (uint)snapshot.Value.Count
                ? InteractionResult.Success<IReadOnlyList<ObjectHandle>>([snapshot.Value[index]])
                : InteractionResult.Success<IReadOnlyList<ObjectHandle>>([]);
        }

        var matches = new List<ObjectHandle>();
        foreach (var handle in snapshot.Value)
        {
            var identity = await _Host.GetDomainIdentityAsync(handle, cancellationToken).ConfigureAwait(false);
            if (!identity.IsSuccess)
            {
                return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                    identity.Error!.Code,
                    identity.Error.Message);
            }

            if (identity.Value is { } value && StringComparer.Ordinal.Equals(value.Value, selector.Value))
            {
                matches.Add(handle);
            }
        }

        return InteractionResult.Success<IReadOnlyList<ObjectHandle>>(matches);
    }

    private static List<(DescriptorKind Kind, IMemberDescriptor Descriptor)> _FindMembers(
        IObjectDescriptor descriptor,
        string identifier)
    {
        var matches = new List<(DescriptorKind, IMemberDescriptor)>();
        matches.AddRange(descriptor.Values
            .Where(member => StringComparer.Ordinal.Equals(member.Id, identifier))
            .Select(static member => (DescriptorKind.VALUE, (IMemberDescriptor)member)));
        matches.AddRange(descriptor.References
            .Where(member => StringComparer.Ordinal.Equals(member.Id, identifier))
            .Select(static member => (DescriptorKind.REFERENCE, (IMemberDescriptor)member)));
        matches.AddRange(descriptor.Collections
            .Where(member => StringComparer.Ordinal.Equals(member.Id, identifier))
            .Select(static member => (DescriptorKind.COLLECTION, (IMemberDescriptor)member)));
        matches.AddRange(descriptor.Actions
            .Where(member => StringComparer.Ordinal.Equals(member.Id, identifier))
            .Select(static member => (DescriptorKind.ACTION, (IMemberDescriptor)member)));
        return matches;
    }

    private static LogicalPathResolution _InvalidTraversal(
        LogicalPath path,
        IReadOnlyList<LogicalPathLocation> locations,
        string identifier) =>
        _Failure(
            BindingResolutionState.INVALID_PATH,
            new InteractionError(
                InteractionErrorCode.INVALID_PATH,
                $"Member '{identifier}' cannot be traversed in this logical path."),
            path,
            locations);

    private static LogicalPathResolution _FromFailure(
        InteractionError error,
        LogicalPath path,
        IReadOnlyList<LogicalPathLocation> locations) =>
        _Failure(_MapState(error.Code), error, path, locations);

    private static BindingResolutionState _MapState(InteractionErrorCode code) => code switch
    {
        InteractionErrorCode.INVALID_PATH or InteractionErrorCode.INVALID_INPUT =>
            BindingResolutionState.INVALID_PATH,
        InteractionErrorCode.TARGET_MISSING or InteractionErrorCode.ROOT_NOT_FOUND or
            InteractionErrorCode.DOMAIN_IDENTITY_NOT_FOUND => BindingResolutionState.TARGET_MISSING,
        InteractionErrorCode.AMBIGUOUS_TARGET or InteractionErrorCode.AMBIGUOUS_DOMAIN_IDENTITY =>
            BindingResolutionState.AMBIGUOUS,
        InteractionErrorCode.TYPE_MISMATCH or InteractionErrorCode.UNSUPPORTED_TARGET_TYPE =>
            BindingResolutionState.TYPE_MISMATCH,
        InteractionErrorCode.PERMISSION_DENIED => BindingResolutionState.PERMISSION_DENIED,
        _ => BindingResolutionState.TEMPORARILY_UNAVAILABLE,
    };

    private static LogicalPathResolution _Resolved(
        LogicalPath path,
        LogicalPathTarget? target,
        IReadOnlyList<LogicalPathLocation> locations) =>
        new(
            BindingResolutionState.RESOLVED,
            path,
            target,
            locations.ToArray(),
            null);

    private static LogicalPathResolution _Failure(
        BindingResolutionState state,
        InteractionError error,
        LogicalPath? path,
        IReadOnlyList<LogicalPathLocation> locations) =>
        new(state, path, null, locations.ToArray(), error);
}
