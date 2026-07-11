using System;
using UDiscord.Core.Models;

namespace UDiscord.Rocket.Game
{
    public sealed class ModerationExecution
    {
        public bool Success { get; set; }
        public ModerationActionType Action { get; set; }
        public string TargetSteamId { get; set; }
        public string TargetDisplayName { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public string Message { get; set; }

        public static ModerationExecution Failed(ModerationActionType action, string message)
        {
            return new ModerationExecution { Success = false, Action = action, Message = message ?? "Action failed." };
        }
    }
}
