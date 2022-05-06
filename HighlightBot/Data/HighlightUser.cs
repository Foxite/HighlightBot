namespace HighlightBot;

public class HighlightUser {
	public ulong DiscordGuildId { get; set; }
	public ulong DiscordUserId { get; set; }
	public DateTime LastActivity { get; set; }
	public TimeSpan HighlightDelay { get; set; } = TimeSpan.FromMinutes(30);
	public bool IgnoreBots { get; set; } = true;
	public bool IgnoreNsfw { get; set; } = false;
	public ICollection<HighlightTerm> Terms { get; set; }
	public ICollection<HighlightUserIgnoredChannel> IgnoredChannels { get; set; }
}
