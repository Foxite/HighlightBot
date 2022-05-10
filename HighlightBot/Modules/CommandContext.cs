using System.Reflection;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.CommandsNext.Builders;
using DSharpPlus.CommandsNext.Entities;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace HighlightBot;

public abstract class CommandContext {
	public DiscordClient Client { get; }
	public IServiceProvider Services { get; }
	public DiscordChannel Channel { get; }
	public DiscordUser User { get; }
	public DiscordGuild? Guild => Channel.Guild;
	public DiscordMember? Member => User as DiscordMember;
	
	protected CommandContext(DiscordClient client, IServiceProvider services, DiscordChannel channel, DiscordUser user) {
		Client = client;
		Services = services;
		Channel = channel;
		User = user;
	}

	public virtual Task RespondAsync(string message) => RespondAsync(dmb => dmb.WithContent(message));
	public abstract Task RespondAsync(Action<DiscordMessageBuilder> builder);
}

public class InteractionContext : CommandContext {
	public SlashCommandsExtension SlashCommandsExtension { get; }
	public string Token { get; }
	public ulong InteractionId { get; }
	public string CommandName { get; }
	public ApplicationCommandType Type { get; }
	
	public IReadOnlyList<DiscordUser> ResolvedUserMentions { get; }
	public IReadOnlyList<DiscordRole> ResolvedRoleMentions { get; }
	public IReadOnlyList<DiscordChannel> ResolvedChannelMentions { get; }
	
	public InteractionContext(DiscordClient client, IServiceProvider services, DiscordChannel channel, DiscordUser user, SlashCommandsExtension slashCommandsExtension, string token, ulong interactionId, string commandName, ApplicationCommandType type, IReadOnlyList<DiscordUser> resolvedUserMentions, IReadOnlyList<DiscordRole> resolvedRoleMentions, IReadOnlyList<DiscordChannel> resolvedChannelMentions) : base(client, services, channel, user) {
		SlashCommandsExtension = slashCommandsExtension;
		Token = token;
		InteractionId = interactionId;
		CommandName = commandName;
		Type = type;
		ResolvedUserMentions = resolvedUserMentions;
		ResolvedRoleMentions = resolvedRoleMentions;
		ResolvedChannelMentions = resolvedChannelMentions;
	}

	public override Task RespondAsync(Action<DiscordMessageBuilder> builder) {
		throw new NotImplementedException();
	}
}

public class StandardContext : CommandContext {
	public CommandsNextExtension CommandsNext { get; }
	public DiscordMessage Message { get; }
	public Command? Command { get; }
	public CommandOverload Overload { get; }
	public IReadOnlyList<string> RawArguments { get; } = (IReadOnlyList<string>) Array.Empty<string>();
	public string RawArgumentString { get; } = string.Empty;
	public string Prefix { get; } = string.Empty;
	
	public StandardContext(DiscordClient client, IServiceProvider services, DiscordMessage message, CommandsNextExtension commandsNext, Command? command, CommandOverload overload) : base(client, services, message.Channel, message.Author) {
		CommandsNext = commandsNext;
		Command = command;
		Overload = overload;
	}

	public override Task RespondAsync(Action<DiscordMessageBuilder> builder) {
		return Message.RespondAsync(builder);
	}
}

public static class CommandsUtils {
	public static CommandContext ToCommandContext(this DSharpPlus.SlashCommands.InteractionContext context) {
		return new InteractionContext(context.Client, context.Services, context.Channel, context.User, context.SlashCommandsExtension, context.Token, context.InteractionId, context.CommandName, context.Type, context.ResolvedUserMentions, context.ResolvedRoleMentions, context.ResolvedChannelMentions);
	}
	
	public static CommandContext ToCommandContext(this DSharpPlus.CommandsNext.CommandContext context) {
		return new StandardContext(context.Client, context.Services, context.Message, context.CommandsNext, context.Command, context.Overload);
	}

	private static CommandBuilder[] BuildStandardCommands(Type type) {
		MethodInfo[] commandMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(mi => mi.GetCustomAttribute<SlashCommandAttribute>() != null).ToArray();
		var commandBuilders = new CommandBuilder[commandMethods.Length];

		for (int i = 0; i < commandMethods.Length; i++) {
			commandBuilders[i] = 
		}
	}

	public static void RegisterStandardCommands<T>(this CommandsNextExtension cne) where T : BaseCommandModule {
		CommandBuilder[] commandBuilders;
		
		var groupAttr = typeof(T).GetCustomAttribute<SlashCommandGroupAttribute>();
		if (groupAttr != null) {
			CommandModuleBuilder moduleBuilder = new CommandModuleBuilder()
				.WithLifespan(typeof(T).GetCustomAttribute<SlashModuleLifespanAttribute>()?.Lifespan switch {
					SlashModuleLifespan.Transient => ModuleLifespan.Transient,
					_ => ModuleLifespan.Singleton
				});

			var groupBuilder = new CommandGroupBuilder();

			commandBuilders = new CommandBuilder[] { groupBuilder };
		} else {
			commandBuilders = BuildStandardCommands(typeof(T));
		}

		cne.RegisterCommands(commandBuilders);
	}

	public static void RegisterSlashCommands<T>(this SlashCommandsExtension sce) where T : ApplicationCommandModule {
		sce.RegisterCommands<T>();
	}
}
