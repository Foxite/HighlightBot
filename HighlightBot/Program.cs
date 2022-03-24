using System.Diagnostics;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Exceptions;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Foxite.Common;
using Foxite.Common.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HighlightBot;

public sealed class Program {
	public static IHost Host { get; set; }

	private static IHostBuilder CreateHostBuilder(string[] args) =>
		Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
			.ConfigureAppConfiguration((hostingContext, configuration) => {
				configuration.Sources.Clear();

				configuration
					.AddJsonFile("appsettings.json", true, true)
					.AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", true, true)
					.AddEnvironmentVariables("HIGHLIGHT_")
					.AddCommandLine(args);
			});

	private static async Task Main(string[] args) {
		using IHost host = CreateHostBuilder(args)
			.ConfigureLogging((_, builder) => {
				builder.AddExceptionDemystifyer();
			})
			.ConfigureServices((hbc, isc) => {
				//isc.Configure<DiscordConfiguration>(hbc.Configuration.GetSection("Discord"));
				isc.Configure<ConnectionStringsConfiguration>(hbc.Configuration.GetSection("ConnectionStrings"));

				isc.AddSingleton(isp => {
					var clientConfig = new DiscordConfiguration {
						Token = hbc.Configuration.GetSection("Discord").GetValue<string>("Token"),
						Intents = DiscordIntents.GuildMessages | DiscordIntents.Guilds,
						LoggerFactory = isp.GetRequiredService<ILoggerFactory>(),
						MinimumLogLevel = LogLevel.Information,
					};
					
					var client = new DiscordClient(clientConfig);
					
					var commandsConfig = new CommandsNextConfiguration {
						Services = isp,
						EnableDms = false,
						EnableMentionPrefix = true,
					};

					client.UseCommandsNext(commandsConfig);

					return client;
				});
				
				isc.AddSingleton(isp => isp.GetRequiredService<DiscordClient>().GetCommandsNext());

				isc.ConfigureDbContext<HighlightDbContext>();

				isc.AddNotifications().AddDiscord(hbc.Configuration.GetSection("DiscordNotifications"));
			})
			.Build();

		Host = host;

		await using (var dbContext = host.Services.GetRequiredService<HighlightDbContext>()) {
			await dbContext.Database.MigrateAsync();
		}

		var commands = host.Services.GetRequiredService<CommandsNextExtension>();
		commands.RegisterCommands<HighlightCommandModule>();

		commands.CommandErrored += (_, eventArgs) => {
			return eventArgs.Exception switch {
				CommandNotFoundException => eventArgs.Context.RespondAsync("Unknown command."),
				ChecksFailedException => eventArgs.Context.RespondAsync("Checks failed 🙁"),
				_ => Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync("Exception while executing command", eventArgs.Exception).ContinueWith(t => eventArgs.Context.RespondAsync("Internal error; devs notified."))
			};
		};

		var discord = host.Services.GetRequiredService<DiscordClient>();

		discord.MessageCreated += OnMessageCreatedAsync;

		discord.ClientErrored += (_, eventArgs) => Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync($"Exception in {eventArgs.EventName}", eventArgs.Exception);

		await discord.ConnectAsync();

		await host.RunAsync();
	}

	private static readonly object m_DatabaseLock = new();

