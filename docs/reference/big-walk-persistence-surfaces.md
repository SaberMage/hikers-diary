# Big Walk: game code that Hikers' Diary must hook (persistence surfaces)

Build read: `GameAssembly.dll` dated 2026-10-06, Unity 6000.3.17f1, IL2CPP. Read date: 2026-11-27.
If the build changes, read this document again before you use an offset or a claim about a method body.

## Sources and tags

Each claim has one or more source tags:

| tag | source | gives |
|---|---|---|
| `[sig]` | `ilspycmd` on `BepInEx/interop/*.dll`, and Cpp2IL `diffable-cs` | class, member, type, field offset, `[SyncVar]`, `[Command]`, `[ClientRpc]` attributes |
| `[ISIL]` | Cpp2IL 2022.1.0-pre-release.21 `--output-as isil` | method body: calls, string literals, branches |
| `[CG]` | Cpp2IL `--output-as dll_il_recovery --use-processor attributeinjector,callanalyzer`, then `ilspycmd` | call graph (`[Calls]`, `[CallerCount]`) |
| `[meta]` | `grep -a` on `il2cpp_data/Metadata/global-metadata.dat` | string literals |
| `[disk]` | files on this computer | observed data |
| INFERRED | a conclusion from the sources, not read directly | make sure at runtime that it is correct before you depend on it |

Field offsets (`@0x..`) are from `diffable-cs`. ISIL offsets in `[rcx+N]` are decimal.

The Cpp2IL tool and dumps were not on disk (the TravelEar dumps are gone). This read downloaded
Cpp2IL 2022.1.0-pre-release.21 and made new dumps in a session scratchpad, which is temporary.
To make them again, see `docs/DESIGN.md` section 5.

In this document, "corpse" means the game object `Corpse`. "Body" means only the avatar of a
player. The game calls player colors "looks". This document calls them colors.

## Summary of top findings

1. The game HAS its own save system: JSON slot files, one for each "game" (`SaveData`). It saves
   these items:
   - puzzle and switch states
   - which props are in which saveable home
   - the inventory
   - sign text
   - player colors, by player identifier
   - the world seed
   - the play time

   It does NOT save loose prop positions, player positions, vehicle positions, or the time of day.
2. The game ALREADY keeps a corpse when a player leaves: `Corpse`. When a remote player object stops
   on the server, `PlayerCharacter.OnStopClient` calls `Corpse.CreateAndSpawn`. When the same
   identifier joins again, `HouseNetworkManager.AddPlayerDelayed` finds the corpse
   (`Corpse.FindMatch`), spawns the player at the corpse transform, moves the pockets back, and
   destroys the corpse. Corpses are runtime spawns. A game load removes the corpses.
3. The network transport is NOT Steam. It is EOS P2P plus IP (`Mirror.MultiplexTransport`), with an
   EOS private lobby. Steam gives only rich presence (friend join). `MirrorIgnorance` is the
   Dissonance voice integration, not the game transport. There is no Steam lobby to make.
4. Player movement is client-authoritative (the owner sends `CmdMove`). Prop movement is
   server-authoritative. No game code calls `AssignClientAuthority`.
5. Switch states, which include lights, are in one server `SyncDictionary`:
   `PeckManager.peckContexts`. Saveable switch states already go to the save file.

---

## 1. Persistence that the game has

### 1.1 Save files

| item | value | source |
|---|---|---|
| folder | `Application.persistentDataPath + "/user_data/save_games"` = `%USERPROFILE%\AppData\LocalLow\House House\Big Walk\user_data\save_games` | `[ISIL]` `SavePlatform.GetPathForName`, `[disk]` |
| file name | `save_<slotName>_<filenameUid>.sav`, plus `.sav.bak` | `[ISIL]` `SavePlatform.GetPathForName` (`"/save_"`, `"_"`, `".sav"`), `[disk]` |
| format | JSON from `JsonUtility.ToJson(SaveData)` | `[ISIL]` `SaveManager.WriteCurrentSaveData` |
| write | `SavePlatform.WriteSaveData(slotName, filenameUid, json)` -> `SaveWithTmpAndBackup(path, json)` | `[sig]` `[ISIL]` |
| cloud | `user_data/steam_autocloud.vdf` is present | `[disk]` |
| `filenameUid` | `Guid.NewGuid().ToString("N")` cut to 16 chars, made in `HostMenuConfirm.ActionStart` | `[ISIL]` |

### 1.2 Save classes

