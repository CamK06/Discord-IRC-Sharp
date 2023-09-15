
# Shitcord-IRC-Sharp

  

Shitcord-IRC-Sharp is a simple IRC bridge for Shitcord written in C#, intended to replace similar node.js bridges because they're buggy and are written in JavaScript (not nearly as buggy as Shitcord itself though, their devs are completely incompetent)

## Features
* Customizable message formatting
* Support for bridging multiple channels
* Support for sending messages with webhooks
* Webhooks inherit the avatar of Shitcord accounts under the same username as IRC
* Shitcord usernames are (optionally) coloured on the IRC side

## Usage
After you build the source as described below, run the program for the first time either with `dotnet` or by running the executable directly.
A basic configuration file will be generated in `config.json`. Add your IRC server info, Shitcord token, the ID of your Shitcord server and the channels to bridge.

Channels are formatted as follows: 

```json
"channels": {
	"#main": DISCORDCHANNELID,
	"#tech": DISCORDCHANNELID
}
```
Once your configuration file is populated with all of the correct info, run the program once more. It should output info about finding the Shitcord channels, joining IRC, etc. If instead you get a message stating that it failed to connect to IRC, Shitcord, or find channels, double check that all values in your configuration are correct.

If the problem persists and you're absolutely sure your configuration is correct, [open an issue](https://github.com/CamK06/Discord-IRC-Sharp/issues/new) providing as much information as possible.

**Note:**
If you have the "useWebhooks" config option enabled, you do NOT need to manually make webhooks. The bot will make them for you, so ensure it has the proper permissions to do so.

## Building
1. Clone the repo: ``git clone https://github.com/CamK06/Discord-IRC-Sharp``
2. Change directories: ``cd Discord-IRC-Sharp``
3. Build the code: ``dotnet build -c Release``
4. The program should now be in the `bin/` directory, copy all of the program files to a directory of your choice. This will be where you run the program

## Docker

```bash
docker build . -t discord-irc-sharp:latest
docker run -d discord-irc-sharp:latest
```

Or with buildx:
```bash
docker buildx build \
  --no-cache \
  --platform linux/amd64 \
  --tag discord-irc-sharp:latest .
docker run -d discord-irc-sharp:latest
```

Or with docker-compose:
Create and adapt `config.json` with the following contents:
```json
{
  "discordToken": "TOKEN",
  "discordServerId": 0,
  "IRCNickname": "Discord-IRC",
  "IRCIp": "irc.example.com",
  "IRCport": 6667,
  "useNickServ": false,
  "nickServPass": "NONE",
  "channels": {},
  "formatting": {
    "discordPrefix": "**<%u/IRC>**",
    "nicknameColours": true,
    "ircMentionsDiscord": false,
    "useWebhooks": false
  }
}
```
``docker compose up -d``
