# M2 — Revision (b): save switch and game start

The milestone is #16. The requests are #13 (game start), #14 (save switch by chat and sleep),
#15 (hint signs) and #9 (save picker). The design is in `docs/DESIGN.md`, spec 9 and 10, revision rules 2, 10 and 12.
The operator approved the milestone on 2026-11-28.

## Scope

- #13: at game start, the first screen (`MicCheckMenu`) is white on black and has no continue
  button. The latest save loads behind it. Then the drowsy view increases to full, the intro
  music decreases to zero, and the continue button appears.
- #14: The player types the `new:` or `goto:` chat command and then sleeps. Then the other
  save loads.
- #15: hint signs for the two commands in the start area.
- #9: the save picker on the pegboards in the start area.

These items are not in the scope: the join at start, the session history walk and the slideshow (revision c).

## Game facts (from `ilspycmd`, 2026-11-28)

- `MainMenuManager` contains all the menus (`micCheckMenu`, `titleMenu`, `splashMenu`,
  `loadingMenu` and more) and its own camera `myCamera`.
- `MicCheckMenu` has `continueButton`, `ActionContinue()` and `GoToTitleMenu()`.
- `MenuMusicPlayer` plays the menu music (`Stinger`, `Loopable`, `MicSelect`, `GoodbyeTrack`).
  It has `Play()`, `Stop()` and `CurrentPlayMode`.
- `MenuFade` and `SplashMenu.fadeGroup` (a `CanvasGroup`) fade the menus.
- `PlayerSleeper` makes the drowsy view: `timeTilSleep`, `forceSleeping`, `smoothSleepiness`.
- The test tool `Spike.AutoHost` (`DiaryHost.TryAutoHost`) already hosts the last save that
  the player played, from the first menu. #13 uses the same steps.

## Tasks, in order

