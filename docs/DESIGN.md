# Hikers' Diary — design

This is the permanent design. Each milestone has its own `M*-PLAN.md`.

## 1. Goal

Big Walk resets the world when a game loads. Hikers' Diary keeps it as the players left it.
The game starts directly in the last save, and players change saves from text chat.

## 2. Operator specification (2026-11-27)

1. The game binary skips the title screen and all setup.
2. Every game is online by default.
3. If there is no save file, the mod makes a save for 2 players. If a save exists, the last
   save that the players played loads.
4. Each player (remote and local) joins at the location where they left. The pose (stand,
   crouch, sit), the facing direction, the held item, and the equipped items are the same.
5. Player bodies (logged-out vessels) stay across game loads.
6. All objects stay at the location where the players left them. Their locations never reset
   between game loads.
7. Vehicle locations (monorail, gondola, solo gondola), the state of the lights, and the time
   of day stay across game loads.
8. Player colors carry to all saves.
9. Save switch without the title screen:
   1. Sit.
   2. Open text chat.
   3. Type a command and submit it.
   4. Fall asleep. The command makes the sleep timeout shorter.
   5. The view and the audio decrease to zero. The other save loads. The view and the audio
      increase again, at the player's pose in that save.
   - Make, or change and load: `new: [<2|3|4>p: ]<Save Title>[, <password>]`. It can change
     the player count or the session password of a save that exists.
   - Join, or load with no change: `goto: <Save Title|Session ID>[, <password>]`. If there is
     no password and a friend's session with that title exists, use the most recent password.
     If the join fails and there was no password, host the local copy. If there is no local
     copy, the player wakes and stands just before the sleep transition is complete.
10. One or two sign objects in the start area show hints for the two commands. The join hint
    shows the title and the ID of the current session.
11. At game start, try to join or load the latest session. If that fails, try the earlier sessions
    in the session history, from the newest to the oldest.
12. Stretch: when a save with history loads, show a slideshow of its last 20 minutes. Use one
    frame and a maximum of 5 s of audio for each minute of play. The slideshow becomes faster,
    so that the last frames show for approximately 300 ms each. It also plays at game start.

## 3. Revisions agreed with the operator

1. Three revisions:
   - (a) Persistence: objects, vehicles, lights, time of day, players, vessels, colors.
   - (b) Save switch by chat and sleep, and the signs.
   - (c) Join at start, and the slideshow (stretch).
2. A save title alone in chat starts a transition ONLY if it is a definite match to a known
   local save or friend session. Normal chat must never start a transition. Only the `new:` command
   makes or changes a save. A picker for saves (the player does not type) comes later. Do not build it
   until the liaison says so.
3. The host is the authority. The host's save holds the state of all players, by Steam ID.
   An OPTIONAL mod daemon runs in the background. For each player who runs it, the latest
   save syncs to all players. Thus any player can host, and when the host disconnects, a
   different player can continue as host. A group can also run the mod as host on a
   dedicated computer.
4. Non-friend discovery: the Steam lobby data holds the title and a hash of the password.
   A lobby search finds the session by its title.
5. At start, the history walk makes a maximum of 3 attempts in approximately 20 s. Then the
   mod hosts the last local save.
6. A player who does not have the mod can break the saved state. Refuse the join, or show a
   warning on a sign.
7. Logged-out vessels have the highest risk (network ownership of player objects). Do a
   spike on them first in revision (a).
8. The slideshow keeps a small ring of low-resolution frames and audio on disk.
9. Gate on the game build. On a build that is not known, the mod stops safely.

## 4. Side project: local control seam (not in revisions a to c)

A separate SPT shell will run on the same computer as a modded session. It will connect to
the mod through a local API and control one pseudo-player for each shell instance. During
revisions (a) to (c), keep the player state model and the input path clean. A local API
must control them later. Do not build the API now.

## 5. Platform facts

- Unity 6000.3.17f1, IL2CPP. BepInEx 6.0.0-be.755.
- The network library is Mirror (MirrorIgnorance transport on Steam). Voice is Dissonance.
- Harmony postfixes on interop proxies work. A postfix on a method of a GENERIC base class
  installs but does not fire. Hook non-generic methods.
- Generic Il2Cpp proxy constructors that take T pass a boxed pointer for struct proxies
  (crash in `coreclr.dll`). See the TravelEar notes for the workaround.
- BepInEx scans every DLL under `BepInEx/plugins` as a plugin. Put helper processes (for
  example the daemon) outside that folder.
- Il2Cpp `ReadOnlyCollection` has no `foreach`. The BepInEx disk log shows Info and above
  only.
- Test with the VR mod disabled first.
- Use `ilspycmd` on `BepInEx/interop/*.dll` to get the signatures. Use Cpp2IL
  2022.1.0-pre.21 to get the bodies (`--use-processor attributeinjector,callanalyzer --output-as dll_il_recovery`,
  plus `diffable-cs` and `isil`). Il2CppDumper does not support metadata v39.
