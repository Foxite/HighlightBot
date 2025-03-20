using System.Diagnostics;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Exceptions;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using DSharpPlus.SlashCommands;
using Foxite.Common;
using Foxite.Common.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HighlightBot;

public sealed class Program {
	public static IHost Host { get; private set; }
	public static bool IsDevelopment { get; private set; }

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
			.ConfigureLogging((hbc, builder) => {
				IsDevelopment = hbc.HostingEnvironment.IsDevelopment();
				
				builder.AddExceptionDemystifyer();
			})
			.ConfigureServices((hbc, isc) => {
				//isc.Configure<DiscordConfiguration>(hbc.Configuration.GetSection("Discord"));
				isc.Configure<ConnectionStringsConfiguration>(hbc.Configuration.GetSection("ConnectionStrings"));

				isc.AddSingleton(isp => {
					var clientConfig = new DiscordConfiguration {
						Token = hbc.Configuration.GetSection("Discord").GetValue<string>("Token"),
						Intents = DiscordIntents.GuildMessages | DiscordIntents.Guilds | DiscordIntents.MessageContents,
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

					var slashConfig = new SlashCommandsConfiguration() {
						Services = isp,
					};
					
					client.UseSlashCommands(slashConfig);

					return client;
				});
				
				// If you include this, an exception occurs while disposing it. If the app is crashing, the original exception will be eaten.
				//isc.AddSingleton(isp => isp.GetRequiredService<DiscordClient>().GetCommandsNext());

				isc.AddDbContext<HighlightDbContext>((isp, dbcob) => {
					ConnectionStringsConfiguration connectionStrings = isp.GetRequiredService<IOptions<ConnectionStringsConfiguration>>().Value;
					dbcob.UseNpgsql(connectionStrings.HighlightDbContext);
				});

				isc.AddNotifications().AddDiscord(hbc.Configuration.GetSection("DiscordNotifications"));
			})
			.Build();

		Host = host;

		await using (var scope = Host.Services.CreateAsyncScope()) {
			var dbContext = scope.ServiceProvider.GetRequiredService<HighlightDbContext>();
			await dbContext.Database.MigrateAsync();
		}

		var discord = host.Services.GetRequiredService<DiscordClient>();
		var commands = discord.GetCommandsNext();
		commands.RegisterCommands<HighlightCommandModule>();
		commands.RegisterCommands<IgnoreModule>();

		var slashCommands = discord.GetSlashCommands();
		slashCommands.RegisterCommands<SlashCommandModule>();
		slashCommands.RegisterCommands<SlashIgnoreModule>();
		
		async Task ReportCommandError(CommandContext e, Exception exception) {
			FormattableString errorMessage =
				@$"Exception while executing classic command {e.Command?.Name}
user: {e.User.Id} ({e.User.Username}#{e.User.Discriminator}), bot: {e.User.IsBot}
message: {e.Message.Id} ({e.Message.JumpLink}), type: {e.Message.MessageType?.ToString() ?? "(null)"}, webhook: {e.Message.WebhookMessage}
channel: {e.Channel.Id} ({e.Channel.Name}) <#{e.Channel.Id}>
{(e.Channel.Guild != null ? $"guild: {e.Channel.Guild.Id} ({e.Channel.Guild.Name})" : "")}";
			Host.Services.GetRequiredService<ILogger<Program>>().LogCritical(exception, errorMessage);
			await Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync(errorMessage, exception.Demystify());
			await e.RespondAsync("Internal error; devs notified.");
		}

		commands.CommandErrored += (_, eventArgs) => {
			Host.Services.GetRequiredService<ILogger<Program>>().LogError(eventArgs.Exception, "Error executing classic command");

			return eventArgs.Exception switch {
				CommandNotFoundException => eventArgs.Context.RespondAsync("Unknown command."),
				ChecksFailedException => eventArgs.Context.RespondAsync("All commands must be used within a server."),
				ArgumentException { Message: "Could not find a suitable overload for the command." } => eventArgs.Context.RespondAsync("Invalid arguments."),
				_ => ReportCommandError(eventArgs.Context, eventArgs.Exception),
			};
		};

		discord.MessageCreated += OnMessageCreatedAsync;

		discord.ClientErrored += (_, eventArgs) => Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync($"Exception in {eventArgs.EventName}\nFurther details unknown", eventArgs.Exception);

		async Task ReportSlashCommandError(InteractionContext e, Exception exception) {
			FormattableString errorMessage =
				@$"Exception while executing slash command {e.QualifiedName}
user: {e.User.Id} ({e.User.Username}#{e.User.Discriminator}), bot: {e.User.IsBot}
interaction id: {e.Interaction.Id}
channel: {e.Channel.Id} ({e.Channel.Name}) <#{e.Channel.Id}>
{(e.Channel.Guild != null ? $"guild: {e.Channel.Guild.Id} ({e.Channel.Guild.Name})" : "")}";
			Host.Services.GetRequiredService<ILogger<Program>>().LogCritical(exception, errorMessage);
			await Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync(errorMessage, exception.Demystify());
			await e.CreateResponseAsync("Internal error; devs notified.");
		}

		slashCommands.SlashCommandErrored += (_, eventArgs) => {
			Host.Services.GetRequiredService<ILogger<Program>>().LogError(eventArgs.Exception, "Error executing slash command");
			
			return eventArgs.Exception switch {
				SlashExecutionChecksFailedException => eventArgs.Context.CreateResponseAsync("All commands must be used within a server."),
				ArgumentException { Message: "Could not find a suitable overload for the command." } => eventArgs.Context.CreateResponseAsync("Invalid arguments."),
				_ => ReportSlashCommandError(eventArgs.Context, eventArgs.Exception),
			};
		};

		await discord.ConnectAsync();

		await host.RunAsync();
	}

	private static Task OnMessageCreatedAsync(DiscordClient discord, MessageCreateEventArgs e) {
		if (e.Guild != null) {
			_ = Task.Run(async () => {
				try {
					List<UserIdAndTerm> allTerms;
					await using (var scope = Host.Services.CreateAsyncScope()) {
						var dbContext = scope.ServiceProvider.GetRequiredService<HighlightDbContext>();

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
							await dbContext.SaveChangesAsync();
						} catch (DbUpdateConcurrencyException) {
							// Happens when the author does not have a user entry in the database, in which case we don't care.
							// If it happens because the database was actually concurrently, we also don't care.
						}

						string content = e.Message.Content;
						DateTime currentTime = DateTime.UtcNow;
						DateTime fiveMinutesAgo = DateTime.UtcNow - TimeSpan.FromMinutes(IsDevelopment ? 0 : 5);
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
								term.Display,
								term.IsCaseSensitive,
							})
							.AsEnumerable()
							.Where(term => Regex.IsMatch(content, term.Regex, term.IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
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
						
						// This is separate because otherwise, a DbConcurrencyException from earlier may re-occur here and prevent an actually useful update.
						await using (var scope = Host.Services.CreateAsyncScope()) {
							var dbContext = scope.ServiceProvider.GetRequiredService<HighlightDbContext>();
							foreach (IGrouping<ulong, string> grouping in termsByUser) {
								var attachedUser = new HighlightUser() {
									DiscordUserId = grouping.Key,
									DiscordGuildId = guild.Id
								};

								dbContext.Users.Attach(attachedUser);
								attachedUser.LastDM = DateTime.UtcNow;
							}
							
							await dbContext.SaveChangesAsync();
						}
					}
				} catch (Exception ex) {
					FormattableString errorMessage =
						@$"Exception in OnMessageCreated
user: {e.Author.Id} ({e.Author.Username}#{e.Author.Discriminator}), bot: {e.Author.IsBot}
message: {e.Message.Id} ({e.Message.JumpLink}), type: {e.Message.MessageType?.ToString() ?? "(null)"}, webhook: {e.Message.WebhookMessage}
channel: {e.Channel.Id} ({e.Channel.Name})\n
{(e.Channel.Guild != null ? $"guild: {e.Channel.Guild.Id} ({e.Channel.Guild.Name})" : "")}";
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
