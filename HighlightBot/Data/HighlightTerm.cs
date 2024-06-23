using System.Text.RegularExpressions;

namespace HighlightBot;

public class HighlightTerm {
	public Guid Id { get; set; }
	public HighlightUser User { get; set; }
	
	public string Regex { get; set; }
	public string Display { get; set; }
	public bool IsCaseSensitive { get; set; }
}