| class.member | kind | meaning | source |
|---|---|---|---|
| `SaveManager.instance` | static prop | returns `BaseManager.instance.saveManager` (`BaseManager@0x60`) | `[ISIL]` |
| `SaveManager.currentData` | field `SaveData` @0x18 | the active slot. `null` means "unsaved world" (`SaveManager.worldName` returns `"unsaved world"`) | `[sig]` `[ISIL]` |
| `SaveManager.WriteCurrentSaveData()` | static | sets `lastPlayedTime` and `totalPlaytime` (from `NetworkTime.time`), then writes JSON. Logs `"cannot save. save data has not been created yet."` if `currentData` is null | `[ISIL]` |
| `SaveManager.SetIntValue(key,int)` / `SetStringValue(key,string)` | static | calls `SaveData.SetIntValue`. Then it calls `WriteCurrentSaveData` only if the value changed | `[ISIL]` |
| `SaveManager.GetIntValue(key[,fallback,saveFallback])`, `GetIntValueWithBackup(key,backupKey,fallback)`, `GetStringValue`, `GetIsInInventory(guid)` | static | read | `[sig]` |
| `SaveManager.GetAllSaveDatasInFolder()` | static | `SavePlatform.GetAllSaveDataJsonsInFolder` -> list of `SaveData` | `[ISIL]` |
| `SaveManager.Delete(SaveData)`, `RenameSlot(old,new,uid)`, `SetPassword(string)` | static | slot management | `[sig]` `[CG]` |
| `SaveData.slotName` / `password` / `filenameUid` / `lastPlayedTimeAsLong` / `skipAidsActive` / `totalPlaytime` | fields | slot header | `[sig]` `[disk]` |
| `SaveData.entries` | `List<SaveEntry{key,int value}>` | all int state | `[sig]` `[disk]` |
| `SaveData.stringEntries` | `List<SaveEntryString>` | sign text, key = `SaveIdentity` GUID | `[sig]` `[disk]`, writer `PeckEffectTextInput.ServerSetText` `[CG]` |
| `SaveData.inventory` | `List<string>` | `Prop.savablePropGuid` values in the inventory | `[sig]` `[disk]` |
| `SaveIdentity.saveGuid` | `string` on a `MonoBehaviour` | stable GUID key for a saveable object | `[sig]` |

There is no player count field in `SaveData`. `[sig]`

### 1.3 What goes into `entries` (all writers of `SaveManager.SetIntValue`) `[CG]`

| writer | key | value |
|---|---|---|
| `TrackedPeckState.SetState(PeckContext)` | `SavableSystem` enum name (for example `LookoutLightRed`, `SpawnHubGate`) or `SaveIdentity.saveGuid` | switch state | 
| `Prop.SavePropHome` / `Prop.LocallySetPinned` | `SaveableHomeName` enum name of the home | the prop that is pinned there (INFERRED: `saveablePropName` value) |
| `BuoySaver.OnChangeServer`, `PeckEffectSavableHome.Peck` | GUID | state |
| `PlayerLooks.ServerSaveLook`, `PlayerLooks.LoadOne`, `PlayerNetworking.ServerSetLook` | `PlayerLooks.GetSaveKey(part, identifier)` = `<identifier>keyLookIdHead` / `Torso` / `Legs` | look id |
| `PlayerTeacher.ServerSaveWalkingProven`, `PlayerNetworking.UserCode_CmdRecordProvenWalking` | `<identifier>keyWalkingProven` | 1 |
| `PegTileSequenceGenerator.SetRandomSeed` (reader) | `worldSeed` | seed |

`Prop.SavePropHome` saves only if `saveablePropName != 0` or `canSaveHomeWithGuid`, and
`propSaveType != Never(2)`, and the home has a `saveableHomeName`. `[ISIL]` (offsets 0xB8, 0xBC, 0xC8)

### 1.4 When the game writes

| trigger | source |
|---|---|
| every changed `SetIntValue` / `SetStringValue` | `[ISIL]` |
| `WorldManager.Update` and `PropInventory.Update`: if `PropInventory.saveRequired` is true and the code runs on the server, they call `PropInventory.FillSaveList` and then `WriteCurrentSaveData` | `[ISIL]` |
| `HostMenuConfirm.ActionStart`, `SessionPasswordMenu.ActionConfirm`, `SettingsWardrobe.ApplyDummySkipAid` | `[CG]` |

### 1.5 When the game reads

| reader | what | source |
|---|---|---|
| `Prop.Start` (server) | reads the saved home of a saveable prop and calls `Prop.ServerSetPinned`. Else it uses `startHome`. It logs `"could not find saved home"` | `[ISIL]` |
| `TrackedPeckState.Initialize` (server) | `GetIntValue(key)` -> `SetState(new PeckContext(state))` | `[ISIL]` |
| `PlayerNetworking.Start` -> `PlayerLooks.ServerLoadLook` -> `LoadOne` | `GetIntValueWithBackup(identifierKey, backupKey, random)` | `[CG]` `[ISIL]` |

### 1.6 Other persistence

| item | where | source |
|---|---|---|
| PlayerPrefs | settings (`SettingsHanger`, `DeviceHanger`), `settings_crossplay`, `latestCrashReported`, `JoinMenu` (INFERRED: recent join code), Rewired `UserDataStore_PlayerPrefs` | `[ISIL]` `[meta]` |
| control bindings | `user_data/control_bindings/*.xml` through `SavePlatform.SaveRebindData` | `[disk]` `[sig]` |
| `Scribe` | development recorder of network commands (`Scribe.Save/Load`, `ScribeSavableData`). It is not a world save | `[sig]` |

---

## 2. Startup flow

### 2.1 Menu path (vanilla)

