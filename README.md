
# Discord-IRC-Sharp

  

Discord-IRC-Sharp is a simple IRC bridge for Discord written in C#, intended to replace similar node.js bridges because they're buggy and are written in JavaScript

## Usage
After you build the source as described below, run the program for the first time either with `dotnet` or by running the executable directly.
A basic configuration file will be generated in `config.json`. Add your IRC server info, Discord token, the ID of your Discord server and the channels to bridge.

Channels are formatted as follows: 

```json
"channels": {
	"#main": DISCORDCHANNELID,
	"#tech": DISCORDCHANNELID
}
```
Once your configuration file is populated with all of the correct info, run the program once more. It should output info about finding the Discord channels, joining IRC, etc. If instead you get a message stating that it failed to connect to IRC, Discord, or find channels, double check that all values in your configuration are correct.

If the problem persists and you're absolutely sure your configuration is correct, [open an issue](https://github.com/CamK06/Discord-IRC-Sharp/issues/new) providing as much information as possible.

## Building
1. Clone the repo: ``git clone https://github.com/CamK06/Discord-IRC-Sharp``
2. Change directories: ``cd Discord-IRC-Sharp``
3. Build the code: ``dotnet build -c Release``
4. The program should now be in the `bin/` directory, copy all of the program files to a directory of your choice. This will be where you run the program
