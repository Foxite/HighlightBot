using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.EntityFrameworkCore;

namespace HighlightBot;

[SlashModuleLifespan(SlashModuleLifespan.Transient)]
public class SlashCommandModule : ApplicationCommandModule {
	public HighlightDbContext DbContext { get; set; }

	protected Task<HighlightUser?> GetUserAsync(InteractionContext context) {
		return DbContext.Users
			.Include(user => user.Terms)
			.Include(user => user.IgnoredChannels)
			.Include(user => user.IgnoredUsers)
			.FirstOrDefaultAsync(user => user.DiscordGuildId == context.Guild.Id && user.DiscordUserId == context.User.Id);
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
				IgnoredChannels = new List<HighlightUserIgnoredChannel>(),
				IgnoredUsers = new List<HighlightUserIgnoredUser>()
			};
			DbContext.Users.Add(user);
		}

		return user;
	}

	private static string GetTermsListForEmbed(HighlightUser user, bool regexes) {
		List<HighlightTerm> terms = user.Terms.Where(term => term.Display.StartsWith('`') == regexes).ToList();
		return string.Join("\n", terms.Select(term => term.Display));
	}

	protected static void AddEmbedOfTrackedTerms(HighlightUser user, DiscordInteractionResponseBuilder dmb) {
		if (user.Terms.Count > 0) {
			DiscordEmbedBuilder embed = new DiscordEmbedBuilder()
				.WithTitle("You're currently tracking the following terms")
				.WithColor(DiscordColor.Yellow);

			string wordsList = GetTermsListForEmbed(user, false);
			if (wordsList.Length > 0) {
				embed.AddField("Words", wordsList);
			}

			string regexesList = GetTermsListForEmbed(user, true);
			if (regexesList.Length > 0) {
				embed.AddField("Regexes", regexesList);
			}

			if (user.IgnoredChannels.Count > 0) {
				embed.AddField("Ignored Channels", string.Join("\n", user.IgnoredChannels.Select(huic => "<#" + huic.ChannelId + ">")));
			}

			if (user.IgnoredUsers.Count > 0) {
				embed.AddField("Ignored Users", string.Join("\n", user.IgnoredUsers.Select(huic => "<@" + huic.IgnoredUserId + ">")));
			}

			embed
				.AddField("Ignore bots", user.IgnoreBots ? "Yes" : "No")
				.AddField("Ignore NSFW", user.IgnoreNsfw ? "Yes" : "No")
				.AddField("Highlight Delay", user.HighlightDelay.TotalHours >= 1 ? user.HighlightDelay.ToString(@"h\:mm\:ss") : user.HighlightDelay.ToString(@"m\:ss"))
				.AddField("To add a new regex:", "Surround a term with `/slashes/`");

			dmb.AddEmbed(embed);
		}
	}
	
	[SlashCommand("show", "Show all your highlights.")]
	public async Task GetAllHighlights(InteractionContext context) {
		HighlightUser? user = await GetUserAsync(context);
		if (user == null || user.Terms.Count == 0) {
			await context.CreateResponseAsync("You're not tracking any words.");
		} else {
			await context.CreateResponseAsync(dmb => AddEmbedOfTrackedTerms(user, dmb));
		}
	}

	[SlashCommand("clear", "Delete all your highlights.")]
	public async Task ClearHighlights(InteractionContext context) {
		HighlightUser? user = await GetUserAsync(context);
		if (user == null || user.Terms.Count == 0) {
			await context.CreateResponseAsync("You're not tracking any words.");
		} else {
			user.Terms.Clear();
			await DbContext.SaveChangesAsync();
			await context.CreateResponseAsync("Deleted all your highlights.");
		}
	}

	[SlashCommand("add", "Add a highlight.")]
	public async Task AddHighlight(InteractionContext context, [Option("term", "The term to add")] string term) {
		HighlightUser user = await GetOrCreateUserAsync(context);

		string pattern;
		string display;
		if (term.StartsWith('/') && term.EndsWith('/')) {
			pattern = term[1..^1];

			if (!Util.IsValidRegex(pattern)) {
				await context.CreateResponseAsync("Invalid regex 🙁");
				return;
			}
			
			display = $"`{term}`";
		} else {
			pattern = $@"\b{Regex.Escape(term.ToLower())}\b";

			if (!Util.IsValidRegex(pattern)) {
				throw new Exception("Escaped pattern is not valid: " + pattern);
			}
			
			display = term;
		}

		bool added = false;
		if (!user.Terms.Any(term => term.Regex == pattern)) {
			added = true;
			user.Terms.Add(new HighlightTerm() {
				Display = display,
				Regex = pattern
			});
		}

		var regexListLength = GetTermsListForEmbed(user, true).Length;
		var wordListLength = GetTermsListForEmbed(user, false).Length;
		if (regexListLength > 1024 || wordListLength > 1024) {
			await context.CreateResponseAsync("Error: This would make your list of words/regexes too long, so the changes are not saved.");

			return;
		}

		await DbContext.SaveChangesAsync();

		string message = $"Added {(added ? 1 : 0)} highlight{(added ? "" : "s")}";
		if (!added) {
			message += "\nNote: some words were already being highlighted.";
		}

		await context.CreateResponseAsync(dmb => {
			dmb.WithContent(message);
			AddEmbedOfTrackedTerms(user, dmb);
		});
	}

	[SlashCommand("remove", "Remove a highlight.")]
	public async Task RemoveHighlights(InteractionContext context, [Option("term", "The term to add")] string highlight) {
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
			await context.CreateResponseAsync(dmb => {
				dmb.WithContent(content);
				AddEmbedOfTrackedTerms(user, dmb);
			});
		}
	}

	[SlashCommand("delay", "Set your delay, the amount of time that must pass before I DM you.")]
	public async Task SetHighlightDelay(InteractionContext context, [Option("minutes", "The delay in minutes.")] long minutes) {
		if (minutes < 0) {
			minutes = 0;
		}

		HighlightUser user = await GetOrCreateUserAsync(context);
		user.HighlightDelay = TimeSpan.FromMinutes(minutes);
		await DbContext.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.CreateResponseAsync($"You're not tracking any words yet, but when you add them, I will notify you if anyone says them and you've been inactive for {minutes} minute{(minutes == 1 ? "" : "s")}.");
		} else {
			await context.CreateResponseAsync($"I will notify you if anyone says one of your highlights and you've been inactive for {minutes} minute{(minutes == 1 ? "" : "s")}.");
		}
	}
}

