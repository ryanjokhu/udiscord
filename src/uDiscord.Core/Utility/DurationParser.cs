using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UDiscord.Core.Models;

namespace UDiscord.Core.Utility
{
    public static class DurationParser
    {
        private static readonly Regex SegmentPattern = new Regex(
            @"(?<value>\d+)\s*(?<unit>[smhdw])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static DurationParseResult Parse(string input, TimeSpan maximum, bool allowPermanent)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return DurationParseResult.Invalid("Duration is required. Examples: 30m, 12h, 7d, 2w.");
            }

            string value = input.Trim().ToLowerInvariant();
            if (allowPermanent && (value == "permanent" || value == "perm" || value == "forever"))
            {
                return DurationParseResult.Permanent();
            }

            MatchCollection matches = SegmentPattern.Matches(value);
            if (matches.Count == 0)
            {
                return DurationParseResult.Invalid("Invalid duration. Use s, m, h, d, or w, for example 45m or 7d.");
            }

            int consumedCharacters = 0;
            long totalSeconds = 0;
            foreach (Match match in matches)
            {
                consumedCharacters += match.Length;
                long amount;
                if (!long.TryParse(match.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out amount))
                {
                    return DurationParseResult.Invalid("Duration contains an invalid number.");
                }

                long multiplier;
                switch (match.Groups["unit"].Value.ToLowerInvariant())
                {
                    case "s": multiplier = 1; break;
                    case "m": multiplier = 60; break;
                    case "h": multiplier = 60 * 60; break;
                    case "d": multiplier = 24 * 60 * 60; break;
                    case "w": multiplier = 7 * 24 * 60 * 60; break;
                    default: return DurationParseResult.Invalid("Unsupported duration unit.");
                }

                try
                {
                    checked
                    {
                        totalSeconds += amount * multiplier;
                    }
                }
                catch (OverflowException)
                {
                    return DurationParseResult.Invalid("Duration is too large.");
                }
            }

            string compact = Regex.Replace(value, @"\s+", string.Empty);
            string reconstructed = Regex.Replace(compact, @"\d+[smhdw]", string.Empty, RegexOptions.IgnoreCase);
            if (reconstructed.Length != 0 || consumedCharacters == 0)
            {
                return DurationParseResult.Invalid("Duration contains unsupported text.");
            }

            if (totalSeconds <= 0)
            {
                return DurationParseResult.Invalid("Duration must be greater than zero.");
            }

            TimeSpan duration = TimeSpan.FromSeconds(totalSeconds);
            if (maximum > TimeSpan.Zero && duration > maximum)
            {
                return DurationParseResult.Invalid("Duration exceeds the configured maximum of " + Format(maximum) + ".");
            }

            return DurationParseResult.Valid(duration);
        }

        public static string Format(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
            {
                int days = (int)Math.Floor(duration.TotalDays);
                int hours = duration.Hours;
                return hours > 0 ? days + "d " + hours + "h" : days + "d";
            }

            if (duration.TotalHours >= 1)
            {
                int hours = (int)Math.Floor(duration.TotalHours);
                int minutes = duration.Minutes;
                return minutes > 0 ? hours + "h " + minutes + "m" : hours + "h";
            }

            if (duration.TotalMinutes >= 1)
            {
                int minutes = (int)Math.Floor(duration.TotalMinutes);
                int seconds = duration.Seconds;
                return seconds > 0 ? minutes + "m " + seconds + "s" : minutes + "m";
            }

            return Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds)) + "s";
        }
    }
}
