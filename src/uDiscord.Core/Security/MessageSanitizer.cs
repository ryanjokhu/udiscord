using System;
using System.Text;
using System.Text.RegularExpressions;

namespace UDiscord.Core.Security
{
    public static class MessageSanitizer
    {
        private static readonly Regex HtmlLikeTags = new Regex(@"<[^>\r\n]{1,200}>", RegexOptions.Compiled);
        private static readonly Regex UnturnedTags = new Regex(@"\{/?(?:color(?:=[^}]{0,32})?|b|i|size(?:=[^}]{0,16})?)\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DiscordCustomEmoji = new Regex(@"<a?:[A-Za-z0-9_]{1,32}:\d{5,25}>", RegexOptions.Compiled);
        private static readonly Regex DiscordMention = new Regex(@"<@!?\d{5,25}>|<@&\d{5,25}>|<#\d{5,25}>", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        public static string FromDiscordToGame(string input, int maximumLength)
        {
            string text = Normalize(input);
            text = DiscordCustomEmoji.Replace(text, "[emoji]");
            text = DiscordMention.Replace(text, "[mention]");
            text = HtmlLikeTags.Replace(text, string.Empty);
            text = UnturnedTags.Replace(text, string.Empty);
            text = StripMarkdownControl(text);
            text = NeutralizeMassMentions(text);
            return Truncate(text, maximumLength);
        }

        public static string FromGameToDiscord(string input, int maximumLength)
        {
            string text = Normalize(input);
            text = HtmlLikeTags.Replace(text, string.Empty);
            text = UnturnedTags.Replace(text, string.Empty);
            text = NeutralizeMassMentions(text);
            return Truncate(text, maximumLength);
        }

        public static string SafeDisplayName(string input, int maximumLength)
        {
            string text = Normalize(input);
            text = HtmlLikeTags.Replace(text, string.Empty);
            text = UnturnedTags.Replace(text, string.Empty);
            text = DiscordMention.Replace(text, string.Empty);
            text = NeutralizeMassMentions(text);
            return Truncate(text, maximumLength);
        }

        public static string NeutralizeMassMentions(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            return input
                .Replace("@everyone", "[everyone]")
                .Replace("@here", "[here]");
        }

        public static string Truncate(string input, int maximumLength)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            if (maximumLength <= 0 || input.Length <= maximumLength)
            {
                return input;
            }

            if (maximumLength == 1)
            {
                return "…";
            }

            return input.Substring(0, maximumLength - 1).TrimEnd() + "…";
        }

        private static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(input.Length);
            foreach (char character in input)
            {
                if (character == '\r' || character == '\n' || character == '\t')
                {
                    builder.Append(' ');
                }
                else if (!char.IsControl(character))
                {
                    builder.Append(character);
                }
            }

            return Whitespace.Replace(builder.ToString(), " ").Trim();
        }

        private static string StripMarkdownControl(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(input.Length);
            foreach (char character in input)
            {
                switch (character)
                {
                    case '`':
                    case '*':
                    case '_':
                    case '~':
                    case '|':
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
