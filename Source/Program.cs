using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Meebey.SmartIrc4net;
using Newtonsoft.Json;

using Discord;
using Discord.WebSocket;

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
        static bool isDiscordReady;

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

            // Send the message to IRC
            string ircChannel = config.channels.FirstOrDefault(x => x.Value == message.Channel.Id).Key;
            if(ircChannel == null) { // If we failed to get the IRC channel
                Log.Write($"IRC channel for \"{message.Channel.Name}\" on Discord does not exist!");
                return Task.CompletedTask;
            }
            irc.SendMessage(SendType.Message, ircChannel, $"<{message.Author.Username}> {message.Content}");

            return Task.CompletedTask;
        }

        private static void OnIRCMessage(object sender, IrcEventArgs e)
        {
            // Get the associated Discord channel
            SocketTextChannel discordChannel = discordChannels[e.Data.Channel];
            if(discordChannel == null) { 
                Log.Write($"Could not get Discord channel for #{e.Data.Channel} on IRC");
                return;
            }

            // Send the message to Discord
            discordChannel.SendMessageAsync($"**<{e.Data.Nick}/IRC>** {e.Data.Message}");
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
    }
}
