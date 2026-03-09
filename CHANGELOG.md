# v0.1.0 | Project setup

- Initial project setup
- Added config dependency
- Added utils and debug functions
- Added config handler

# v0.1.1 | Throw passport anywhere patch

- Added PassportPatch.cs and registerd it as GlobalPatch in Plugin.cs

# v0.1.2 | Bags for Everyone

- Added BagsForEveryoneModule and BagsForEveryonePatch
- BagsForEveryonePatch adds a postfix to SingleItemSpawner.TrySpawnItems that checks if the spawner is the first one in the biome, and if so, it spawns extra bags based on the player count.

# v0.1.3 | Easy Backpack

- Added EasyBackpackModule and EasyBackpackPatch
- EasyBackpackPatch adds a new keybind to open the backpack UI whilst wearing it.

# v0.1.4 | Better Airport

- Added BetterAirportModule
- BetterAirportModule adds patches to make the airport more enjoyable, such as increasing the conveyor belt speed and making the terminals position at the start.

# v0.1.5 | Open Mesa

- Added OpenMesaModule and OpenMesaPatch
- OpenMesaPatch makes it so that the Mesa biome is open no matter the seed.

# v0.1.6 | Stashed Bugle

- Added StashedBugleModule
- StashedBugleModule adds a new keybind to toggle give / remove a Bugle.

# v0.1.7 | Better Bugle + Replace Bing Bong

- Added BetterBugle and ReplaceBingBong
- Play sounds and music in game with the Bugle (need a server to host the audio like mine), and can also with same server host sounds that start with SFX_VO_BingBong_ to replace voicelines (or add more) to BingBong

# v0.1.8 | Changed dependencies

- Added ModConfig and PeakPresence (my mod), Updated BepInEx

# v0.1.9 | AudioSyncWorker & Dynamic BingBong voices

- Centralized Audio loading in Worker class
- Now load BingBong voicelines dynamically after Sound reloads.

# v0.1.10 | Bonkable items

- Added Bonkable component to items when thrown.