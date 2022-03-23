# HighlightBot
CarlBot recently disabled !hl for non-moderators, so here is !hl packed in a selfhostable bot.

## Docker deployment
0. Bot user, as usual. It needs the message content intent. When joining it into a server, make sure it has permissions to read message history in every channel. If a user does not have that permission in any channel, the bot will not DM them for messages said in those channels.
1. Copy postgres.env.example and highlightbot.env.example and remove the .example suffixes. Add the bot token to the env file, and optionally the webhook where errors should be sent.
2. If using the docker-compose file, make sure to pick the prod variant because it doesn't publish the database ports. This is useful during testing but a major security risk in production. Alternatively, if you want to use your own database, modify the connection string. (Note that only Postgres works properly right now, adding support for others is easy but requires getting your hands dirty.)
3. `docker-compose up`

## Usage
To use any command, mention the bot followed by the command. There is currently no way to set a command prefix or use slash commands.

These are the commands:
- show
- add
- remove/rm
- clear
- delay
- ignore

Add highlighted terms using `add`. If anyone says one of those terms in any channel you have access to, and you haven't said anything for a certain amount of time (30 minutes by default, change using `delay`), the bot will DM you about it.

If you want to ignore a specific channel you can use `ignore` followed by the mention of the channel.
