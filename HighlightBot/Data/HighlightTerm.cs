namespace HighlightBot;

public class HighlightTerm {
	public Guid Id { get; set; }
	public HighlightUser User { get; set; }
	
	//public bool Regex { get; set; }]
	public string Value { get; set; }
}
