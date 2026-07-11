using System;
using Rocket.Core.Logging;

namespace UDiscord.Rocket.Infrastructure
{
    internal static class PluginLog
    {
        public static bool DebugEnabled { get; set; }
        public static string SensitiveToken { private get; set; }

        public static void Info(string message)
        {
            Logger.Log("[uDiscord] " + Sanitize(message));
        }

        public static void Warn(string message)
        {
            Logger.LogWarning("[uDiscord] " + Sanitize(message));
        }

        public static void Error(string message)
        {
            Logger.LogError("[uDiscord] " + Sanitize(message));
        }

        public static void Debug(string message)
        {
            if (DebugEnabled) Logger.Log("[uDiscord:debug] " + Sanitize(message));
        }

        public static void Exception(Exception exception, string context)
        {
            if (exception == null)
            {
                Error(context);
                return;
            }

            Logger.LogException(exception, "[uDiscord] " + Sanitize(context));
        }

        private static string Sanitize(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            string token = SensitiveToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                message = message.Replace(token, "[REDACTED]");
            }

            return message;
        }
    }
}
