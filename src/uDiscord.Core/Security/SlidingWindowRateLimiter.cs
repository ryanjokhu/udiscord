using System;
using System.Collections.Generic;

namespace UDiscord.Core.Security
{
    public sealed class SlidingWindowRateLimiter
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, Queue<DateTime>> _events = new Dictionary<string, Queue<DateTime>>(StringComparer.Ordinal);
        private readonly int _limit;
        private readonly TimeSpan _window;
        private DateTime _lastCleanupUtc;

        public SlidingWindowRateLimiter(int limit, TimeSpan window)
        {
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
            if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
            _limit = limit;
            _window = window;
            _lastCleanupUtc = DateTime.UtcNow;
        }

        public bool TryAcquire(string key, DateTime utcNow, out TimeSpan retryAfter)
        {
            retryAfter = TimeSpan.Zero;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            lock (_sync)
            {
                Queue<DateTime> queue;
                if (!_events.TryGetValue(key, out queue))
                {
                    queue = new Queue<DateTime>();
                    _events[key] = queue;
                }

                DateTime cutoff = utcNow - _window;
                while (queue.Count > 0 && queue.Peek() <= cutoff)
                {
                    queue.Dequeue();
                }

                if (queue.Count >= _limit)
                {
                    retryAfter = queue.Peek() + _window - utcNow;
                    if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
                    return false;
                }

                queue.Enqueue(utcNow);
                CleanupIfNeeded(utcNow);
                return true;
            }
        }

        public void Clear(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_sync)
            {
                _events.Remove(key);
            }
        }

        private void CleanupIfNeeded(DateTime utcNow)
        {
            if (utcNow - _lastCleanupUtc < _window)
            {
                return;
            }

            _lastCleanupUtc = utcNow;
            DateTime cutoff = utcNow - _window;
            List<string> emptyKeys = new List<string>();
            foreach (KeyValuePair<string, Queue<DateTime>> pair in _events)
            {
                while (pair.Value.Count > 0 && pair.Value.Peek() <= cutoff)
                {
                    pair.Value.Dequeue();
                }

                if (pair.Value.Count == 0)
                {
                    emptyKeys.Add(pair.Key);
                }
            }

            foreach (string key in emptyKeys)
            {
                _events.Remove(key);
            }
        }
    }
}