| step | class.method | effect | source |
|---|---|---|---|
| 1 | `BaseManager.Awake` | makes managers (`ControlsManager`, `SettingsWardrobe`, `AudioManager`, network manager) | `[ISIL]` |
| 2 | `BaseManager.Start` | if static `BaseManager.AutoHostModeRequested` (@0x8) is set, it clears the flag, calls `NetworkMinder.SetToIpTransport()`, and calls `NetworkManager.StartHost()`. This uses the IP transport only. No game code sets the flag (INFERRED from `[CG]`: one reference) | `[ISIL]` |
| 3 | `TitleMenu.GoToHostMenu` / `GoToJoinMenu` | menu | `[sig]` |
| 4 | `HostMenuSelect.RefreshSlotCards` -> `GameSlotCard.PlaySlot` -> `HostMenuSelect.GoToHostMenuConfirm` | pick a slot | `[ISIL]` |
| 5 | `HostMenuConfirm.ActionStart` | It makes a `SaveData` if `this.saveData` (@0x48) is null. It can rename the file (`File.Move`). It sets `BaseManager.instance.saveManager.currentData = saveData`. It calls `WriteCurrentSaveData`. It calls `NetworkMinder.SetServerPassword(saveData.password)` | `[ISIL]` |
| 6 | `PlayerCountMenu.SetPlayerCount(PlayerCount)` then `ActionProceed` | `ActionProceed` calls `NetworkMinder.StartHost()` | `[ISIL]` |
| 7 | `NetworkMinder.StartHost()` | It calls `NetworkTime.ResetStatics`. It calls `SetToBothTransports` if `SteamManager.Initialized`, else `SetToIpTransport`. Then it calls `NetworkManager.StartHost` | `[ISIL]` |
| 8 | `HouseNetworkManager.OnStartHost` -> `CreateHostEosLobbyAsync` | It calls `EOSPrivateLobbyManager.CreateLobbyWithResult` and logs `"Lobby code will be :  "`. It uses `SaveManager.worldName` and PlayerPrefs `settings_crossplay` | `[ISIL]` |
| 9 | `HouseNetworkManager.LoadSubScenes` | additive scene loads after the online scene `"WorldScene"` | `[ISIL]` `[meta]` |
| 10 | `WorldManager.Start` | if no server and no connection: `SceneManager.LoadScene` back to base. Tags `baseOnly` / `joinLocalHost` (development) | `[ISIL]` |
| 11 | `HouseNetworkManager.OnClientConnect` | `NetworkClient.Ready`, `NetworkClient.AddPlayer` | `[ISIL]` |
| 12 | `HouseNetworkManager.OnServerAddPlayer` -> `AddPlayerDelayed` | see section 7 | `[ISIL]` |

`PlayerCount` enum: `NotSet=0, PlayerCount2=2, PlayerCount3=3, PlayerCount4=4`. Holders:
`PlayerCountSwapper.playerCount` (static), `HouseAuthenticator.WelcomeMessage.playerCount`,
`DevSettings.playerCount`. `[sig]` The world content changes with the player count (INFERRED from
`PlayerCountSwapper`).

### 2.2 Join path

| class.method | meaning | source |
|---|---|---|
| `NetworkMinder.JoinGame(address)` | `NetworkManager.StartClient`. Callers: `JoinMenu.ConnectTo(joinCode)`, `HouseSteamManager.OnRichJoinRequested`, `NetworkMinder.SetTransportAndConnect` | `[CG]` |
| join code alphabet | `"ABCDEFGHJKLMNPQRSTUVWXYZ23456789"` in `NetworkMinder` | `[ISIL]` |
| `HouseSteamManager.SetSteamStatus(worldName, joinCode)` | `SteamFriends.SetRichPresence` (`"in a session"`, connect key) | `[ISIL]` |
| `HouseSteamManager.PollForFriendGames(List<JoinFriendCard.FriendGameInfo>)` | reads friends' sessions (title and code) | `[sig]` |
| `HouseAuthenticator` | password check (`password`@0x30, `SendPassword`), version check, identifier check | `[sig]` |

### 2.3 Seam to skip the menus (INFERRED)

To do the work of steps 5 to 7 in code, do these steps:

1. Set `SaveManager.instance.currentData` to the loaded `SaveData`.
2. Call `NetworkMinder.SetServerPassword`.
3. Read the body of `PlayerCountMenu.SetPlayerCount`.
4. Set the player count field (the field that `PlayerCountMenu.SetPlayerCount` writes).
5. Call `NetworkMinder.StartHost()`.

Do not use `BaseManager.AutoHostModeRequested`. It selects the IP transport only, and it does not
set `currentData`.

---

## 3. Networked world objects (props)

| class.member | kind | meaning | source |
|---|---|---|---|
| `Prop : NetworkBehaviour` | class | every item that a player can carry | `[sig]` |
| `Prop.allProps` | static `List<Prop>` | all props | `[sig]` |
| `Prop.propHomeShellReference` | `[SyncVar(hook=OnChangePropHome)]` `ShellReference` @0x28C | the home (hand, pocket, slot) that holds the prop. An empty value means that the prop is loose | `[sig]` |
| `Prop.houseNetworkTransform` | `HouseNetworkTransform` @0xB0 | position sync | `[sig]` |
| `Prop.networkIdentity`, `Prop.rb` | refs | | `[sig]` |
| `Prop.startHome` @0x68, `defaultStartPosition` | fields | where it starts | `[sig]` |
| `Prop.saveablePropName` @0xB8, `canSaveHomeWithGuid` @0xBC, `savablePropGuid` @0xC0, `propSaveType` @0xC8 | save ids | stable ids for the game save | `[sig]` |
| `Prop.exclusiveHolder`, `currentHome`, `isInInventory` | state | | `[sig]` |
| `Prop.SetHeld/SetDropped/SetLoose/SetFixed/Position(localPos,localRot)` | methods | local state changes | `[sig]` |
| `Prop.ServerSetPinned(PropHome)` / `ServerSetUnpinned()` | server | put the prop in a home | `[sig]` `[CG]` |
| `PropHome` | `MonoBehaviour` | slot. `pinnedProp`, `saveableHomeName`, `isInventory`, `parentCharacter`, `parentProp`, `shellReference` | `[sig]` |
| `PropHome.GetSaveableHome(SaveableHomeName)` | static | find a saveable home | `[sig]` |
| `PropInventory` | singleton | inventory zones. `FillSaveList(List<string>)` | `[sig]` `[ISIL]` |

