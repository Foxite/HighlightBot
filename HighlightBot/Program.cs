using System.Diagnostics;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Exceptions;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using Foxite.Common;
using Foxite.Common.Notifications;
using HighlightBot.Services;
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

		commands.CommandErrored += (_, eventArgs) => {
			Host.Services.GetRequiredService<ILogger<Program>>().LogError(eventArgs.Exception, "Error executing classic command");

			return eventArgs.Exception switch {
				CommandNotFoundException => eventArgs.Context.RespondAsync("Unknown command."),
				ChecksFailedException => eventArgs.Context.RespondAsync("Checks failed 🙁"),
				ArgumentException { Message: "Could not find a suitable overload for the command." } => eventArgs.Context.RespondAsync("Invalid arguments."),
				_ => Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync("Exception while executing command", eventArgs.Exception).ContinueWith(t => eventArgs.Context.RespondAsync("Internal error; devs notified."))
			};
		};

		discord.MessageCreated += OnMessageCreatedAsync;

		discord.ClientErrored += (_, eventArgs) => Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync($"Exception in {eventArgs.EventName}", eventArgs.Exception);

		slashCommands.SlashCommandErrored += (_, eventArgs) => {
			Host.Services.GetRequiredService<ILogger<Program>>().LogError(eventArgs.Exception, "Error executing slash command");
			
			return eventArgs.Exception switch {
				SlashExecutionChecksFailedException => eventArgs.Context.CreateResponseAsync("Checks failed 🙁"),
				ArgumentException { Message: "Could not find a suitable overload for the command." } => eventArgs.Context.CreateResponseAsync("Invalid arguments."),
				_ => Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync("Exception while executing command", eventArgs.Exception).ContinueWith(t => eventArgs.Context.CreateResponseAsync("Internal error; devs notified."))
			};
		};

		await discord.ConnectAsync();

		await host.RunAsync();
	}

	private static Task OnMessageCreatedAsync(DiscordClient discord, MessageCreateEventArgs e) {
		if (e.Guild == null) {
			return Task.CompletedTask;
		}

		_ = Task.Run(async () => {
			try {
				await using var scope = Host.Services.CreateAsyncScope();
				var messageHandler = scope.ServiceProvider.GetRequiredService<MessageHandler>();
					
				await messageHandler.HandleMessage(e);
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

		return Task.CompletedTask;
	}
}