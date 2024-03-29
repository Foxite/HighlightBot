using System.Text;
using System.Text.RegularExpressions;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HighlightBot;

public static class Util {
	// TODO move to Foxite.Common
	// TODO unit testing (in Foxite.Common)
	public static string JoinOxfordComma(ICollection<string> strings) {
		var ret = new StringBuilder();
		int i = 0;
		foreach (string str in strings) {
			if (i > 0) {
				if (strings.Count != 2) {
					ret.Append(',');
				}

				ret.Append(' ');

				if (i == strings.Count - 1) {
					ret.Append("and ");
				}
			}

			ret.Append(str);

			i++;
		}

		return ret.ToString();
	}

	// TODO move to Foxite.Common
	public static string Ellipsis(this string str, int maxLength, string ellipsis = "…") {
		if (str.Length > maxLength) {
			return str[..(maxLength - ellipsis.Length)] + ellipsis;
		} else {
			return str;
		}
	}

	// https://stackoverflow.com/a/1775017
	public static bool IsValidRegex(string pattern) {
		if (string.IsNullOrWhiteSpace(pattern)) {
			return false;
		}

		try {
			_ = Regex.IsMatch("", pattern);
		} catch (ArgumentException) {
			return false;
		}

		return true;
	}

	public static Task CreateResponseAsync(this InteractionContext context, Action<DiscordInteractionResponseBuilder> action) {
		var dmb = new DiscordInteractionResponseBuilder();
		action(dmb);
		return context.CreateResponseAsync(dmb);
	}
}
