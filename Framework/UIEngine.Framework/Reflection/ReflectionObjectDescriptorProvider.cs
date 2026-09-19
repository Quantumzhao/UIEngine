using System.Reflection;
using UIEngine.Core;

namespace UIEngine.Reflection;

/// <summary>Creates semantic descriptors for explicitly annotated reference types.</summary>
public sealed class ReflectionObjectDescriptorProvider : IObjectDescriptorProvider
{
    public bool CanDescribe(Type objectType)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        return !objectType.IsValueType;
    }

    public ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
        UIEngineHost host,
        object instance,
        ObjectHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(instance);

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.CANCELLED,
                "Descriptor discovery was cancelled."));
        }

        if (!CanDescribe(instance.GetType()))
        {
            return ValueTask.FromResult(InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.UNSUPPORTED_TARGET_TYPE,
                "Reflection descriptors require a reference-type target."));
        }

        var metadata = ReflectionTypeMetadataCache.GetOrCreate(instance.GetType());
        IObjectDescriptor descriptor = new ReflectionObjectDescriptor(host, instance, handle, metadata);
        return ValueTask.FromResult(InteractionResult.Success(descriptor));
    }
}

/// <summary>Describes one live object without recursively expanding its references.</summary>
internal sealed class ReflectionObjectDescriptor : IObjectDescriptor
{
    public ReflectionObjectDescriptor(
        UIEngineHost host,
        object instance,
        ObjectHandle handle,
        ReflectionTypeMetadata metadata)
    {
        Identity = handle.Identity;
        TypeName = instance.GetType().FullName ?? instance.GetType().Name;
        DisplayName = instance.GetType().Name;
        Summary = _ReadSummary(metadata.SummaryMember, instance);

        Values = metadata.DescriptorMembers
            .Where(static member => member.Kind == ReflectionMemberKind.VALUE)
            .Select(member => (IValueDescriptor)new ReflectionValueDescriptor(instance, member))
            .ToArray();
        References = metadata.DescriptorMembers
            .Where(static member => member.Kind == ReflectionMemberKind.REFERENCE)
            .Select(member => (IReferenceDescriptor)new ReflectionReferenceDescriptor(host, instance, member))
            .ToArray();
        Collections = metadata.DescriptorMembers
            .Where(static member => member.Kind == ReflectionMemberKind.COLLECTION)
            .Select(member => (ICollectionDescriptor)new ReflectionCollectionDescriptor(instance, member))
            .ToArray();
        Actions = metadata.DescriptorMembers
            .Where(static member => member.Kind == ReflectionMemberKind.ACTION)
            .Select(member => (IActionDescriptor)new ReflectionActionDescriptor(instance, member))
            .ToArray();
    }

    public ObjectIdentity Identity { get; }

    public string TypeName { get; }

    public string DisplayName { get; }

    public string? Summary { get; }

    public IReadOnlyList<IValueDescriptor> Values { get; }

    public IReadOnlyList<IReferenceDescriptor> References { get; }

    public IReadOnlyList<ICollectionDescriptor> Collections { get; }

    public IReadOnlyList<IActionDescriptor> Actions { get; }

    private static string? _ReadSummary(MemberInfo? summaryMember, object instance)
    {
        if (summaryMember is null)
        {
            return null;
        }

        try
        {
            return ReflectionMemberAccess.Read(summaryMember, instance)?.ToString();
        }
        catch (TargetInvocationException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>Provides shared metadata and weak target access for reflected member descriptors.</summary>
internal abstract class ReflectionMemberDescriptor : IMemberDescriptor
{
    private readonly WeakReference<object> _Target;

    protected ReflectionMemberDescriptor(object target, ReflectionMemberMetadata metadata)
    {
        _Target = new WeakReference<object>(target);
        Metadata = metadata;
    }

    public string Id => Metadata.Id;

    public string DisplayName => Metadata.DisplayName;

    protected ReflectionMemberMetadata Metadata { get; }

    protected bool TryGetTarget(out object? target) => _Target.TryGetTarget(out target);
}

/// <summary>Describes a scalar member whose live operations are supplied in Part 7.</summary>
internal sealed class ReflectionValueDescriptor : ReflectionMemberDescriptor, IValueDescriptor
{
    public ReflectionValueDescriptor(object target, ReflectionMemberMetadata metadata)
        : base(target, metadata)
    {
    }

    public Type ValueType => Metadata.MemberType;

    public bool CanRead => false;

    public bool CanWrite => false;

    public ValueTask<InteractionResult<object?>> ReadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_Unavailable(cancellationToken));

    public ValueTask<InteractionResult<object?>> WriteAsync(
        object? value,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_Unavailable(cancellationToken));

    private static InteractionResult<object?> _Unavailable(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? InteractionResult.Failure<object?>(
                InteractionErrorCode.CANCELLED,
                "The value operation was cancelled.")
            : InteractionResult.Failure<object?>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                "Live value operations are introduced in Part 7.");
}

/// <summary>Resolves an exposed object reference to a host-scoped handle on demand.</summary>
internal sealed class ReflectionReferenceDescriptor : ReflectionMemberDescriptor, IReferenceDescriptor
{
    private readonly UIEngineHost _Host;

    public ReflectionReferenceDescriptor(
        UIEngineHost host,
        object target,
        ReflectionMemberMetadata metadata)
        : base(target, metadata)
    {
        _Host = host;
    }

    public Type ReferenceType => Metadata.MemberType;

    public ValueTask<InteractionResult<ObjectHandle>> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(InteractionResult.Failure<ObjectHandle>(
                InteractionErrorCode.CANCELLED,
                "Reference resolution was cancelled."));
        }

        if (!TryGetTarget(out var target) || target is null)
        {
            return ValueTask.FromResult(InteractionResult.Failure<ObjectHandle>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "The containing object is no longer available."));
        }

