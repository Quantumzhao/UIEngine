namespace UIEngine.Core;

/// <summary>Resolves durable data-only bindings to transient live member descriptors.</summary>
public sealed class BindingResolver
{
    private readonly UIEngineHost _Host;
    private readonly LogicalPathResolver _Paths;

    internal BindingResolver(UIEngineHost host, LogicalPathResolver paths)
    {
        _Host = host;
        _Paths = paths;
    }

    public async ValueTask<BindingResolution> ResolveAsync(
        BindingReference binding,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _ResolveCoreAsync(binding, cancellationToken).ConfigureAwait(false);
        _Host.RecordBindingDiagnostic(resolution);
        return resolution;
    }

    private async ValueTask<BindingResolution> _ResolveCoreAsync(
        BindingReference binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var validation = _Validate(binding);
        if (validation is not null)
        {
            return validation;
        }

        LogicalPath? parsedPath = null;
        if (binding.Path is not null)
        {
            var parsed = LogicalPath.Parse(binding.Path);
            if (!parsed.IsSuccess)
            {
                return _Failure(BindingResolutionState.INVALID_PATH, parsed.Error!);
            }

            parsedPath = parsed.Value;
        }

        if (binding.FallbackPolicy != BindingFallbackPolicy.PATH_ONLY)
        {
            var identity = new DomainIdentity(binding.DomainIdentity!);
            var identityResult = _Host.ResolveDomainIdentity(identity);
            if (identityResult.IsSuccess)
            {
                var described = await _Host.DescribeAsync(identityResult.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (!described.IsSuccess)
                {
                    return _Failure(_MapState(described.Error!.Code), described.Error);
                }

                _Host.TryGetCanonicalPath(identityResult.Value, out var knownPath);
                if (binding.FallbackPolicy == BindingFallbackPolicy.DOMAIN_IDENTITY_ONLY)
                {
                    return _ResolveMember(
                        binding,
                        identityResult.Value,
                        described.Value,
                        knownPath);
                }

                var corroboratingPath = await _Paths.ResolveAsync(parsedPath!, cancellationToken)
                    .ConfigureAwait(false);
                if (corroboratingPath.Target is { Kind: DescriptorKind.INSTANCE } corroboratedTarget &&
                    StringComparer.Ordinal.Equals(
                        corroboratedTarget.OwnerDescriptor.DomainIdentity?.Value,
                        binding.DomainIdentity))
                {
                    return _ResolveMember(
                        binding,
                        corroboratedTarget.OwnerHandle,
                        corroboratedTarget.OwnerDescriptor,
                        corroboratingPath.CanonicalPath);
                }

                if (knownPath is not null && !knownPath.Equals(parsedPath))
                {
                    return _ResolveMember(
                        binding,
                        identityResult.Value,
                        described.Value,
                        knownPath);
                }

                return corroboratingPath.IsResolved
                    ? _Failure(
                        BindingResolutionState.TARGET_MISSING,
                        new InteractionError(
                            InteractionErrorCode.TARGET_MISSING,
                            "The path resolved to a different domain identity; the binding was not changed."))
                    : _Failure(corroboratingPath.State, corroboratingPath.Error!);
            }

            if (binding.FallbackPolicy == BindingFallbackPolicy.DOMAIN_IDENTITY_ONLY)
            {
                return _Failure(_MapState(identityResult.Error!.Code), identityResult.Error);
            }

            if (identityResult.Error!.Code is not (
                InteractionErrorCode.DOMAIN_IDENTITY_NOT_FOUND or
                InteractionErrorCode.AMBIGUOUS_DOMAIN_IDENTITY))
            {
                return _Failure(_MapState(identityResult.Error.Code), identityResult.Error);
            }
        }

        var pathResolution = await _Paths.ResolveAsync(parsedPath!, cancellationToken).ConfigureAwait(false);
        if (!pathResolution.IsResolved)
        {
            return _Failure(pathResolution.State, pathResolution.Error!);
        }

        if (pathResolution.Target is not { Kind: DescriptorKind.INSTANCE } pathTarget)
        {
            return _Failure(
                BindingResolutionState.INVALID_PATH,
                new InteractionError(
                    InteractionErrorCode.INVALID_PATH,
                    "A binding path must identify the object that owns the member."));
        }

        if (binding.DomainIdentity is not null &&
            !StringComparer.Ordinal.Equals(
                pathTarget.OwnerDescriptor.DomainIdentity?.Value,
                binding.DomainIdentity))
        {
            return _Failure(
                BindingResolutionState.TARGET_MISSING,
                new InteractionError(
                    InteractionErrorCode.TARGET_MISSING,
                    "The path resolved to a different domain identity; the binding was not changed."));
        }

        return _ResolveMember(
            binding,
            pathTarget.OwnerHandle,
            pathTarget.OwnerDescriptor,
            pathResolution.CanonicalPath);
    }

    private static BindingResolution? _Validate(BindingReference binding)
    {
        if (string.IsNullOrWhiteSpace(binding.MemberId))
        {
            return _Failure(
                BindingResolutionState.INVALID_PATH,
                new InteractionError(
                    InteractionErrorCode.INVALID_PATH,
                    "A binding member identifier cannot be empty or whitespace."));
        }

        var hasIdentity = !string.IsNullOrWhiteSpace(binding.DomainIdentity);
        var hasPath = !string.IsNullOrWhiteSpace(binding.Path);
        var valid = binding.FallbackPolicy switch
        {
            BindingFallbackPolicy.DOMAIN_IDENTITY_ONLY => hasIdentity,
            BindingFallbackPolicy.PATH_ONLY => hasPath,
            BindingFallbackPolicy.DOMAIN_IDENTITY_THEN_PATH => hasIdentity && hasPath,
            _ => false,
        };
        return valid
            ? null
            : _Failure(
                BindingResolutionState.INVALID_PATH,
                new InteractionError(
                    InteractionErrorCode.INVALID_PATH,
                    "The binding does not contain the identity and path required by its fallback policy."));
    }

    private static BindingResolution _ResolveMember(
        BindingReference binding,
        ObjectHandle ownerHandle,
        IObjectDescriptor ownerDescriptor,
        LogicalPath? canonicalPath)
    {
        var matches = new List<(DescriptorKind Kind, IMemberDescriptor Descriptor, Type? Type)>();
        matches.AddRange(ownerDescriptor.Values
            .Where(member => StringComparer.Ordinal.Equals(member.Id, binding.MemberId))
            .Select(static member => (DescriptorKind.VALUE, (IMemberDescriptor)member, (Type?)member.ValueType)));
        matches.AddRange(ownerDescriptor.References
            .Where(member => StringComparer.Ordinal.Equals(member.Id, binding.MemberId))
            .Select(static member => (DescriptorKind.REFERENCE, (IMemberDescriptor)member, (Type?)member.ReferenceType)));
        matches.AddRange(ownerDescriptor.Collections
            .Where(member => StringComparer.Ordinal.Equals(member.Id, binding.MemberId))
            .Select(static member => (DescriptorKind.COLLECTION, (IMemberDescriptor)member, (Type?)member.ElementType)));
        matches.AddRange(ownerDescriptor.Actions
            .Where(member => StringComparer.Ordinal.Equals(member.Id, binding.MemberId))
            .Select(static member => (DescriptorKind.ACTION, (IMemberDescriptor)member, (Type?)null)));

        if (matches.Count == 0)
        {
            return _Failure(
                BindingResolutionState.TARGET_MISSING,
                new InteractionError(
                    InteractionErrorCode.TARGET_MISSING,
                    $"Member '{binding.MemberId}' was not found on the resolved object."));
        }

        if (matches.Count > 1)
        {
            return _Failure(
                BindingResolutionState.AMBIGUOUS,
                new InteractionError(
                    InteractionErrorCode.AMBIGUOUS_TARGET,
                    $"Member '{binding.MemberId}' is ambiguous on the resolved object."));
        }

        var match = matches[0];
        if (match.Kind != binding.ExpectedDescriptorKind)
        {
            return _Failure(
                BindingResolutionState.TYPE_MISMATCH,
                new InteractionError(
                    InteractionErrorCode.TYPE_MISMATCH,
                    $"Member '{binding.MemberId}' is a {match.Kind} descriptor, not {binding.ExpectedDescriptorKind}."));
        }

        var typeName = match.Type?.FullName ?? match.Type?.Name;
        if (binding.ExpectedTypeName is not null &&
            (match.Type is null || !_MatchesTypeName(match.Type, binding.ExpectedTypeName)))
        {
            return _Failure(
                BindingResolutionState.TYPE_MISMATCH,
                new InteractionError(
                    InteractionErrorCode.TYPE_MISMATCH,
                    $"Member '{binding.MemberId}' does not have expected type '{binding.ExpectedTypeName}'."));
        }

        return new BindingResolution(
            BindingResolutionState.RESOLVED,
            new ResolvedBindingTarget(
                ownerHandle,
                ownerDescriptor,
                match.Descriptor,
                match.Kind,
                typeName),
            canonicalPath,
            null);
    }

    private static bool _MatchesTypeName(Type type, string expected) =>
        StringComparer.Ordinal.Equals(type.FullName, expected) ||
        StringComparer.Ordinal.Equals(type.AssemblyQualifiedName, expected);

    private static BindingResolutionState _MapState(InteractionErrorCode code) => code switch
    {
        InteractionErrorCode.INVALID_PATH or InteractionErrorCode.INVALID_INPUT =>
            BindingResolutionState.INVALID_PATH,
        InteractionErrorCode.TYPE_MISMATCH or InteractionErrorCode.UNSUPPORTED_TARGET_TYPE =>
            BindingResolutionState.TYPE_MISMATCH,
        InteractionErrorCode.TARGET_MISSING or InteractionErrorCode.ROOT_NOT_FOUND or
            InteractionErrorCode.DOMAIN_IDENTITY_NOT_FOUND => BindingResolutionState.TARGET_MISSING,
        InteractionErrorCode.AMBIGUOUS_TARGET or InteractionErrorCode.AMBIGUOUS_DOMAIN_IDENTITY =>
            BindingResolutionState.AMBIGUOUS,
        InteractionErrorCode.PERMISSION_DENIED => BindingResolutionState.PERMISSION_DENIED,
        _ => BindingResolutionState.TEMPORARILY_UNAVAILABLE,
    };

    private static BindingResolution _Failure(BindingResolutionState state, InteractionError error) =>
        new(state, null, null, error);
}
