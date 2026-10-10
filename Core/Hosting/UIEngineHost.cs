using System.Reflection;
using System.Runtime.CompilerServices;
using LanguageExt;
using Microsoft.Extensions.Logging;
using UIEngine.Core.Attributes;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

/// <summary>Owns roots, runtime handles, and live interaction.</summary>
/// <remarks>
/// Live graph operations are synchronous and must be called on the domain model's owning thread.
/// UIEngine and its domain model share one owning thread.
/// </remarks>
public sealed class UIEngineHost : IDisposable
{
    private static UIEngineHost? _Instance;

    private readonly ConditionalWeakTable<object, _HandleHolder> _Handles = new();
    private readonly Dictionary<Guid, WeakReference<object>> _Objects = [];
    private readonly Dictionary<Guid, _RootEntry> _Roots = [];
    private readonly System.Collections.Generic.HashSet<UIEngineWorkspace> _Workspaces = [];
    private readonly NodeRefreshRouter _RefreshRouter = new();
    private readonly HostSettings _Settings;
    private readonly ILogger _Logger;

    public UIEngineHost(UIEngineHostOptions? options = null)
    {
        if (_Instance is not null)
        {
            throw new InvalidOperationException("Only one UIEngine host can be active.");
        }

        _Settings = new HostSettings(options ?? new UIEngineHostOptions());
        _Logger = _Settings.LoggerFactory.CreateLogger<UIEngineHost>();
        _DiscoverRoots();
        _Instance = this;
    }

    public static UIEngineHost Instance => _Instance ?? throw new InvalidOperationException(
        "No UIEngine host is active.");

    public IReadOnlyList<Guid> RootIds => _Roots.Keys.ToArray();

    internal int MaxCollectionItems => _Settings.MaxCollectionItems;

    public Guid GetOrCreateHandle(object instance)
    {
        if (instance.GetType().IsValueType)
        {
            throw new ArgumentException("Runtime handles require reference types.", nameof(instance));
        }

        return _GetOrCreateHandle(instance);
    }

    public Either<InteractionError, ResolvedNode> ResolveRootNode(Guid rootId)
    {
        if (!_Roots.TryGetValue(rootId, out var root))
        {
            return Left(new InteractionError(
                InteractionErrorCode.NOT_FOUND,
                $"Root '{rootId}' was not found."));
        }

        var created = _CreateMemberNode(root.OwnerHandle, root.Owner, root.Member);
        if (!created.IsRight)
        {
            return Left((InteractionError)created);
        }

        BaseNode node = (BaseNode)created;
        if (node is LiveReferenceNode reference)
        {
            var resolved = reference.ResolveTarget();
            if (!resolved.IsRight)
            {
                return Left((InteractionError)resolved);
            }

            node = (LiveResolvedReferenceNode)resolved;
        }

        return Right(new ResolvedNode(
            LogicalPath.Empty.Append(new RootLogicalPathSegment(rootId)),
            node));
    }

    public void Dispose()
    {
        foreach (var workspace in _Workspaces.ToArray())
        {
            workspace.Dispose();
        }

        _RefreshRouter.Dispose();
        _Roots.Clear();
        _Objects.Clear();
        _Handles.Clear();

        if (ReferenceEquals(_Instance, this))
        {
            _Instance = null;
        }
    }

    internal static bool TryGetInstance(out UIEngineHost host)
    {
        host = _Instance!;
        return host is not null;
    }

    internal Either<InteractionError, string> ResolveRootName(Guid rootId) =>
        _Roots.TryGetValue(rootId, out var root)
            ? Right(root.Member.Name)
            : Left(new InteractionError(
                InteractionErrorCode.NOT_FOUND,
                $"Root '{rootId}' was not found."));

    internal void RegisterWorkspace(UIEngineWorkspace workspace) => _Workspaces.Add(workspace);

    internal void UnregisterWorkspace(UIEngineWorkspace workspace) => _Workspaces.Remove(workspace);