	private static Task OnMessageCreatedAsync(DiscordClient discord, MessageCreateEventArgs e) {
		if (e.Guild != null && !e.Author.IsBot) {
			_ = Task.Run(async () => {
				try {
					List<UserIdAndTerm> allTerms;
					lock (m_DatabaseLock) {
						using var dbContext = Host.Services.GetRequiredService<HighlightDbContext>();
						// Create a "fake" entity object and attach it to EF, then modify it and save the changes.
						// This allows to update an entity without querying for it in the database.
						// This is useful, because this function is expected to run a lot.
						// Note that TODO we still need to implement in-memory caching for the database
						// https://stackoverflow.com/a/58367364
						var author = new HighlightUser() {
							DiscordGuildId = e.Guild.Id,
							DiscordUserId = e.Author.Id,
						};
						dbContext.Attach(author);
						author.LastActivity = DateTime.UtcNow;

						try {
							dbContext.SaveChanges();
						} catch (DbUpdateConcurrencyException) {
							// Happens when the author does not have a user entry in the database, in which case we don't care.
							// If it happens because the database was actually concurrently, we also don't care.
						}

						string content = e.Message.Content.ToLowerInvariant();
						DateTime currentTime = DateTime.UtcNow;
						allTerms = dbContext.Terms
							.Where(term =>
								term.User.DiscordGuildId == e.Guild.Id &&
								term.User.DiscordUserId != e.Author.Id &&
								term.User.LastActivity + term.User.HighlightDelay < currentTime &&
								EF.Functions.Like(content, "%" + term.Value + "%") && // TODO escape pattern
								!term.User.IgnoredChannels.Any(huic => huic.ChannelId == e.Channel.Id))
							.Select(term => new UserIdAndTerm() {
								Value = term.Value,
								DiscordUserId = term.User.DiscordUserId
							})
							.ToList();
					}

					DiscordGuild guild = discord.Guilds[e.Guild.Id];
					var sender = (DiscordMember) e.Author;
					IReadOnlyList<DiscordMessage> lastMessages = await e.Channel.GetMessagesBeforeAsync(e.Message.Id + 1, 5);

					var notificationEmbed = new DiscordEmbedBuilder() {
						Author = new DiscordEmbedBuilder.EmbedAuthor() {
							IconUrl = sender.GuildAvatarUrl ?? sender.AvatarUrl,
							Name = sender.DisplayName
						},
						Color = new Optional<DiscordColor>(DiscordColor.Yellow),
						Description = string.Join('\n', lastMessages.Select(message => $"{Formatter.Bold($"[{message.CreationTimestamp.ToUniversalTime():T}] {(message.Author as DiscordMember)?.DisplayName ?? message.Author.Username}:")} {message.Content.Ellipsis(500)}")),
						Timestamp = DateTimeOffset.UtcNow,
					};
					notificationEmbed = notificationEmbed
						.AddField("Source message", $"{Formatter.MaskedUrl("Jump to", e.Message.JumpLink)}");

					foreach (IGrouping<ulong, string> grouping in allTerms.GroupBy(userIdAndTerm => userIdAndTerm.DiscordUserId, userIdAndTerm => userIdAndTerm.Value)) {
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
							await target.SendMessageAsync(
								new DiscordMessageBuilder() {
									Content = $"In {Formatter.Bold(guild.Name)} {e.Channel.Mention}, you were mentioned with the highlighted word{(terms.Count > 1 ? "s" : "")} {Util.JoinOxfordComma(terms)}",
									Embed = notificationEmbed
								}
							);
						} catch (Exception ex) {
							FormattableString errorMessage = $"Couldn't DM {grouping.Key} ({target?.Username}#{target?.Discriminator})";
							Host.Services.GetRequiredService<ILogger<Program>>().LogCritical(ex, errorMessage);
							await Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync(errorMessage, ex.Demystify());
						}
					}
				} catch (Exception ex) {
					FormattableString errorMessage =
						@$"Exception in OnMessageCreated
{e.Author.Id} ({e.Author.Username}#{e.Author.Discriminator}), bot: {e.Author.IsBot}
message: {e.Message.Id} ({e.Message.JumpLink}), type: {e.Message.MessageType?.ToString() ?? "(null)"}, webhook: {e.Message.WebhookMessage}
channel {e.Channel.Id} ({e.Channel.Name})\n
{(e.Channel.Guild != null ? $"guild {e.Channel.Guild.Id} ({e.Channel.Guild.Name})" : "")}";
					Host.Services.GetRequiredService<ILogger<Program>>().LogCritical(ex, errorMessage);
					await Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync(errorMessage, ex.Demystify());
				}
			});
		}

		return Task.CompletedTask;
	}

	private class UserIdAndTerm {
		// Would use an anonymous class here but that requires you to use "var", which prevents you from declaring the variable somewhere else.
		public ulong DiscordUserId { get; set; }
		public string Value { get; set; }
	}
}
