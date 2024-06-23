using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace HighlightBot;

public class HighlightDbContext : DbContext {
	public DbSet<HighlightUser> Users { get; set; }
	public DbSet<HighlightTerm> Terms { get; set; }

	public HighlightDbContext() : base() { }
	public HighlightDbContext(DbContextOptions<HighlightDbContext> dbContextOptions) : base(dbContextOptions) { }

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
		if (!optionsBuilder.IsConfigured) {
			// For `dotnet ef`
			optionsBuilder.UseNpgsql("Host=database; Port=5432; Username=highlightbot; Password=highlightbot");
		}
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder) {
		base.OnModelCreating(modelBuilder);

		modelBuilder
			.Entity<HighlightTerm>()
			.HasOne(term => term.User)
			.WithMany(user => user.Terms)
			.HasForeignKey("user_serverid", "user_userid");

		// [Index(nameof(User), IsUnique = false)]
		modelBuilder.Entity<HighlightTerm>()
			.HasIndex("user_serverid", "user_userid")
			.IsUnique(false);
		
		modelBuilder
			.Entity<HighlightUser>()
			.HasKey(nameof(HighlightUser.DiscordGuildId), nameof(HighlightUser.DiscordUserId));

		modelBuilder.Entity<HighlightTerm>()
			.Property(term => term.RegexOptions)
			.HasDefaultValue(RegexOptions.IgnoreCase);
	}
}
