using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UDiscord.Core.Models;
using UDiscord.Rocket.Infrastructure;

namespace UDiscord.Rocket.Persistence
{
    public sealed class MuteStore
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, MuteRecord> _records = new Dictionary<string, MuteRecord>(StringComparer.Ordinal);
        private readonly string _path;
        private readonly PersistenceWorker _worker;
        private readonly Formatting _formatting;

        public MuteStore(string path, PersistenceWorker worker, bool writeIndented)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _worker = worker ?? throw new ArgumentNullException(nameof(worker));
            _formatting = writeIndented ? Formatting.Indented : Formatting.None;
        }

        public int Count
        {
            get
            {
                lock (_sync) return _records.Count;
            }
        }

        public void Load()
        {
            lock (_sync)
            {
                _records.Clear();
                string json = AtomicFile.ReadAllTextOrQuarantine(_path, PluginLog.Warn);
                if (string.IsNullOrWhiteSpace(json)) return;

                try
                {
                    List<MuteRecord> records = JsonConvert.DeserializeObject<List<MuteRecord>>(json) ?? new List<MuteRecord>();
                    DateTime now = DateTime.UtcNow;
                    foreach (MuteRecord record in records)
                    {
                        if (record == null || string.IsNullOrWhiteSpace(record.SteamId) || record.IsExpired(now)) continue;
                        _records[record.SteamId] = record;
                    }
                }
                catch (Exception exception)
                {
                    string quarantine = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    try { File.Copy(_path, quarantine, true); } catch { }
                    PluginLog.Exception(exception, "Mute data could not be parsed. Existing file was preserved and no mutes were loaded.");
                }
            }
        }

        public bool TryGetActive(string steamId, DateTime utcNow, out MuteRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(steamId)) return false;

            bool removedExpired = false;
            lock (_sync)
            {
                if (!_records.TryGetValue(steamId, out record)) return false;
                if (!record.IsExpired(utcNow)) return true;
                _records.Remove(steamId);
                record = null;
                removedExpired = true;
            }

            if (removedExpired) QueueSave();
            return false;
        }

        public void Upsert(MuteRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.SteamId)) throw new ArgumentException("Mute record SteamId is required.", nameof(record));
            lock (_sync)
            {
                _records[record.SteamId] = record;
            }
            QueueSave();
        }

        public bool Remove(string steamId, out MuteRecord removed)
        {
            removed = null;
            if (string.IsNullOrWhiteSpace(steamId)) return false;
            bool result;
            lock (_sync)
            {
                result = _records.TryGetValue(steamId, out removed) && _records.Remove(steamId);
            }
            if (result) QueueSave();
            return result;
        }

        public int PurgeExpired(DateTime utcNow)
        {
            int removed;
            lock (_sync)
            {
                List<string> expired = _records.Where(pair => pair.Value == null || pair.Value.IsExpired(utcNow)).Select(pair => pair.Key).ToList();
                foreach (string key in expired) _records.Remove(key);
                removed = expired.Count;
            }
            if (removed > 0) QueueSave();
            return removed;
        }

        public IReadOnlyList<MuteRecord> Snapshot()
        {
            lock (_sync)
            {
                return _records.Values.Select(Clone).ToList();
            }
        }

        public void SaveNow()
        {
            List<MuteRecord> snapshot;
            lock (_sync)
            {
                snapshot = _records.Values.Select(Clone).OrderBy(record => record.SteamId, StringComparer.Ordinal).ToList();
            }

            string json = JsonConvert.SerializeObject(snapshot, _formatting);
            AtomicFile.WriteAllText(_path, json);
        }

        private void QueueSave()
        {
            if (!_worker.TryEnqueue(SaveNow))
            {
                PluginLog.Error("Unable to queue mute persistence. Current in-memory moderation remains active, but restart durability is at risk.");
            }
        }

        private static MuteRecord Clone(MuteRecord record)
        {
            return new MuteRecord
            {
                SteamId = record.SteamId,
                LastKnownName = record.LastKnownName,
                Reason = record.Reason,
                ActorDiscordId = record.ActorDiscordId,
                ActorDisplayName = record.ActorDisplayName,
                CreatedUtc = record.CreatedUtc,
                ExpiresUtc = record.ExpiresUtc,
                OperationId = record.OperationId
            };
        }
    }
}
