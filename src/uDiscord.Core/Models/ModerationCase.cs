using System;

namespace UDiscord.Core.Models
{
    public sealed class ModerationCase
    {
        public long CaseId { get; set; }
        public string OperationId { get; set; }
        public ModerationActionType Action { get; set; }
        public string ActorDiscordId { get; set; }
        public string ActorDisplayName { get; set; }
        public string TargetSteamId { get; set; }
        public string TargetDisplayName { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public bool Succeeded { get; set; }
        public string Result { get; set; }
        public string ServerName { get; set; }
        public string PluginVersion { get; set; }
    }
}
