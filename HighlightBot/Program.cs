using DSharpPlus;
using DSharpPlus.CommandsNext;
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
				builder.AddSystemdConsole();
				builder.AddExceptionDemystifyer();
			})
			.ConfigureServices((hbc, isc) => {
				//isc.Configure<DiscordConfiguration>(hbc.Configuration.GetSection("Discord"));
				isc.Configure<ConnectionStringsConfiguration>(hbc.Configuration.GetSection("ConnectionStrings"));

				isc.AddSingleton(isp => {
					var clientConfig = new DiscordConfiguration {
						Token = hbc.Configuration.GetSection("Discord").GetValue<string>("Token"),
						Intents = DiscordIntents.All, // Not sure which one, but there is an intent that is necessary to get the permissions of any user.
						LoggerFactory = isp.GetRequiredService<ILoggerFactory>(),
						MinimumLogLevel = LogLevel.Information,
						MessageCacheSize = 0
					};
					var commandsConfig = new CommandsNextConfiguration() {
						
					};

					commandsConfig.Services = isp;
					commandsConfig.EnableDms = false;
					commandsConfig.EnableMentionPrefix = true;
					
					var client = new DiscordClient(clientConfig);
					client.UseCommandsNext(commandsConfig);

					return client;
				});
				
				isc.AddSingleton(isp => {
				})

				isc.AddSingleton<HttpClient>();

				isc.ConfigureDbContext<HighlightDbContext>();

				isc.AddNotifications().AddDiscord(hbc.Configuration.GetSection("DiscordNotifications"));
			})
			.Build();

		Host = host;

		await using (var dbContext = host.Services.GetRequiredService<FridgeDbContext>()) {
			await dbContext.Database.MigrateAsync();
		}

		var commands = host.Services.GetRequiredService<CommandService>();
		commands.AddModule<FridgeCommandModule>();
		commands.AddTypeParser(new ChannelParser());
		commands.AddTypeParser(new DiscordEmojiParser());

		var discord = host.Services.GetRequiredService<DiscordClient>();

		discord.MessageReactionAdded += (client, ea) => OnReactionModifiedAsync(client, ea.Message, ea.Emoji, true);
		discord.MessageReactionRemoved += (client, ea) => OnReactionModifiedAsync(client, ea.Message, ea.Emoji, false);

		discord.MessageCreated += OnMessageCreatedAsync;

		discord.ClientErrored += (_, eventArgs) => Host.Services.GetRequiredService<NotificationService>().SendNotificationAsync($"Exception in {eventArgs.EventName}", eventArgs.Exception);

		await discord.ConnectAsync();

		await host.RunAsync();
	}
}

public class HighlightDbContext : DbContext {
	
}