### 3.1 Spawn path and identity

- The only game call to `NetworkServer.SpawnObject` is `Corpse.CreateAndSpawn`. The only
  `AddPlayerForConnection` call is `HouseNetworkManager.AddPlayerDelayed`. `[CG]`
- Thus props are scene objects that Mirror spawns with `NetworkServer.SpawnObjects`, and each has
  a `NetworkIdentity.sceneId` (INFERRED from `[CG]`).
- `netId` changes each session. Stable keys, best first: `Prop.savablePropGuid`,
  `NetworkIdentity.sceneId`, the GameObject path. We measured these keys on 2026-11-27 (T3, slot
  `ModTests`). The slot has 306 loose props. Each one has a `savablePropGuid` and a `sceneId`. No two props have the
  same `sceneId` or the same path. In two loads, the `sceneId` and the path of each prop did
  not change. `[disk]` (two Diary files)
- `ShellReference` = `{ushort ticket, uint netId, int index}` `[sig]`. It points to a component in a
  `SeaShell` (`poses`, `peckSwitches`, `propHomes`, `trackedPeckStates` lists) on a
  `NetworkIdentity`. `TicketOffice.tickets` maps `ushort` tickets to `ITicketed`. Because `netId` is
  per session, save `sceneId + index` and not a `ShellReference` (INFERRED).

### 3.2 Position sync: `HouseNetworkTransform` (`LobbyNetworking`)

| member | meaning | source |
|---|---|---|
| `isOwned` getter | `(netIdentity.isServer && !isPlayer) || netIdentity.isLocalPlayer` | `[ISIL]` |
| `[Command(channel=1)] CmdMove(Vector3, uint packedRotation, ushort platformTicket)` | owner -> server | `[sig]` |
| `[ClientRpc(channel=1)] RpcMove(...)` | server -> clients | `[sig]` |
| `CmdVelocity` / `RpcVelocity` | velocity | `[sig]` |
| `OwnerUpdate(bool forceFullSync)` | called from `OnStartServer` and `OnPlayerConnected` | `[CG]` |
| `isPlayer` @0x98, `syncOnSpawn` @0x9B, `_syncedPosition` @0xD0, `_syncedRotation` @0xDC | fields | `[sig]` |
| `HouseNetworkTransformManager` | job that applies transforms (max 1024) | `[sig]` |

Thus the server moves a prop. To restore a loose prop, set the `Rigidbody`/transform on the host.
Then let `OwnerUpdate` send it (INFERRED). `platformTicket` makes positions relative to a moving
platform (`StickyPlatform`), for example a train car (INFERRED).

---

## 4. Vehicles

No class or string has the name gondola or monorail. `[meta]` (search for `gondola`, `monorail`)
The classes below are the networked spline movers. Which world object is which vehicle is
INFERRED. At runtime, list `NetworkedTrain` and `MovementDriver` objects by GameObject name.

### 4.0 Runtime vehicle map (T4, 2026-11-27)

On the host, with slot `ModTests` and 2 players, the mod found these objects. `[runtime]`

| class | GameObject path | `hasCable` | splines | cars | vehicle (INFERRED) |
|---|---|---|---|---|---|
| `NetworkedTrain` | `TrainSystem/TrainNew/TrainNetworking` | false | 1 | 15 | the monorail |
| `NetworkedTrain` | `ChairLift/ChairliftTrainNew/NetworkedTrain` | true | 4 | 64 | the gondola (the cable cars) |
| `MovementDriver` | `LandmarksPlayerCountAny/Carousel/Positioner/CarouselNetworking` | | | | the carousel |

The mod did not find a third `NetworkedTrain` for the solo gondola. It is possible that the solo
gondola is one of the 64 cable cars. It is also possible that the solo gondola uses a different
class. Each path is unique, so the path is the key.

### 4.1 `NetworkedTrain : NetworkBehaviour` (train cars on splines, and cable vehicles with `hasCable`)

| member | kind | meaning | source |
|---|---|---|---|
| `syncTime` | `[SyncVar] double` @0xC8 | `NetworkTime.time` at the last change | `[sig]` |
| `syncSpeed` | `[SyncVar] float` @0xD0 | speed at `syncTime` | `[sig]` |
| `syncSplineIndex` | `[SyncVar] int` @0xD4 | spline at `syncTime` | `[sig]` |
| `syncDistance` | `[SyncVar] float` @0xD8 | distance along that spline at `syncTime` | `[sig]` |
| `targetSpeed` | `[SyncVar] float` @0xDC | set by `EnginePeck(PeckContext)` | `[sig]` `[ISIL]` |
| `_currentSplineIndex` @0xF4, `_currentDistance` @0xF8, `_currentSpeed` @0xFC | local | result of the extrapolation | `[sig]` |
| `splines`, `cars : TrainCar[]`, `hasCable` @0xEC, `acceleration`, `deceleration`, `fullSpeed`, `engineSystem : PeckSystemReference` | config | | `[sig]` |
| `FixedUpdate` | | It calls `GetProgressedPosition(syncSplineIndex, syncDistance, speed, NetworkTime.time - syncTime)`. The server writes the four sync vars when the speed changes | `[ISIL]` |
| `SetDistance(float)` | | writes only `_currentDistance`. No game caller. `FixedUpdate` overwrites it | `[ISIL]` `[CG]` |
| `GetDistanceFraction()` | | progress 0..1 | `[sig]` |

