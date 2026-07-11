using System;

namespace UDiscord.Core.Models
{
    public sealed class MuteRecord
    {
        public string SteamId { get; set; }
        public string LastKnownName { get; set; }
        public string Reason { get; set; }
        public string ActorDiscordId { get; set; }
        public string ActorDisplayName { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public string OperationId { get; set; }

        public bool IsPermanent => !ExpiresUtc.HasValue;

        public bool IsExpired(DateTime utcNow)
        {
            return ExpiresUtc.HasValue && ExpiresUtc.Value <= utcNow;
        }
    }
}
