using System.Collections.Specialized;
using System.ComponentModel;

namespace UIEngine.Core;

/// <summary>Routes domain notifications into refresh requests for this host's live nodes.</summary>
internal sealed class NodeRefreshRouter : IDisposable
{
    private readonly Dictionary<Guid, _PropertyRoute> _PropertyRoutes = [];
    private readonly List<_CollectionRoute> _CollectionRoutes = [];
    private readonly List<_ProgrammaticRoute> _ProgrammaticRoutes = [];

    public void RegisterReflectedMember(
        Guid owner,
        object instance,
        BaseNode node,
        string memberName)
    {
        if (instance is not INotifyPropertyChanged source)
        {
            return;
        }

        if (!_PropertyRoutes.TryGetValue(owner, out var route))
        {
            route = new _PropertyRoute(
                source,
                propertyName => _RefreshCollections(owner, propertyName));
            _PropertyRoutes.Add(owner, route);
        }

        route.Add(node, memberName);
    }

    public void RegisterCollection(
        Guid owner,
        object instance,
        BaseNode node,
        string memberName,
        Func<object, object?> read)
    {
        var route = new _CollectionRoute(owner, instance, node, memberName, read);
        _CollectionRoutes.Add(route);
        route.Refresh();
    }

    public void RegisterProgrammatic(
        object instance,
        BaseNode node,
        ValueExposure exposure)
    {
        var source = exposure.GetRefreshSource(instance);
        if (source is null)
        {
            return;
        }

        var route = new _ProgrammaticRoute(
            source,
            node,
            exposure.RefreshPropertyName);
        _ProgrammaticRoutes.Add(route);
    }

    public void Dispose()
    {
        foreach (var route in _PropertyRoutes.Values)
        {
            route.Dispose();
        }

        foreach (var route in _CollectionRoutes)
        {
            route.Dispose();
        }

        foreach (var route in _ProgrammaticRoutes)
        {
            route.Dispose();
        }

        _PropertyRoutes.Clear();
        _CollectionRoutes.Clear();
        _ProgrammaticRoutes.Clear();
    }

    private void _RefreshCollections(Guid owner, string? propertyName)
    {
        foreach (var route in _CollectionRoutes)
        {
            if (route.Owner == owner && _Matches(route.MemberName, propertyName))
            {
                route.Refresh();
            }
        }
    }

    private static bool _Matches(string memberName, string? propertyName) =>
        string.IsNullOrEmpty(propertyName) ||
        StringComparer.Ordinal.Equals(memberName, propertyName);

    private sealed class _PropertyRoute : IDisposable
    {
        private readonly WeakReference<INotifyPropertyChanged> _Source;
        private readonly PropertyChangedEventHandler _Handler;
        private readonly Action<string?> _AfterRefreshRequest;
        private readonly List<_MemberRegistration> _Registrations = [];

        public _PropertyRoute(
            INotifyPropertyChanged source,
            Action<string?> afterRefreshRequest)
        {
            _Source = new WeakReference<INotifyPropertyChanged>(source);
            _AfterRefreshRequest = afterRefreshRequest;
            _Handler = _OnPropertyChanged;
            source.PropertyChanged += _Handler;
        }

        public void Add(BaseNode node, string memberName) =>
            _Registrations.Add(new _MemberRegistration(node, memberName));

        public void Dispose()
        {
            if (_Source.TryGetTarget(out var source))
            {
                source.PropertyChanged -= _Handler;
            }

            _Registrations.Clear();
        }

        private void _OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
        {
            for (var index = _Registrations.Count - 1; index >= 0; index--)
            {
                var registration = _Registrations[index];
                if (!registration.Node.TryGetTarget(out var node))
                {
                    _Registrations.RemoveAt(index);
                    continue;
                }

                if (_Matches(registration.MemberName, eventArgs.PropertyName))
                {
                    node.RequestRefresh(NodeRefreshKind.PROPERTY_CHANGED);
                }
            }

            _AfterRefreshRequest(eventArgs.PropertyName);
        }
    }

    private sealed record _MemberRegistration(
        WeakReference<BaseNode> Node,
        string MemberName)
    {
        public _MemberRegistration(BaseNode node, string memberName)
            : this(new WeakReference<BaseNode>(node), memberName)
        {
        }
    }

    private sealed class _CollectionRoute : IDisposable
    {
        private readonly WeakReference<object> _Owner;
        private readonly WeakReference<BaseNode> _Node;
        private readonly Func<object, object?> _Read;
        private readonly NotifyCollectionChangedEventHandler _Handler;
        private WeakReference<INotifyCollectionChanged>? _Source;

        public _CollectionRoute(
            Guid ownerHandle,
            object instance,
            BaseNode node,
            string memberName,
            Func<object, object?> read)
        {
            Owner = ownerHandle;
            _Owner = new WeakReference<object>(instance);
            _Node = new WeakReference<BaseNode>(node);
            MemberName = memberName;
            _Read = read;
            _Handler = _OnCollectionChanged;
        }

        public Guid Owner { get; }

        public string MemberName { get; }

        public void Refresh()
        {
            _Detach();
            if (!_Owner.TryGetTarget(out var owner))
            {
                return;
            }

            INotifyCollectionChanged? source;
            try
            {
                source = _Read(owner) as INotifyCollectionChanged;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or System.Reflection.TargetInvocationException)
            {
                return;
            }

            if (source is null)
            {
                return;
            }

            _Source = new WeakReference<INotifyCollectionChanged>(source);
            source.CollectionChanged += _Handler;
        }

        public void Dispose() => _Detach();

        private void _OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
        {
            if (_Node.TryGetTarget(out var node))
            {
                node.RequestRefresh(NodeRefreshKind.COLLECTION_CHANGED);
            }
            else
            {
                _Detach();
            }
        }

        private void _Detach()
        {
            if (_Source is not null && _Source.TryGetTarget(out var source))
            {
                source.CollectionChanged -= _Handler;
            }

            _Source = null;
        }
    }

    private sealed class _ProgrammaticRoute : IDisposable
    {
        private readonly WeakReference<INotifyPropertyChanged> _Source;
        private readonly WeakReference<BaseNode> _Node;
        private readonly string? _PropertyName;
        private readonly PropertyChangedEventHandler _Handler;

        public _ProgrammaticRoute(
            INotifyPropertyChanged source,
            BaseNode node,
            string? propertyName)
        {
            _Source = new WeakReference<INotifyPropertyChanged>(source);
            _Node = new WeakReference<BaseNode>(node);
            _PropertyName = propertyName;
            _Handler = _OnPropertyChanged;
            source.PropertyChanged += _Handler;
        }

        public void Dispose()
        {
            if (_Source.TryGetTarget(out var source))
            {
                source.PropertyChanged -= _Handler;
            }
        }

        private void _OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
        {
            if (!_Node.TryGetTarget(out var node))
            {
                Dispose();
                return;
            }

            if (_PropertyName is null || _Matches(_PropertyName, eventArgs.PropertyName))
            {
                node.RequestRefresh(NodeRefreshKind.PROPERTY_CHANGED);
            }
        }
    }
}