Restore (T4 test, 2026-11-27): on the host, use the `Network*` setters to set `syncSplineIndex`,
`syncDistance`, `syncSpeed`, `targetSpeed`, and `syncTime = NetworkTime.time`. Also set
`_currentSplineIndex`, `_currentDistance` and `_currentSpeed`. On the server, `FixedUpdate`
compares the sync vars with the `_current*` values. If the sync vars and the `_current*` values are
different, `FixedUpdate` copies the `_current*` values to the sync vars. Then it logs `"{0} server sync. index: ..."`. `[ISIL]` `[runtime]` Do not save
`syncTime`. `NetworkTime` resets at each host start (`NetworkMinder.StartHost` calls
`NetworkTime.ResetStatics`). The engine on/off state is a `TrackedPeckState` (section 5).

### 4.2 `MovementDriver : NetworkBehaviour, IOccasional` (one moving object driven by a peck system)

| member | kind | meaning | source |
|---|---|---|---|
| `syncPosition` | `[SyncVar(hook=OnSetPosition)] float` @0x68 | position | `[sig]` |
| `syncTime` | `[SyncVar(hook=OnSetTime)] double` @0x70 | time of the last sync | `[sig]` |
| `syncSpeed` | `[SyncVar(hook=OnSetSpeed)] float` @0x78 | speed | `[sig]` |
| `_localPosition` @0x90, `_localSpeed` @0x8C, `_aimSpeed` @0x88 | local | | `[sig]` |
| `ServerSync()` | method | called from `OccasionalUpdate` | `[CG]` |
| `activeSystem : PeckSystemReference`, `OnPeckActive(PeckContext)` | control | | `[sig]` |

### 4.3 Other

| class | meaning | source |
|---|---|---|
| `TrainCar` | one car: `mainBody`, `platformingBody`, `grip`, `proxy` | `[sig]` |
| `ChairLiftWireMinder`, `ChairLiftPole`, `ChairliftMap`, `ChairliftAudio` | chairlift cable and map. Not networked | `[sig]` |
| `GoodbyeTrain` | end train: `networkedTrain`, `loadingZone`, turnstiles | `[sig]` |
| `SyncedMachine` | `[SyncVar] double startTime`. Machines with an animation loop | `[sig]` |

---

## 5. Lights, switches, time of day

### 5.1 Switch state (lights are switch systems, INFERRED)

`HouseLight` and `HouseLightManager` only cull and set quality. They have no network state. `[sig]`
A light switch is a `PeckSwitch` that drives a `TrackedPeckState`, and `PeckEffect*` components
(for example `PeckEffectToggle.settingsPerState`) apply the state (INFERRED from `[sig]`). The
lookout lights are `SavableSystem.LookoutLightRed/Green/Blue/Yellow`.

| class.member | kind | meaning | source |
|---|---|---|---|
| `PeckManager.peckContexts` | `SyncDictionary<ShellReference, PeckContext>` @0x70 | all switch states. The server writes them | `[sig]` `[ISIL]` |
| `PeckManager.SetState(TrackedPeckState, PeckContext)`, `ServerRetrigger`, `ServerReject` | static | | `[sig]` |
| `TrackedPeckState.SetState(PeckContext)` | server | writes `peckContexts[shellReference]`, then saves if the state is saveable | `[ISIL]` |
| `TrackedPeckState.SetState(int)` | | int overload | `[sig]` |
| `TrackedPeckState.currentPeckContext` @0x38, `shellReference` @0x9C, `label` @0x20, `savableSystem` @0x6C, `saveIdentity` @0x70, `hasInitialState`/`initialState` | fields | | `[sig]` |
| `PeckContext` | struct | fields `playerIdentity`, `propIdentity`, `sbyte compressedState`, `actionNumber`. Property `state` | `[sig]` |
| `PlayerNetworking.CmdUsePeckSwitch(ShellReference, PeckContext, int)` | `[Command]` | the client sends a switch press | `[sig]` |
| `PeckSwitch.Peck(...)`, `GetNextState(int)` | | | `[sig]` |

Owner: the server (host). Clients request with `[Command]`s. `[sig]`

Test on 2026-11-27: the game has 26 saveable switch states (lights, doors, radio stations,
gauntlet chambers). A test aid changed `LookoutLightRed` from 1 to 0. After a game load, the state
was 0. Thus, the game save keeps the switch states. `[runtime]`

### 5.2 Time of day: `LobbyEnviroNetworking : NetworkBehaviour`

| member | kind | meaning | source |
|---|---|---|---|
| `syncTime` | `[SyncVar] double` @0x70 | `NetworkTime.time` at the last server sync | `[sig]` `[ISIL]` |
| `syncDate` | `[SyncVar] long` @0x78 | Enviro date/time in ticks | `[sig]` `[ISIL]` |
| `isSimulating` | `[SyncVar] bool` @0x80 | | `[sig]` |
| `Update` (server branch) | | It logs `"enviro server time sync"`. It writes `syncTime = NetworkTime.time` and `syncDate` from `EnviroManager.instance` | `[ISIL]` |
| `Update` (client branch) | | It sets `predicted = syncDate + ConvertRealLifeSecondsToEnviroSeconds(NetworkTime.time - syncTime)`. It calls `EnviroTimeModule.SetDateTime(...)`. It logs `"enviro client time sync"` | `[ISIL]` |
| `OnStartClient` | | `SkyManager.SyncCloudsBasedOnNetworkTime` | `[ISIL]` |
| `SkyManager.SetFixedTime(float)`, `ClearFixedTime()`, `GetCurrentTime()` | static | sky time | `[sig]` |

