using UDiscord.Core.Security;
using Xunit;

namespace UDiscord.Core.Tests
{
    public sealed class MessageSanitizerTests
    {
        [Fact]
        public void RemovesRichTextAndMassMentions()
        {
            string result = MessageSanitizer.FromDiscordToGame("<color=red>hello</color> @everyone", 200);
            Assert.DoesNotContain("<color", result);
            Assert.DoesNotContain("@everyone", result);
            Assert.Contains("hello", result);
        }

        [Fact]
        public void BoundsOutputLength()
        {
            string result = MessageSanitizer.FromGameToDiscord(new string('a', 100), 20);
            Assert.Equal(20, result.Length);
        }

        [Fact]
        public void ReplacesDiscordMentions()
        {
            string result = MessageSanitizer.FromDiscordToGame("hello <@123456789012345678>", 200);
            Assert.Contains("[mention]", result);
        }
    }
}