    internal Either<InteractionError, object> ResolveTarget(Guid handle)
    {
        if (_Objects.TryGetValue(handle, out var reference) && reference.TryGetTarget(out var target))
        {
            return Right(target);
        }

        _Objects.Remove(handle);
        return Left(new InteractionError(
            InteractionErrorCode.UNAVAILABLE,
            "The target object is no longer available."));
    }

    internal Either<InteractionError, LiveObjectNode> CreateObjectNode(
        Guid handle,
        string name)
    {
        var target = ResolveTarget(handle);
        if (!target.IsRight)
        {
            return Left((InteractionError)target);
        }

        return Execute(
            "create object node",
            () => _CreateObjectNode(
                target.IfLeft(static error => throw new InvalidOperationException(error.Message)),
                handle,
                name));
    }

    internal Either<InteractionError, T> Execute<T>(string operation, Func<Either<InteractionError, T>> action)
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

    internal void ReportInvocationFault(Exception exception) => 
        _ReportUnexpected("action invocation", exception);

    private void _DiscoverRoots()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .OrderBy(static assembly => assembly.FullName, StringComparer.Ordinal)
            .ToArray();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                _ReportRootDiscoveryFailure(assembly.FullName ?? assembly.GetName().Name ?? "assembly", exception);
                types = exception.Types.Where(static type => type is not null).ToArray()!;
            }
            catch (Exception exception)
            {
                _ReportRootDiscoveryFailure(assembly.FullName ?? assembly.GetName().Name ?? "assembly", exception);
                continue;
            }

            foreach (var type in types.OrderBy(static type => type.FullName, StringComparer.Ordinal))
            {
                _DiscoverRoots(type);
            }
        }
    }

    private void _DiscoverRoots(Type type)
    {
        MemberInfo[] members;
        try
        {
            const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            members = type.GetMembers(FLAGS)
                .Where(static member => member.IsDefined(typeof(RootAttribute), inherit: false))
                .OrderBy(static member => member.Name, StringComparer.Ordinal)
                .ThenBy(static member => member.ToString(), StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception)
        {
            _ReportRootDiscoveryFailure(type.FullName ?? type.Name, exception);
            return;
        }

        foreach (var member in members)
        {
            try
            {
                if (!_IsSupportedRoot(member))
                {
                    _ReportInvalidRoot(member);
                    continue;
                }

                var reflected = ReflectionMetadata.CreateMember(member, member.Name);
                if (reflected is null)
                {
                    _ReportInvalidRoot(member);
                    continue;
                }

                var owner = member.DeclaringType!;
                var id = Guid.NewGuid();
                _Roots.Add(id, new _RootEntry(reflected, _GetOrCreateHandle(owner), owner));
            }
            catch (Exception exception)
            {
                _ReportRootDiscoveryFailure(
                    $"{member.DeclaringType?.FullName}.{member.Name}",
                    exception);
            }
        }
    }

    private static bool _IsSupportedRoot(MemberInfo member)
    {
        if (member.DeclaringType?.ContainsGenericParameters != false)
        {
            return false;
        }

        return member switch
        {
            FieldInfo field => field.IsPublic && field.IsStatic,
            PropertyInfo property => property.GetMethod is { IsPublic: true, IsStatic: true } &&
                property.GetIndexParameters().Length == 0,
            MethodInfo method => method.IsPublic && method.IsStatic &&
                method.IsDefined(typeof(ActionAttribute), inherit: true),
            _ => false,
        };
    }

    private void _ReportInvalidRoot(MemberInfo member) => RuntimeDiagnostics.RootSkipped(
        _Logger,
        member.DeclaringType?.FullName,
        member.Name);

    private void _ReportRootDiscoveryFailure(string source, Exception exception)
    {
        RuntimeDiagnostics.RootDiscoveryFailure(
            _Logger,
            source,
            exception,
            _Settings.IncludeSensitiveDiagnosticData);
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

    private Either<InteractionError, LiveObjectNode> _CreateObjectNode(
        object instance,
        Guid handle,
        string name)
    {
        var metadata = ReflectionMetadata.Get(instance.GetType());
        _Settings.Exposures.TryGetValue(instance.GetType(), out var exposure);
        var programmaticNames = exposure?.Values.Select(static value => value.Name)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var members = new List<BaseNode>();
        foreach (var member in metadata.Members.Where(member => !programmaticNames.Contains(member.Name)))
        {
            var created = _CreateMemberNode(handle, instance, member);
            if (!created.IsRight)
            {
                return Left((InteractionError)created);
            }

            members.Add((BaseNode)created);
        }

        if (exposure is not null)
        {
            foreach (var value in exposure.Values)
            {
                var valueNode = new LiveValueNode(new ValueNodeBinding(
                    handle,
                    value.Name,
                    value.ValueType,
                    canRead: true,
                    value.CanWrite,
                    value.IsNullable,
                    ValueConversion.GetEnumOptions(value.ValueType),
                    value.Range,
                    [],
                    value.Read,
                    value.CanWrite ? value.Write : null));
                members.Add(valueNode);
                _RefreshRouter.RegisterProgrammatic(instance, valueNode, value);
            }
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

        return Right(new LiveObjectNode(
            name,
            instance.GetType(),
            handle,
            summary,
            members.OrderBy(static member => member.Name, StringComparer.Ordinal).ToArray()));
    }

    private Either<InteractionError, BaseNode> _CreateMemberNode(
        Guid ownerHandle,
        object owner,
        ReflectedMember member)
    {
        switch (member.Kind)
        {
            case ReflectedMemberKind.VALUE:
                var valueBinding = new ValueNodeBinding(
                    ownerHandle,
                    member.Name,
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
                var valueNode = new LiveValueNode(valueBinding, member);
                _RefreshRouter.RegisterReflectedMember(
                    ownerHandle, owner, valueNode, member.Member.Name);
                return Right<BaseNode>(valueNode);
            case ReflectedMemberKind.REFERENCE:
                var referenceNode = new LiveReferenceNode(new ReferenceNodeBinding(
                    ownerHandle,
                    member.Name,
                    member.ValueType,
                    target => ReflectionMetadata.Read(member.Member, target)), member);
                _RefreshRouter.RegisterReflectedMember(
                    ownerHandle, owner, referenceNode, member.Member.Name);
                return Right<BaseNode>(referenceNode);
            case ReflectedMemberKind.COLLECTION:
                var collectionRead = (object target) => ReflectionMetadata.Read(member.Member, target);
                var collectionNode = new LiveCollectionNode(new CollectionNodeBinding(
                    ownerHandle,
                    member.Name,
                    member.ValueType,
                    collectionRead), member);
                _RefreshRouter.RegisterReflectedMember(
                    ownerHandle, owner, collectionNode, member.Member.Name);
                _RefreshRouter.RegisterCollection(
                    ownerHandle,
                    owner,
                    collectionNode,
                    member.Member.Name,
                    collectionRead);
                return Right<BaseNode>(collectionNode);
            case ReflectedMemberKind.ACTION:
                var methodNode = LiveMethodNode.Create(ownerHandle, member);
                return methodNode.IsRight
                    ? Right<BaseNode>((LiveMethodNode)methodNode)
                    : Left((InteractionError)methodNode);
            default:
                throw new InvalidOperationException("Unknown member kind.");
        }
    }

    private void _ReportUnexpected(string operation, Exception exception) =>
        RuntimeDiagnostics.UnexpectedFailure(
            _Logger,
            operation,
            exception,
            _Settings.IncludeSensitiveDiagnosticData);

    private Either<InteractionError, T> _UnexpectedFailure<T>(string operation, Exception exception)
    {
        _ReportUnexpected(operation, exception);
        return Left(new InteractionError(
            InteractionErrorCode.FAULT,
            $"The {operation} operation failed unexpectedly."));
    }

    private sealed record _HandleHolder(Guid Handle);

    private sealed record _RootEntry(ReflectedMember Member, Guid OwnerHandle, object Owner);
}
