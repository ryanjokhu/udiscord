using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace UDiscord.Rocket.Infrastructure
{
    public sealed class BoundedWorker<T> : IDisposable
    {
        private readonly BlockingCollection<T> _queue;
        private readonly Func<T, CancellationToken, Task> _handler;
        private readonly string _name;
        private CancellationTokenSource _cancellation;
        private Task _worker;
        private int _started;

        public BoundedWorker(string name, int capacity, Func<T, CancellationToken, Task> handler)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _name = string.IsNullOrWhiteSpace(name) ? "worker" : name;
            _queue = new BlockingCollection<T>(new ConcurrentQueue<T>(), capacity);
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public int Count => _queue.Count;

        public void Start(CancellationToken parentToken)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            _worker = Task.Run(() => WorkLoopAsync(_cancellation.Token), _cancellation.Token);
        }

        public bool TryEnqueue(T item)
        {
            if (_queue.IsAddingCompleted) return false;
            try
            {
                return _queue.TryAdd(item);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public async Task StopAsync(TimeSpan timeout, bool drain)
        {
            _queue.CompleteAdding();
            if (!drain) _cancellation?.Cancel();
            if (_worker == null) return;

            Task completed = await Task.WhenAny(_worker, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != _worker)
            {
                PluginLog.Warn(_name + " did not stop within " + timeout.TotalSeconds + "s; cancelling remaining work.");
                _cancellation?.Cancel();
                completed = await Task.WhenAny(_worker, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                if (completed != _worker)
                {
                    PluginLog.Warn(_name + " is still stopping after cancellation; shutdown will continue without an unbounded wait.");
                    return;
                }
            }

            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task WorkLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                foreach (T item in _queue.GetConsumingEnumerable(cancellationToken))
                {
                    try
                    {
                        await _handler(item, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        PluginLog.Exception(exception, _name + " failed to process an item.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Dispose()
        {
            _queue.Dispose();
            _cancellation?.Dispose();
        }
    }
}
