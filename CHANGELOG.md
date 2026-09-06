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

# v0.1.14 | Audio fixes & optimisations

- Songs are now kept compressed in memory and decoded during playback instead of being held as raw PCM for the whole session. A 127-file, 110-minute bank measured ~82 MB on disk but around 0.9-1.8 GB once decompressed; it now costs roughly its size on disk, for a little CPU while a bugle is actually sounding.
- Fixed the audio bank sync never removing anything. The removal step ran on the sync's background thread and called `FindObjectsByType`/`Object.Destroy`, which are main-thread only, so it threw on the first song it tried to remove and aborted the whole sync — downloads included. Songs dropped from the bank had simply been reloading from disk on every launch ever since.
- Syncing now makes the folder match the bank exactly: anything the bank doesn't list is pulled out, including files added by hand and stale copies of a song left behind under a different extension. Nothing is deleted — files are moved to a `_removed` subfolder of the bank directory, which the loader ignores. BingBong voice lines are exempt and never touched.
- A sync that can't reach the bank, or gets an empty list back, now cancels instead of treating "no entries" as "nothing belongs here" and clearing out the whole folder.
- Fixed the game freezing the first time a sound is played in a session. `DownloadHandlerAudioClip` hands back a `DecompressOnLoad` clip whose PCM data is only decoded on first use, so that decode was landing inside the first `AudioSource.Play()` on the main thread. The clips are now decoded during loading instead, where the cost is expected and spread across frames.
- Fixed an empty sounds folder permanently breaking the audio system. With no songs loaded, picking a starting song indexed an empty list and threw, and because that ran from the load-complete callback it left the loading flag stuck on, silently killing every later sync and refresh for the rest of the session. The song index is also clamped now, since a refresh that removes songs could leave it past the end of the list.
- Fixed a Bugle picked up off the ground being completely silent. The patch that installs the custom sound only ran for items already in hand, but `ItemState.Ground` is the default and `Item.Start` doesn't set state, so anything spawned into the world never got it — while the vanilla Bugle sound was being muted regardless. The vanilla sound is now only muted where the replacement actually took over.
- Fixed BingBong subtitles never resolving: the localisation key was registered in lowercase, but `LocalizedText.GetText` upper-cases ids before looking them up. The entry also had a single string where one per language is required, which threw for every non-English player.
- Fixed `SFX_Instance` being created with `new`. It derives from `ScriptableObject`, which has to go through `ScriptableObject.CreateInstance` or it skips Unity's native-side initialisation.
- BingBong responses are now built once and shared instead of being rebuilt every time an item is enabled, which was allocating a `ScriptableObject` per response each time and never releasing the previous ones.
- Refreshing songs no longer scans the scene for audio sources once per song — a bank of N songs meant N full scene scans — and no longer forces a blocking garbage collection.
- File hashing is computed on demand on the sync thread rather than for every clip while loading, where it was reading each file end to end and running SHA256 on the main thread.
- The audio bank sync no longer drives `UnityWebRequest` from a background thread, which is main-thread only, and no longer busy-waits on it while blocking a second thread-pool thread.
- The song display no longer scans every loaded object looking for its font on every frame. It falls back to the default font immediately and retries for the nicer one on an interval, stopping once found.
- The audio bank URL is normalised before use: a missing scheme is filled in (http for localhost, https otherwise) and a trailing slash trimmed. Without a scheme every request failed outright with "Invalid BugleSoundAPIURL in config", since the URL could not be parsed.

# v0.1.15 | BingBong live voice (in-game client)

