using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using UIEngine.Core.Attributes;

namespace UIEngine.Core;

/// <summary>Owns roots, runtime identity, live interaction, and action lifetime.</summary>
/// <remarks>
/// Live graph operations are synchronous and must be called on the domain model's owning thread.
/// A frontend on another thread is responsible for arranging that handoff outside Core.
/// </remarks>
public sealed class UIEngineHost : IDisposable
{
    private readonly ConditionalWeakTable<object, _HandleHolder> _Handles = new();
    private readonly Dictionary<Guid, WeakReference<object>> _Objects = [];
    private readonly Dictionary<string, _RootEntry> _Roots = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string?> _DomainIdentities = [];
    private readonly Dictionary<string, HashSet<Guid>> _DomainIdentityIndex = [];
    private readonly ConcurrentDictionary<ActionInvocation, byte> _Invocations = [];
    private readonly HostSettings _Settings;
    private readonly ILogger _Logger;

    public UIEngineHost(UIEngineHostOptions? options = null)
    {
        _Settings = new HostSettings(options ?? new UIEngineHostOptions());
        _Logger = _Settings.LoggerFactory.CreateLogger<UIEngineHost>();
    }

    public IReadOnlyList<RootRegistration> Roots =>
        _Roots.Values.Select(static root => root.Registration).ToArray();
    internal int MaxCollectionItems => _Settings.MaxCollectionItems;

