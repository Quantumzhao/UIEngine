using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
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

    private readonly object _Gate = new();
    private readonly ConditionalWeakTable<object, _IdentityHolder> _Identities = new();
    private readonly Dictionary<ObjectIdentity, WeakReference<object>> _Objects = [];
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
        _Logger = Configuration.LoggerFactory.CreateLogger<UIEngineHost>();
        _HOST_CREATED(_Logger, Configuration.DescriptorProviders.Count, null);
    }

    public UIEngineHostConfiguration Configuration { get; }

    public IReadOnlyList<IObjectDescriptorProvider> Providers => Configuration.DescriptorProviders;

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
            return InteractionResult.Success(handle);
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
            var result = hasProgrammaticExposure
                ? await _ProgrammaticExposureProvider
                    .DescribeAsync(this, instance, handle, cancellationToken)
                    .ConfigureAwait(false)
                : await provider!
                    .DescribeAsync(this, instance, handle, cancellationToken)
                    .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                _DISCOVERY_COMPLETED(_Logger, handle.Identity.RuntimeId, null);
            }
            else
            {
                _DISCOVERY_FAILED(_Logger, handle.Identity.RuntimeId, result.Error!.Code, null);
            }

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

    private static InteractionResult<T> _DisposedFailure<T>() => InteractionResult.Failure<T>(
        InteractionErrorCode.HOST_DISPOSED,
        "The UIEngine host has been disposed.");

    private sealed record _IdentityHolder(ObjectIdentity Identity);

    private sealed record _RootEntry(RegisteredRoot Registration, object Instance);
}
