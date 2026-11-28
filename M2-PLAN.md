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
   4. When the local player exists, start the drowsy view without the snore (see T1). The game
      does not start the sleep state in the first 30 s after the game start. Thus, add a patch
      on `PlayerSleeper.Update`. The patch must keep the full drowsy view and call
      `PostProcessingManager.SetSleepiness(1)`.
   5. While the first screen is visible, make the game input control the first screen, not the
      player:
      - Keep the menu controls (`ControlsManager.SetMenuMode(true)`).
      - Open the world menu. Hide the pause menu of the world menu.
      - Skip `WorldMenuManager.Update`. Keep `WorldManager.inUI` false. Then ESC has no effect.
      - Block `PlayerMenu.SetMenuOpen` for the local player. Then ESC does not change the pose.
      - Do not let the game lock the cursor. If the game locks the cursor, unlock the cursor.
        Then move the cursor to its last position.
      - Hide the crosshair.
   6. Then do the fade: make the black image transparent gradually during 5 s
      (`Start.FadeSeconds`). At the same time, decrease the volume of the intro music to zero.
      Increase the volume of the Environment and SFX buses from zero. Do not let the game stop
      the intro music before the fade ends.
   7. Then show the continue button. When the user clicks the continue button, do these steps:
      - Close the first screen and the world menu.
      - Set the gameplay controls.
      - Lock the cursor.
      - Show the crosshair.
      - Stop the drowsy view.
   Result (2026-11-28): the operator tested T2 on a flat screen and accepted it. It is possible
   that the `MainMenu` canvas is not visible in VR. The operator must test T2 in VR.
3. **T3 — save switch (#14).** Plan it after T2. For T3, the chat input (`TextChatInput`),
   the sleep (`PlayerSleeper`) and a save load from the gameplay are necessary.
4. **T4 — hint signs (#15).** Plan it after T3.
5. **T5 — save picker (#9).** Plan it after T4. For the photo, the frame capture is necessary
   (rule 10).

## Test

The operator tests #13 on HFENDULEAM in VR. First, the agent does a test with
`$TEMP/gears/run-game.sh` and examines the logs.

## Gate

`pwsh scripts/gates.ps1` is green (build, unit tests, `traceable-reqs check`, docs site).
When the work on a request starts, activate its requirements in `traceable-reqs.toml`.
