using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace UIEngine.Core;

public sealed class UIEngineHost
{
    private readonly object _Gate = new();
    private readonly ConditionalWeakTable<object, _IdentityHolder> _Identities = new();
    private readonly Dictionary<ObjectIdentity, WeakReference<object>> _Objects = [];
    private readonly Dictionary<string, _RootEntry> _Roots = new(StringComparer.Ordinal);

    public UIEngineHost(IEnumerable<IObjectDescriptorProvider>? providers = null)
    {
        var providerList = (providers ?? []).ToArray();
        Providers = new ReadOnlyCollection<IObjectDescriptorProvider>(providerList);
    }

    public IReadOnlyList<IObjectDescriptorProvider> Providers { get; }

    public IReadOnlyList<RegisteredRoot> Roots
    {
        get
        {
            lock (_Gate)
            {
                return _Roots.Values.Select(static entry => entry.Registration).ToArray();
            }
        }
    }

    public InteractionResult<ObjectHandle> RegisterRoot(string identifier, object instance)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return InteractionResult.Failure<ObjectHandle>(
                InteractionErrorCode.INVALID_ROOT_IDENTIFIER,
                "A root identifier cannot be empty or whitespace.");
        }

        ArgumentNullException.ThrowIfNull(instance);

        if (instance.GetType().IsValueType)
        {
            return InteractionResult.Failure<ObjectHandle>(
                InteractionErrorCode.UNSUPPORTED_TARGET_TYPE,
                "A root must be a reference type.");
        }

        lock (_Gate)
        {
            if (_Roots.ContainsKey(identifier))
            {
                return InteractionResult.Failure<ObjectHandle>(
                    InteractionErrorCode.DUPLICATE_ROOT_IDENTIFIER,
                    $"A root with identifier '{identifier}' is already registered.");
            }

            var handle = _GetOrCreateHandleCore(instance);
            var registration = new RegisteredRoot(identifier, handle, instance.GetType());
            _Roots.Add(identifier, new _RootEntry(registration, instance));
            return InteractionResult.Success(handle);
        }
    }

    public ObjectHandle GetOrCreateHandle(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (instance.GetType().IsValueType)
        {
            throw new ArgumentException("Runtime identity requires a reference type.", nameof(instance));
        }

        lock (_Gate)
        {
            return _GetOrCreateHandleCore(instance);
        }
    }

    public ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
        ObjectHandle handle,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.CANCELLED,
                "Descriptor discovery was cancelled."));
        }

        if (!TryResolve(handle, out var instance) || instance is null)
        {
            return ValueTask.FromResult(InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "The target object is no longer available."));
        }

        var provider = Providers.FirstOrDefault(candidate => candidate.CanDescribe(instance.GetType()));
        if (provider is null)
        {
            return ValueTask.FromResult(InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                $"No descriptor provider supports '{instance.GetType().FullName}'."));
        }

        return provider.DescribeAsync(this, instance, handle, cancellationToken);
    }

    internal bool TryResolve(ObjectHandle handle, out object? instance)
    {
        lock (_Gate)
        {
            if (_Objects.TryGetValue(handle.Identity, out var reference) &&
                reference.TryGetTarget(out instance))
            {
                return true;
            }

            _Objects.Remove(handle.Identity);
            instance = null;
            return false;
        }
    }

    private ObjectHandle _GetOrCreateHandleCore(object instance)
    {
        if (_Identities.TryGetValue(instance, out var existing))
        {
            return new ObjectHandle(existing.Identity);
        }

        var identity = new ObjectIdentity(Guid.NewGuid());
        _Identities.Add(instance, new _IdentityHolder(identity));
        _Objects.Add(identity, new WeakReference<object>(instance));
        return new ObjectHandle(identity);
    }

    private sealed record _IdentityHolder(ObjectIdentity Identity);

    private sealed record _RootEntry(RegisteredRoot Registration, object Instance);
}
