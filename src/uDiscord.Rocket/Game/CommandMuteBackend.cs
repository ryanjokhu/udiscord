using System;
using System.Linq;
using System.Reflection;
using UDiscord.Core.Models;
using UDiscord.Core.Utility;
using UDiscord.Rocket.Configuration;

namespace UDiscord.Rocket.Game
{
    public sealed class CommandMuteBackend
    {
        private readonly PlayerResolver _players;

        public CommandMuteBackend(PlayerResolver players)
        {
            _players = players ?? throw new ArgumentNullException(nameof(players));
        }

        public ModerationExecution Mute(
            string target,
            string reason,
            TimeSpan? duration,
            MuteBackendSettings settings)
        {
            ModerationActionType action = duration.HasValue
                ? ModerationActionType.TemporaryMute
                : ModerationActionType.Mute;

            if (settings == null || !settings.UsesCommands)
                return ModerationExecution.Failed(action, "Command mute backend is not configured.");

            PlayerResolution resolution = _players.ResolveOnline(target);
            if (resolution.Status == PlayerResolutionStatus.Ambiguous)
                return ModerationExecution.Failed(action, "Target is ambiguous: " + string.Join(", ", resolution.AmbiguousMatches));

            string steamId;
            string displayName;
            if (resolution.Status == PlayerResolutionStatus.Found)
            {
                steamId = resolution.SteamIdText;
                displayName = resolution.DisplayName;
            }
            else
            {
                if (!settings.AllowOfflineTargets)
                    return ModerationExecution.Failed(action, "The configured mute command backend only allows online targets.");

                ulong raw;
                if (!ulong.TryParse((target ?? string.Empty).Trim(), out raw))
                    return ModerationExecution.Failed(action, "Offline mutes require a valid Steam64 ID.");

                steamId = raw.ToString();
                displayName = steamId;
            }

            string template = duration.HasValue ? settings.TemporaryMuteCommand : settings.MuteCommand;
            string command = Render(template, steamId, displayName, reason, duration);
            string error;
            if (!TryExecute(command, out error))
                return ModerationExecution.Failed(action, error);

            DateTime? expiresUtc = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : (DateTime?)null;
            return new ModerationExecution
            {
                Success = true,
                Action = action,
                TargetSteamId = steamId,
                TargetDisplayName = displayName,
                ExpiresUtc = expiresUtc,
                Message = duration.HasValue
                    ? "Dispatched configured temporary mute command for " + displayName + " (" + steamId + ") for " + DurationParser.Format(duration.Value) + "."
                    : "Dispatched configured mute command for " + displayName + " (" + steamId + ")."
            };
        }

        public ModerationExecution Unmute(string target, MuteBackendSettings settings)
        {
            if (settings == null || !settings.UsesCommands)
                return ModerationExecution.Failed(ModerationActionType.Unmute, "Command mute backend is not configured.");

            PlayerResolution resolution = _players.ResolveOnline(target);
            if (resolution.Status == PlayerResolutionStatus.Ambiguous)
                return ModerationExecution.Failed(ModerationActionType.Unmute, "Target is ambiguous: " + string.Join(", ", resolution.AmbiguousMatches));

            string steamId;
            string displayName;
            if (resolution.Status == PlayerResolutionStatus.Found)
            {
                steamId = resolution.SteamIdText;
                displayName = resolution.DisplayName;
            }
            else
            {
                if (!settings.AllowOfflineTargets)
                    return ModerationExecution.Failed(ModerationActionType.Unmute, "The configured unmute command backend only allows online targets.");

                ulong raw;
                if (!ulong.TryParse((target ?? string.Empty).Trim(), out raw))
                    return ModerationExecution.Failed(ModerationActionType.Unmute, "Unmute requires an online player or Steam64 ID.");

                steamId = raw.ToString();
                displayName = steamId;
            }

            string command = Render(settings.UnmuteCommand, steamId, displayName, string.Empty, null);
            string error;
            if (!TryExecute(command, out error))
                return ModerationExecution.Failed(ModerationActionType.Unmute, error);

            return new ModerationExecution
            {
                Success = true,
                Action = ModerationActionType.Unmute,
                TargetSteamId = steamId,
                TargetDisplayName = displayName,
                Message = "Dispatched configured unmute command for " + displayName + " (" + steamId + ")."
            };
        }

        private static string Render(string template, string steamId, string displayName, string reason, TimeSpan? duration)
        {
            string safeReason = SanitizeArgument(reason, 300);
            string safeName = SanitizeArgument(displayName, 64);
            string rendered = (template ?? string.Empty)
                .Replace("{steamid}", steamId ?? string.Empty)
                .Replace("{player}", safeName)
                .Replace("{reason}", safeReason)
                .Replace("{duration}", duration.HasValue ? DurationParser.Format(duration.Value) : string.Empty)
                .Trim();

            if (rendered.StartsWith("/", StringComparison.Ordinal)) rendered = rendered.Substring(1);
            return rendered;
        }

        private static string SanitizeArgument(string value, int maximumLength)
        {
            string text = (value ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace(";", ",")
                .Trim();

            while (text.Contains("  ")) text = text.Replace("  ", " ");
            return text.Length <= maximumLength ? text : text.Substring(0, maximumLength);
        }

        private static bool TryExecute(string command, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(command))
            {
                error = "Configured mute command rendered to an empty command.";
                return false;
            }

            try
            {
                Type rocketType = Type.GetType("Rocket.Core.R, Rocket.Core", false);
                Type consolePlayerType = Type.GetType("Rocket.API.ConsolePlayer, Rocket.API", false);
                if (rocketType == null || consolePlayerType == null)
                {
                    error = "Rocket command infrastructure could not be resolved at runtime.";
                    return false;
                }

                PropertyInfo commandsProperty = rocketType.GetProperty("Commands", BindingFlags.Public | BindingFlags.Static);
                object commands = commandsProperty == null ? null : commandsProperty.GetValue(null, null);
                if (commands == null)
                {
                    error = "Rocket command manager is unavailable.";
                    return false;
                }

                object consolePlayer = CreateConsolePlayer(consolePlayerType);
                if (consolePlayer == null)
                {
                    error = "Rocket console command caller could not be created.";
                    return false;
                }

                MethodInfo execute = commands.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "Execute", StringComparison.Ordinal)) return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 2 && parameters[1].ParameterType == typeof(string);
                    });

                if (execute == null)
                {
                    error = "Rocket command manager does not expose the expected Execute(caller, command) method.";
                    return false;
                }

                object result = execute.Invoke(commands, new[] { consolePlayer, (object)command });
                if (execute.ReturnType == typeof(bool) && result is bool && !(bool)result)
                {
                    error = "Rocket rejected the configured command. Confirm that the command name is registered and the template is correct.";
                    return false;
                }

                return true;
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                error = "Configured moderation command failed: " + inner.Message;
                return false;
            }
            catch (Exception exception)
            {
                error = "Unable to dispatch configured moderation command: " + exception.Message;
                return false;
            }
        }

        private static object CreateConsolePlayer(Type consolePlayerType)
        {
            PropertyInfo instance = consolePlayerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instance != null)
            {
                object existing = instance.GetValue(null, null);
                if (existing != null) return existing;
            }

            ConstructorInfo constructor = consolePlayerType.GetConstructor(Type.EmptyTypes);
            return constructor == null ? null : constructor.Invoke(null);
        }
    }
}
