using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

using Meebey.SmartIrc4net;
using Newtonsoft.Json;

namespace Discord_IRC_Sharp
{
    class Program
    {
        static void Main(string[] args) => new Program().Run().GetAwaiter().GetResult();
        static IrcClient irc = new IrcClient();
        static RelayConfig config;

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
        }

        private static void OnIRCMessage(object sender, IrcEventArgs e)
        {
            Console.WriteLine(e.Data.Message);
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
