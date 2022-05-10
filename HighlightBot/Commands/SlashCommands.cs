using System.Text.RegularExpressions;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.EntityFrameworkCore;

namespace HighlightBot;

[SlashModuleLifespan(SlashModuleLifespan.Transient)]
public class SlashCommandModule : ApplicationCommandModule {
	public HighlightDbContext DbContext { get; set; }

	protected Task<HighlightUser?> GetUserAsync(InteractionContext context) {
		return DbContext.Users.Include(user => user.Terms).Include(user => user.IgnoredChannels).FirstOrDefaultAsync(user => user.DiscordGuildId == context.Guild.Id && user.DiscordUserId == context.User.Id);
	}

	/// <summary>
	/// Does not save changes, which you might not want to do either.
	/// </summary>
	protected async Task<HighlightUser> GetOrCreateUserAsync(InteractionContext context) {
		HighlightUser? user = await GetUserAsync(context);
		if (user == null) {
			user = new HighlightUser() {
				DiscordGuildId = context.Guild.Id,
				DiscordUserId = context.User.Id,
				LastActivity = DateTime.UtcNow,
				Terms = new List<HighlightTerm>(),
				IgnoredChannels = new List<HighlightUserIgnoredChannel>()
			};
			DbContext.Users.Add(user);
		}

		return user;
	}

	protected static void AddEmbedOfTrackedTerms(HighlightUser user, DiscordMessageBuilder dmb) {
		if (user.Terms.Count > 0) {
			DiscordEmbedBuilder embed = new DiscordEmbedBuilder()
				.WithTitle("You're currently tracking the following terms")
				.WithColor(DiscordColor.Yellow);

			List<HighlightTerm> words = user.Terms.Where(term => !term.Display.StartsWith('`')).ToList();
			List<HighlightTerm> regexes = user.Terms.Where(term => term.Display.StartsWith('`')).ToList();
			if (words.Count > 0) {
				embed.AddField("Words", string.Join("\n", words.Select(term => term.Display)));
			}

			if (regexes.Count > 0) {
				embed.AddField("Regexes", string.Join("\n", regexes.Select(term => term.Display)));
			}

			if (user.IgnoredChannels.Count > 0) {
				embed.AddField("Ignored Channels", string.Join("\n", user.IgnoredChannels.Select(huic => "<#" + huic.ChannelId + ">")));
			}

			embed.AddField("Ignore bots", user.IgnoreBots ? "Yes" : "No");
			embed.AddField("Ignore NSFW", user.IgnoreNsfw ? "Yes" : "No");

			embed
				.AddField("Highlight Delay", user.HighlightDelay.TotalHours >= 1 ? user.HighlightDelay.ToString(@"h\:mm\:ss") : user.HighlightDelay.ToString(@"m\:ss"))
				.AddField("To add a new regex:", "Surround a term with `/slashes/`");

			dmb
				.WithEmbed(embed)
				.WithAllowedMentions(Mentions.None);
		}
	}
	
	[SlashCommand("show", "Show your current highlights.")]
	public async Task GetAllHighlights(InteractionContext context) {
		HighlightUser? user = await GetUserAsync(context);
		if (user == null || user.Terms.Count == 0) {
			await context.RespondAsync("You're not tracking any words.");
		} else {
			await context.RespondAsync(dmb => AddEmbedOfTrackedTerms(user, dmb));
		}
	}

	[SlashCommand("clear", "Remove all your highlights.")]
	public async Task ClearHighlights(InteractionContext context) {
		HighlightUser? user = await GetUserAsync(context);
		if (user == null || user.Terms.Count == 0) {
			await context.RespondAsync("You're not tracking any words.");
		} else {
			user.Terms.Clear();
			await DbContext.SaveChangesAsync();
			await context.RespondAsync("Deleted all your highlights.");
		}
	}

	[SlashCommand("add", "Add a highlight or regex.")]
	public async Task AddHighlight(InteractionContext context, string terms) {
		HighlightUser user = await GetOrCreateUserAsync(context);

		string[] lines = terms.Split("\n");
		int added = 0;
		foreach (string line in lines) {
			string pattern;
			string display;
			if (line.StartsWith('/') && line.EndsWith('/')) {
				pattern = line[1..^1];

				if (!Util.IsValidRegex(pattern)) {
					await context.RespondAsync("Invalid regex 🙁");
					return;
				}
				
				display = $"`{line}`";
			} else {
				pattern = $@"\b{Regex.Escape(line.ToLower())}\b";

				if (!Util.IsValidRegex(pattern)) {
					throw new Exception("Escaped pattern is not valid: " + pattern);
				}
				
				display = line;
			}
			if (!user.Terms.Any(term => term.Regex == pattern)) {
				added++;
				user.Terms.Add(new HighlightTerm() {
					Display = display,
					Regex = pattern
				});
			}
		}

		await DbContext.SaveChangesAsync();

		string message = $"Added {added} highlight{(added == 1 ? "" : "s")}";
		if (added != lines.Length) {
			message += "\nNote: some words were already being highlighted.";
		}

		await context.RespondAsync(dmb => {
			dmb.WithContent(message);
			AddEmbedOfTrackedTerms(user, dmb);
		});
	}

