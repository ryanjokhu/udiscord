using System;
using UDiscord.Core.Security;
using Xunit;

namespace UDiscord.Core.Tests
{
    public sealed class SlidingWindowRateLimiterTests
    {
        [Fact]
        public void RejectsRequestsAfterLimit()
        {
            var limiter = new SlidingWindowRateLimiter(2, TimeSpan.FromSeconds(10));
            DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.True(limiter.TryAcquire("user", now, out _));
            Assert.True(limiter.TryAcquire("user", now.AddSeconds(1), out _));
            Assert.False(limiter.TryAcquire("user", now.AddSeconds(2), out TimeSpan retry));
            Assert.True(retry > TimeSpan.Zero);
            Assert.True(limiter.TryAcquire("user", now.AddSeconds(11), out _));
        }
    }
}
