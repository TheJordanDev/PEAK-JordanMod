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

# v0.1.11 | Fix for last update

- Fixed Easy Backpack: PEAK's backpack rework (jetpacks/fannypacks/rocketpacks) removed `BackpackSlot.hasBackpack` and changed `GUIManager.OpenBackpackWheel`'s signature, which broke the build. Updated to use `BackpackSlot.backpackType` and pass slot count/type, and brought the `BackpackWheel.Update()` override back in sync with the new vanilla logic (missing null guard, jetpack fuel slice handling).
- Fixed Stashed Bugle not giving the Bugle: it was passing the item's display name to `SpawnItemInHand`, which actually expects the prefab's Resources name. Now resolves the real prefab name through the ItemDatabase first.
- Fixed AudioSyncWorker getting permanently stuck after a failed sync (e.g. missing API URL): the sync ran on a background thread and called a Unity API (`StartCoroutine`) that's main-thread only, throwing silently and leaving the loading flag stuck forever. Added a main-thread dispatch queue so background work can safely hop back to the main thread.
- Fixed a leftover debug log using the Error level for a successful download, making normal syncs look like they were failing.
- Fixed the in-game right-click "reload songs" prompt on the Bugle: it was calling the wrong method and didn't actually reload anything. It now properly unloads and reloads from local files (distinct from the Sync keybind, which pulls from the audio bank API).
- Fixed the right-click confirmation always showing "No answer, not refreshing songs" even after successfully confirming, due to a timing check that ran before the confirmation window instead of after.
- Fixed an error (NullReferenceException) every time a thrown item bonked something: `Bonkable` added at runtime has no serialized prefab data, so its sound-effect array was `null` instead of empty. It now defaults to an empty array so bonks still ragdoll/knock back the target, just silently.

# v0.1.12 | Added customization keybinds

- Open the passport when pressing the "Open Passport" keybind (default: P)
- Removed a leftover debug keybind on the BingBong module that was also bound to P and would have fired alongside the new passport keybind.

# v0.1.13 | Fix for PEAK update

- Rebuilt against the September PEAK update. Two game methods gained an optional parameter, which compiles fine but leaves an older build calling a signature that no longer exists, so both threw a MissingMethodException at runtime:
  - `Player.EmptySlot(slot)` became `EmptySlot(slot, bool andBroadcast = true)`. This killed the whole "Toggle Bugle" keybind (default: V) — both giving the Bugle and swapping it for the Megaphone.
  - `PhotonNetwork.InstantiateItemRoom(name, position, rotation)` gained a fourth `bool warnIfNotHost = true`. This one threw inside the Bags for Everyone postfix on `SingleItemSpawner.TrySpawnItems`, and because a throwing postfix propagates out of the method it patched, it took down every `SingleItemSpawner` in the level rather than just the backpack one. Backpacks and BingBong stopped spawning, and lighting a campfire no longer advanced the biome: the spawn loop runs inside `MapHandler`'s transition coroutine, so the throw killed that coroutine and the campfire items, day/night blend, biome title and fog reveal all silently never happened.
- Removed the manual `RPCRemoveItemFromSlot` call the Bugle keybind made right after `EmptySlot`. `EmptySlot` already forwards the removal to the host itself, so the extra call only logged "Only Master Client can remove items!" on every press for anyone who wasn't hosting.
- The "Open Backpack" keybind (default: B) no longer opens the wheel while the backpack is held in your hands. The wheel pulls items out of the backpack's on-back visuals, which the game deactivates while the backpack is in hand, so anything taken out was spawned into a disabled rig and disappeared. Hold-interacting the held backpack still opens it the normal way.
- Hardened the Bags for Everyone patch. Its master-client and spawner-name checks now run in the postfix itself instead of inside the method that spawns the bags, and the spawn is wrapped in a try/catch. Because Mono resolves a method's call targets when it JITs that method, a changed game API used to throw before any of those checks could run, taking down every `SingleItemSpawner` in the level — which is what stopped campfires from advancing the biome in 0.1.12. A failure now costs the extra bags and nothing else.
- The extra backpacks are registered with the spawner's `SpawnedItemTracker`, so a quicksave records them instead of restoring only the vanilla one, and the patch no longer adds a fresh batch on top every time a run is loaded from a save.