        try
        {
            var referencedObject = ReflectionMemberAccess.Read(Metadata.Member, target);
            if (referencedObject is null)
            {
                return ValueTask.FromResult(InteractionResult.Failure<ObjectHandle>(
                    InteractionErrorCode.TARGET_UNAVAILABLE,
                    $"Reference '{Id}' is null."));
            }

            if (referencedObject.GetType().IsValueType)
            {
                return ValueTask.FromResult(InteractionResult.Failure<ObjectHandle>(
                    InteractionErrorCode.UNSUPPORTED_TARGET_TYPE,
                    $"Reference '{Id}' produced a value type."));
            }

            return ValueTask.FromResult(InteractionResult.Success(
                _Host.GetOrCreateHandle(referencedObject)));
        }
        catch (TargetInvocationException exception)
        {
            return ValueTask.FromResult(InteractionResult.Failure<ObjectHandle>(
                InteractionErrorCode.INVOCATION_FAILED,
                exception.InnerException?.Message ?? exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return ValueTask.FromResult(InteractionResult.Failure<ObjectHandle>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                exception.Message));
        }
    }
}

/// <summary>Describes an exposed collection without enumerating it during discovery.</summary>
internal sealed class ReflectionCollectionDescriptor : ReflectionMemberDescriptor, ICollectionDescriptor
{
    public ReflectionCollectionDescriptor(object target, ReflectionMemberMetadata metadata)
        : base(target, metadata)
    {
        ElementType = ReflectionTypeClassifier.GetCollectionElementType(metadata.MemberType);
    }

    public Type ElementType { get; }

    public ValueTask<InteractionResult<IReadOnlyList<ObjectHandle>>> SnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var result = cancellationToken.IsCancellationRequested
            ? InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                InteractionErrorCode.CANCELLED,
                "Collection enumeration was cancelled.")
            : InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                "Finite collection snapshots are introduced in Part 7.");
        return ValueTask.FromResult(result);
    }
}

/// <summary>Describes an exposed action whose invocation is supplied in Part 7.</summary>
internal sealed class ReflectionActionDescriptor : ReflectionMemberDescriptor, IActionDescriptor
{
    public ReflectionActionDescriptor(object target, ReflectionMemberMetadata metadata)
        : base(target, metadata)
    {
        Parameters = ((MethodInfo)metadata.Member)
            .GetParameters()
            .Select(static parameter => (IParameterDescriptor)new ReflectionParameterDescriptor(parameter))
            .ToArray();
    }

    public IReadOnlyList<IParameterDescriptor> Parameters { get; }

    public ValueTask<InteractionResult<object?>> InvokeAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var result = cancellationToken.IsCancellationRequested
            ? InteractionResult.Failure<object?>(
                InteractionErrorCode.CANCELLED,
                "Action invocation was cancelled.")
            : InteractionResult.Failure<object?>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                "Action invocation is introduced in Part 7.");
        return ValueTask.FromResult(result);
    }
}

/// <summary>Describes one reflected action parameter.</summary>
internal sealed class ReflectionParameterDescriptor : IParameterDescriptor
{
    public ReflectionParameterDescriptor(ParameterInfo parameter)
    {
        Id = parameter.Name ?? $"arg{parameter.Position}";
        DisplayName = Id;
        ParameterType = parameter.ParameterType;
        IsRequired = !parameter.IsOptional;
        DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public Type ParameterType { get; }

    public bool IsRequired { get; }

    public object? DefaultValue { get; }
}

/// <summary>Reads reflected properties, fields, and parameterless summary methods.</summary>
internal static class ReflectionMemberAccess
{
    public static object? Read(MemberInfo member, object instance) => member switch
    {
        PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(instance),
        FieldInfo field => field.GetValue(instance),
        MethodInfo method when method.GetParameters().Length == 0 => method.Invoke(instance, null),
        _ => throw new InvalidOperationException($"Member '{member.Name}' cannot be read without arguments."),
    };
}

/// <summary>Provides reflection type-shape helpers shared by descriptor implementations.</summary>
internal static class ReflectionTypeClassifier
{
    public static Type GetCollectionElementType(Type collectionType)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType() ?? typeof(object);
        }

        var enumerableType = collectionType
            .GetInterfaces()
            .Append(collectionType)
            .Where(static type => type.IsGenericType)
            .FirstOrDefault(static type => type.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerableType?.GetGenericArguments()[0] ?? typeof(object);
    }
}
