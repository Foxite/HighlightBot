using System.Diagnostics;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Exceptions;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
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
						AlwaysCacheMembers = false
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
				
				// If you include this, an exception occurs while disposing it. If the app is crashing, the original exception will be eaten.
				//isc.AddSingleton(isp => isp.GetRequiredService<DiscordClient>().GetCommandsNext());

				isc.ConfigureDbContext<HighlightDbContext>();

				isc.AddNotifications().AddDiscord(hbc.Configuration.GetSection("DiscordNotifications"));
			})
			.Build();

		Host = host;

		await using (var dbContext = host.Services.GetRequiredService<HighlightDbContext>()) {
			await dbContext.Database.MigrateAsync();
		}

		var discord = host.Services.GetRequiredService<DiscordClient>();
		var commands = discord.GetCommandsNext();
		commands.RegisterCommands<HighlightCommandModule>();
		commands.RegisterCommands<IgnoreModule>();

		commands.CommandErrored += (_, eventArgs) => {
			return eventArgs.Exception switch {
				CommandNotFoundException => eventArgs.Context.RespondAsync("Unknown command."),
				ChecksFailedException => eventArgs.Context.RespondAsync("Checks failed 🙁"),
				ArgumentException { Message: "Could not find a suitable overload for the command." } => eventArgs.Context.RespondAsync("Invalid arguments."),
				_ => Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync("Exception while executing command", eventArgs.Exception).ContinueWith(t => eventArgs.Context.RespondAsync("Internal error; devs notified."))
			};
		};

		discord.MessageCreated += OnMessageCreatedAsync;

		discord.ClientErrored += (_, eventArgs) => Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync($"Exception in {eventArgs.EventName}", eventArgs.Exception);

		await discord.ConnectAsync();

		await host.RunAsync();
	}

	private static readonly object m_DatabaseLock = new();

	private static Task OnMessageCreatedAsync(DiscordClient discord, MessageCreateEventArgs e) {
		if (e.Guild != null) {
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
						DateTime fiveMinutesAgo = DateTime.UtcNow - TimeSpan.FromMinutes(5);
						allTerms = dbContext.Terms
							.Where(term =>
								term.User.DiscordGuildId == e.Guild.Id &&
								term.User.DiscordUserId != e.Author.Id &&
								(!(e.Author.IsBot && term.User.IgnoreBots)) &&
								(!(e.Channel.IsNSFW && term.User.IgnoreNsfw)) &&
								term.User.LastActivity + term.User.HighlightDelay < currentTime &&
								term.User.LastDM < fiveMinutesAgo &&
								!term.User.IgnoredChannels.Any(huic => huic.ChannelId == e.Channel.Id) &&
								!term.User.IgnoredUsers.Any(huiu => huiu.IgnoredUserId == e.Author.Id)
							)
							.Select(term => new {
								term.User.DiscordUserId,
								term.User.DiscordGuildId,
								term.Regex,
								term.Display
							})
							.AsEnumerable()
							// TODO TEMPORARY HACK! this does not scale.
							// Find a way to get PCRE regexes going in the database (postgres seems to use POSIX regexes)
							.Where(term => Regex.IsMatch(content, term.Regex, RegexOptions.IgnoreCase))
							.Select(term => new UserIdAndTerm() {
								DiscordUserId = term.DiscordUserId,
								Value = term.Display
							})
							.ToList();
					}

					if (allTerms.Count > 0) {
						DiscordGuild guild = discord.Guilds[e.Guild.Id];
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
								Host.Services.GetRequiredService<ILogger<Program>>().LogCritical(ex, errorMessage);
								await Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync(errorMessage, ex.Demystify());
							}
						}

						lock (m_DatabaseLock) {
							using var dbContext = Host.Services.GetRequiredService<HighlightDbContext>();
							foreach (IGrouping<ulong, string> grouping in termsByUser) {
								var attachedUser = new HighlightUser() {
									DiscordUserId = grouping.Key,
									DiscordGuildId = guild.Id
								};

								dbContext.Users.Attach(attachedUser);
								attachedUser.LastActivity = DateTime.UtcNow;
								attachedUser.LastDM = DateTime.UtcNow;
							}

							dbContext.SaveChanges();
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
