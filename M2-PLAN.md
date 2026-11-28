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
   1. At the first screen, hide the continue button. Host the latest save. Use the steps of
      `TryAutoHost`. If there is no save, do not change the first screen.
   2. When the host starts, move the `MainMenu` canvas to `DontDestroyOnLoad`. Put a black
      image behind the first screen. Make the text and the lines white. Disable
      `MenuVolumeMeter` until the local player exists.
   3. When the local player exists, start the drowsy view without the snore (see T1). Open the
      menu for the local player (`SetMenuOpen(true)`, `CursorManager.SetFree()`). Then the
      game input controls the first screen and the mouse cursor, not the player. The pause
      menu must not open.
      The game does not start the sleep state in the first 30 s after the game start. Thus, while
      the first screen holds the input, a patch on `PlayerSleeper.Update` sets the full drowsy
      view for the local player.
   4. Then the black image becomes transparent gradually, and the intro music decreases to
      zero at the same time. The player stays in the drowsy view.
   5. Then the continue button appears. The continue button closes the first screen, closes
      the menu for the local player (`SetMenuOpen(false)`, `BlockActionsPostMenu()`,
      `CursorManager.SetLocked()`) and ends the drowsy view.
   Open: it is possible that the `MainMenu` canvas is not visible in VR. The operator must test
   it in VR.
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