	[SlashCommand("remove", "Remove a highlight.")]
	public async Task RemoveHighlights(InteractionContext context, string highlight) {
		HighlightUser? user = await GetUserAsync(context);
		if (user == null || user.Terms.Count == 0) {
			await context.CreateResponseAsync("You're not tracking any words.");
		} else {
			if (highlight.StartsWith('/') && highlight.EndsWith('/')) {
				highlight = $"`{highlight}`";
			}
			HighlightTerm? term = user.Terms.FirstOrDefault(term => term.Display == highlight);
			string content;
			if (term != null) {
				user.Terms.Remove(term);
				await DbContext.SaveChangesAsync();
				content = "Deleted the highlight: " + highlight;
			} else {
				content = $"You are not tracking {highlight}.";
			}
			await context.RespondAsync(dmb => {
				dmb
					.WithContent(content)
					.WithAllowedMentions(Mentions.None);
				AddEmbedOfTrackedTerms(user, dmb);
			});
		}
	}

	[SlashCommand("delay", "Set the minimum amount of time you have to be inactive, before you get notified.")]
	public async Task SetHighlightDelay(InteractionContext context, int minutes) {
		HighlightUser user = await GetOrCreateUserAsync(context);
		user.HighlightDelay = TimeSpan.FromMinutes(minutes);
		await DbContext.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.RespondAsync($"You're not tracking any words yet, but when you add them, I will notify you if anyone says them and you've been inactive for {minutes} minute{(minutes == 1 ? "" : "s")}.");
		} else {
			await context.RespondAsync($"I will notify you if anyone says one of your highlights and you've been inactive for {minutes} minute{(minutes == 1 ? "" : "s")}.");
		}
	}
}

[SlashCommandGroup("ignore", "Ignore something.")]
public class SlashIgnoreModule : SlashCommandModule {
	[SlashCommand("bots", "Ignore bots.")]
	public async Task IgnoreBots(InteractionContext context) {
		HighlightUser user = await GetOrCreateUserAsync(context);

		user.IgnoreBots = !user.IgnoreBots;

		await DbContext.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(user.IgnoreBots ? "not notify you" : "now notify you again")} if a bot says them.");
		} else {
			await context.RespondAsync($"I will {(user.IgnoreBots ? "not notify you anymore" : "now notify you again")} if a bot says one of your highlights.");
		}
	}

	[SlashCommand("nsfw", "Ignore NSFW channels.")]
	public async Task IgnoreNsfw(InteractionContext context) {
		HighlightUser user = await GetOrCreateUserAsync(context);

		user.IgnoreNsfw = !user.IgnoreNsfw;

		await DbContext.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(user.IgnoreBots ? "not notify you" : "now notify you again")} anyone says them in an NSFW channel.");
		} else {
			await context.RespondAsync($"I will {(user.IgnoreBots ? "not notify you anymore" : "now notify you again")} if anyone says one of your highlights in an NSFW channel.");
		}
	}

	[SlashCommand("channel", "Ignore a channel.")]
	public async Task IgnoreChannel(InteractionContext context, DiscordChannel channel) {
		HighlightUser user = await GetOrCreateUserAsync(context);

		HighlightUserIgnoredChannel? existingEntry = user.IgnoredChannels.FirstOrDefault(huic => huic.ChannelId == channel.Id);
		if (existingEntry != null) {
			user.IgnoredChannels.Remove(existingEntry);
		} else {
			user.IgnoredChannels.Add(new HighlightUserIgnoredChannel() {
				ChannelId = channel.Id
			});
		}
		
		await DbContext.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(existingEntry == null ? "not notify you" : "now notify you again")} if someone says them in {channel.Mention}");
		} else {
			await context.RespondAsync($"I will {(existingEntry == null ? "not notify you anymore" : "now notify you again")} if anyone says one of your highlights in {channel.Mention}.");
		}
	}
}

public static class InteractionUtils {
	public static Task RespondAsync(this InteractionContext context, string content) =>
		RespondAsync(context, dmb => dmb.WithContent(content));

	public static Task RespondAsync(this InteractionContext context, Action<DiscordMessageBuilder> builder) {
		var dmb = new DiscordMessageBuilder();
		builder(dmb);

		return context.CreateResponseAsync(new DiscordInteractionResponseBuilder(dmb));
	}
}
