namespace HighlightBot;

public class HighlightUserIgnoredUser {
	public Guid Id { get; set; }
	public HighlightUser User { get; set; }
	public ulong IgnoredUserId { get; set; }
}
