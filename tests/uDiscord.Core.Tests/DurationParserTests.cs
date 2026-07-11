using System;
using UDiscord.Core.Utility;
using Xunit;

namespace UDiscord.Core.Tests
{
    public sealed class DurationParserTests
    {
        [Theory]
        [InlineData("30m", 1800)]
        [InlineData("12h", 43200)]
        [InlineData("7d", 604800)]
        [InlineData("2w", 1209600)]
        [InlineData("1h30m", 5400)]
        public void ParsesSupportedDurations(string input, int expectedSeconds)
        {
            var result = DurationParser.Parse(input, TimeSpan.FromDays(365), false);
            Assert.True(result.Success);
            Assert.Equal(expectedSeconds, result.Duration.TotalSeconds);
        }

        [Fact]
        public void SupportsPermanentWhenAllowed()
        {
            var result = DurationParser.Parse("permanent", TimeSpan.FromDays(365), true);
            Assert.True(result.Success);
            Assert.True(result.IsPermanent);
        }

        [Theory]
        [InlineData("")]
        [InlineData("4x")]
        [InlineData("abc")]
        [InlineData("0m")]
        public void RejectsInvalidDurations(string input)
        {
            var result = DurationParser.Parse(input, TimeSpan.FromDays(365), false);
            Assert.False(result.Success);
        }
    }
}
