using System.Text.RegularExpressions;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace HighlightBot.Services;

public class TermFinder {
	private readonly HighlightDbContext m_DbContext;
	private readonly TimeSpan m_DmCooldown;
	
	public TermFinder(HighlightDbContext dbContext) {
		m_DbContext = dbContext;
		m_DmCooldown = TimeSpan.FromMinutes(Program.IsDevelopment ? 0 : 5);
	}

	public async Task<List<UserIdAndTerm>> FindTerms(MessageCreateEventArgs e) {
		// Create a "fake" entity object and attach it to EF, then modify it and save the changes.
		// This allows to update an entity without querying for it in the database.
		// This is useful, because this function is expected to run a lot.
		// https://stackoverflow.com/a/58367364
		var author = new HighlightUser() {
			DiscordGuildId = e.Guild.Id,
			DiscordUserId = e.Author.Id,
		};
		m_DbContext.Attach(author);
		author.LastActivity = DateTime.UtcNow;

		try {
			await m_DbContext.SaveChangesAsync();
		} catch (DbUpdateConcurrencyException) {
			// Happens when the author does not have a user entry in the database, in which case we don't care.
			// If it happens because the database was actually concurrently, we also don't care.
		}

		string content = e.Message.Content.ToLowerInvariant();
		DateTime currentTime = DateTime.UtcNow;
		DateTime cooldownEnd = DateTime.UtcNow - m_DmCooldown;
		return m_DbContext.Terms
			.Where(term =>
				term.User.DiscordGuildId == e.Guild.Id &&
				term.User.DiscordUserId != e.Author.Id &&
				!(e.Author.IsBot && term.User.IgnoreBots) &&
				!(e.Channel.IsNSFW && term.User.IgnoreNsfw) &&
				term.User.LastActivity + term.User.HighlightDelay < currentTime &&
				term.User.LastDM < cooldownEnd &&
				!term.User.IgnoredChannels.Any(huic => huic.ChannelId == e.Channel.Id) &&
				!term.User.IgnoredUsers.Any(huiu => huiu.IgnoredUserId == e.Author.Id)
			)
			.Select(term => new {
				term.User.DiscordUserId,
				term.User.DiscordGuildId,
				term.Regex,
				term.Display
			})
			.AsEnumerable()
			.Where(term => Regex.IsMatch(content, term.Regex, RegexOptions.IgnoreCase))
			.Select(term => new UserIdAndTerm() {
				DiscordUserId = term.DiscordUserId,
				Value = term.Display
			})
			.ToList();
	}
}