    public InteractionResult<Guid> SetRoot(string identifier, object instance)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return InteractionResult.Failure<Guid>(
                InteractionErrorCode.INVALID_INPUT,
                "A root identifier cannot be empty or whitespace.");
        }

        if (instance.GetType().IsValueType)
        {
            return InteractionResult.Failure<Guid>(
                InteractionErrorCode.TYPE_MISMATCH,
                "A root must be a reference type.");
        }

        var handle = _GetOrCreateHandle(instance);
        var registration = new RootRegistration(identifier, handle);
        _Roots[identifier] = new _RootEntry(registration, instance);
        return InteractionResult.Success(handle);
    }

    public InteractionResult<RootRegistration> RemoveRoot(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return InteractionResult.Failure<RootRegistration>(
                InteractionErrorCode.INVALID_INPUT,
                "A root identifier cannot be empty or whitespace.");
        }

        return _Roots.Remove(identifier, out var root)
            ? InteractionResult.Success(root.Registration)
            : InteractionResult.Failure<RootRegistration>(
                InteractionErrorCode.NOT_FOUND,
                $"Root '{identifier}' was not found.");
    }

    public Guid GetOrCreateHandle(object instance)
    {
        if (instance.GetType().IsValueType)
        {
            throw new ArgumentException("Runtime handles require reference types.", nameof(instance));
        }

        return _GetOrCreateHandle(instance);
    }

    public InteractionResult<string?> GetDomainIdentity(Guid handle)
    {
        if (_DomainIdentities.TryGetValue(handle, out var cached))
        {
            return InteractionResult.Success(cached);
        }

        var target = ResolveTarget(handle);
        if (!target.IsSuccess)
        {
            return InteractionResult.Failure<string?>(target.Error!);
        }

        var discovered = _DiscoverDomainIdentity(target.Value);
        if (!discovered.IsSuccess)
        {
            return discovered;
        }

        _DomainIdentities[handle] = discovered.Value;
        if (discovered.Value is { } identity)
        {
            if (!_DomainIdentityIndex.TryGetValue(identity, out var handles))
            {
                handles = [];
                _DomainIdentityIndex.Add(identity, handles);
            }

            handles.Add(handle);
        }

        return discovered;
    }

    public InteractionResult<Guid> ResolveDomainIdentity(string identity)
    {
        if (!_DomainIdentityIndex.TryGetValue(identity, out var handles))
        {
            return _DomainIdentityNotFound(identity);
        }

        handles.RemoveWhere(handle =>
            !_Objects.TryGetValue(handle, out var reference) || !reference.TryGetTarget(out _));
        if (handles.Count == 0)
        {
            _DomainIdentityIndex.Remove(identity);
            return _DomainIdentityNotFound(identity);
        }

        return handles.Count == 1
            ? InteractionResult.Success(handles.Single())
            : InteractionResult.Failure<Guid>(
                InteractionErrorCode.AMBIGUOUS,
                $"Domain identity '{identity}' matches multiple live objects.");
    }

    public InteractionResult<ResolvedNode> ResolveRootNode(string identifier)
    {
        if (!_Roots.TryGetValue(identifier, out var root))
        {
            return InteractionResult.Failure<ResolvedNode>(
                InteractionErrorCode.NOT_FOUND,
                $"Root '{identifier}' was not found.");
        }

        var created = CreateObjectNode(root.Registration.Handle, identifier);
        if (!created.IsSuccess)
        {
            return InteractionResult.Failure<ResolvedNode>(created.Error!);
        }

        return InteractionResult.Success(new ResolvedNode(
            LogicalPath.Root.Append(identifier),
            created.Value));
    }

    public InteractionResult<ResolvedPath> ResolvePath(
        string path,
        string? expectedDomainIdentity = null)
    {
        var parsed = LogicalPath.Parse(path);
        return parsed.IsSuccess
            ? ResolvePath(parsed.Value, expectedDomainIdentity)
            : InteractionResult.Failure<ResolvedPath>(parsed.Error!);
    }

    public InteractionResult<ResolvedPath> ResolvePath(
        LogicalPath path,
        string? expectedDomainIdentity = null)
    {
        var resolved = Execute(
            "resolve path",
            () => PathResolution.Resolve(this, path));
        if (!resolved.IsSuccess || expectedDomainIdentity is null)
        {
            return resolved;
        }

        return resolved.Value.Node is IObjectNode objectNode && StringComparer.Ordinal.Equals(
                objectNode.DomainIdentity,
                expectedDomainIdentity)
            ? resolved
            : InteractionResult.Failure<ResolvedPath>(
                InteractionErrorCode.NOT_FOUND,
                "The path resolves to a conflicting domain identity.");
    }

    public void Dispose()
    {
        var invocations = _Invocations.Keys.ToArray();
        _Invocations.Clear();
        _Roots.Clear();
        _Objects.Clear();
        _DomainIdentities.Clear();
        _DomainIdentityIndex.Clear();
        _Handles.Clear();

        foreach (var invocation in invocations)
        {
            invocation.CompleteHostDisposed();
        }
    }

    internal InteractionResult<object> ResolveTarget(Guid handle)
    {
        if (_Objects.TryGetValue(handle, out var reference) && reference.TryGetTarget(out var target))
        {
            return InteractionResult.Success(target);
        }

        _Objects.Remove(handle);
        return InteractionResult.Failure<object>(
            InteractionErrorCode.UNAVAILABLE,
            "The target object is no longer available.");
    }

    internal InteractionResult<LiveObjectNode> CreateObjectNode(
        Guid handle,
        string id)
    {
        var target = ResolveTarget(handle);
        if (!target.IsSuccess)
        {
            return InteractionResult.Failure<LiveObjectNode>(target.Error!);
        }

        var identity = GetDomainIdentity(handle);
        if (!identity.IsSuccess)
        {
            return InteractionResult.Failure<LiveObjectNode>(identity.Error!);
        }

        return Execute(
            "create object node",
            () => _CreateObjectNode(target.Value, handle, id, identity.Value));
    }

    internal InteractionResult<T> Execute<T>(string operation, Func<InteractionResult<T>> action)
    {
        try
        {
            return action();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return _UnexpectedFailure<T>(operation, exception.InnerException);
        }
        catch (Exception exception)
        {
            return _UnexpectedFailure<T>(operation, exception);
        }
    }

    internal ActionInvocation CreateInvocation()
    {
        var invocation = new ActionInvocation(_InvocationCompleted);
        _Invocations.TryAdd(invocation, 0);

        return invocation;
    }

    private Guid _GetOrCreateHandle(object instance)
    {
        if (_Handles.TryGetValue(instance, out var existing))
        {
            return existing.Handle;
        }

        var handle = Guid.NewGuid();
        _Handles.Add(instance, new _HandleHolder(handle));
        _Objects.Add(handle, new WeakReference<object>(instance));
        return handle;
    }

    private InteractionResult<string?> _DiscoverDomainIdentity(object instance)
    {
        if (_Settings.Exposures.TryGetValue(instance.GetType(), out var exposure))
        {
            return _NormalizeIdentity(exposure.GetDomainIdentity(instance));
        }

        var metadata = ReflectionMetadata.Get(instance.GetType());
        string? attributed = null;
        if (metadata.DomainIdentityMember is { } member)
        {
            if (member is PropertyInfo { PropertyType: var propertyType } property &&
                propertyType == typeof(string) && ReflectionMetadata.CanRead(property))
            {
                attributed = (string?)ReflectionMetadata.Read(property, instance);
            }
            else if (member is FieldInfo { FieldType: var fieldType } field &&
                fieldType == typeof(string))
            {
                attributed = (string?)ReflectionMetadata.Read(field, instance);
            }
            else
            {
                return InteractionResult.Failure<string?>(
                    InteractionErrorCode.INVALID_INPUT,
                    "A domain identity member must be a readable string field or property.");
            }
        }

        return _NormalizeIdentity(attributed);
    }

    private InteractionResult<LiveObjectNode> _CreateObjectNode(
        object instance,
        Guid handle,
        string id,
        string? identity)
    {
        var metadata = ReflectionMetadata.Get(instance.GetType());
        _Settings.Exposures.TryGetValue(instance.GetType(), out var exposure);
        var programmaticIds = exposure?.Values.Select(static value => value.Id)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var members = new List<BaseNode>();
        foreach (var member in metadata.Members.Where(member => !programmaticIds.Contains(member.Id)))
        {
            switch (member.Kind)
            {
                case ReflectedMemberKind.VALUE:
                    var valueBinding = new ValueNodeBinding(
                        this,
                        handle,
                        member.Id,
                        member.ValueType,
                        ReflectionMetadata.CanRead(member.Member),
                        ReflectionMetadata.CanWrite(member.Member) &&
                            member.Member.GetCustomAttribute<ExposeAttribute>(inherit: true)?.ReadOnly != true,
                        member.IsNullable,
                        member.Options,
                        member.Range,
                        member.ValidationAttributes,
                        target => ReflectionMetadata.Read(member.Member, target),
                        ReflectionMetadata.CanWrite(member.Member)
                            ? (target, value) => ReflectionMetadata.Write(member.Member, target, value)
                            : null);
                    members.Add(new LiveValueNode(valueBinding, member));
                    break;
                case ReflectedMemberKind.REFERENCE:
                    members.Add(new LiveReferenceNode(new ReferenceNodeBinding(
                        this,
                        handle,
                        member.Id,
                        member.ValueType,
                        target => ReflectionMetadata.Read(member.Member, target)), member));
                    break;
                case ReflectedMemberKind.COLLECTION:
                    members.Add(new LiveCollectionNode(new CollectionNodeBinding(
                        this,
                        handle,
                        member.Id,
                        member.ValueType,
                        target => ReflectionMetadata.Read(member.Member, target)), member));
                    break;
                case ReflectedMemberKind.ACTION:
                    var action = ReflectedAction.Create((MethodInfo)member.Member);
                    if (!action.IsSuccess)
                    {
                        return InteractionResult.Failure<LiveObjectNode>(action.Error!);
                    }

                    members.Add(new LiveMethodNode(
                        new MethodNodeBinding(this, handle, member.Id, action.Value),
                        member));
                    break;
                default:
                    throw new InvalidOperationException("Unknown member kind.");
            }
        }

        if (exposure is not null)
        {
            members.AddRange(exposure.Values.Select(value => (BaseNode)new LiveValueNode(
                new ValueNodeBinding(
                    this,
                    handle,
                    value.Id,
                    value.ValueType,
                    canRead: true,
                    value.CanWrite,
                    value.IsNullable,
                    ValueConversion.GetEnumOptions(value.ValueType),
                    value.Range,
                    [],
                    value.Read,
                    value.CanWrite ? value.Write : null))));
        }

        string? summary = null;
        if (exposure is not null)
        {
            summary = exposure.GetSummary(instance);
        }
        else if (metadata.SummaryMember is not null)
        {
            summary = ReflectionMetadata.Read(metadata.SummaryMember, instance)?.ToString();
        }

        return InteractionResult.Success(new LiveObjectNode(
            this,
            id,
            instance.GetType(),
            handle,
            identity,
            summary,
            members.OrderBy(static member => member.Id, StringComparer.Ordinal).ToArray()));
    }

    private static InteractionResult<string?> _NormalizeIdentity(string? value)
    {
        if (value is null)
        {
            return InteractionResult.Success<string?>(null);
        }

        return string.IsNullOrWhiteSpace(value)
            ? InteractionResult.Failure<string?>(
                InteractionErrorCode.INVALID_INPUT,
                "A domain identity cannot be empty or whitespace.")
            : InteractionResult.Success<string?>(value);
    }

    private void _InvocationCompleted(ActionInvocation invocation)
    {
        _Invocations.TryRemove(invocation, out _);

        if (invocation.Fault is not null)
        {
            _ReportUnexpected("action invocation", invocation.Fault);
        }
    }

    private void _ReportUnexpected(string operation, Exception exception) =>
        RuntimeDiagnostics.UnexpectedFailure(
            _Logger,
            operation,
            exception,
            _Settings.IncludeSensitiveDiagnosticData);

    private InteractionResult<T> _UnexpectedFailure<T>(string operation, Exception exception)
    {
        _ReportUnexpected(operation, exception);
        return InteractionResult.Failure<T>(
            InteractionErrorCode.FAULT,
            $"The {operation} operation failed unexpectedly.");
    }

    private static InteractionResult<Guid> _DomainIdentityNotFound(string identity) =>
        InteractionResult.Failure<Guid>(
            InteractionErrorCode.NOT_FOUND,
            $"No live object has domain identity '{identity}'.");

    private sealed record _HandleHolder(Guid Handle);

    private sealed record _RootEntry(RootRegistration Registration, object Instance);
}
