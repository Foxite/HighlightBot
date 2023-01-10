using DSharpPlus.SlashCommands;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace HighlightBot;

[SlashModuleLifespan(SlashModuleLifespan.Scoped)]
public class SlashCommandModule : ApplicationCommandModule {
	public HighlightDbContext DbContext { get; set; } = null!;
	
	protected CommandSession Session { get; private set; } = null!;
	protected InteractionHighlightCommandContext Hcc { get; private set; } = null!;

	public override Task<bool> BeforeSlashExecutionAsync(InteractionContext ctx) {
		Session = new CommandSession(DbContext);
		Hcc = new InteractionHighlightCommandContext(ctx);
		return Task.FromResult(true);
	}

	[SlashCommand("show", "Show all your highlights.")]
	public Task GetAllHighlights(InteractionContext context) {
		return Session.GetAllHighlights(Hcc);
	}

	[SlashCommand("clear", "Delete all your highlights.")]
	public Task ClearHighlights(InteractionContext context) {
		return Session.ClearHighlights(Hcc);
	}

	[SlashCommand("add", "Add a highlight.")]
	public Task AddHighlight(InteractionContext context, [Option("term", "The term to add")] string term) {
		return Session.AddHighlight(Hcc, new[] { term });
	}

	[SlashCommand("remove", "Remove a highlight.")]
	public Task RemoveHighlights(InteractionContext context, [Option("term", "The term to add")] string highlight) {
		return Session.RemoveHighlights(Hcc, highlight);
	}

	[SlashCommand("delay", "Set your delay, the amount of time that must pass before I DM you.")]
	public Task SetHighlightDelay(InteractionContext context, [Option("minutes", "The delay in minutes.")] long minutes) {
		return Session.SetHighlightDelay(Hcc, minutes);
	}
}

[SlashCommandGroup("ignore", "Ignore something.")]
[SlashModuleLifespan(SlashModuleLifespan.Scoped)]
public class SlashIgnoreModule : SlashCommandModule {
	[SlashCommand("bots", "Ignore all bots."), Priority(1)]
	public Task IgnoreBots(InteractionContext context, [Option("ignore", "Ignore bots?", true)] bool ignore) {
		return Session.IgnoreBots(Hcc, ignore);
	}

	[SlashCommand("nsfw", "Ignore NSFW channels."), Priority(1)]
	public Task IgnoreNsfw(InteractionContext context, [Option("ignore", "Ignore NSFW?", true)] bool ignore) {
		return Session.IgnoreNsfw(Hcc, ignore);
	}

	[SlashCommand("user", "Ignore a user.")]
	public Task IgnoreUser(InteractionContext context, [Option("ignore", "The user to ignore.", true)] DiscordUser user) {
		return Session.IgnoreUser(Hcc, user);
	}

	[SlashCommand("channel", "Ignore a channel.")]
	public Task IgnoreChannel(InteractionContext context, [Option("ignore", "The channel to ignore.", true)] DiscordChannel channel) {
		return Session.IgnoreChannel(Hcc, channel);
	}
}
