using System;

namespace UDiscord.Core.Models
{
    public sealed class DurationParseResult
    {
        public bool Success { get; private set; }
        public bool IsPermanent { get; private set; }
        public TimeSpan Duration { get; private set; }
        public string Error { get; private set; }

        public static DurationParseResult Permanent()
        {
            return new DurationParseResult { Success = true, IsPermanent = true, Duration = TimeSpan.Zero };
        }

        public static DurationParseResult Valid(TimeSpan duration)
        {
            return new DurationParseResult { Success = true, Duration = duration };
        }

        public static DurationParseResult Invalid(string error)
        {
            return new DurationParseResult { Success = false, Error = error ?? "Invalid duration." };
        }
    }
}
