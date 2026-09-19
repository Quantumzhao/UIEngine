using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using UIEngine.Core.Attributes;
using UIEngine.Core.Exposure;

namespace UIEngine.Core;

public sealed class UIEngineHost : IDisposable, IAsyncDisposable
{
    private static readonly Action<ILogger, int, Exception?> _HOST_CREATED = LoggerMessage.Define<int>(
        LogLevel.Debug,
        UIEngineDiagnosticEventIds.HOST_CREATED,
        "UIEngine host created with {DescriptorProviderCount} descriptor providers.");
    private static readonly Action<ILogger, Exception?> _HOST_DISPOSED = LoggerMessage.Define(
        LogLevel.Debug,
        UIEngineDiagnosticEventIds.HOST_DISPOSED,
        "UIEngine host disposed.");
    private static readonly Action<ILogger, Guid, Exception?> _DISCOVERY_STARTED = LoggerMessage.Define<Guid>(
        LogLevel.Debug,
        UIEngineDiagnosticEventIds.DISCOVERY_STARTED,
        "Descriptor discovery started for runtime object {RuntimeId}.");
    private static readonly Action<ILogger, Guid, Exception?> _DISCOVERY_COMPLETED = LoggerMessage.Define<Guid>(
        LogLevel.Debug,
        UIEngineDiagnosticEventIds.DISCOVERY_COMPLETED,
        "Descriptor discovery completed for runtime object {RuntimeId}.");
    private static readonly Action<ILogger, Guid, InteractionErrorCode, Exception?> _DISCOVERY_FAILED =
        LoggerMessage.Define<Guid, InteractionErrorCode>(
            LogLevel.Warning,
            UIEngineDiagnosticEventIds.DISCOVERY_FAILED,
            "Descriptor discovery failed for runtime object {RuntimeId} with code {ErrorCode}.");
    private static readonly Action<ILogger, InteractionDispatchOperation, Exception?> _DISPATCH_FAILED =
        LoggerMessage.Define<InteractionDispatchOperation>(
            LogLevel.Warning,
            UIEngineDiagnosticEventIds.DISPATCH_FAILED,
            "Interaction dispatch failed for operation {DispatchOperation}.");

    private readonly object _Gate = new();
    private readonly ConditionalWeakTable<object, _IdentityHolder> _Identities = new();
    private readonly Dictionary<ObjectIdentity, WeakReference<object>> _Objects = [];
    private readonly Dictionary<ObjectIdentity, DomainIdentity?> _DomainIdentities = [];
    private readonly Dictionary<DomainIdentity, HashSet<ObjectIdentity>> _DomainIdentityIndex = [];
    private readonly Dictionary<ObjectIdentity, LogicalPath> _CanonicalPaths = [];
    private readonly Dictionary<string, _RootEntry> _Roots = new(StringComparer.Ordinal);
    private readonly ILogger _Logger;
    private readonly ProgrammaticExposureDescriptorProvider _ProgrammaticExposureProvider;
    private int _IsDisposed;

    public UIEngineHost()
        : this(new UIEngineHostOptions())
    {
    }

    public UIEngineHost(IEnumerable<IObjectDescriptorProvider> providers)
        : this(new UIEngineHostOptions { DescriptorProviders = providers })
    {
    }

    public UIEngineHost(UIEngineHostOptions options)
    {
        Configuration = new UIEngineHostConfiguration(options);
        _ProgrammaticExposureProvider = new ProgrammaticExposureDescriptorProvider(
            Configuration.Exposure,
            Configuration.ReflectionProvider);
        Paths = new LogicalPathResolver(this);
        Bindings = new BindingResolver(this, Paths);
        _Logger = Configuration.LoggerFactory.CreateLogger<UIEngineHost>();
        _HOST_CREATED(_Logger, Configuration.DescriptorProviders.Count, null);
    }

    public UIEngineHostConfiguration Configuration { get; }

    public IReadOnlyList<IObjectDescriptorProvider> Providers => Configuration.DescriptorProviders;

    public LogicalPathResolver Paths { get; }

    public BindingResolver Bindings { get; }

    public bool IsDisposed => Volatile.Read(ref _IsDisposed) != 0;

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
        if (IsDisposed)
        {
            return _DisposedFailure<ObjectHandle>();
        }

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
            if (IsDisposed)
            {
                return _DisposedFailure<ObjectHandle>();
            }

            if (_Roots.ContainsKey(identifier))
            {
                return InteractionResult.Failure<ObjectHandle>(
                    InteractionErrorCode.DUPLICATE_ROOT_IDENTIFIER,
                    $"A root with identifier '{identifier}' is already registered.");
            }

