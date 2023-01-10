using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace HighlightBot; 

public class CommandSession {
	private readonly HighlightDbContext m_DbContext;

	public CommandSession(HighlightDbContext dbContext) {
		m_DbContext = dbContext;
	}

	public Task SaveChangesAsync() {
		return m_DbContext.SaveChangesAsync();
	}

	public Task<HighlightUser?> GetUserAsync(ulong guildId, ulong userId) {
		return m_DbContext.Users
			.Include(user => user.Terms)
			.Include(user => user.IgnoredChannels)
			.Include(user => user.IgnoredUsers)
			.FirstOrDefaultAsync(user => user.DiscordGuildId == guildId && user.DiscordUserId == userId);
	}

	/// <summary>
	/// Does not save changes, which you might not want to do either.
	/// </summary>
	public async Task<HighlightUser> GetOrCreateUserAsync(ulong guildId, ulong userId) {
		HighlightUser? user = await GetUserAsync(guildId, userId);
		if (user == null) {
			user = new HighlightUser() {
				DiscordGuildId = guildId,
				DiscordUserId = userId,
				LastActivity = DateTime.UtcNow,
				Terms = new List<HighlightTerm>(),
				IgnoredChannels = new List<HighlightUserIgnoredChannel>(),
				IgnoredUsers = new List<HighlightUserIgnoredUser>()
			};
			m_DbContext.Users.Add(user);
		}

		return user;
	}

	public string GetTermsListForEmbed(HighlightUser user, bool regexes) {
		List<HighlightTerm> terms = user.Terms.Where(term => term.Display.StartsWith('`') == regexes).ToList();
		return string.Join("\n", terms.Select(term => term.Display));
	}

	public void AddEmbedOfTrackedTerms(HighlightUser user, IDiscordMessageBuilder dmb) {
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
}