- Speak through BingBong with a live microphone. Audio is streamed from a relay rather than over PEAK's Photon servers, so it adds no load there at all. This release is the **in-game half only** — the relay and the web app that captures the microphone are not built yet, so the feature does nothing on its own until they exist.
- Every BingBong in the world gets a voice source as it spawns, positioned on the plush and inheriting the 3D setup of his own voice lines, so the live voice carries and attenuates like he does and obeys the game's volume sliders. Several BingBongs each speak from their own position.
- His mouth animates to the live audio, on every client rather than just the speaker's, and hands control back to the vanilla animation while one of his own voice lines plays.
- Canned voice lines take priority: the live voice ducks for the length of the line and then resumes at the live position, so the two never talk over each other. Detected from the RPC body, so the duck stays in step across the lobby without any networking of our own.
- New "BingBong Voice" config section, all of it applied live. The relay URL is derived from `BugleSoundAPIURL` when left empty, since that is where the relay will live, and can be overridden to point anywhere — a bare host or `host:port` is completed automatically. Range defaults to 50m, further than his 30m voice lines carry.
- Playback goes through `OnAudioFilterRead` rather than a streaming `AudioClip`. A clip created with `stream: true` fills Unity's decode buffer *ahead* of playback, and that read-ahead cannot be satisfied from a live stream because the audio has not arrived yet — the buffer drained to empty on a cycle no matter how deep it was, which was audible as constant clicking. The filter callback runs in lockstep with playback instead, so demand can never outrun real time.
- Incoming audio is resampled to the output rate by the mod rather than relying on Unity's mixer, so a device running at 44.1kHz or 48kHz sounds identical. A jitter buffer absorbs network timing, rendering what it has and padding the rest on a shortfall rather than muting and refilling, which pumped audibly when it happened often.
- Both Harmony patches are wrapped so that a failure can only cost the voice feature: the patch body is a try/catch around an isolated method, and repeated failures disable it for the session rather than breaking the item.
- `tools/stub_relay.py` is a development stub of the relay, enough to test the client end to end before the real backend exists. It speaks the same wire format and generates steady, swept and speech-shaped test signals, so no microphone or browser is needed.

# v0.1.16 | BingBong live voice: return audio

- Talking near BingBong now sends your own microphone back to whoever is puppeting him, so it is a conversation rather than a broadcast. Each player only ever transmits their own microphone, over the WebSocket that was already open — no second connection, and still nothing over PEAK's Photon servers.
- The uplink is gated on being within the same range BingBong's voice carries and attenuated by the same curve, so holding him up and talking works and walking away fades you out. It only transmits while someone is actually connected on the other end; an idle lobby sends nothing at all.
- Capture taps `MicWrapper.Read` rather than the obvious `MicrophoneRelay`, which copies only the first 256 samples of each frame before handing them on. Vanilla only ever wants a loudness number from it so the truncation is invisible in game, but streaming it gives a buzz at the frame rate instead of speech.
- The microphone is downmixed, low-passed and resampled from the device's rate to 16kHz by the mod. Decimating 48kHz to 16kHz without filtering first folds everything above 8kHz back into the speech band, which sounds like a second voice made of hiss.
- The noise gate learns the room instead of using a fixed level. A fixed threshold cannot work here: measured on real hardware, a quiet voice sits below what a noisy room measures, so any single number either cuts speech or passes the room. It tracks the background level, opens a configurable factor above it, and holds for 300ms so it does not close between words.
- The gate opens on entering range and closes only past a margin beyond it, with a minimum time in each state, so standing on the boundary cannot chatter it on and off.
- New `UplinkMuteKey` (default: F8) mutes the outgoing microphone for the rest of the session, since reaching for the config file is too slow to be the answer to "stop sending". It is not persisted — a mute you forgot you set is worse than one that clears on the next launch.
- The mod now understands the relay's `uplink_denied` and `uplink_roster` messages: a relay with the uplink switched off is believed and not asked again on that connection, and the log names who else is sending room audio, which is what tells "the relay is dropping my audio" apart from "nobody is listening".
- Uplink frames carry a different magic byte from the downlink ones, and the relay only ever forwards them to the speaker. A mis-routed frame therefore cannot replay a room's own audio back into it, which would be a feedback loop rather than a bug.
- The counters that let the receiver detect a dropped frame advance only for frames actually sent, so a gated pause leaves a contiguous timeline. Start and stop messages tell the receiver to rejoin the live edge instead of filling the pause with silence.
- `tools/stub_relay.py` gained the return direction: it forwards uplink audio to the speaker, stamps the stream id itself so a modified client cannot claim another player's, captures each stream to a WAV, and reports what fraction of real time actually arrived. `--deny-uplink` exercises the refusal path.
- The relay and the web app that would play this back are not built yet, so the return direction currently only works against that stub.
