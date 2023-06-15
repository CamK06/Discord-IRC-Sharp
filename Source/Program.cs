using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
        static Dictionary<string, ulong> discordWebhookIDs = new Dictionary<string, ulong>();
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
            discord.MessageUpdated += OnMessageUpdated;
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
                        var webhook = discordChannel.GetWebhooksAsync().Result.FirstOrDefault(x => x.Name == "Shitcord-IRC-Relay");
                        if(webhook == null)
                            webhook = await discordChannel.CreateWebhookAsync("Shitcord-IRC-Relay");
                        
                        // Add the webhook
                        discordWebhooks.Add(channel.Key.ToLower(), new DiscordWebhookClient(webhook));
                        discordWebhookIDs.Add(channel.Key.ToLower(), webhook.Id);
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
            irc.CtcpVersion = "Discord-IRC-Sharp by Cam K. https://github.com/CamK06/Discord-IRC-Sharp";
            Log.Write("Connected!");
            irc.Listen(true);

            // Nickserv
            if(config.useNickServ)
                irc.RfcPrivmsg("NickServ", $"IDENTIFY {config.nickServPass}", Priority.Medium);

            await Task.Delay(-1);   // This shouldn't be reached (I think) but it's here just in case
        }

        private Task OnMessageUpdated(Cacheable<IMessage, ulong> cacheable, SocketMessage message, ISocketMessageChannel channel)
        {
            // If the message was from a bot, ignore it to avoid spam
            if(message.Author.IsBot)
                return Task.CompletedTask;

            // If the message was in a channel we don't care about
            if(!config.channels.ContainsValue(channel.Id))
                return Task.CompletedTask;

            // Get the username colour for IRC
            string username = guild.GetUser(message.Author.Id).Nickname ?? message.Author.Username;
            int colour = 0;
            discordColours.TryGetValue(username, out colour);
            if(colour == 0) { // We didn't get a value
                colour = new Random().Next(2, 13);
                discordColours.Add(username, colour);
            }

            // Store the message content for it to be modified later
            string ircMessage = WithCleanEmoteNames($"<EDIT> <{(config.formatting.nicknameColours ? $"{colour}" : "")}{username}> {message.Content}");

            // Send the message to IRC
            string ircChannel = config.channels.FirstOrDefault(x => x.Value == message.Channel.Id).Key;
            if(ircChannel == null) // If we failed to get the IRC channel
                return Task.CompletedTask;

            if(!string.IsNullOrWhiteSpace(message.Content)) {
                if(message.Content.Contains('\n')) {
                    foreach(string line in message.Content.Split('\n'))
                        if(!string.IsNullOrWhiteSpace(line))
                            irc.SendMessage(SendType.Message, ircChannel, $"<EDIT> <{(config.formatting.nicknameColours ? $"{colour}" : "")}{username}> {line}");
                }
                else
                    irc.SendMessage(SendType.Message, ircChannel, ircMessage);
            }

            // Search for links within the message
            HttpClient client = new HttpClient();
            HttpResponseMessage msg;
            foreach(string word in message.Content.Split(' ')) {
                if(!Uri.IsWellFormedUriString(word, UriKind.Absolute))
                    break;
                
                msg = client.GetAsync(word).Result;
                if(msg.StatusCode != HttpStatusCode.OK)
                    continue;

                if(!msg.Content.Headers.ContentType.ToString().Contains("text/html"))
                    irc.SendMessage(SendType.Message, ircChannel, $"^ [{msg.Content.Headers.ContentType}] ({msg.Content.Headers.ContentLength/1000.0f}KiB)");
                else {
                    string title = Regex.Match(msg.Content.ReadAsStringAsync().Result, @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>", RegexOptions.IgnoreCase).Groups["Title"].Value;   
                    if(!string.IsNullOrWhiteSpace(title))
                        irc.SendMessage(SendType.Message, ircChannel, $"^ {title}");
                }
            }

            return Task.CompletedTask;
        }

        private Task OnDiscordMessage(SocketMessage message)
        {
            // If the message was from a bot
            // TODO: Make this configurable
            if(message.Author.IsWebhook)
                if(discordWebhookIDs.ContainsValue((message.Author).Id))
                    return Task.CompletedTask;

            // If the message was sent in a channel we don't care about
            if(!config.channels.ContainsValue(message.Channel.Id))
                return Task.CompletedTask;

            // Get the username colour for IRC
            string username = guild.GetUser(message.Author.Id).Nickname ?? message.Author.Username;
            int colour = 0;
            discordColours.TryGetValue(username, out colour);
            if(colour == 0) { // We didn't get a value
                colour = new Random().Next(2, 13);
                discordColours.Add(username, colour);
            }

            // Store the message content for it to be modified later
            string ircMessage = WithCleanEmoteNames($"<{(config.formatting.nicknameColours ? $"{colour}" : "")}{username}> {message.Content}");

            // Send the message to IRC
            string ircChannel = config.channels.FirstOrDefault(x => x.Value == message.Channel.Id).Key;
            if(ircChannel == null) // If we failed to get the IRC channel
                return Task.CompletedTask;

            if(!string.IsNullOrWhiteSpace(message.Content)) {
                if(message.Content.Contains('\n')) {
                    foreach(string line in message.Content.Split('\n'))
                        if(!string.IsNullOrWhiteSpace(line))
                            irc.SendMessage(SendType.Message, ircChannel, $"<{(config.formatting.nicknameColours ? $"{colour}" : "")}{username}> {line}");
                }
                else
                    irc.SendMessage(SendType.Message, ircChannel, ircMessage);
            }

            // Send attachments if applicable
            HttpClient client = new HttpClient();
            HttpResponseMessage msg;
            if(message.Attachments != null) {
                foreach(var attachment in message.Attachments) {
                    irc.SendMessage(SendType.Message, ircChannel, $"<{(config.formatting.nicknameColours ? $"{colour}" : "")}{username}> {attachment.Url}");
                    msg = client.GetAsync(attachment.Url).Result;
                    if(msg.StatusCode == HttpStatusCode.OK)
                        irc.SendMessage(SendType.Message, ircChannel, $"^ [{msg.Content.Headers.ContentType}] ({msg.Content.Headers.ContentLength/1000.0f}KiB)");
                }
            }

            // Search for links within the message
            foreach(string word in message.Content.Split(' ')) {
                if(!Uri.IsWellFormedUriString(word, UriKind.Absolute))
                    break;
                
                msg = client.GetAsync(word).Result;
                if(msg.StatusCode != HttpStatusCode.OK)
                    continue;

                if(!msg.Content.Headers.ContentType.ToString().Contains("text/html"))
                    irc.SendMessage(SendType.Message, ircChannel, $"^ [{msg.Content.Headers.ContentType}] ({msg.Content.Headers.ContentLength/1000.0f}KiB)");
                else {
                    string title = Regex.Match(msg.Content.ReadAsStringAsync().Result, @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>", RegexOptions.IgnoreCase).Groups["Title"].Value;   
                    if(!string.IsNullOrWhiteSpace(title))
                        irc.SendMessage(SendType.Message, ircChannel, $"^ {title}");
                }
            }

            return Task.CompletedTask;
        }

        private static void OnIRCMessage(object sender, IrcEventArgs e)
        {
            // Store the message content for it to be modified later
            string messageContent = WithEmotes(e.Data.Message.Replace("@", "(at)"));

            // Search for links within the message to send info to other IRC users
            HttpClient client = new HttpClient();
            HttpResponseMessage msg;
            foreach(string word in messageContent.Split(' ')) {
                if(!Uri.IsWellFormedUriString(word, UriKind.Absolute))
                    break;
                
                msg = client.GetAsync(word).Result;
                if(msg.StatusCode != HttpStatusCode.OK)
                    continue;

                if(!msg.Content.Headers.ContentType.ToString().Contains("text/html"))
                    irc.SendMessage(SendType.Message, e.Data.Channel, $"^ [{msg.Content.Headers.ContentType}] ({msg.Content.Headers.ContentLength/1000.0f}KiB)");
                else {
                    string title = Regex.Match(msg.Content.ReadAsStringAsync().Result, @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>", RegexOptions.IgnoreCase).Groups["Title"].Value;   
                    if(!string.IsNullOrWhiteSpace(title))
                        irc.SendMessage(SendType.Message, e.Data.Channel, $"^ {title}");
                }
            }

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
                SocketGuildUser user = discord.GetGuild(config.discordServerId).Users.FirstOrDefault(x => x.Username.ToLower() == e.Data.Nick.ToLower() || (x.Nickname != null && x.Nickname.ToLower() == e.Data.Nick.ToLower()));
                if(user != null)
                    avatarUrl = user.GetAvatarUrl();

                // Send the message
                discordWebhooks[e.Data.Channel.ToLower()].SendMessageAsync(messageContent, username: user.Nickname ?? e.Data.Nick, avatarUrl: avatarUrl);
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

        // This whole thing sucks but I guess it works
        private static string WithEmotes(string message)
        {
            string[] words = message.Split(' ');
            foreach(string word in words) {
                if(word.StartsWith(":") && word.EndsWith(":")) { // If we're possibly dealing with an emote
                    Emote emote = GetEmoteFromName(word.Replace(":", ""));
                    if(emote != null)
                        message = message.Replace(word, $"{emote}");
                }
            }

            return message;
        }

        private static string WithCleanEmoteNames(string message)
        {
            string[] words = message.Split(' ');
            foreach(string word in words) {
                if(word.Contains("<")) {
                    Emote.TryParse(word, out Emote emote);
                    if(emote != null)
                        message = message.Replace(word, $":{emote.Name}:");
                }
            }

            return message;
        }

        // AAAAAAAAAAAAAA THIS CODE SUCKS
        private static Emote GetEmoteFromName(string name)
        {
            return discord.GetGuild(config.discordServerId).GetEmotesAsync().Result.FirstOrDefault(x => x.Name.ToLower() == name.ToLower());
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
        public bool useNickServ { get; set; } = false;
        public string nickServPass { get; set; } = "NONE";
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
