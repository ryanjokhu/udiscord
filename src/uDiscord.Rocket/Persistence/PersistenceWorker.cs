using System;
using System.Threading;
using System.Threading.Tasks;
using UDiscord.Rocket.Infrastructure;

namespace UDiscord.Rocket.Persistence
{
    public sealed class PersistenceWorker : IDisposable
    {
        private readonly BoundedWorker<Action> _worker;
        private readonly CancellationToken _parentToken;

        public PersistenceWorker(int capacity, CancellationToken parentToken)
        {
            _parentToken = parentToken;
            _worker = new BoundedWorker<Action>("persistence worker", capacity, ExecuteAsync);
        }

        public void Start()
        {
            _worker.Start(_parentToken);
        }

        public bool TryEnqueue(Action action)
        {
            if (action == null) return false;
            bool accepted = _worker.TryEnqueue(action);
            if (!accepted) PluginLog.Error("Persistence queue is full; the operation was not queued.");
            return accepted;
        }

        public Task StopAsync(TimeSpan timeout)
        {
            return _worker.StopAsync(timeout, true);
        }

        private static Task ExecuteAsync(Action action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _worker.Dispose();
        }
    }
}