            var handle = _GetOrCreateHandleCore(instance);
            var registration = new RegisteredRoot(identifier, handle, instance.GetType());
            _Roots.Add(identifier, new _RootEntry(registration, instance));
            _CanonicalPaths[handle.Identity] = LogicalPath.Root.Append(identifier);
            return InteractionResult.Success(handle);
        }
    }

    public InteractionResult<ObjectHandle> ReplaceRoot(string identifier, object instance)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<ObjectHandle>();
        }

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
            if (IsDisposed)
            {
                return _DisposedFailure<ObjectHandle>();
            }

            if (!_Roots.ContainsKey(identifier))
            {
                return InteractionResult.Failure<ObjectHandle>(
                    InteractionErrorCode.ROOT_NOT_FOUND,
                    $"No root with identifier '{identifier}' is registered.");
            }

            var handle = _GetOrCreateHandleCore(instance);
            var registration = new RegisteredRoot(identifier, handle, instance.GetType());
            _Roots[identifier] = new _RootEntry(registration, instance);
            _CanonicalPaths[handle.Identity] = LogicalPath.Root.Append(identifier);
            return InteractionResult.Success(handle);
        }
    }

    public InteractionResult<RegisteredRoot> UnregisterRoot(string identifier)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<RegisteredRoot>();
        }

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return InteractionResult.Failure<RegisteredRoot>(
                InteractionErrorCode.INVALID_ROOT_IDENTIFIER,
                "A root identifier cannot be empty or whitespace.");
        }

        lock (_Gate)
        {
            if (IsDisposed)
            {
                return _DisposedFailure<RegisteredRoot>();
            }

            if (!_Roots.Remove(identifier, out var entry))
            {
                return InteractionResult.Failure<RegisteredRoot>(
                    InteractionErrorCode.ROOT_NOT_FOUND,
                    $"No root with identifier '{identifier}' is registered.");
            }

            return InteractionResult.Success(entry.Registration);
        }
    }

    public ObjectHandle GetOrCreateHandle(object instance)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(instance);

        if (instance.GetType().IsValueType)
        {
            throw new ArgumentException("Runtime identity requires a reference type.", nameof(instance));
        }

        lock (_Gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _GetOrCreateHandleCore(instance);
        }
    }

    /// <summary>Gets and indexes the optional domain identity for an encountered runtime object.</summary>
    public async ValueTask<InteractionResult<DomainIdentity?>> GetDomainIdentityAsync(
        ObjectHandle handle,
        CancellationToken cancellationToken = default)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<DomainIdentity?>();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<DomainIdentity?>(
                InteractionErrorCode.CANCELLED,
                "Domain identity discovery was cancelled.");
        }

        if (!TryResolve(handle, out var instance) || instance is null)
        {
            return IsDisposed
                ? _DisposedFailure<DomainIdentity?>()
                : InteractionResult.Failure<DomainIdentity?>(
                    InteractionErrorCode.TARGET_UNAVAILABLE,
                    "The target object is no longer available.");
        }

        lock (_Gate)
        {
            if (_DomainIdentities.TryGetValue(handle.Identity, out var cached))
            {
                return InteractionResult.Success(cached);
            }
        }

        var discovered = await _DiscoverDomainIdentityAsync(instance, cancellationToken).ConfigureAwait(false);
        if (!discovered.IsSuccess)
        {
            return discovered;
        }

        lock (_Gate)
        {
            if (IsDisposed)
            {
                return _DisposedFailure<DomainIdentity?>();
            }

            if (_DomainIdentities.TryGetValue(handle.Identity, out var cached))
            {
                return InteractionResult.Success(cached);
            }

            _DomainIdentities.Add(handle.Identity, discovered.Value);
            if (discovered.Value is { } identity)
            {
                if (!_DomainIdentityIndex.TryGetValue(identity, out var matches))
                {
                    matches = [];
                    _DomainIdentityIndex.Add(identity, matches);
                }

                matches.Add(handle.Identity);
            }

            return discovered;
        }
    }

    /// <summary>Resolves one encountered domain identity without traversing the object graph.</summary>
    public InteractionResult<ObjectHandle> ResolveDomainIdentity(DomainIdentity identity)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<ObjectHandle>();
        }

        if (string.IsNullOrWhiteSpace(identity.Value))
        {
            return InteractionResult.Failure<ObjectHandle>(
                InteractionErrorCode.INVALID_DOMAIN_IDENTITY,
                "A domain identity cannot be empty or whitespace.");
        }

        lock (_Gate)
        {
            if (IsDisposed)
            {
                return _DisposedFailure<ObjectHandle>();
            }

            if (!_DomainIdentityIndex.TryGetValue(identity, out var indexedIdentities))
            {
                return _DomainIdentityNotFound(identity);
            }

            indexedIdentities.RemoveWhere(runtimeIdentity =>
                !_Objects.TryGetValue(runtimeIdentity, out var reference) ||
                !reference.TryGetTarget(out _));
            if (indexedIdentities.Count == 0)
            {
                _DomainIdentityIndex.Remove(identity);
                return _DomainIdentityNotFound(identity);
            }

            if (indexedIdentities.Count > 1)
            {
                return InteractionResult.Failure<ObjectHandle>(
                    InteractionErrorCode.AMBIGUOUS_DOMAIN_IDENTITY,
                    $"Domain identity '{identity}' matches multiple live objects.");
            }

            return InteractionResult.Success(new ObjectHandle(indexedIdentities.Single()));
        }
    }

    public async ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
        ObjectHandle handle,
        CancellationToken cancellationToken = default)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<IObjectDescriptor>();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.CANCELLED,
                "Descriptor discovery was cancelled.");
        }

        if (!TryResolve(handle, out var instance) || instance is null)
        {
            return IsDisposed
                ? _DisposedFailure<IObjectDescriptor>()
                : InteractionResult.Failure<IObjectDescriptor>(
                    InteractionErrorCode.TARGET_UNAVAILABLE,
                    "The target object is no longer available.");
        }

        var domainIdentity = await GetDomainIdentityAsync(handle, cancellationToken).ConfigureAwait(false);
        if (!domainIdentity.IsSuccess)
        {
            _DISCOVERY_FAILED(_Logger, handle.Identity.RuntimeId, domainIdentity.Error!.Code, null);
            return InteractionResult.Failure<IObjectDescriptor>(
                domainIdentity.Error.Code,
                domainIdentity.Error.Message);
        }

        var hasProgrammaticExposure = _ProgrammaticExposureProvider.CanDescribe(instance.GetType());
        var provider = hasProgrammaticExposure
            ? null
            : Configuration.CustomDescriptorProviders.FirstOrDefault(
                candidate => candidate.CanDescribe(instance.GetType()));
        provider ??= hasProgrammaticExposure
            ? null
            : Configuration.ReflectionProvider is not null &&
                Configuration.ReflectionProvider.CanDescribe(instance.GetType())
                    ? Configuration.ReflectionProvider
                    : null;
        if (!hasProgrammaticExposure && provider is null)
        {
            var unavailable = InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                $"No descriptor provider supports '{instance.GetType().FullName}'.");
            _DISCOVERY_FAILED(_Logger, handle.Identity.RuntimeId, unavailable.Error!.Code, null);
            return unavailable;
        }

        _DISCOVERY_STARTED(_Logger, handle.Identity.RuntimeId, null);
        try
        {
            var policy = provider as IInteractionDispatchPolicy;
            var canDiscoverDirectly = policy?.CanExecuteDirectly(
                InteractionDispatchOperation.DESCRIPTOR_DISCOVERY) == true;
            var result = await ExecuteInteractionAsync(
                InteractionDispatchOperation.DESCRIPTOR_DISCOVERY,
                canDiscoverDirectly,
                async () =>
                {
                    var discovered = hasProgrammaticExposure
                        ? await _ProgrammaticExposureProvider
                            .DescribeAsync(this, instance, handle, cancellationToken)
                            .ConfigureAwait(false)
                        : await provider!
                            .DescribeAsync(this, instance, handle, cancellationToken)
                            .ConfigureAwait(false);
                    return discovered.IsSuccess
                        ? await DispatchedObjectDescriptor.CreateAsync(
                            this,
                            discovered.Value,
                            domainIdentity.Value,
                            policy,
                            cancellationToken).ConfigureAwait(false)
                        : discovered;
                },
                cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                _DISCOVERY_COMPLETED(_Logger, handle.Identity.RuntimeId, null);
                return result;
            }

            _DISCOVERY_FAILED(_Logger, handle.Identity.RuntimeId, result.Error!.Code, null);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var cancelled = InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.CANCELLED,
                "Descriptor discovery was cancelled.");
            _DISCOVERY_FAILED(_Logger, handle.Identity.RuntimeId, cancelled.Error!.Code, null);
            return cancelled;
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            var disposed = _DisposedFailure<IObjectDescriptor>();
            _DISCOVERY_FAILED(_Logger, handle.Identity.RuntimeId, disposed.Error!.Code, null);
            return disposed;
        }
        catch (Exception exception)
        {
            var failed = InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                exception.Message);
            _DISCOVERY_FAILED(
                _Logger,
                handle.Identity.RuntimeId,
                failed.Error!.Code,
                Configuration.IncludeSensitiveDiagnosticData ? exception : null);
            return failed;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _IsDisposed, 1) != 0)
        {
            return;
        }

        lock (_Gate)
        {
            _Roots.Clear();
            _Objects.Clear();
            _DomainIdentities.Clear();
            _DomainIdentityIndex.Clear();
            _CanonicalPaths.Clear();
            _Identities.Clear();
        }

        _HOST_DISPOSED(_Logger, null);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal bool TryResolve(ObjectHandle handle, out object? instance)
    {
        if (IsDisposed)
        {
            instance = null;
            return false;
        }

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

    internal void RecordCanonicalPath(ObjectHandle handle, LogicalPath path)
    {
        lock (_Gate)
        {
            if (!IsDisposed)
            {
                _CanonicalPaths[handle.Identity] = path;
            }
        }
    }

    internal bool TryGetCanonicalPath(ObjectHandle handle, out LogicalPath? path)
    {
        lock (_Gate)
        {
            return _CanonicalPaths.TryGetValue(handle.Identity, out path);
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

    internal async ValueTask<InteractionResult<ObjectHandle>> EncounterAsync(
        object instance,
        CancellationToken cancellationToken = default)
    {
        ObjectHandle handle;
        try
        {
            handle = GetOrCreateHandle(instance);
        }
        catch (ObjectDisposedException)
        {
            return _DisposedFailure<ObjectHandle>();
        }

        var identity = await GetDomainIdentityAsync(handle, cancellationToken).ConfigureAwait(false);
        return identity.IsSuccess
            ? InteractionResult.Success(handle)
            : InteractionResult.Failure<ObjectHandle>(identity.Error!.Code, identity.Error.Message);
    }

    internal ValueTask<InteractionResult<T>> ExecuteInteractionAsync<T>(
        InteractionDispatchOperation operation,
        bool canExecuteDirectly,
        Func<InteractionResult<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ExecuteInteractionAsync(
            operation,
            canExecuteDirectly,
            () => ValueTask.FromResult(action()),
            cancellationToken);
    }

    internal async ValueTask<InteractionResult<T>> ExecuteInteractionAsync<T>(
        InteractionDispatchOperation operation,
        bool canExecuteDirectly,
        Func<ValueTask<InteractionResult<T>>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsDisposed)
        {
            return _DisposedFailure<T>();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<T>(
                InteractionErrorCode.CANCELLED,
                $"The {operation} interaction was cancelled.");
        }

        try
        {
            if (canExecuteDirectly || Configuration.Dispatcher.CheckAccess())
            {
                return await _ExecuteCheckedAsync(action, cancellationToken).ConfigureAwait(false);
            }

            var pending = await Configuration.Dispatcher.InvokeAsync(
                () => _ExecuteCheckedAsync(action, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            return await pending.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<T>(
                InteractionErrorCode.CANCELLED,
                $"The {operation} interaction was cancelled.");
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            return _DisposedFailure<T>();
        }
        catch (Exception exception)
        {
            _DISPATCH_FAILED(
                _Logger,
                operation,
                Configuration.IncludeSensitiveDiagnosticData ? exception : null);
            return InteractionResult.Failure<T>(
                InteractionErrorCode.DISPATCH_FAILED,
                $"The dispatcher failed while executing {operation}.");
        }
    }

    private async ValueTask<InteractionResult<T>> _ExecuteCheckedAsync<T>(
        Func<ValueTask<InteractionResult<T>>> action,
        CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<T>();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<T>(
                InteractionErrorCode.CANCELLED,
                "The interaction was cancelled before it executed.");
        }

        return await action().ConfigureAwait(false);
    }

    private async ValueTask<InteractionResult<DomainIdentity?>> _DiscoverDomainIdentityAsync(
        object instance,
        CancellationToken cancellationToken)
    {
        try
        {
            if (Configuration.Exposure.TryGet(instance.GetType(), out var registration) &&
                registration?.GetDomainIdentity is not null)
            {
                return _NormalizeDomainIdentity(registration.GetDomainIdentity(instance));
            }

            var configuredProvider = Configuration.DomainIdentityProviders.FirstOrDefault(
                provider => provider.CanProvideIdentity(instance.GetType()));
            if (configuredProvider is not null)
            {
                var provided = await configuredProvider
                    .GetIdentityAsync(instance, cancellationToken)
                    .ConfigureAwait(false);
                return provided.IsSuccess
                    ? _NormalizeDomainIdentity(provided.Value)
                    : InteractionResult.Failure<DomainIdentity?>(
                        provided.Error!.Code,
                        provided.Error.Message);
            }

            return _DiscoverBuiltInDomainIdentity(instance);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<DomainIdentity?>(
                InteractionErrorCode.CANCELLED,
                "Domain identity discovery was cancelled.");
        }
        catch (Exception exception)
        {
            return InteractionResult.Failure<DomainIdentity?>(
                InteractionErrorCode.IDENTITY_PROVIDER_FAILED,
                $"Domain identity discovery failed: {exception.Message}");
        }
    }

    private static InteractionResult<DomainIdentity?> _DiscoverBuiltInDomainIdentity(object instance)
    {
        var attributedMembers = instance.GetType()
            .GetMembers(BindingFlags.Instance | BindingFlags.Public)
            .Where(static member => member.IsDefined(typeof(DomainIdentitySourceAttribute), inherit: true))
            .ToArray();
        if (attributedMembers.Length > 1)
        {
            return InteractionResult.Failure<DomainIdentity?>(
                InteractionErrorCode.CONFLICTING_DOMAIN_IDENTITY,
                $"Type '{instance.GetType().FullName}' has multiple attributed domain identity members.");
        }

        string? attributedValue = null;
        if (attributedMembers.Length == 1)
        {
            var member = attributedMembers[0];
            if (member is PropertyInfo { PropertyType: var propertyType } property &&
                propertyType == typeof(string) &&
                property.GetMethod is not null &&
                property.GetIndexParameters().Length == 0)
            {
                attributedValue = (string?)property.GetValue(instance);
            }
            else if (member is FieldInfo { FieldType: var fieldType } field && fieldType == typeof(string))
            {
                attributedValue = (string?)field.GetValue(instance);
            }
            else
            {
                return InteractionResult.Failure<DomainIdentity?>(
                    InteractionErrorCode.INVALID_DOMAIN_IDENTITY,
                    $"Attributed domain identity member '{member.Name}' must be a readable string field or property.");
            }
        }

        var interfaceValue = (instance as IStableDomainIdentity)?.DomainIdentity;
        if ((interfaceValue is not null && string.IsNullOrWhiteSpace(interfaceValue)) ||
            (attributedValue is not null && string.IsNullOrWhiteSpace(attributedValue)))
        {
            return InteractionResult.Failure<DomainIdentity?>(
                InteractionErrorCode.INVALID_DOMAIN_IDENTITY,
                "A supplied domain identity cannot be empty or whitespace.");
        }

        if (interfaceValue is not null &&
            attributedValue is not null &&
            !StringComparer.Ordinal.Equals(interfaceValue, attributedValue))
        {
            return InteractionResult.Failure<DomainIdentity?>(
                InteractionErrorCode.CONFLICTING_DOMAIN_IDENTITY,
                $"Type '{instance.GetType().FullName}' supplies conflicting interface and attributed domain identities.");
        }

        return _NormalizeDomainIdentity(interfaceValue ?? attributedValue);
    }

    private static InteractionResult<DomainIdentity?> _NormalizeDomainIdentity(string? value)
    {
        if (value is null)
        {
            return InteractionResult.Success<DomainIdentity?>(null);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return InteractionResult.Failure<DomainIdentity?>(
                InteractionErrorCode.INVALID_DOMAIN_IDENTITY,
                "A supplied domain identity cannot be empty or whitespace.");
        }

        return InteractionResult.Success<DomainIdentity?>(new DomainIdentity(value));
    }

    private static InteractionResult<ObjectHandle> _DomainIdentityNotFound(DomainIdentity identity) =>
        InteractionResult.Failure<ObjectHandle>(
            InteractionErrorCode.DOMAIN_IDENTITY_NOT_FOUND,
            $"No live encountered object has domain identity '{identity}'.");

    private static InteractionResult<T> _DisposedFailure<T>() => InteractionResult.Failure<T>(
        InteractionErrorCode.HOST_DISPOSED,
        "The UIEngine host has been disposed.");

    private sealed record _IdentityHolder(ObjectIdentity Identity);

    private sealed record _RootEntry(RegisteredRoot Registration, object Instance);

}
