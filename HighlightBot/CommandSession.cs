using System.Dynamic;
using System.Text.RegularExpressions;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.EntityFrameworkCore;

namespace HighlightBot; 

public class CommandSession {
	private readonly HighlightDbContext m_DbContext;

	public CommandSession(HighlightDbContext dbContext) {
		m_DbContext = dbContext;
	}

	private Task SaveChangesAsync() {
		return m_DbContext.SaveChangesAsync();
	}

	private Task<HighlightUser?> GetUserAsync(HighlightCommandContext commandContext) {
		return m_DbContext.Users
			.Include(user => user.Terms)
			.Include(user => user.IgnoredChannels)
			.Include(user => user.IgnoredUsers)
			.FirstOrDefaultAsync(user => user.DiscordGuildId == commandContext.Guild.Id && user.DiscordUserId == commandContext.User.Id);
	}

	/// <summary>
	/// Does not save changes, which you might not want to do either.
	/// </summary>
	private async Task<HighlightUser> GetOrCreateUserAsync(HighlightCommandContext commandContext) {
		HighlightUser? user = await GetUserAsync(commandContext);
		if (user == null) {
			user = new HighlightUser() {
				DiscordGuildId = commandContext.Guild.Id,
				DiscordUserId = commandContext.User.Id,
				LastActivity = DateTime.UtcNow,
				Terms = new List<HighlightTerm>(),
				IgnoredChannels = new List<HighlightUserIgnoredChannel>(),
				IgnoredUsers = new List<HighlightUserIgnoredUser>()
			};
			m_DbContext.Users.Add(user);
		}

		return user;
	}

	private string GetTermsListForEmbed(HighlightUser user, bool regexes) {
		List<HighlightTerm> terms = user.Terms.Where(term => term.Display.StartsWith('`') == regexes).ToList();
		return string.Join("\n", terms.Select(term => term.Display));
	}