Restore (INFERRED): on the host, after the scene loads, set the Enviro time with
`EnviroManager.instance` `Time.SetDateTime(...)`. The next server sync sends it to clients. The
starting time of day comes from the Enviro configuration in the scene (INFERRED). The game does not
save it. `[CG]` (no save call)

---

## 6. Player

### 6.1 Classes

| class | meaning | source |
|---|---|---|
| `PlayerCharacter : NetworkBehaviour` | root. `allPlayerCharacters` (static), about 40 module fields (`hands`@0x68, `looks`@0x98, `poser`@0xA0, `croucher`@0xC8, `registry`@0xF0, `misc`@0xF8, `sleeper`@0x108, `texter`@0x128, `sitter`@0x130, ...), `playerNetworking`@0x1A0, `houseNetworkTransform`@0x1B0, `rb`@0x1F8 | `[sig]` |
| `PlayerNetworking : NetworkBehaviour` | all player sync vars and commands | `[sig]` |
| `PlayerRegistry` | refs: `corpsePrefab`@0x58, `blindfoldPocket`@0xB0, `backpackPocket`@0xB8, `holsterPocket`@0xC0 | `[sig]` |
| `PlayerLooks` | colors (look ids) | `[sig]` |
| `PlayerPoser`, `PlayerPose` | poses on world objects (seats). `PlayerPose` is `ITicketed`, in a `SeaShell` | `[sig]` |
| `PlayerCroucher`, `PlayerSitter`, `PlayerSleeper`, `PlayerHands`, `PlayerMisc` | modules | `[sig]` |

### 6.2 `PlayerNetworking` sync vars `[sig]`

| field | type @offset | hook | meaning |
|---|---|---|---|
| `lookIdHead`, `lookIdTorso`, `lookIdLegs` | `int` @0x7C/0x80/0x84 | `OnSetLook*` | player colors |
| `playerHeldInformation` | `PlayerHeldInformation` @0x88 | `OnSetHeld` | held prop: `identity` (`NetworkIdentity`), `heldType`, drop data, `actionNumber` |
| `poseReference` | `ShellReferenceWithActionNumber` @0xC0 | `OnSetPose` | current `PlayerPose` (empty = no pose) |
| `trueCrouchness` | `float` @0xD0 | | crouch 0..1 |
| `isSitting` | `bool` @0x14B | | ground sit (`PlayerSitter`) |
| `isSleeping` | `bool` @0x148 | | |
| `headState` | `Vector2` @0xE4 | | head look (INFERRED: pitch and yaw) |
| `controlsVelocity` | `Vector3` @0xD8 | | |
| `heldSwitch` | `ShellReferenceWithActionNumber` @0x138 | `OnSetHeldSwitch` | switch held down |
| `username` @0xF0, `identifier` @0xF8, `moderationName` @0x100, `epicUserId` @0x120, `platform` @0x159 | | | identity |
| `isHost` @0x128, `isGhost` @0xEC, `isAudioGhost`, `is2DVoice`, `isMuted`, `hasMenuEyes`, `isTextChatting` @0x14C, `isPending` @0x158 | | | flags |
| `inGameBlockedUsers` | `SyncHashSet<ulong>` @0x118 | | |

Facing: the rotation of the player body goes through `HouseNetworkTransform` (`CmdMove` packed
rotation), and the head direction goes through `headState`. This is INFERRED.

### 6.3 Server entry points for restore `[sig]`

| method | use |
|---|---|
| `[Server] PlayerNetworking.ServerEnterPoseAuto(ShellReference)` / `ServerExitPoseAuto()` | put the player in a pose |
| `[Server] PlayerNetworking.ServerPickUpPropAutomatic(Prop)` / `ServerDropPropAutomatic(bool)` | held prop |
| `[Server] PlayerNetworking.ServerSetLook(int lookId, LookPart, bool saveChange)` | colors |
| `Prop.ServerSetPinned(PropHome)` with `PlayerRegistry.backpackPocket` / `holsterPocket` / `blindfoldPocket` | equipped items |
| `[Command] CmdSetCrouchness(float)`, `CmdSetSitting(bool)`, `CmdEnterPose(ShellReference,int)` | client side (owner) |

### 6.4 Colors

- Key: `PlayerLooks.GetSaveKey(LookPart, identifier)` -> `<identifier>keyLookIdHead` etc. `[ISIL]` `[disk]`
- Load: `PlayerNetworking.Start` -> `PlayerLooks.ServerLoadLook(forceNew)` -> `LoadOne` ->
  `SaveManager.GetIntValueWithBackup(key, backupKey, GetRandomLookId())` -> `ServerSetLook`. `[CG]` `[ISIL]`
- Change: `CmdChangeLook(int, LookPart)` -> `ServerSetLook(..., saveChange)` -> `ServerSaveLook`. `[CG]`
- Colors are per save slot. To carry colors to all saves, copy these keys into each slot (INFERRED).

### 6.5 Identifier (Steam ID mapping)

- `HouseAuthenticator.AuthedCredentials`: `playerIdentifier`, `platformUserId`, `productUserId`,
  `platform`, `displayName`. `[sig]`
- `IdentifyConnectionAsync` uses `EOSSingleton.GetProductUserId`, `ProductUserId.FromString`,
  `EOSProjectManager.QueryPlatformIdentity`, `IdentifierAlreadyExists`. `[ISIL]`
