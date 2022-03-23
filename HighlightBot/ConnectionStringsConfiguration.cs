using Microsoft.EntityFrameworkCore;

namespace HighlightBot;

public class ConnectionStringsConfiguration {
	public Backend Mode { get; set; }
	public string HighlightDbContext { get; set; }

	private readonly Dictionary<Type, Func<string>> m_GetValues = new();

	public ConnectionStringsConfiguration() {
		m_GetValues.Add(typeof(HighlightDbContext), () => HighlightDbContext);
	}

	public string GetConnectionString<TDbContext>() where TDbContext : DbContext => m_GetValues[typeof(TDbContext)]();

	public enum Backend {
		Sqlite,
		Postgres
	}
}