1. **T1 — start flow spike (#13).** Log the order of the menus, the scenes and the cameras
   from the game start to the gameplay, with `AutoHost` on. Find these facts:
   - Does the menu canvas stay when the game scene loads, or does the game unload it?
   - Which camera draws the drowsy view, and when is it ready?
   - How does the game go from the menus to the gameplay (`ActionContinue`, the loading
     menu)?
   If the menu canvas cannot stay over the game view, tell the operator before you start T2.
   Result (2026-11-28, test tool `Spike.LogStartFlow`, flat screen):
   - At 0.02 s, `BaseScene` contains `MicCheckMenu` and `UICamera`. The music is `MicSelect`.
   - At 3 s, `AutoHost` starts the host, and `WorldScene` starts to load.
   - At 5 s, the game unloads `BaseScene`. The menus and `UICamera` stop. The music
     continues (`MicSelect`).
   - At 7.3 s, `PlayerCamera` (in `WorldScene`) starts.
   - The drowsy view is the sleep state of `PlayerSleeper`. It starts after 70 s with no player
     action. `smoothSleepiness` then increases from 0 to 1 in 0.2 s.
   - Thus, the menu canvas does not stay by itself. The next spike moves the menu canvas to
     `DontDestroyOnLoad` before the game unloads `BaseScene`.
   Result of spikes 2 to 6 (2026-11-28, flat screen):
   - The root canvas of `MicCheckMenu` is `MainMenu`. It is `ScreenSpaceOverlay` and has no
     camera. When the host starts, the spike moves it to `DontDestroyOnLoad`. It stays after
     the game unloads `BaseScene`, and the game view is visible behind it. The text is black,
     and no black background is behind it.
   - From 5 s to 9.5 s, `MenuVolumeMeter.Update` throws approximately 33 exceptions.
   - The canvas does not make the frames slower. With the canvas, the frame time is 3.4 ms to
     3.6 ms. Without the canvas, it is 3.4 ms to 4.2 ms. The start flow log caused the slow
     frames that the operator saw, because it searched all objects in each frame.
   - The snore is `PlayerAudioReferences.enterSleepSound`. `PlayerSleeper.Update` plays it in
     the frame in which the sleep state starts. If the code sets `_wasSleeping` and
     `_sleepSoundToggle` to true before it sets `forceSleeping`, the snore does not play, and
     the drowsy view starts in 0.3 s.
   - The game blocks the player input for a menu with `PlayerMenu.SetMenuOpen(bool)`
     (`PlayerCharacter.menu`) and `PlayerMenu.BlockActionsPostMenu()`. It releases the mouse
     cursor with `CursorManager.SetFree()` and locks it with `CursorManager.SetLocked()`.
<!-- [doc->REQ-GAME-START] -->
2. **T2 — game start (#13).** When T2 starts, add `REQ-GAME-START` to `traceable-reqs.toml`.
   1. In the first frame of the first screen, put a black image behind the content of the
      first screen. Make the text and the divider lines white. Hide the continue button. Set
      the sort order of the `MainMenu` canvas to 32000. Then `WorldUICanvas` cannot cover the
      `MainMenu` canvas.
   2. After 3 s, host the latest save. Use the steps of `TryAutoHost`. If there is no save,
      return the colors to their initial values. Then show the continue button.
   3. When the host starts, move the `MainMenu` canvas to `DontDestroyOnLoad`. Disable
      `MenuVolumeMeter` until the game view is ready.
   4. When the local player exists, start the drowsy view without the snore. When the game
      starts a new sleep state, it plays the snore. Thus, set `preventSleeping`. Then the game
      does not start its sleep state. Add a patch on `PlayerSleeper.Update`. In the patch, keep
      the full drowsy view. Call `PostProcessingManager.SetSleepiness(1)`.
   5. While the first screen is visible, make the game input control the first screen, not the
      player:
      - Keep the menu controls (`ControlsManager.SetMenuMode(true)`).
      - Open the world menu. Hide the pause menu of the world menu.
      - Skip `WorldMenuManager.Update`. Keep `WorldManager.inUI` false. Then ESC has no effect.
      - Block `PlayerMenu.SetMenuOpen` for the local player. Then ESC does not change the pose.
      - Do not let the game lock the cursor. If the game locks the cursor, unlock the cursor.
        Then move the cursor to its last position.
      - Hide the crosshair.
      - Send the sleep state of the local player to the other players
        (`PlayerNetworking.CmdSetSleeping(true)`). Then they see the sleep pose.
   6. Then do the fade: make the black image transparent gradually during 5 s
      (`Start.FadeSeconds`). At the same time, decrease the volume of the intro music to zero.
      Increase the volume of the Environment and SFX buses from zero. Do not let the game stop
      the intro music before the fade ends.
   7. Then show the continue button. When the user clicks the continue button, do these steps:
      - Close the first screen and the world menu.
      - Set the gameplay controls.
      - Lock the cursor.
      - Show the crosshair.
      - Clear `preventSleeping`. Set `_wasSleeping` and `_sleepSoundToggle`. Then the game
        finds the end of the sleep state and plays the wake sound.
      - Send the end of the sleep state to the other players.
      - Stop the drowsy view with the smoothing of the game. Do not set `smoothSleepiness`.
        Give the value of `smoothSleepiness` to `SetSleepiness`. Use the same value for the
        alpha of the first screen. When the value is 0.01 or less, remove the first screen.
        The drowsy view stops in approximately 0.33 s.
   Result (2026-11-28): the operator tested T2 on a flat screen and accepted it. It is possible
   that the `MainMenu` canvas is not visible in VR. The operator must test T2 in VR.
<!-- [doc->REQ-SAVE-SWITCH] -->
3. **T3 — save switch (#14).** The requirements are `REQ-SAVE-SWITCH` (the switch) and
   `REQ-CHAT-COMMAND` (the parser and rule 2). When a task starts, activate its stages.

   These are the game facts. Their source is `ilspycmd` (2026-11-28). The interop DLLs have the
   signatures only. Thus, the spike must measure the behavior.
   - `TextChatInput` (`instance`, `inputIsOpen`, `inputField`) has `OpenInput()`,
     `CloseInput()`, `GetString()` and `RecordMessage(string)`. Possibly, `RecordMessage`
     sends the message. `TextChatSource.AddMessage(TextChatMessage)` shows a message over a player.
     `TextChatMessage` has `message` and `sendingPlayer`.
   - `PlayerSitter.isSittingLocal` and `isSittingCorrected` tell if the player sits.
   - `PlayerSleeper` has `timeTilSleep` (30 s), `timeAtLastAction`, `RecordAction()`,
     `forceSleeping`, `preventSleeping` and `combinedIsSleeping`.
   - `SaveData` has `slotName`, `password`, `filenameUid` and `lastPlayedTimeAsLong`. The
     player count is not a field. `SaveManager.GetAllSaveDatasInFolder()`,
     `WriteCurrentSaveData()`, `RenameSlot(...)` and `SetPassword(string)` exist.
   - `NetworkMinder` has `StartHost()`, `JoinGame(string)` and `SetServerPassword(string)`.
     `DisconnectConfirmMenu.ActionDisconnect()` ends a session from the gameplay.
     `DiaryHost.HostLatestSave` has the steps to host a save from the menus.

   Open questions for the spike:
   - Which method sends a chat message? Can the mod read the text and stop the message
     before the game sends it?
   - After `ActionDisconnect`, does the game return to `BaseScene` and the menus? How long
     does it take? Can the mod then host a different save with the steps of `HostLatestSave`?
   - Does the T2 black cover (the `MainMenu` canvas in `DontDestroyOnLoad`) stay visible while the
     game disconnects and loads the new save? If it does not stay, which other cover can stay?
   - Where does the game keep the player count of a save?
   - Does the current `DiaryHost` code restore the pose of the player in the new save?

   Do these tasks in this order:
   <!-- [doc->REQ-CHAT-COMMAND] -->
   1. **T3a — command parser (Core, unit tests).** Parse `new: [<2|3|4>p: ]<title>[, <password>]`
      and `goto: <title|session ID>[, <password>]`. If a text starts with `new:` or `goto:`, it is a
      command. If a text is a definite match to the title of a local save, it is also a command
      (rule 2). Other text is not a command. The match ignores the case and the spaces at the two ends. A title that matches
      two or more saves is not a definite match.
   2. **T3b — spike (flat screen).** Answer the open questions above. Log these items while
      the mod disconnects and hosts a new save from the gameplay:
      - the chat messages that the game sends
      - the sit state and the sleep state
      - the scenes and the cameras
      Result (2026-11-28, test tool `Spike.SwitchSpike`, flat screen, 4 runs):
      - The game sends a chat message in `PlayerTexter.TrySendTextChat()`. `PlayerTexter.Update`
        calls it when the player pushes the submit button. Then `CompleteInput` and
        `CompleteDisplayMessage` send the message and show it. `TextChatInput.Update` only
        shows the previous entries of the history. The events `onSubmit` and `onEndEdit` of the
        input field have no listeners.
      - A prefix on `TrySendTextChat` can read `TextChatInput.GetString()`. If the text is a
        command, the prefix calls `PlayerTexter.TextChatCancel()` and skips the method. Then
        the input closes, and the game does not send the message. The test tool did these steps.
      - In `BaseScene`, `TextChatInput.instance` throws `NullReferenceException`. Read
        `TextChatInput.instance` only after the world loads.
      - The initial value of `timeTilSleep` is 30 s. After 25 s with no action, the mod set it
        to 5 s. The sleep state started 0.1 s later.
      - `DisconnectConfirmMenu.ActionDisconnect()` ends the session. After 0.2 s, `BaseScene`,
        `UICamera` and `MicCheckMenu` are active again. Then the steps of `HostLatestSave` host
        a save. After 2 s, the world loads. 3.6 s after the disconnection, `PlayerCamera` starts.
      - `DiaryHost` restored the props, the vehicles, the time and the pose of the host when
        the save loads the second time. No change was necessary.
      - A save does not keep the player count. The entries are the game keys only (for example
        `worldSeed` and the gates). Thus, the diary must keep the player count of each save.
      - After the disconnection, the game shows the first screen again. Thus, T3c can do the T2
        start steps again with the target save:
        - show the black first screen
        - host the save
        - fade in
        During the disconnection, the T2 cover is not necessary. The view must be black before
        the disconnection.
      - The test did not make the player sit. T3c must measure `PlayerSitter.isSittingLocal`.
   3. **T3c — local switch.** When a command arrives from the local player, do not send it to
      the chat. While the player sits, set `timeTilSleep` to a short value (default 5 s). If
      the player stands or cancels the command, set the initial value again. When the sleep
      starts, decrease the view and the audio to zero (fade out). Then disconnect. Then host
      the target save. For `new:`, first make the save, or change its player count and its
      password. Then use the T2 cover. Then increase the view and the audio again (fade in).
      The player wakes at the saved pose. The continue button is not necessary for a switch.
      Design (2026-11-28):
      - The chat: a prefix on `PlayerTexter.TrySendTextChat` gives the text to `ChatCommand.Parse`
        with the titles of the local saves. If the result is a command, the prefix stops the
        message (the T3b steps). A new command replaces the previous command.
      - The target: a `goto:` command must have an exact match to the title of one local save.
        If it has no match, the mod writes a log line and does not start the switch. T3d adds the
        friend sessions. A `new:` command with no match makes a new save. If a `new:` command with a
        match gives a player count or a password, the mod changes them in that save.
        If two or more saves have the title, the mod writes a log line and does not start the
        switch.
      - A new save: do the steps of `HostMenuConfirm.ActionStart` (Cpp2IL body). Make a
        `SaveData` with empty lists. Set `filenameUid` to the first 16 characters of a new GUID
        (format `N`). Set `slotName` and `password`.
      - The player count: the diary file keeps it (`DiarySave.PlayerCount`, version 8). The
        snapshot writes `PlayerCountSwapper.playerCount`. When the mod hosts a save, it reads the
        count from the diary file. The default is 2 players. T2 (`HostLatestSave`) uses the same
        step.
      - `SwitchSequence` (Core, unit tests) holds the order: `Idle`, `Pending`, `FadeOut`,
        `Disconnect`. In `Pending`, the mod uses the short timeout while the player sits. If the
        player stands after the player sits, the command stops. When the sleep starts while the player sits,
        the fade-out starts. If the player wakes during the fade-out, the switch stops and the
        view and the audio return. At the end of the fade-out, the phase is `Disconnect`.
      - The fade-out: a new canvas (`ScreenSpaceOverlay`, sort order 32000, in
        `DontDestroyOnLoad`) has a black image. Its alpha increases from 0 to 1 during
        `Start.FadeSeconds`. `AudioListener.volume` decreases from 1 to 0 at the same time.
      - The disconnection: call `DisconnectConfirmMenu.ActionDisconnect()`. Then start the T2
        sequence again in the switch mode, with the target save. The switch mode has these
        differences:
        - The intro music stops at the first screen. The fade-in does not change the music.
        - When the first screen is black, remove the fade-out canvas. Then set
          `AudioListener.volume` to 1. The game buses stay at zero until the fade-in.
        - The host step hosts the target save, not the latest save.
        - At the end of the fade, do the continue steps immediately. The game does not show
          the continue button.
      - Config: `Switch.SaveSwitch` (default true) and `Switch.SleepSeconds` (default 5).
      - Test aid: `Spike.SwitchCommand` (empty = off). 10 s after the gameplay starts, the mod
        sends this text through `TrySendTextChat` and calls `PlayerSitter.SetSittingLocal(true)`.
      Result (2026-11-28, flat screen, test aid, `Start.GameStart` false and `Spike.AutoHost` true, 3 runs):
      - `PlayerSitter.SetSittingLocal(true)` makes the player sit with no seat. `isSittingLocal` is
        then true.
      - With a sit, the sleep started 5 s after the command. The fade-out took 5 s. The view of the
        target save came back 11 s after the disconnection, at the saved pose.
      - `goto: ModTests` hosted the same save again. `new: 3p: SwitchTest, pw1` made a new save and
        hosted it for 3 players. The next game start hosted `SwitchTest` for 3 players, from the
        diary file. Then `goto: ModTests` hosted `ModTests` for 2 players.
      - The log has no exceptions. The chat did not send the commands.
      - The operator must examine the view and the audio of the fade-out and the fade-in.
   4. **T3d — join a friend session.** A `goto:` command gives the title of a friend session or
      a session ID. Join it with the given password, or with the most recent password for that
      session. If the mod cannot join the session and there is no password, host the local
      save. If there is no local save, the player wakes and stands before the fade out is
      complete. Plan T3d after T3c, because the spike did not measure the EOS lobby search.

      These are the game facts. Their source is `ilspycmd` and the Cpp2IL ISIL dump (2026-11-28).
      - The session ID is the join code: 6 characters (`EOSPrivateLobbyManager.IsValidCode`). It is
        also the EOS lobby ID. `JoinLobbyAndConnectByCode` calls `JoinLobby(code, crossplay)`, and
        `JoinLobby` calls `LobbyInterface.JoinLobbyById`. The crossplay value comes from
        `PlayerPrefs` key `settings_crossplay`.
      - The host lobby has the attributes `world_name`, `world_username` and
        `world_owner_platformid`. The bucket is `defaultbucket`. The spike must measure if
        `world_name` is the save title.
      - The friend sessions come from `FriendGamePresenceManager.Presence.CurrentLobbies`. It is a list of
        `BigWalkLobbyData` (`lobbyCode`, `userName`, and `roomName` from the base type `LobbyData`). `JoinMenu` gets this list
        through `RegisterForLobbyUpdate`. On Steam, the source is the rich presence of the friends.
      - To join, `JoinMenu.ConnectTo(code)` calls `NetworkMinder.SetToEOSTransport()` and
        `NetworkMinder.JoinGame(code)`. The test aid `Spike.AutoJoinCode` already uses it (M1).
        If the host has a password, the client shows `PasswordMenu`. `SubmitPassword` calls
        `NetworkMinder.SetServerPassword` and sends the password.
      - If a join fails, `JoinMenu.OnFailedConnection` and `HouseAuthenticator.OnAuthResponseMessage`
        call `MainMenuManager.ShowErrorMenu`. `PasswordMenu.Cancel` calls `NetworkManager.StopClient`.
      - The EOS lobby search is in `com.Epic.OnlineServices.dll`. `EOSManager.Instance` has
        `GetEOSLobbyInterface()` and `GetProductUserId()`. `LobbyInterface.CreateLobbySearch` makes a
        `LobbySearch`. It has `SetParameter` (an `AttributeData` and a `ComparisonOp`), `SetLobbyId`,
        `Find` (with a `LobbySearchOnFindCallback`), `GetSearchResultCount` and
        `CopySearchResultByIndex`. `LobbyDetails.CopyInfo` gives `LobbyDetailsInfo`
        (`PermissionLevel`, `BucketId`, `AllowJoinById`, `AvailableSlots`). If the permission level
        is not `PublicAdvertised`, a search by attribute can find no lobby.

      Open questions for the spike:
      - Does `CurrentLobbies` have the sessions of the friends during the gameplay, or only while
        `JoinMenu` is open? Is `roomName` the save title of the host?
      - Which permission level, bucket and attributes does the lobby of the host have?
      - Does a lobby search by `world_name` find the lobby of a friend? Does it find the lobby of
        a different user who is not a friend? Does a search by lobby ID find it?
      - Does `DelegateSupport.ConvertDelegate` work for `LobbySearchOnFindCallback` (a `ref`
        parameter)? If it does not work, read `GetSearchResultCount` some seconds after `Find`.
      - After a failed join (a bad code or a bad password), which menu shows? How long does it
        take? Can the mod then host the local save with `DiaryHost.HostSave`?

      Do these tasks in this order:
      1. **T3d-1 — spike (flat screen, two computers).** The test aid is `Spike.LobbySearch`. If it is empty, it is off.
         10 s after the gameplay starts, the mod logs these items:
         - the `CurrentLobbies` list (code, user name and world name of each session)
         - the lobby of this session (`LobbyDetailsInfo` and all attributes)
         - the result of a lobby search by `world_name` equal to the test text
         - the result of a search by lobby ID, if the test text is a valid code
         - for each result: the lobby ID, the permission level and the attributes
         The mod repeats the search every 30 s. To test a failed join, use `Spike.AutoJoinCode`
         with a bad code. Then use it with a good code and a bad `Spike.AutoJoinPassword`. Log the
         menus and the time until the first screen or the title menu shows.
         Test: KITSUBITO hosts a save with the second game account. HFENDULEAM searches for its
         title and its code. Then HFENDULEAM hosts a save, and KITSUBITO searches for it. If the two accounts are not friends, the
         `CurrentLobbies` test needs the operator.
         Result of runs 1 to 3 (2026-11-28, HFENDULEAM only, host `ModTests`):
         - `CurrentLobbies` had 0 sessions (`SteamPresence`). No friend was in a session.
         - The lobby of the host has these values: permission `JoinViaPresence`, presence
           disabled, join by ID enabled, bucket `defaultbucket`, 12 members maximum. `world_name`
           is the save title (`ModTests`).
         - An EOS search by attribute finds only `PublicAdvertised` lobbies. Thus, a search by
           title cannot find a lobby of the game. For rule 4, the mod must change the permission
           of the lobby of the host to `PublicAdvertised`. A join by code does not need a search.
         - The `ValueType` wrappers of `com.Epic.OnlineServices.dll` are not safe. The game
           stopped in `LobbyDetails.CopyInfo` (run 1) and in `CopyLobbyDetailsHandle` (run 2),
           with no exception. `SetParameter` returned `InvalidParameters` (run 3).
           `DelegateSupport.ConvertDelegate` cannot convert a delegate with a `ref` parameter.
           `LobbyInfo.detailsInfo` (a copy that the game keeps) is safe to read.
         Result of runs 4 to 8 (2026-11-28, HFENDULEAM and KITSUBITO, `EosLobbyNative`):
         - The spike calls the EOS C API in `EOSSDK-Win64-Shipping.dll` (P/Invoke). The
           interop assembly has no `*_API_LATEST` constants. The Cpp2IL dump shows that each
           `ApiVersion` of the options is 1. The native callback works. The game did not stop,
           on Windows and on Proton.
         - A search by lobby ID finds the lobby of the other account, in the two directions.
           The result has permission 1 (`JoinViaPresence`), join by ID enabled, and the
           attributes `WORLD_NAME` (the save title), `WORLD_USERNAME` and
           `WORLD_OWNER_PLATFORMID`. EOS gives the attribute keys in upper case.
         - A search by `world_name` finds 0 lobbies (permission is not `PublicAdvertised`).
         - The two game accounts are friends. During the gameplay, `CurrentLobbies` has the
           session of the friend: code, user name, and `roomName` = the save title of the host.
         - A bad code: `ConnectingMenu`, then the error menu `LobbyJoinFailed` 1.5 s after
           `ConnectTo`. A bad password: `PasswordMenu` 3.1 s after `ConnectTo`, then the error
           menu `NoConnectWrongPassword` 0.2 s after `SubmitPassword`.
         - Not measured: `DiaryHost.HostSave` after a failed join. T3d-2 tests it.
         - For T3d-2: resolve a title through `CurrentLobbies` (friend sessions), and a code
           with no search. A title of a session that is not a friend session needs a
           `PublicAdvertised` lobby. T3d-2 does not do it.
      2. **T3d-2 — join a friend session.** Plan the design from the spike result. The parts are
         these: the target (a local save, a friend session or a lobby search result), the password
         memory (the most recent password of each session, in the diary folder), the join from the
         first screen in the switch mode, and the change to the local save if the join fails.
         <!-- [doc->REQ-FRIEND-JOIN] -->
         Design (2026-11-28). The requirement is `REQ-FRIEND-JOIN`.
         - The target (Core `SwitchTarget.Resolve`, unit tests). The mod examines a `goto:` target
           in this order: the title of one friend session (`CurrentLobbies.roomName`), the title of
           one local save, and a session ID (`EOSPrivateLobbyManager.IsValidCode`, in upper case).
           If two friend sessions or two local saves have the title, the target is not valid. A
           `new:` command uses only the local saves (T3c). For rule 2, the parser gets the local
           titles and the titles of the friend sessions that are not also local titles.
         - The lobby check. When a join command arrives, the mod starts an EOS search by lobby ID
           (`EosLobby`, the C API of T3d-1). The result gives `WORLD_NAME`. The fade-out does not
           go to `Disconnect` until the mod has the result. After 10 s, the mod uses "no lobby".
         - The local copy is the local save that has the title of the session. If the search finds
           no lobby and the command has no password, the mod hosts the local copy. If the search
           finds no lobby and there is no local copy (or the command has a password), the switch
           stops at the end of the fade-out. The cover goes away, the player wakes and stands.
         - The password is the password of the command. If the command has no password, the mod
           uses the most recent password of the session ID or of the title (Core `PasswordBook`,
           file `passwords.json` in the diary folder, unit tests). When a join is successful, the
           mod writes the password for the title and for the session ID.
         - The join. After the disconnection, the T2 steps start again in the switch mode. The host
           step calls `NetworkMinder.SetServerPassword` and `JoinMenu.ConnectTo(code)`, and it
           moves the `MainMenu` canvas to `DontDestroyOnLoad`. When `PasswordMenu` shows, the mod
           submits the password. The session starts when the local player exists. In the switch
           mode, the fade-out cover (sort order 32001) stays until the session starts. Thus the
           menus of the join are not visible.
         - A failed join. During the join, a prefix on `MainMenuManager.ShowErrorMenu` stops the
           error menu. A join that takes more than 30 s also fails. Then the mod stops the client.
           If the command has no password and a local copy exists, the mod hosts the local copy.
           If not, it hosts the latest save. If there is no save, the first screen and the menus
           of the game come back.
         - Remove `LobbySpike`. Keep `EosLobby` with the search by lobby ID only.
         - Test aid: `Spike.SwitchCommand` (T3c) with `goto: <code>`. Tests: a good code, a bad
           code, a good code with a bad password, and a friend title.
         Result (2026-11-28, flat screen, HFENDULEAM joins KITSUBITO, `Start.GameStart` false and
         `Spike.AutoHost` true):
         - The lobby check took 0.2 s to 0.25 s. It gave the title `aoieu` for the code `HY4HUE`.
           A bad code (`Z9Z9Z9`) gave result 18 (not found) and 0 lobbies.
         - `goto: hy4hue` with no password: the password menu showed 2.4 s after the join call. The
           empty password gave `NoConnectWrongPassword`. There was no local copy. Thus the mod
           hosted the latest save. Two bugs were found and fixed:
           - A client stop in the prefix on `ShowErrorMenu` caused a `NullReferenceException` in
             `NetworkAuthenticator.ClientReject`. The prefix is in the auth handler of the game. Now
             the mod stops the client only if it is active 1 s after the failure.
           - The game leaves the joined lobby approximately 1 s to 2 s after the failure. The end of the
             leave sets `currentLobbyCode` to null. That occurred after the fallback host made its
             code. Then the host lobby had no code. The leave callback sets the field inline, thus
             no method can be patched. Now, for 15 s after the fallback host, the mod puts the code
             back if it is empty. Then the host lobby had its code (`Hosting lobby RVWHPU`).
         - `goto: HY4HUE, a`: the join was successful 4.5 s after the join call. `passwords.json`
           had `code:HY4HUE` and `title:aoieu`. KITSUBITO saw the client.
         - `goto: aoieu` (a friend title, no password): the mod used the password from the memory.
           The join was successful 4.7 s after the join call.
         - `goto: Z9Z9Z9`: no lobby and no local copy. The player woke and stood at the end of the
           fade-out (1 s later: sit false, sleep false, timeout 30 s).
         - Not tested: the local copy after a failed join. The operator must examine the view of
           the join (the cover stays black until the session starts).

   Test: the agent does each test with `$TEMP/gears/run-game.sh` and examines the logs. Then
   the operator does the acceptance test of #14 on a flat screen.
4. **T4 — hint signs (#15).** Plan it after T3.
5. **T5 — save picker (#9).** Plan it after T4. For the photo, the frame capture is necessary
   (rule 10).

## Test

The operator tests #13 on HFENDULEAM in VR. First, the agent does a test with
`$TEMP/gears/run-game.sh` and examines the logs.

## Gate

`pwsh scripts/gates.ps1` is green (build, unit tests, `traceable-reqs check`, docs site).
When the work on a request starts, activate its requirements in `traceable-reqs.toml`.