- `PlayerNetworking.identifier` = `playerIdentifier`. This is INFERRED: the value is the EOS PUID
  when EOS is up. Otherwise, it is the platform user id. The save on this computer has both forms:
  `76561198018819420keyLookIdHead` (SteamID64) and `0002bae7f1144f248dacff74f1585f9fkeyLookIdHead`
  (32-hex PUID). `[disk]`
- `PlayerNetworking.PlatformUserIdChanged` (`Action<ulong>`) and `epicUserId` give the other ids. `[sig]`
- Store the save by Steam ID as `DESIGN.md` says. Also map the game identifier, because the game
  keys (colors, corpse match) use the identifier.

---

## 7. Player disconnect and the corpse (logged-out vessels)

### 7.1 Sequence when a client leaves

| step | code | source |
|---|---|---|
| 1 | `HouseNetworkManager` does not override `OnServerDisconnect` | `[sig]` |
| 2 | Mirror `NetworkManager.OnServerDisconnect` -> `NetworkConnectionToClient.DestroyOwnedObjects` + `RemoveFromObservingsObservers` (= `DestroyPlayerForConnection`) | `[ISIL]` (Mirror) |
| 3 | Mirror destroys the player object. On the host, `PlayerCharacter.OnStopClient` runs | `[ISIL]` |
| 4 | `PlayerCharacter.OnStopClient`: if `isLocalPlayer` -> `PlayerCameraMinder.ReturnCamera` only. Else if `isServer` and not `misc.blockCorpse` (`PlayerMisc@0x28`): `Corpse.CreateAndSpawn(this, registry.corpsePrefab)` | `[ISIL]` |
| 5 | then: `PlayerActions.ActionReleaseWorldSwitch`, `PlayerHands.Drop`, `PlayerPoser.ExitPose`, `PlayerMisc.EmptyAllPockets` | `[ISIL]` |

### 7.2 `Corpse : NetworkBehaviour`

| member | kind | meaning | source |
|---|---|---|---|
| `Corpse.allCorpses` | static list | | `[sig]` |
| `headColorIndex`, `torsoColorIndex`, `legsColorIndex` | `[SyncVar]` int @0x68/0x6C/0x70 | colors | `[sig]` |
| `identifier` | `string` @0x78 | owner identifier. Not a sync var | `[sig]` |
| `prop` | `Prop` @0x88 | the corpse is a `Prop`: players can carry it, and it moves by `HouseNetworkTransform` | `[sig]` |
| `backpackHome`, `holsterHome`, `grasperHome`, `blindfoldHome` | `PropHome` @0x90..0xA8 | the owner's items | `[sig]` |
| `CreateAndSpawn(PlayerCharacter, GameObject prefab)` | static | `Instantiate` at the player, `NetworkServer.SpawnObject`, `ServerSetPinned` of the pockets and the held prop (`PlayerHands.Drop`) into corpse homes | `[ISIL]` |
| `FindMatch(string identifier)` / `IsMatch` | static / private | look up by identifier | `[sig]` `[CG]` |
| `ExpireDueToRevival(PlayerCharacter)` | | moves the pockets back to the player, calls `ServerEmptyAllPockets` and `Prop.PrepareForDestroy`, then destroys the corpse | `[ISIL]` |
| `SpecialCorpseZone` (`spawnTransform`), `PlayerMisc.specialCorpseZone` | | some zones put the corpse at a fixed point (INFERRED) | `[sig]` |

### 7.3 Rejoin: `HouseNetworkManager.AddPlayerDelayed` (`<AddPlayerDelayed>d__13.MoveNext`) `[ISIL]`

1. The method reads `AuthedCredentials` from `conn.authenticationData`. If the data is missing, it
   logs `"no auth data"`.
2. It sets `start = NetworkManager.GetStartPosition()`.
3. It calls `corpse = Corpse.FindMatch(credentials.playerIdentifier)`. If the method finds a corpse,
   it sets `start = corpse.transform`.
4. It calls `Instantiate(playerPrefab, start.position, start.rotation)`. Then it calls
   `NetworkServer.AddPlayerForConnection`.
5. If the method found a corpse (server only), it moves `corpse.backpackHome.pinnedProp` to the
   player with `ServerSetPinned`. It logs `"corpse expiring backpack transfer"`.
6. Then it calls `ServerEmptyAllPockets` and `PrepareForDestroy`, and it destroys the corpse.

### 7.4 Meaning for Hikers' Diary (INFERRED)

- Inside one session, the game already does most of the "logged-out vessels" work: the corpse
  stays, it keeps the items and colors, and the owner rejoins at the corpse.
- Across a game load, the mod must save each corpse (identifier, transform, colors, pocket props).
  At load, the mod must spawn the corpses again with these steps:
  1. Call `Instantiate(PlayerRegistry.corpsePrefab)`.
  2. Set the fields.
  3. Call `NetworkServer.Spawn`.

  Then the vanilla rejoin path puts each player at their corpse.
- A `PlayerCharacter` is necessary for `Corpse.CreateAndSpawn`. For a load, the mod must do the
  steps of that method itself.
- The host's own player is `isLocalPlayer`, so the game makes no corpse for the host. The mod must
  save the host's player state directly.
- Ownership: `AddPlayerForConnection` gives the client authority over its player. The client then
  moves it with `CmdMove`. The server cannot move an owned player with only a transform write
  (INFERRED). Spawn the player at the correct position instead (step 4).

---

## 8. Text chat

