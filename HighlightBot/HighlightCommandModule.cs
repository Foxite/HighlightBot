using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace HighlightBot;

[ModuleLifespan(ModuleLifespan.Transient)]
public class HighlightCommandModule : BaseCommandModule {
	public HighlightDbContext DbContext { get; set; } = null!;
	
	protected CommandSession Session { get; private set; } = null!;
	protected ClassicHighlightCommandContext Hcc { get; private set; } = null!;
	
	public override Task BeforeExecutionAsync(CommandContext ctx) {
		Session = new CommandSession(DbContext);
		Hcc = new ClassicHighlightCommandContext(ctx);
		return Task.CompletedTask;
	}

	[Command("show")]
	public Task GetAllHighlights(CommandContext context) {
		return Session.GetAllHighlights(Hcc);
	}

	[Command("clear")]
	public Task ClearHighlights(CommandContext context) {
		return Session.ClearHighlights(Hcc);
	}

	[Command("add")]
	public Task AddHighlight(CommandContext context, [RemainingText] string? terms) {
		if (terms == null) {
			return context.RespondAsync("You must specify one or more terms to add.");
		}

		return Session.AddHighlight(Hcc, terms.Split('\n'));
	}

	[Command("remove"), Aliases("rm")]
	public Task RemoveHighlights(CommandContext context, [RemainingText] string? highlight) {
		if (highlight == null) {
			return context.RespondAsync("You must specify a term to remove.");
		}

		return Session.RemoveHighlights(Hcc, highlight);
	}

	[Command("delay")]
	public Task SetHighlightDelay(CommandContext context, long minutes) {
		return Session.SetHighlightDelay(Hcc, minutes);
	}
}

[Group("ignore")]
[ModuleLifespan(ModuleLifespan.Transient)]
public class IgnoreModule : HighlightCommandModule {
	[Command("bots"), Priority(1)]
	public Task IgnoreBots(CommandContext context, bool? ignore) {
		return Session.IgnoreBots(Hcc, ignore);
	}

	[Command("nsfw"), Priority(1)]
	public Task IgnoreNsfw(CommandContext context, bool? ignore) {
		return Session.IgnoreNsfw(Hcc, ignore);
	}

	[GroupCommand]
	public Task IgnoreUser(CommandContext context, DiscordUser user) {
		return Session.IgnoreUser(Hcc, user);
	}

	[GroupCommand]
	public Task IgnoreChannel(CommandContext context, DiscordChannel channel) {
		return Session.IgnoreChannel(Hcc, channel);
	}
}
