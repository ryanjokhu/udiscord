using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UDiscord.Core.Models;
using UDiscord.Rocket.Infrastructure;

namespace UDiscord.Rocket.Persistence
{
    public sealed class CaseStore
    {
        private sealed class CaseState
        {
            public long NextCaseId { get; set; }
        }

        private readonly object _sync = new object();
        private readonly LinkedList<ModerationCase> _recentCases = new LinkedList<ModerationCase>();
        private readonly string _casesPath;
        private readonly string _statePath;
        private readonly int _maximumLoaded;
        private readonly PersistenceWorker _worker;
        private long _nextCaseId = 1;

        public CaseStore(string casesPath, string statePath, int maximumLoaded, PersistenceWorker worker)
        {
            _casesPath = casesPath ?? throw new ArgumentNullException(nameof(casesPath));
            _statePath = statePath ?? throw new ArgumentNullException(nameof(statePath));
            _maximumLoaded = Math.Max(100, maximumLoaded);
            _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        }

        public void Load()
        {
            lock (_sync)
            {
                _recentCases.Clear();
                long highestCaseId = 0;
                if (File.Exists(_casesPath))
                {
                    try
                    {
                        foreach (string line in File.ReadLines(_casesPath, Encoding.UTF8))
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            try
                            {
                                ModerationCase item = JsonConvert.DeserializeObject<ModerationCase>(line);
                                if (item == null || item.CaseId <= 0) continue;
                                highestCaseId = Math.Max(highestCaseId, item.CaseId);
                                _recentCases.AddLast(item);
                                while (_recentCases.Count > _maximumLoaded) _recentCases.RemoveFirst();
                            }
                            catch (JsonException exception)
                            {
                                PluginLog.Warn("Skipped malformed moderation case line: " + exception.Message);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        PluginLog.Exception(exception, "Unable to load moderation cases. Existing journal remains untouched.");
                    }
                }

                long stateNext = ReadStateNextId();
                _nextCaseId = Math.Max(highestCaseId + 1, Math.Max(1, stateNext));
            }
        }

        public ModerationCase Record(ModerationCase item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            string serialized;
            long next;
            lock (_sync)
            {
                item.CaseId = _nextCaseId++;
                if (string.IsNullOrWhiteSpace(item.OperationId)) item.OperationId = "case_" + item.CaseId.ToString(CultureInfo.InvariantCulture);
                _recentCases.AddLast(Clone(item));
                while (_recentCases.Count > _maximumLoaded) _recentCases.RemoveFirst();
                serialized = JsonConvert.SerializeObject(item, Formatting.None);
                next = _nextCaseId;
            }

            bool queued = _worker.TryEnqueue(() => AppendCase(serialized, next));
            if (!queued)
            {
                PluginLog.Error("Moderation case #" + item.CaseId + " is only in memory because the persistence queue is full.");
            }

            return Clone(item);
        }

        public ModerationCase Get(long caseId)
        {
            lock (_sync)
            {
                ModerationCase item = _recentCases.LastOrDefault(candidate => candidate.CaseId == caseId);
                return item == null ? null : Clone(item);
            }
        }

        public IReadOnlyList<ModerationCase> GetHistory(string steamId, int maximum)
        {
            if (string.IsNullOrWhiteSpace(steamId)) return new List<ModerationCase>();
            int take = Math.Max(1, Math.Min(25, maximum));
            lock (_sync)
            {
                return _recentCases
                    .Reverse()
                    .Where(item => string.Equals(item.TargetSteamId, steamId, StringComparison.Ordinal))
                    .Take(take)
                    .Select(Clone)
                    .ToList();
            }
        }

        public long PeekNextCaseId()
        {
            lock (_sync) return _nextCaseId;
        }

        private void AppendCase(string serialized, long nextCaseId)
        {
            string directory = Path.GetDirectoryName(_casesPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(_casesPath, serialized + Environment.NewLine, new UTF8Encoding(false));
            AtomicFile.WriteAllText(_statePath, JsonConvert.SerializeObject(new CaseState { NextCaseId = nextCaseId }, Formatting.Indented));
        }

        private long ReadStateNextId()
        {
            string json = AtomicFile.ReadAllTextOrQuarantine(_statePath, PluginLog.Warn);
            if (string.IsNullOrWhiteSpace(json)) return 1;
            try
            {
                CaseState state = JsonConvert.DeserializeObject<CaseState>(json);
                return state == null ? 1 : Math.Max(1, state.NextCaseId);
            }
            catch (Exception exception)
            {
                PluginLog.Warn("Unable to parse case state: " + exception.Message);
                return 1;
            }
        }

        private static ModerationCase Clone(ModerationCase item)
        {
            return new ModerationCase
            {
                CaseId = item.CaseId,
                OperationId = item.OperationId,
                Action = item.Action,
                ActorDiscordId = item.ActorDiscordId,
                ActorDisplayName = item.ActorDisplayName,
                TargetSteamId = item.TargetSteamId,
                TargetDisplayName = item.TargetDisplayName,
                Reason = item.Reason,
                CreatedUtc = item.CreatedUtc,
                ExpiresUtc = item.ExpiresUtc,
                Succeeded = item.Succeeded,
                Result = item.Result,
                ServerName = item.ServerName,
                PluginVersion = item.PluginVersion
            };
        }
    }
}