| class.member | side | meaning | source |
|---|---|---|---|
| `PlayerTexter.TrySendTextChat()` | local client | `CompleteInput`, `TextChatInput.CloseInput`, sounds | `[ISIL]` |
| `PlayerTexter.CompleteInput(string message, out bool messageSent)` | local client | removes new lines, adds the message to the recent list, sends `CmdSendTextChatMessage` (inlined), and calls `CompleteDisplayMessage`. It logs `"textChat message was null or empty and will be ignored"` | `[ISIL]` |
| `[Command] PlayerNetworking.CmdSendTextChatMessage(string)` -> `UserCode_CmdSendTextChatMessage` | server | sends `RpcTextChatMessage(message)` with no filter | `[ISIL]` |
| `[ClientRpc] PlayerNetworking.RpcTextChatMessage(string)` -> `PlayerTexter.ReceieveMessage(string)` (sic) | all clients | shows the message | `[ISIL]` |
| `TextChatInput` | UI | `CHARLIMIT = 200`, `OpenInput`, `CloseInput`, `GetString` | `[sig]` |
| `PlayerNetworking.isTextChatting`, `CmdSetIsTextChatting`, `CmdTextChatStroke` | | typing state | `[sig]` |

Hook points (INFERRED): a prefix on `PlayerTexter.CompleteInput` sees a local command before the
client sends it, and the prefix can stop it. On the host, a prefix on
`PlayerNetworking.UserCode_CmdSendTextChatMessage` sees commands from all clients. Do not hook the
`CmdSendTextChatMessage` wrapper. IL2CPP inlined it into `CompleteInput`, so a hook on it does not
fire for that path.

---

## 9. Sleep and sit

### 9.1 `PlayerSleeper` (module, not a `MonoBehaviour`)

| member | @offset | meaning | source |
|---|---|---|---|
| `timeTilSleep` | 0x10 | idle seconds before sleep. The value is prefab data (this read did not examine it) | `[sig]` |
| `preventSleeping` | 0x15 | if true, the idle timer resets each frame | `[ISIL]` |
| `forceSleeping` | 0x16 | if true, `timeAtLastAction = 0` (sleep now) | `[ISIL]` |
| `timeAtLastAction` | 0x20 | reset by any Rewired button or axis input, and by `sleeplessZone` | `[ISIL]` |
| `localIsSleeping` | 0x1E | | `[sig]` |
| `sleeplessZone` | 0x38 | `SleeplessZone` blocks sleep | `[sig]` `[ISIL]` |
| `Update()` | | local player: `sleeping = Time.time - timeAtLastAction > timeTilSleep`. On change: `PlayerNetworking.CmdSetSleeping(bool)`. Also checks `PlayerNetworking@0xEC` (`isGhost`) | `[ISIL]` |
| `RecordAction()`, `combinedIsSleeping` | | | `[sig]` |

The mod can make the sleep timeout shorter with `timeTilSleep`, or start sleep with
`forceSleeping` (INFERRED from `[ISIL]`). `PlayerDreamer` and `HouseHouse.Dream.DreamController`
(`RpcSetDream`) are the dream visuals (not read).

### 9.2 Sit

| member | meaning | source |
|---|---|---|
| `PlayerSitter.SetSittingLocal(bool)`, `isSittingCorrected`, `SetNoSitActionTime(float)` | ground sit. It sends `CmdSetSitting` | `[sig]` `[CG]` |
| `PlayerNetworking.isSitting` | `[SyncVar] bool` @0x14B | `[sig]` |
| `PlayerPose.allowSitting` @0x27, `emptyAllPockets`, `leaveWithJump`, `onEnterSwitch` | pose on a world object (bench, seat) | `[sig]` |
| `PlayerMisc.isInPoseOrSitting` | | `[sig]` |

---

## Risks and open questions

1. Vehicle names: no gondola or monorail symbol exists. Map them at runtime: log the GameObject
   name, `hasCable`, and the splines of each `NetworkedTrain` and `MovementDriver`.
2. Corpse respawn on load: `Corpse.identifier` is not a sync var, and a source player is necessary
   for `CreateAndSpawn`. The mod must set the same fields on each corpse that it makes. Test that
   `FindMatch` and the rejoin transfer work on a corpse that the mod makes. This is the spike for
   revision (a).
3. Host player: the game makes no corpse for the host. When the host signs off, the session ends.
   The save must hold the host pose from the last snapshot.
4. Identifier form: `playerIdentifier` can be the EOS PUID or the platform id. Confirm the rule
   before you key the save by Steam ID.
5. Transport: EOS P2P + IP. The "Steam lobby" plan in `DESIGN.md` section 3.4 does not match the
   game. Non-friend discovery must use the EOS lobby (`EOSPrivateLobbyManager`, `BigWalkLobbyData`,
   `StatusSetter.lobbyData`) or Steam rich presence.
6. Prop keys: no test shows that `sceneId` is stable across loads and builds. Save the GameObject
   path as a second key.
7. IL2CPP inlining: IL2CPP can inline Mirror `Cmd*` wrappers into their callers. This occurs for
   `CmdSendTextChatMessage`. Hook `UserCode_*` methods or the caller.
8. Conflict with the vanilla save: the game writes pinned homes and switch states into the slot
   file. The mod's prop restore must run after `Prop.Start` and `TrackedPeckState.Initialize`, or
   the vanilla load moves props back to their saved homes.
9. This read did not examine these items:
   - the body of `PlayerCountMenu.SetPlayerCount` (where the count is stored)
   - `HostMenuSelect.ActionSelectSaveData` (ISIL failed: "Jump target not found")
   - the server branch condition of `LobbyEnviroNetworking.Update`
   - the value of `PlayerSleeper.timeTilSleep`
