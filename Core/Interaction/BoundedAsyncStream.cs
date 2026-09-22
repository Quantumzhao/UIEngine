using System.Threading.Channels;

namespace UIEngine.Core;

/// <summary>Small single-reader bounded stream shared by progress and observation.</summary>
internal sealed class BoundedAsyncStream<T>
{
    private readonly object _Gate = new();
    private readonly Queue<T> _Items = [];
    private readonly Channel<bool> _Signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private readonly int _Capacity;
    private readonly Func<long, T>? _CreateOverflow;
    private long _Dropped;
    private bool _SignalPending;
    private bool _Completed;
    private int _ReaderStarted;

    public BoundedAsyncStream(int capacity, Func<long, T>? createOverflow = null)
    {
        _Capacity = capacity;
        _CreateOverflow = createOverflow;
    }

    public void Publish(T item)
    {
        var release = false;
        lock (_Gate)
        {
            if (_Completed)
            {
                return;
            }

            if (_Items.Count == _Capacity)
            {
                _Items.Dequeue();
                _Dropped++;
            }

            _Items.Enqueue(item);
            if (!_SignalPending)
            {
                _SignalPending = true;
                release = true;
            }
        }

        if (release)
        {
            _Signal.Writer.TryWrite(true);
        }
    }

    public void Complete()
    {
        var release = false;
        lock (_Gate)
        {
            if (_Completed)
            {
                return;
            }

            _Completed = true;
            if (!_SignalPending)
            {
                _SignalPending = true;
                release = true;
            }
        }

        if (release)
        {
            _Signal.Writer.TryWrite(true);
        }
    }

    public async IAsyncEnumerable<T> ReadAllAsync()
    {
        if (Interlocked.Exchange(ref _ReaderStarted, 1) != 0)
        {
            throw new InvalidOperationException("A bounded stream supports one reader.");
        }

        while (true)
        {
            await _Signal.Reader.ReadAsync();

            T[] items;
            T? overflow = default;
            var hasOverflow = false;
            bool completed;
            lock (_Gate)
            {
                if (_Dropped > 0 && _CreateOverflow is not null)
                {
                    overflow = _CreateOverflow(_Dropped);
                    hasOverflow = true;
                }

                _Dropped = 0;
                items = _Items.ToArray();
                _Items.Clear();
                completed = _Completed;
                _SignalPending = false;
            }

            if (hasOverflow)
            {
                yield return overflow!;
            }

            foreach (var item in items)
            {
                yield return item;
            }

            if (completed)
            {
                yield break;
            }
        }
    }
}
