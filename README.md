# Dont Pull Me More Players

BepInEx 6 IL2CPP plugin for **Dont Pull Me v2.5** that raises the multiplayer target from 4 to 8 players.

## Requirements

- Dont Pull Me v2.5
- BepInEx 6.0.0-be.785 Unity IL2CPP x64

## Installation

After GitHub Actions builds the project, download the artifact and place:

`DontPullMeMorePlayers.dll`

into:

`Dont Pull Me/BepInEx/plugins/DontPullMeMorePlayers/`

Then launch the game and check `BepInEx/LogOutput.log` for:

`Loading [Dont Pull Me More Players 1.0.3]`

The plugin logs every target method it successfully patches.

## What is patched

The plugin discovers targets at runtime and attempts to raise the player limit to 8 in:

- Heathen Steamworks LobbyManager Create methods
- LobbyManager MaxMembers setter/getter
- Steam Matchmaking CreateLobby
- Steam Matchmaking SetLobbyMemberLimit
- FishySteamworks SetMaximumClients / GetMaximumClients
- FishySteamworks server StartConnection overloads containing a maximum-clients Int32 parameter

No proprietary game assemblies are stored in this repository.
