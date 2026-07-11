using System;

namespace UDiscord.Core.Utility
{
    public static class OperationIds
    {
        public static string New(string prefix)
        {
            string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "op" : prefix.Trim().ToLowerInvariant();
            return safePrefix + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 10);
        }
    }
}
