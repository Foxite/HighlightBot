using System.Text.RegularExpressions;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace HighlightBot;

[ModuleLifespan(ModuleLifespan.Transient)]
public class HighlightCommandModule : BaseCommandModule {
	protected CommandSession Session { get; }
	
	public HighlightCommandModule(HighlightDbContext dbContext) {
		Session = new CommandSession(dbContext);
	}

	[Command("show")]
	public async Task GetAllHighlights(CommandContext context) {
		HighlightUser? user = await Session.GetUserAsync(context.Guild.Id, context.User.Id);
		if (user == null || user.Terms.Count == 0) {
			await context.RespondAsync("You're not tracking any words.");
		} else {
			await context.RespondAsync(dmb => Session.AddEmbedOfTrackedTerms(user, dmb));
		}
	}

	[Command("clear")]
	public async Task ClearHighlights(CommandContext context) {
		HighlightUser? user = await Session.GetUserAsync(context.Guild.Id, context.User.Id);
		if (user == null || user.Terms.Count == 0) {
			await context.RespondAsync("You're not tracking any words.");
		} else {
			user.Terms.Clear();
			await Session.SaveChangesAsync();
			await context.RespondAsync("Deleted all your highlights.");
		}
	}

	[Command("add")]
	public async Task AddHighlight(CommandContext context, [RemainingText] string? terms) {
		if (terms == null) {
			await context.RespondAsync("You must specify one or more terms to add.");
			return;
		}

		HighlightUser user = await Session.GetOrCreateUserAsync(context.Guild.Id, context.User.Id);

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

		var regexListLength = Session.GetTermsListForEmbed(user, true).Length;
		var wordListLength = Session.GetTermsListForEmbed(user, false).Length;
		if (regexListLength > 1024 || wordListLength > 1024) {
			await context.RespondAsync("Error: This would make your list of words/regexes too long, so the changes are not saved.");

			return;
		}

		await Session.SaveChangesAsync();

		string message = $"Added {added} highlight{(added == 1 ? "" : "s")}";
		if (added != lines.Length) {
			message += "\nNote: some words were already being highlighted.";
		}

		await context.RespondAsync(dmb => {
			dmb.WithContent(message);
			Session.AddEmbedOfTrackedTerms(user, dmb);
		});
	}

	[Command("remove"), Aliases("rm")]
	public async Task RemoveHighlights(CommandContext context, [RemainingText] string? highlight) {
		if (highlight == null) {
			await context.RespondAsync("You must specify a term to remove.");
			return;
		}
		
		HighlightUser? user = await Session.GetUserAsync(context.Guild.Id, context.User.Id);
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
				await Session.SaveChangesAsync();
				content = "Deleted the highlight: " + highlight;
			} else {
				content = $"You are not tracking {highlight}.";
			}
			await context.RespondAsync(dmb => {
				dmb
					.WithContent(content)
					.WithAllowedMentions(Mentions.None);
				Session.AddEmbedOfTrackedTerms(user, dmb);
			});
		}
	}

	[Command("delay")]
	public async Task SetHighlightDelay(CommandContext context, int minutes) {
		if (minutes < 0) {
			minutes = 0;
		}

		HighlightUser user = await Session.GetOrCreateUserAsync(context.Guild.Id, context.User.Id);
		user.HighlightDelay = TimeSpan.FromMinutes(minutes);
		await Session.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.RespondAsync($"You're not tracking any words yet, but when you add them, I will notify you if anyone says them and you've been inactive for {minutes} minute{(minutes == 1 ? "" : "s")}.");
		} else {
			await context.RespondAsync($"I will notify you if anyone says one of your highlights and you've been inactive for {minutes} minute{(minutes == 1 ? "" : "s")}.");
		}
	}
}

[Group("ignore")]
public class IgnoreModule : HighlightCommandModule {
	public IgnoreModule(HighlightDbContext dbContext) : base(dbContext) {
	}
	
	[Command("bots"), Priority(1)]
	public async Task IgnoreBots(CommandContext context) {
		HighlightUser user = await Session.GetOrCreateUserAsync(context.Guild.Id, context.User.Id);

		user.IgnoreBots = !user.IgnoreBots;

		await Session.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(user.IgnoreBots ? "not notify you" : "now notify you again")} if a bot says them.");
		} else {
			await context.RespondAsync($"I will {(user.IgnoreBots ? "not notify you anymore" : "now notify you again")} if a bot says one of your highlights.");
		}
	}

	[Command("nsfw"), Priority(1)]
	public async Task IgnoreNsfw(CommandContext context) {
		HighlightUser user = await Session.GetOrCreateUserAsync(context.Guild.Id, context.User.Id);

		user.IgnoreNsfw = !user.IgnoreNsfw;

		await Session.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(user.IgnoreNsfw ? "not notify you" : "now notify you again")} anyone says them in an NSFW channel.");
		} else {
			await context.RespondAsync($"I will {(user.IgnoreNsfw ? "not notify you anymore" : "now notify you again")} if anyone says one of your highlights in an NSFW channel.");
		}
	}

	[GroupCommand]
	public async Task IgnoreUser(CommandContext context, DiscordUser user) {
		HighlightUser hlUser = await Session.GetOrCreateUserAsync(context.Guild.Id, context.User.Id);

		HighlightUserIgnoredUser? existingEntry = hlUser.IgnoredUsers.FirstOrDefault(huiu => huiu.IgnoredUserId == user.Id);
		if (existingEntry != null) {
			hlUser.IgnoredUsers.Remove(existingEntry);
		} else {
			hlUser.IgnoredUsers.Add(new HighlightUserIgnoredUser() {
				IgnoredUserId = user.Id
			});
		}
		
		await Session.SaveChangesAsync();

		if (hlUser.Terms.Count == 0) {
			await context.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(existingEntry == null ? "not notify you" : "now notify you again")} if {user.Username} says them.");
		} else {
			await context.RespondAsync($"I will {(existingEntry == null ? "not notify you anymore" : "now notify you again")} if {user.Username} says one of your highlights.");
		}
	}

	[GroupCommand]
	public async Task IgnoreChannel(CommandContext context, DiscordChannel channel) {
		HighlightUser user = await Session.GetOrCreateUserAsync(context.Guild.Id, context.User.Id);

		HighlightUserIgnoredChannel? existingEntry = user.IgnoredChannels.FirstOrDefault(huic => huic.ChannelId == channel.Id);
		if (existingEntry != null) {
			user.IgnoredChannels.Remove(existingEntry);
		} else {
			user.IgnoredChannels.Add(new HighlightUserIgnoredChannel() {
				ChannelId = channel.Id
			});
		}
		
		await Session.SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await context.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(existingEntry == null ? "not notify you" : "now notify you again")} if someone says them in {channel.Mention}");
		} else {
			await context.RespondAsync($"I will {(existingEntry == null ? "not notify you anymore" : "now notify you again")} if anyone says one of your highlights in {channel.Mention}.");
		}
	}
}
