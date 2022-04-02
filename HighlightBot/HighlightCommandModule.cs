using System.Text.RegularExpressions;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace HighlightBot;

[ModuleLifespan(ModuleLifespan.Transient)]
public class HighlightCommandModule : BaseCommandModule {
	public HighlightDbContext DbContext { get; set; }

	private Task<HighlightUser?> GetUserAsync(CommandContext context) {
		return DbContext.Users.Include(user => user.Terms).Include(user => user.IgnoredChannels).FirstOrDefaultAsync(user => user.DiscordGuildId == context.Guild.Id && user.DiscordUserId == context.User.Id);
	}

	/// <summary>
	/// Does not save changes, which you might not want to do either.
	/// </summary>
	private async Task<HighlightUser> GetOrCreateUserAsync(CommandContext context) {
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

	private static void AddEmbedOfTrackedTerms(HighlightUser user, DiscordMessageBuilder dmb) {
		DiscordEmbedBuilder embed = new DiscordEmbedBuilder()
			.WithTitle("You're currently tracking the following words")
			.WithColor(DiscordColor.Yellow)
			.WithDescription(string.Join("\n", user.Terms.Select(term => term.Display)))
			.AddField("Highlight Delay", user.HighlightDelay.TotalHours >= 1 ? user.HighlightDelay.ToString(@"h\:mm\:ss") : user.HighlightDelay.ToString(@"m\:ss"));

		if (user.IgnoredChannels.Count > 0) {
			embed.AddField("Ignored Channels", string.Join("\n", user.IgnoredChannels.Select(huic => "<#" + huic.ChannelId + ">")));
		}

		dmb
			.WithEmbed(embed)
			.WithAllowedMentions(Mentions.None);
	}
	
	[Command("show")]
	public async Task GetAllHighlights(CommandContext context) {
		HighlightUser? user = await GetUserAsync(context);
		if (user == null || user.Terms.Count == 0) {
			await context.RespondAsync("You're not tracking any words.");
		} else {
			await context.RespondAsync(dmb => AddEmbedOfTrackedTerms(user, dmb));
		}
	}

	[Command("clear")]
	public async Task ClearHighlights(CommandContext context) {
		HighlightUser? user = await GetUserAsync(context);
		if (user == null || user.Terms.Count == 0) {
			await context.RespondAsync("You're not tracking any words.");
		} else {
			user.Terms.Clear();
			await DbContext.SaveChangesAsync();
			await context.RespondAsync("Deleted all your highlights.");
		}
	}

	[Command("add")]
	public async Task AddHighlight(CommandContext context, [RemainingText] string terms) {
		HighlightUser user = await GetOrCreateUserAsync(context);

		string[] lines = terms.Split("\n");
		int added = 0;
		foreach (string line in lines) {
			string pattern;
			string display;
			if (line.StartsWith('/') && line.EndsWith('/')) {
				pattern = line[1..^1];
				display = $"`{line}`";
			} else {
				pattern = $@"\b{Regex.Escape(line.ToLower())}\b";
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

	[Command("remove"), Aliases("rm")]
	public async Task RemoveHighlights(CommandContext context, string highlight) {
		HighlightUser? user = await GetUserAsync(context);
		if (user == null || user.Terms.Count == 0) {
			await context.RespondAsync("You're not tracking any words.");
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

	[Command("delay")]
	public async Task SetHighlightDelay(CommandContext context, int minutes) {
		HighlightUser user = await GetOrCreateUserAsync(context);
		user.HighlightDelay = TimeSpan.FromMinutes(minutes);
		await DbContext.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.RespondAsync($"You're not tracking any words yet, but when you add them, I will notify you if anyone says them and you've been inactive for {minutes} minute{(minutes == 1 ? "" : "s")}.");
		} else {
			await context.RespondAsync($"I will notify you if anyone says one of your highlights and you've been inactive for {minutes} minute{(minutes == 1 ? "" : "s")}.");
		}
	}

	[Command("ignore")]
	public async Task IgnoreChannel(CommandContext context, DiscordChannel channel) {
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
