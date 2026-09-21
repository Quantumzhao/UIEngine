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
    private static readonly Action<ILogger, string, Exception?> _CONFIGURATION_INVALID =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            UIEngineDiagnosticEventIds.CONFIGURATION_INVALID,
            "UIEngine host configuration is invalid in area {ConfigurationArea}.");
    private static readonly Action<ILogger, InteractionDispatchOperation, Exception?> _DISPATCH_FAILED =
        LoggerMessage.Define<InteractionDispatchOperation>(
            LogLevel.Warning,
            UIEngineDiagnosticEventIds.DISPATCH_FAILED,
            "Interaction dispatch failed for operation {DispatchOperation}.");
    private static readonly Action<ILogger, Guid, string?, InteractionErrorCode, Exception?> _OBSERVATION_FAILED =
        LoggerMessage.Define<Guid, string?, InteractionErrorCode>(
            LogLevel.Warning,
            UIEngineDiagnosticEventIds.OBSERVATION_FAILED,
            "Observation failed for runtime object {RuntimeId}, member {MemberId}, with code {ErrorCode}.");
    private static readonly Action<ILogger, BindingResolutionState, Exception?> _BINDING_RESOLVED =
        LoggerMessage.Define<BindingResolutionState>(
            LogLevel.Debug,
            UIEngineDiagnosticEventIds.BINDING_RESOLVED,
            "Binding resolution completed with state {BindingState}.");
    private static readonly Action<ILogger, BindingResolutionState, InteractionErrorCode, Exception?>
        _BINDING_BROKEN = LoggerMessage.Define<BindingResolutionState, InteractionErrorCode>(
            LogLevel.Warning,
            UIEngineDiagnosticEventIds.BINDING_BROKEN,
            "Binding resolution failed with state {BindingState} and code {ErrorCode}.");
    private static readonly Action<ILogger, string, InteractionErrorCode, Exception?> _VALIDATION_FAILED =
        LoggerMessage.Define<string, InteractionErrorCode>(
            LogLevel.Warning,
            UIEngineDiagnosticEventIds.VALIDATION_FAILED,
            "Validation failed for member {MemberId} with code {ErrorCode}.");
    private static readonly Action<ILogger, string, Exception?> _MUTATION_COMPLETED =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            UIEngineDiagnosticEventIds.MUTATION_COMPLETED,
            "Mutation completed for member {MemberId}.");
    private static readonly Action<ILogger, string, InteractionErrorCode, Exception?> _MUTATION_FAILED =
        LoggerMessage.Define<string, InteractionErrorCode>(
            LogLevel.Warning,
            UIEngineDiagnosticEventIds.MUTATION_FAILED,
            "Mutation failed for member {MemberId} with code {ErrorCode}.");
    private static readonly Action<ILogger, string, Exception?> _INVOCATION_STARTED =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            UIEngineDiagnosticEventIds.INVOCATION_STARTED,
            "Invocation started for action {ActionId}.");
    private static readonly Action<ILogger, string, InvocationStatus, Exception?> _INVOCATION_COMPLETED =
        LoggerMessage.Define<string, InvocationStatus>(
            LogLevel.Debug,
            UIEngineDiagnosticEventIds.INVOCATION_COMPLETED,
            "Invocation completed for action {ActionId} with status {InvocationStatus}.");
    private static readonly Action<ILogger, string, InteractionErrorCode, Exception?> _INVOCATION_FAILED =
        LoggerMessage.Define<string, InteractionErrorCode>(
            LogLevel.Warning,
            UIEngineDiagnosticEventIds.INVOCATION_FAILED,
            "Invocation failed for action {ActionId} with code {ErrorCode}.");
    private static readonly Action<ILogger, string, InteractionErrorCode, Exception?> _COLLECTION_ACCESS_FAILED =
        LoggerMessage.Define<string, InteractionErrorCode>(
            LogLevel.Warning,
            UIEngineDiagnosticEventIds.COLLECTION_ACCESS_FAILED,
            "Collection access failed for member {MemberId} with code {ErrorCode}.");
    private static readonly Action<ILogger, string, string, Exception?> _CAPABILITY_MISMATCH =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            UIEngineDiagnosticEventIds.CAPABILITY_MISMATCH,
            "Member {MemberId} does not provide required capability {Capability}.");

    private readonly object _Gate = new();
    private readonly ConditionalWeakTable<object, _IdentityHolder> _Identities = new();
    private readonly Dictionary<ObjectIdentity, WeakReference<object>> _Objects = [];
    private readonly Dictionary<ObjectIdentity, DomainIdentity?> _DomainIdentities = [];
    private readonly Dictionary<DomainIdentity, HashSet<ObjectIdentity>> _DomainIdentityIndex = [];
    private readonly Dictionary<ObjectIdentity, LogicalPath> _CanonicalPaths = [];
    private readonly Dictionary<string, _RootEntry> _Roots = new(StringComparer.Ordinal);
    private readonly HashSet<ObservationSubscription> _Subscriptions = [];
    private readonly HashSet<ActionInvocation> _Invocations = [];
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
        Configuration = _CreateConfiguration(options);
        _ProgrammaticExposureProvider = new ProgrammaticExposureDescriptorProvider(
            Configuration.Exposure,
            Configuration.ReflectionProvider);
        Paths = new LogicalPathResolver(this);
        Bindings = new BindingResolver(this, Paths);
        _Logger = Configuration.LoggerFactory.CreateLogger<UIEngineHost>();
        _HOST_CREATED(_Logger, Configuration.DescriptorProviders.Count, null);
    }

    private static UIEngineHostConfiguration _CreateConfiguration(UIEngineHostOptions options)
    {
        try
        {
            return new UIEngineHostConfiguration(options);
        }
        catch (Exception exception)
        {
            var loggerFactory = options?.LoggerFactory ??
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
            var logger = loggerFactory.CreateLogger<UIEngineHost>();
            _CONFIGURATION_INVALID(
                logger,
                "HostOptions",
                options?.IncludeSensitiveDiagnosticData == true ? exception : null);
            throw;
        }
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

    /// <summary>Creates a bounded, host-owned stream for one live object or exposed member.</summary>
    public async ValueTask<InteractionResult<IObservationSubscription>> ObserveAsync(
        ObjectHandle handle,
        ObservationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new ObservationRequest();
        if (IsDisposed)
        {
            return _DisposedFailure<IObservationSubscription>();
        }

        if (request.MemberId is not null && string.IsNullOrWhiteSpace(request.MemberId))
        {
            return InteractionResult.Failure<IObservationSubscription>(
                InteractionErrorCode.INVALID_INPUT,
                "An observed member identifier cannot be empty or whitespace.");
        }

        if (request.PollingInterval is { } pollingInterval && pollingInterval <= TimeSpan.Zero)
        {
            return InteractionResult.Failure<IObservationSubscription>(
                InteractionErrorCode.INVALID_INPUT,
                "An observation polling interval must be positive.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<IObservationSubscription>(
                InteractionErrorCode.CANCELLED,
                "Observation setup was cancelled.");
        }

        if (!TryResolve(handle, out var instance) || instance is null)
        {
            return IsDisposed
                ? _DisposedFailure<IObservationSubscription>()
                : InteractionResult.Failure<IObservationSubscription>(
                    InteractionErrorCode.TARGET_UNAVAILABLE,
                    "The observed object is no longer available.");
        }

        var identity = await GetDomainIdentityAsync(handle, cancellationToken).ConfigureAwait(false);
        if (!identity.IsSuccess)
        {
            return InteractionResult.Failure<IObservationSubscription>(
                identity.Error!.Code,
                identity.Error.Message);
        }

        var subscription = new ObservationSubscription(
            handle,
            request.MemberId,
            Configuration.CollectionLimits.ObservationBufferCapacity,
            _UntrackSubscription,
            exception => _ReportObservationFailure(
                handle.Identity,
                request.MemberId,
                InteractionErrorCode.OBSERVATION_FAILED,
                exception));
        long orderingToken = 0;
        void Publish(ObservationAdapterChange change)
        {
            subscription.Publish(new ChangeRecord(
                handle.Identity,
                identity.Value,
                change.MemberId,
                change.Kind,
                change.OldValue,
                change.NewValue,
                Interlocked.Increment(ref orderingToken),
                DateTimeOffset.UtcNow)
            {
                OldIndex = change.OldIndex,
                NewIndex = change.NewIndex,
            });
        }

        void ReportFailure(Exception exception) => _ReportObservationFailure(
            handle.Identity,
            request.MemberId,
            InteractionErrorCode.OBSERVATION_FAILED,
            exception);

        var sourceSubscription = await ObservationSourceFactory.SubscribeAsync(
            this,
            instance,
            handle,
            identity.Value,
            request,
            Publish,
            ReportFailure,
            cancellationToken).ConfigureAwait(false);
        if (!sourceSubscription.IsSuccess)
        {
            subscription.Dispose();
            _ReportObservationFailure(
                handle.Identity,
                request.MemberId,
                sourceSubscription.Error!.Code,
                null);
            return InteractionResult.Failure<IObservationSubscription>(
                sourceSubscription.Error.Code,
                sourceSubscription.Error.Message);
        }

        if (sourceSubscription.Value is null)
        {
            subscription.Dispose();
            _ReportObservationFailure(
                handle.Identity,
                request.MemberId,
                InteractionErrorCode.OBSERVATION_FAILED,
                null);
            return InteractionResult.Failure<IObservationSubscription>(
                InteractionErrorCode.OBSERVATION_FAILED,
                "The observation adapter returned no disposable subscription.");
        }

        subscription.SetSourceSubscription(sourceSubscription.Value);
        lock (_Gate)
        {
            if (IsDisposed)
            {
                subscription.Dispose();
                return _DisposedFailure<IObservationSubscription>();
            }

            _Subscriptions.Add(subscription);
        }

        return InteractionResult.Success<IObservationSubscription>(subscription);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _IsDisposed, 1) != 0)
        {
            return;
        }

        ObservationSubscription[] subscriptions;
        ActionInvocation[] invocations;
        lock (_Gate)
        {
            subscriptions = _Subscriptions.ToArray();
            invocations = _Invocations.ToArray();
            _Subscriptions.Clear();
            _Invocations.Clear();
            _Roots.Clear();
            _Objects.Clear();
            _DomainIdentities.Clear();
            _DomainIdentityIndex.Clear();
            _CanonicalPaths.Clear();
            _Identities.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }

        foreach (var invocation in invocations)
        {
            invocation.CompleteHostDisposed();
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

    internal void ReportObservationFailure(
        ObjectIdentity identity,
        string? memberId,
        InteractionErrorCode errorCode,
        Exception? exception = null) =>
        _ReportObservationFailure(identity, memberId, errorCode, exception);

    private void _ReportObservationFailure(
        ObjectIdentity identity,
        string? memberId,
        InteractionErrorCode errorCode,
        Exception? exception)
    {
        _OBSERVATION_FAILED(
            _Logger,
            identity.RuntimeId,
            memberId,
            errorCode,
            Configuration.IncludeSensitiveDiagnosticData ? exception : null);
    }

    private void _UntrackSubscription(ObservationSubscription subscription)
    {
        lock (_Gate)
        {
            _Subscriptions.Remove(subscription);
        }
    }

    internal InteractionResult<ActionInvocation> CreateInvocation(
        string actionId,
        bool supportsCancellation)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<ActionInvocation>();
        }

        var invocation = new ActionInvocation(
            actionId,
            supportsCancellation,
            Configuration.InvocationProgressBufferCapacity,
            _UntrackInvocation);
        lock (_Gate)
        {
            if (IsDisposed)
            {
                invocation.CompleteHostDisposed();
                return _DisposedFailure<ActionInvocation>();
            }

            _Invocations.Add(invocation);
        }

        return InteractionResult.Success(invocation);
    }

    internal void RecordBindingDiagnostic(BindingResolution resolution)
    {
        if (resolution.IsResolved)
        {
            _BINDING_RESOLVED(_Logger, resolution.State, null);
            return;
        }

        _BINDING_BROKEN(
            _Logger,
            resolution.State,
            resolution.Error?.Code ?? InteractionErrorCode.TARGET_MISSING,
            null);
    }

    internal void RecordMutationDiagnostic(string memberId, InteractionError? error)
    {
        if (error is null)
        {
            _MUTATION_COMPLETED(_Logger, memberId, null);
            return;
        }

        if (error.Code == InteractionErrorCode.VALIDATION_FAILED)
        {
            _VALIDATION_FAILED(_Logger, memberId, error.Code, null);
        }

        _MUTATION_FAILED(_Logger, memberId, error.Code, null);
    }

    internal void RecordCollectionDiagnostic(string memberId, InteractionError error) =>
        _COLLECTION_ACCESS_FAILED(_Logger, memberId, error.Code, null);

    internal void RecordCapabilityMismatch(string memberId, string capability) =>
        _CAPABILITY_MISMATCH(_Logger, memberId, capability, null);

    internal void RecordInvocationFailure(string actionId, InteractionError error)
    {
        if (error.Code == InteractionErrorCode.VALIDATION_FAILED)
        {
            _VALIDATION_FAILED(_Logger, actionId, error.Code, null);
        }

        _INVOCATION_FAILED(_Logger, actionId, error.Code, null);
    }

    internal async Task TrackInvocationAsync(string actionId, IActionInvocation invocation)
    {
        _INVOCATION_STARTED(_Logger, actionId, null);
        var completion = await invocation.Completion.ConfigureAwait(false);
        if (completion.IsSuccess)
        {
            _INVOCATION_COMPLETED(_Logger, actionId, invocation.Status, null);
            return;
        }

        _INVOCATION_FAILED(
            _Logger,
            actionId,
            completion.Error!.Code,
            Configuration.IncludeSensitiveDiagnosticData ? invocation.Fault?.Exception : null);
    }

    private void _UntrackInvocation(ActionInvocation invocation)
    {
        lock (_Gate)
        {
            _Invocations.Remove(invocation);
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
