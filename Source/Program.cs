using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Meebey.SmartIrc4net;
using Newtonsoft.Json;

using Discord;
using Discord.WebSocket;
using Discord.Webhook;

namespace Discord_IRC_Sharp
{
    class Program
    {
        static void Main(string[] args) => new Program().Run().GetAwaiter().GetResult();
        
        static RelayConfig config;
        
        // IRC
        static IrcClient irc = new IrcClient();

        // Discord
        static DiscordSocketClient discord;
        static SocketGuild guild;
        static Dictionary<string, SocketTextChannel> discordChannels = new Dictionary<string, SocketTextChannel>();
        static Dictionary<string, DiscordWebhookClient> discordWebhooks = new Dictionary<string, DiscordWebhookClient>();
        static Dictionary<ulong, string> webhookAvatars = new Dictionary<ulong, string>();
        static bool isDiscordReady;
        static Dictionary<string, int> discordColours = new Dictionary<string, int>();

        public async Task Run()
        {
            // Load the config
            Log.Write("Loading configuration...");
            if(File.Exists("config.json")) { 
                string json = File.ReadAllText("config.json");
                config = JsonConvert.DeserializeObject<RelayConfig>(json);
                Log.Write("Configuration loaded!");
            }
            else {
                // Create a blank config
                config = new RelayConfig();
                File.WriteAllText("config.json", JsonConvert.SerializeObject(config, Formatting.Indented));
                Log.Write("No configuration file found! A blank config has been written to config.json");
                return;
            }

            // Connect to Discord
            Log.Write("Connecting to Discord...");
            discord = new DiscordSocketClient();
            discord.Log += (LogMessage msg) => {
                Log.Write("Discord.NET: " + msg.Message);
                return Task.CompletedTask;
            };
            discord.Ready += () => { 
                isDiscordReady = true;
                return Task.CompletedTask;
            };
            discord.MessageReceived += OnDiscordMessage;
            await discord.LoginAsync(TokenType.Bot, config.discordToken);
            await discord.StartAsync();

            while(!isDiscordReady);

            // Check for the Discord server and channels
            guild = discord.GetGuild(config.discordServerId);
            if(guild == null) {
                Log.Write("Unable to find the specified Discord server! Check your configuration.");
                return;
            }
            else 
                Log.Write("Successfully found Discord server: " + guild.Name);

            foreach(var channel in config.channels) {
                SocketTextChannel discordChannel = (SocketTextChannel)discord.GetChannel(channel.Value);
                if(discordChannel != null) {
                    Log.Write("Found Discord channel: " + discordChannel.Name);

                    // Add the channel or webhook according to the config
                    if(config.formatting.useWebhooks) {
                        // Get or create the webhook
                        var webhook = discordChannel.GetWebhooksAsync().Result.FirstOrDefault(x => x.Name == "Discord-IRC-Relay");
                        if(webhook == null)
                            webhook = await discordChannel.CreateWebhookAsync("Discord-IRC-Relay");
                        
                        // Add the webhook
                        discordWebhooks.Add(channel.Key, new DiscordWebhookClient(webhook));
                    }
                    else
                        discordChannels.Add(channel.Key, discordChannel);
                }
                else
                    Log.Write("Could not find Discord channel: " + channel.Value);
            }

            
            // Connect to IRC server
            Log.Write("Connecting to IRC...");
            irc.Connect(config.IRCIp, config.IRCport);
            irc.Login(config.IRCNickname, config.IRCNickname);

            // Join IRC channels
            foreach(var channel in config.channels) {
                irc.RfcJoin(channel.Key, Priority.High);
                Log.Write($"Joined IRC channel: {channel.Key}");
            }

            // Listen for messages
            irc.OnChannelMessage += OnIRCMessage;
            Log.Write("Connected!");
            irc.Listen(true);
        
            await Task.Delay(-1);   // This shouldn't be reached (I think) but it's here just in case
        }

        private Task OnDiscordMessage(SocketMessage message)
        {
            // If the message was from a bot
            // TODO: Make this configurable and only check for self if disabled
            if(message.Author.IsBot)
                return Task.CompletedTask;

            // If the message was sent in a channel we don't care about
            if(!config.channels.ContainsValue(message.Channel.Id))
                return Task.CompletedTask;

            // Get the username colour for IRC
            int colour = 0;
            discordColours.TryGetValue(message.Author.Username, out colour);
            if(colour == 0) { // We didn't get a value
                colour = new Random().Next(1, 15);
                discordColours.Add(message.Author.Username, colour);
            }

            // Store the message content for it to be modified later
            string ircMessage = $"<{(config.formatting.nicknameColours ? $"{colour}" : "")}{message.Author.Username}> {message.Content}";

            // Send the message to IRC
            string ircChannel = config.channels.FirstOrDefault(x => x.Value == message.Channel.Id).Key;
            if(ircChannel == null) // If we failed to get the IRC channel
                return Task.CompletedTask;
            irc.SendMessage(SendType.Message, ircChannel, ircMessage);

            return Task.CompletedTask;
        }

        private static void OnIRCMessage(object sender, IrcEventArgs e)
        {
            // Store the message content for it to be modified later
            string messageContent = e.Data.Message.Replace("@", "(at)");

            // Check for user mentions
            if(config.formatting.ircMentionsDiscord) {
                string firstWord = e.Data.Message.Split(' ').FirstOrDefault();
                if(firstWord != null && firstWord.EndsWith(':')) { // If it's a mention
                    // Search for the user
                    SocketUser user = discord.GetGuild(config.discordServerId).Users.FirstOrDefault(x => x.Username.ToLower() == firstWord.Replace(":", "").ToLower());
                    if(user != null)
                        messageContent = messageContent.Replace(firstWord, user.Mention);
                }
            }

            // Send the message to Discord

            if(config.formatting.useWebhooks) {
                // Get the avatar, if applicable
                string avatarUrl = null;
                SocketUser user = discord.GetGuild(config.discordServerId).Users.FirstOrDefault(x => x.Username.ToLower() == e.Data.Nick.ToLower());
                if(user != null)
                    avatarUrl = user.GetAvatarUrl();

                // Send the message
                discordWebhooks[e.Data.Channel].SendMessageAsync(messageContent, username: e.Data.Nick, avatarUrl: avatarUrl);
                return;
            }
            else {
                // Get the associated Discord channel
                SocketTextChannel discordChannel = discordChannels[e.Data.Channel];
                if(discordChannel == null) { 
                    Log.Write($"Could not get Discord channel for #{e.Data.Channel} on IRC");
                    return;
                }
                discordChannel.SendMessageAsync($"{config.formatting.discordPrefix.Replace("%u", e.Data.Nick)} {messageContent}");
            }
        }
    }

    class RelayConfig
    {
        // Discord stuff
        public string discordToken { get; set; } = "TOKEN";
        public ulong discordServerId { get; set; } = 0;

        // IRC stuff
        public string IRCNickname { get; set; } = "Discord-IRC";
        public string IRCIp { get; set; } = "irc.example.com";
        public int IRCport { get; set; } = 6667;
        public Dictionary<string, ulong> channels { get; set; } = new Dictionary<string, ulong>();
        public FormattingConfig formatting { get; set; } = new FormattingConfig();
    }

    class FormattingConfig
    {
        public string discordPrefix { get; set; } = "**<%u/IRC>**";
        public bool nicknameColours { get; set; } = true;
        public bool ircMentionsDiscord { get; set; } = false;
        public bool useWebhooks { get; set; } = false;
    }
}
