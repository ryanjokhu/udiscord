namespace UDiscord.Core.Models
{
    public enum BotConnectionState
    {
        Disabled,
        Starting,
        Connecting,
        Online,
        Degraded,
        Reconnecting,
        Stopping,
        Stopped
    }
}
