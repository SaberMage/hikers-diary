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
   4. **T3d — join a friend session.** A `goto:` command gives the title of a friend session or
      a session ID. Join it with the given password, or with the most recent password for that
      session. If the mod cannot join the session and there is no password, host the local
      save. If there is no local save, the player wakes and stands before the fade out is
      complete. Plan T3d after T3c, because the spike did not measure the EOS lobby search.

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
