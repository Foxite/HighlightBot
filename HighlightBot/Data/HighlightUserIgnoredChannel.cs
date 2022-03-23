namespace HighlightBot;

public class HighlightUserIgnoredChannel {
	public Guid Id { get; set; }
	public HighlightUser User { get; set; }
	public ulong ChannelId { get; set; }
}
