using System.Diagnostics;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Foxite.Common;
using Foxite.Common.Notifications;
using Microsoft.Extensions.Logging;

namespace HighlightBot.Services; 

public class MessageHandler {
	private readonly ILogger<MessageHandler> m_Logger;
	private readonly TermFinder m_TermFinder;
	private readonly DiscordClient m_Discord;
	private readonly NotificationService m_NotificationService;
	private readonly HighlightDbContext m_DbContext;

	public MessageHandler(ILogger<MessageHandler> logger, TermFinder termFinder, DiscordClient discord, NotificationService notificationService, HighlightDbContext dbContext) {
		m_Logger = logger;
		m_TermFinder = termFinder;
		m_Discord = discord;
		m_NotificationService = notificationService;
		m_DbContext = dbContext;
	}
	
	public async Task HandleMessage(MessageCreateEventArgs e) {
		List<UserIdAndTerm> allTerms = await m_TermFinder.FindTerms(e);

		if (allTerms.Count == 0) {
			return;
		}

		DiscordGuild guild = m_Discord.Guilds[e.Guild.Id];
		IReadOnlyList<DiscordMessage> lastMessages = await e.Channel.GetMessagesBeforeAsync(e.Message.Id + 1, 5); // Maybe use the message cache here?

		var notificationEmbed = new DiscordEmbedBuilder() {
			Author = new DiscordEmbedBuilder.EmbedAuthor() {
				IconUrl = (e.Author as DiscordMember)?.GuildAvatarUrl ?? e.Author.AvatarUrl,
				Name = (e.Author as DiscordMember)?.DisplayName ?? e.Author.Username
			},
			Color = new Optional<DiscordColor>(DiscordColor.Yellow),
			Description = string.Join('\n', lastMessages.Backwards().Select(message => $"{Formatter.Bold($"[{message.CreationTimestamp.ToUniversalTime():T}] {(message.Author as DiscordMember)?.DisplayName ?? message.Author.Username}:")} {message.Content.Ellipsis(500)}")),
			Timestamp = DateTimeOffset.UtcNow,
		};
		notificationEmbed = notificationEmbed
			.AddField("Source message", $"{Formatter.MaskedUrl("Jump to", e.Message.JumpLink)}");

		List<IGrouping<ulong, string>> termsByUser = allTerms.GroupBy(userIdAndTerm => userIdAndTerm.DiscordUserId, userIdAndTerm => userIdAndTerm.Value).ToList();
		foreach (IGrouping<ulong, string> grouping in termsByUser) {
			DiscordMember? target = null;
			try {
				target = await guild.GetMemberAsync(grouping.Key);
				if (target == null) {
					continue;
				}

				Permissions targetPermissions = target.PermissionsIn(e.Channel);
				if ((targetPermissions & Permissions.AccessChannels) == 0 || (targetPermissions & Permissions.ReadMessageHistory) == 0) {
					continue;
				}

				List<string> terms = grouping.ToList();
				var discordMessageBuilder = new DiscordMessageBuilder() {
					Content = $"In {Formatter.Bold(guild.Name)} {e.Channel.Mention}, you were mentioned with the highlighted word{(terms.Count > 1 ? "s" : "")} {Util.JoinOxfordComma(terms)}",
					Embed = notificationEmbed
				};
				await target.SendMessageAsync(discordMessageBuilder);
			} catch (Exception ex) {
				if (ex is UnauthorizedException or NotFoundException) {
					// 404: User left guild or account was deleted, don't care
					// 403: User blocked bot or does not allow DMs, either way, don't care
					continue;
				}

				FormattableString errorMessage = $"Couldn't DM {grouping.Key} ({target?.Username}#{target?.Discriminator})";
				m_Logger.LogCritical(ex, errorMessage);
				await m_NotificationService.SendNotificationAsync(errorMessage, ex.Demystify());
			}
		}

		// This is separate because otherwise, a DbConcurrencyException from earlier may re-occur here and prevent an actually useful update.
		foreach (IGrouping<ulong, string> grouping in termsByUser) {
			var attachedUser = new HighlightUser() {
				DiscordUserId = grouping.Key,
				DiscordGuildId = guild.Id
			};

			m_DbContext.Users.Attach(attachedUser);
			attachedUser.LastDM = DateTime.UtcNow;
		}

		await m_DbContext.SaveChangesAsync();
	}
}

public class UserIdAndTerm {
	// Would use an anonymous class here but that requires you to use "var", which prevents you from declaring the variable somewhere else.
	public ulong DiscordUserId { get; set; }
	public string Value { get; set; }
}
