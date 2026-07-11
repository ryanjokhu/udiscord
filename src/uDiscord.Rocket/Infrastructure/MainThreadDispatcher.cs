using System;
using System.Threading;
using System.Threading.Tasks;
using Rocket.Core.Utils;

namespace UDiscord.Rocket.Infrastructure
{
    public sealed class MainThreadDispatcher : IDisposable
    {
        private readonly TimeSpan _timeout;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private volatile bool _accepting = true;

        public MainThreadDispatcher(TimeSpan timeout)
        {
            _timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : timeout;
        }

        public void StopAccepting()
        {
            if (!_accepting) return;
            _accepting = false;
            try { _shutdown.Cancel(); } catch (ObjectDisposedException) { }
        }

        public Task RunAsync(Action action, CancellationToken cancellationToken)
        {
            return RunAsync<object>(() =>
            {
                action();
                return null;
            }, cancellationToken);
        }

        public async Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!_accepting) throw new InvalidOperationException("Plugin is stopping and no longer accepts game-thread work.");

            TaskCompletionSource<T> completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskDispatcher.QueueOnMainThread(() =>
            {
                if (!_accepting)
                {
                    completion.TrySetException(new InvalidOperationException("Plugin stopped before the game-thread action ran."));
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled();
                    return;
                }

                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });

            using (CancellationTokenSource waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token))
            {
                Task timeoutTask = Task.Delay(_timeout, waitCancellation.Token);
                Task completed = await Task.WhenAny(completion.Task, timeoutTask).ConfigureAwait(false);
                if (completed != completion.Task)
                {
                    if (_shutdown.IsCancellationRequested)
                        throw new InvalidOperationException("Plugin stopped while waiting for the Unturned main thread.");
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("Timed out waiting for the Unturned main thread.");
                }

                waitCancellation.Cancel();
                return await completion.Task.ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            StopAccepting();
            _shutdown.Dispose();
        }
    }
}
