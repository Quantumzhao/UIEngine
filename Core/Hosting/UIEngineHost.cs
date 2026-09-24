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
/// A frontend on another thread is responsible for arranging that handoff outside Core.
/// </remarks>
public sealed class UIEngineHost : IDisposable
{
    private readonly ConditionalWeakTable<object, _HandleHolder> _Handles = new();
    private readonly Dictionary<Guid, WeakReference<object>> _Objects = [];
    private readonly Dictionary<string, _RootEntry> _Roots = new(StringComparer.Ordinal);
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

    public Either<InteractionError, Guid> SetRoot(string name, object instance)
    {
        if (instance.GetType().IsValueType)
        {
            return Left(new InteractionError(
                InteractionErrorCode.TYPE_MISMATCH,
                "A root must be a reference type."));
        }

        var handle = _GetOrCreateHandle(instance);
        var registration = new RootRegistration(name, handle);
        _Roots[name] = new _RootEntry(registration, instance);
        return Right(handle);
    }

    public Either<InteractionError, RootRegistration> RemoveRoot(string name)
    {
        return _Roots.Remove(name, out var root)
            ? Right(root.Registration)
            : Left(new InteractionError(
                InteractionErrorCode.NOT_FOUND,
                $"Root '{name}' was not found."));
    }

    public Guid GetOrCreateHandle(object instance)
    {
        if (instance.GetType().IsValueType)
        {
            throw new ArgumentException("Runtime handles require reference types.", nameof(instance));
        }

        return _GetOrCreateHandle(instance);
    }

    public Either<InteractionError, ResolvedNode> ResolveRootNode(string name)
    {
        if (!_Roots.TryGetValue(name, out var root))
        {
            return Left(new InteractionError(
                InteractionErrorCode.NOT_FOUND,
                $"Root '{name}' was not found."));
        }

        var created = CreateObjectNode(root.Registration.Handle, name);
        if (!created.IsRight)
        {
            return Left((InteractionError)created);
        }

        return Right(new ResolvedNode(
            LogicalPath.Root.Append(name),
            (LiveObjectNode)created));
    }

    public Either<InteractionError, ResolvedPath> ResolvePath(LogicalPath path) =>
        Execute(
            "resolve path",
            () => PathResolution.Resolve(this, path));

    public void Dispose()
    {
        _Roots.Clear();
        _Objects.Clear();
        _Handles.Clear();
    }

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
            switch (member.Kind)
            {
                case ReflectedMemberKind.VALUE:
                    var valueBinding = new ValueNodeBinding(
                        this,
                        handle,
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
                    members.Add(new LiveValueNode(valueBinding, member));
                    break;
                case ReflectedMemberKind.REFERENCE:
                    members.Add(new LiveReferenceNode(new ReferenceNodeBinding(
                        this,
                        handle,
                        member.Name,
                        member.ValueType,
                        target => ReflectionMetadata.Read(member.Member, target)), member));
                    break;
                case ReflectedMemberKind.COLLECTION:
                    members.Add(new LiveCollectionNode(new CollectionNodeBinding(
                        this,
                        handle,
                        member.Name,
                        member.ValueType,
                        target => ReflectionMetadata.Read(member.Member, target)), member));
                    break;
                case ReflectedMemberKind.ACTION:
                    var methodNode = LiveMethodNode.Create(this, handle, member);
                    if (!methodNode.IsRight)
                    {
                        return Left((InteractionError)methodNode);
                    }

                    members.Add((LiveMethodNode)methodNode);
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
                    value.Name,
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

        return Right(new LiveObjectNode(
            this,
            name,
            instance.GetType(),
            handle,
            summary,
            members.OrderBy(static member => member.Name, StringComparer.Ordinal).ToArray()));
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

    private sealed record _RootEntry(RootRegistration Registration, object Instance);
}