	private void AddEmbedOfTrackedTerms(HighlightUser user, IDiscordMessageBuilder dmb) {
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

			embed.AddField("Ignore bots", user.IgnoreBots ? "Yes" : "No");
			embed.AddField("Ignore NSFW", user.IgnoreNsfw ? "Yes" : "No");

			embed
				.AddField("Highlight Delay", user.HighlightDelay.TotalHours >= 1 ? user.HighlightDelay.ToString(@"h\:mm\:ss") : user.HighlightDelay.ToString(@"m\:ss"))
				.AddField("To add a new regex:", "Surround a term with `/slashes/`");

			dmb
				.AddEmbed(embed)
				.AddMentions(Mentions.None);
		}
	}

	
	
	public async Task GetAllHighlights(HighlightCommandContext hcc) {
		HighlightUser? user = await GetUserAsync(hcc);
		if (user == null || user.Terms.Count == 0) {
			await hcc.RespondAsync("You're not tracking any words.");
		} else {
			await hcc.RespondAsync(dmb => AddEmbedOfTrackedTerms(user, dmb));
		}
	}
	
	public async Task ClearHighlights(HighlightCommandContext hcc) {
		HighlightUser? user = await GetUserAsync(hcc);
		if (user == null || user.Terms.Count == 0) {
			await hcc.RespondAsync("You're not tracking any words.");
		} else {
			user.Terms.Clear();
			await SaveChangesAsync();
			await hcc.RespondAsync("Deleted all your highlights.");
		}
	}

	public async Task AddHighlight(HighlightCommandContext hcc, ICollection<string> terms) {
		HighlightUser user = await GetOrCreateUserAsync(hcc);

		int added = 0;
		foreach (string term in terms) {
			string pattern;
			string display;
			if (term.StartsWith('/') && term.EndsWith('/')) {
				pattern = term[1..^1];

				if (!Util.IsValidRegex(pattern)) {
					await hcc.RespondAsync("Invalid regex 🙁");
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
			if (!user.Terms.Any(t => t.Regex == pattern)) {
				added++;
				user.Terms.Add(new HighlightTerm() {
					Display = display,
					Regex = pattern
				});
			}
		}

		var regexListLength = GetTermsListForEmbed(user, true).Length;
		var wordListLength = GetTermsListForEmbed(user, false).Length;
		if (regexListLength > 1024 || wordListLength > 1024) {
			await hcc.RespondAsync("Error: This would make your list of words/regexes too long, so the changes are not saved.");

			return;
		}

		await SaveChangesAsync();

		string message = $"Added {added} highlight{(added == 1 ? "" : "s")}";
		if (added != terms.Count) {
			message += "\nNote: some words were already being highlighted.";
		}

		await hcc.RespondAsync(dmb => {
			dmb.WithContent(message);
			AddEmbedOfTrackedTerms(user, dmb);
		});
	}

	public async Task RemoveHighlights(HighlightCommandContext hcc, string highlight) {
		HighlightUser? user = await GetUserAsync(hcc);
		if (user == null || user.Terms.Count == 0) {
			await hcc.RespondAsync("You're not tracking any words.");
		} else {
			if (highlight.StartsWith('/') && highlight.EndsWith('/')) {
				highlight = $"`{highlight}`";
			}
			HighlightTerm? term = user.Terms.FirstOrDefault(term => term.Display == highlight);
			string content;
			if (term != null) {
				user.Terms.Remove(term);
				await SaveChangesAsync();
				content = "Deleted the highlight: " + highlight;
			} else {
				content = $"You are not tracking {highlight}.";
			}
			await hcc.RespondAsync(dmb => {
				dmb.WithContent(content);
				AddEmbedOfTrackedTerms(user, dmb);
			});
		}
	}

	public async Task SetHighlightDelay(HighlightCommandContext hcc, long minutes) {
		if (minutes < 0) {
			minutes = 0;
		}

		HighlightUser user = await GetOrCreateUserAsync(hcc);
		user.HighlightDelay = TimeSpan.FromMinutes(minutes);
		await SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await hcc.RespondAsync($"You're not tracking any words yet, but when you add them, I will notify you if anyone says them and you've been inactive for {minutes} minute{(minutes == 1 ? "" : "s")}.");
		} else {
			await hcc.RespondAsync($"I will notify you if anyone says one of your highlights and you've been inactive for {minutes} minute{(minutes == 1 ? "" : "s")}.");
		}
	}



	public async Task IgnoreBots(HighlightCommandContext hcc, bool? value) {
		HighlightUser user = await GetOrCreateUserAsync(hcc);

		user.IgnoreBots = value ?? !user.IgnoreBots;

		await SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await hcc.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(user.IgnoreBots ? "not notify you" : "now notify you again")} if a bot says them.");
		} else {
			await hcc.RespondAsync($"I will {(user.IgnoreBots ? "not notify you anymore" : "now notify you again")} if a bot says one of your highlights.");
		}
	}


	public async Task IgnoreNsfw(HighlightCommandContext hcc, bool? value) {
		HighlightUser user = await GetOrCreateUserAsync(hcc);

		user.IgnoreNsfw = value ?? !user.IgnoreNsfw;

		await SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await hcc.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(user.IgnoreNsfw ? "not notify you" : "now notify you again")} anyone says them in an NSFW channel.");
		} else {
			await hcc.RespondAsync($"I will {(user.IgnoreNsfw ? "not notify you anymore" : "now notify you again")} if anyone says one of your highlights in an NSFW channel.");
		}
	}

	public async Task IgnoreUser(HighlightCommandContext hcc, DiscordUser user) {
		HighlightUser hlUser = await GetOrCreateUserAsync(hcc);

		HighlightUserIgnoredUser? existingEntry = hlUser.IgnoredUsers.FirstOrDefault(huiu => huiu.IgnoredUserId == user.Id);
		if (existingEntry != null) {
			hlUser.IgnoredUsers.Remove(existingEntry);
		} else {
			hlUser.IgnoredUsers.Add(new HighlightUserIgnoredUser() {
				IgnoredUserId = user.Id
			});
		}

		await SaveChangesAsync();

		if (hlUser.Terms.Count == 0) {
			await hcc.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(existingEntry == null ? "not notify you" : "now notify you again")} if {user.Username} says them.");
		} else {
			await hcc.RespondAsync($"I will {(existingEntry == null ? "not notify you anymore" : "now notify you again")} if {user.Username} says one of your highlights.");
		}
	}
	
	public async Task IgnoreChannel(HighlightCommandContext hcc, DiscordChannel channel) {
		HighlightUser user = await GetOrCreateUserAsync(hcc);

		HighlightUserIgnoredChannel? existingEntry = user.IgnoredChannels.FirstOrDefault(huic => huic.ChannelId == channel.Id);
		if (existingEntry != null) {
			user.IgnoredChannels.Remove(existingEntry);
		} else {
			user.IgnoredChannels.Add(new HighlightUserIgnoredChannel() {
				ChannelId = channel.Id
			});
		}
		
		await SaveChangesAsync();

		if (user.Terms.Count == 0) {
			await hcc.RespondAsync($"You're not tracking any words yet, but when you add them, I will {(existingEntry == null ? "not notify you" : "now notify you again")} if someone says them in {channel.Mention}");
		} else {
			await hcc.RespondAsync($"I will {(existingEntry == null ? "not notify you anymore" : "now notify you again")} if anyone says one of your highlights in {channel.Mention}.");
		}
	}
}

public abstract class HighlightCommandContext {
	public DiscordUser User { get; }
	public DiscordGuild Guild { get; }

	protected HighlightCommandContext(DiscordUser user, DiscordGuild guild) {
		User = user;
		Guild = guild;
	}

	public abstract Task RespondAsync(Action<IDiscordMessageBuilder> buildMessage);
	public virtual Task RespondAsync(string content) => RespondAsync(dmb => dmb.WithContent(content));
}

public class InteractionHighlightCommandContext : HighlightCommandContext {
	private readonly InteractionContext m_Context;

	public InteractionHighlightCommandContext(InteractionContext context) : base(context.User, context.Guild) {
		m_Context = context;
	}
	
	public override Task RespondAsync(Action<IDiscordMessageBuilder> buildMessage) {
		return m_Context.CreateResponseAsync(buildMessage);
	}
}

public class ClassicHighlightCommandContext : HighlightCommandContext {
	private readonly CommandContext m_Context;

	public ClassicHighlightCommandContext(CommandContext context) : base(context.User, context.Guild) {
		m_Context = context;
	}

	public override Task RespondAsync(Action<IDiscordMessageBuilder> buildMessage) {
		return m_Context.RespondAsync(buildMessage);
	}
}