[SlashCommandGroup("ignore", "Ignore something.")]
public class SlashIgnoreModule : SlashCommandModule {
	[SlashCommand("bots", "Ignore all bots."), Priority(1)]
	public async Task IgnoreBots(InteractionContext context, [Option("ignore", "Ignore bots?", true)] bool ignore) {
		HighlightUser user = await GetOrCreateUserAsync(context);

		user.IgnoreBots = ignore;

		await DbContext.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.CreateResponseAsync($"You're not tracking any words yet, but when you add them, I will {(user.IgnoreBots ? "not notify you" : "now notify you again")} if a bot says them.");
		} else {
			await context.CreateResponseAsync($"I will {(user.IgnoreBots ? "not notify you anymore" : "now notify you again")} if a bot says one of your highlights.");
		}
	}

	[SlashCommand("nsfw", "Ignore NSFW channels."), Priority(1)]
	public async Task IgnoreNsfw(InteractionContext context, [Option("ignore", "Ignore NSFW?", true)] bool ignore) {
		HighlightUser user = await GetOrCreateUserAsync(context);

		user.IgnoreNsfw = ignore;

		await DbContext.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.CreateResponseAsync($"You're not tracking any words yet, but when you add them, I will {(user.IgnoreNsfw ? "not notify you" : "now notify you again")} anyone says them in an NSFW channel.");
		} else {
			await context.CreateResponseAsync($"I will {(user.IgnoreNsfw ? "not notify you anymore" : "now notify you again")} if anyone says one of your highlights in an NSFW channel.");
		}
	}

	[SlashCommand("user", "Ignore a user.")]
	public async Task IgnoreUser(InteractionContext context, [Option("ignore", "The user to ignore.", true)] DiscordUser user) {
		HighlightUser hlUser = await GetOrCreateUserAsync(context);

		HighlightUserIgnoredUser? existingEntry = hlUser.IgnoredUsers.FirstOrDefault(huiu => huiu.IgnoredUserId == user.Id);
		if (existingEntry != null) {
			hlUser.IgnoredUsers.Remove(existingEntry);
		} else {
			hlUser.IgnoredUsers.Add(new HighlightUserIgnoredUser() {
				IgnoredUserId = user.Id
			});
		}
		
		await DbContext.SaveChangesAsync();

		if (hlUser.Terms.Count == 0) {
			await context.CreateResponseAsync($"You're not tracking any words yet, but when you add them, I will {(existingEntry == null ? "not notify you" : "now notify you again")} if {user.Username} says them.");
		} else {
			await context.CreateResponseAsync($"I will {(existingEntry == null ? "not notify you anymore" : "now notify you again")} if {user.Username} says one of your highlights.");
		}
	}

	[SlashCommand("channel", "Ignore a channel.")]
	public async Task IgnoreChannel(InteractionContext context, [Option("ignore", "The channel to ignore.", true)] DiscordChannel channel) {
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
			await context.CreateResponseAsync($"You're not tracking any words yet, but when you add them, I will {(existingEntry == null ? "not notify you" : "now notify you again")} if someone says them in {channel.Mention}");
		} else {
			await context.CreateResponseAsync($"I will {(existingEntry == null ? "not notify you anymore" : "now notify you again")} if anyone says one of your highlights in {channel.Mention}.");
		}
	}
}
