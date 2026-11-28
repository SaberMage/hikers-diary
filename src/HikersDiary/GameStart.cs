using HarmonyLib;
using HikersDiary.Core;
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HikersDiary;

/// <summary>
/// The game start (#13). The first screen (<c>MicCheckMenu</c>) stays white on black while the latest save loads
/// behind it. The local player starts in the drowsy view without the snore, and the game input controls the first
/// screen. Then the black image becomes transparent and the intro music decreases to zero. Then the continue
/// button starts the gameplay. <see cref="StartSequence"/> holds the order. This class does the game work.
/// </summary>
// [impl->REQ-GAME-START]
internal static class GameStart
{
    internal static bool Enabled;
    internal static float FadeSeconds = 5f;

    // After continue: the game decreases the drowsy view with its own smoothing, and the first screen fades out with it.
    private static bool _waking;
    private static float _sleepSentAt = -10f;
    private static CanvasGroup _wakeGroup;

    // Above the canvases of the game, for example WorldUICanvas, which covers the screen while the world loads.
    private const int SortingOrder = 32000;

    private static StartSequence _seq;
    private static MicCheckMenu _mic;
    private static Canvas _canvas;
    private static Image _backdrop;
    private static MenuMusicPlayer _music;
    private static readonly System.Collections.Generic.List<(Graphic g, Color c)> _colors = new();
    private static readonly System.Collections.Generic.List<(AudioBus bus, float volume)> _gameBuses = new();
    internal static bool OwnMenuCall;

    // The switch mode (#14, T3c): after a disconnection, the same steps host the target save. The fade-out cover of
    // the switch stays black until the first screen is black.
    private static bool _switching;
    private static System.Action _hostTarget;
    private static GameObject _switchCover;

    private static StartPhase Phase => _seq?.Phase ?? StartPhase.Done;

    /// <summary>True from the first screen until the game start is complete.</summary>
    internal static bool Active => Phase is >= StartPhase.WaitHost and < StartPhase.Done;

    /// <summary>True while the first screen controls the input of the local player.</summary>
    internal static bool HoldsInput => Phase is >= StartPhase.WaitView and < StartPhase.Done;

    /// <summary>True while the game must not stop the intro music. The fade stops it.</summary>
    internal static bool KeepsMusic => !_switching && Phase is >= StartPhase.WaitHost and < StartPhase.Ready;

    /// <summary>Call it in each frame.</summary>
    internal static void Step()
    {
        if (_seq == null)
        {
            if (!Enabled) return;
            _seq = new StartSequence(FadeSeconds);
        }
        if (_seq.Phase == StartPhase.Done) return;

        float now = Time.time;
        StartPhase before = _seq.Phase;
        if (before == StartPhase.WaitMenu) _mic = Object.FindObjectOfType<MicCheckMenu>();
        PlayerCharacter local = before is StartPhase.WaitPlayer or StartPhase.WaitView ? DiaryHost.LocalPlayer() : null;
        bool playerReady = local != null && local.sleeper != null && WorldMenuManager.instance != null;
        bool viewReady = playerReady && PlayerCameraOn();
        StartPhase after = _seq.Step(now, _mic != null, NetworkServer.active, playerReady, viewReady);

        if (after != before)
        {
            Plugin.Logger.LogInfo($"Diary: game start {before} -> {after} at {now:0.00} s.");
            Enter(after, local);
        }
        if (Active) KeepCursor();
        if (HoldsInput)
        {
            // The game switches the controls back to the gameplay while the world loads. Keep the menu controls.
            if (!ControlsManager.menuModeActive)
            {
                OwnMenuCall = true;
                ControlsManager.SetMenuMode(true);
                OwnMenuCall = false;
            }
            SetCrosshair(false);
            ClearEscState();
            SendSleeping(true, now);
        }
        float cover = _seq.Cover(now);
        if (_backdrop != null) _backdrop.color = new Color(0f, 0f, 0f, cover);
        SetGameSound(1f - cover);
    }

    // The game sound (the Environment and SFX buses, not the menu music and the menu sounds) increases while the
    // intro music decreases. The voice buses do not change.
    private static void FindGameBuses()
    {
        foreach (var b in Resources.FindObjectsOfTypeAll<AudioBus>())
        {
            if (b == null || b.BusVolume == null || b.name is "ui" or "music 2D menu") continue;
            if (b.AudioSettingsGroup.ToString() is "Environment" or "SFX") _gameBuses.Add((b, b.BusVolume.Value));
        }
    }

    private static void SetGameSound(float part)
    {
        foreach (var (bus, volume) in _gameBuses) if (bus != null) bus.BusVolume.Value = volume * part;
    }

    private static void Enter(StartPhase phase, PlayerCharacter local)
    {
        switch (phase)
        {
            case StartPhase.WaitHost:
                StyleFirstScreen();
                if (_switching)
                {
                    _music?.Stop();
                    EndSwitchCover();
                }
                break;
            case StartPhase.Hosting:
                if (_switching) _hostTarget();
                else if (!DiaryHost.HostLatestSave()) Stop();
                break;
            case StartPhase.WaitPlayer:
                // The game unloads BaseScene and its menus when the game scene loads. Keep the canvas.
                _canvas.transform.SetParent(null, true);
                Object.DontDestroyOnLoad(_canvas.gameObject);
                SetMeters(false); // the meter throws while the game scene loads
                break;
            case StartPhase.WaitView:
                // The game must not enter its sleep state while the first screen shows: a new sleep state plays the
                // snore. The patch on PlayerSleeper.Update makes the drowsy view.
                local.sleeper.preventSleeping = true;
                // The open world menu stops the player input and frees the cursor. Hide the pause menu in it.
                var wm = WorldMenuManager.instance;
                OwnMenuCall = true;
                wm.OpenMenu();
                OwnMenuCall = false;
                if (wm.pauseMenuForCurrentMode != null) wm.pauseMenuForCurrentMode.gameObject.SetActive(false);
                CursorManager.SetFree();
                Plugin.Logger.LogInfo($"Diary: game start holds the input. Event system: {EventSystem.current?.name ?? "none"}.");
                break;
            case StartPhase.Fade:
                SetMeters(true);
                if (!_switching) _music?._micSelectASC?.FadeOut(FadeSeconds);
                break;
            case StartPhase.Ready when _switching:
                Continue(); // a switch does not show the continue button
                break;
            case StartPhase.Ready:
                SetGameSound(1f);
                _gameBuses.Clear();
                _music?.Stop();
                _music?.Disable();
                _mic.continueButton.gameObject.SetActive(true);
                EventSystem.current?.SetSelectedGameObject(_mic.continueButton.gameObject);
                break;
        }
    }

    /// <summary>The continue button: give the input back to the player and end the drowsy view.</summary>
    internal static void Continue()
    {
        PlayerCharacter local = DiaryHost.LocalPlayer();
        if (local != null)
        {
            // The game was not in its sleep state. Give it the end of a sleep state, so that it plays the wake sound.
            local.sleeper.preventSleeping = false;
            local.sleeper._wasSleeping = true;
            local.sleeper._sleepSoundToggle = true;
            local.sleeper.RecordAction();
        }
        Plugin.Logger.LogInfo($"Diary: game start continue at {Time.time:0.00} s.");
        SendSleeping(false, Time.time);
        StartWake();
        Stop();
        OwnMenuCall = true;
        WorldMenuManager.instance?.CloseMenu();
        local?.menu?.SetMenuOpen(false); // the pause menu starts closed
        ClearEscState();
        ControlsManager.SetMenuMode(false);
        OwnMenuCall = false;
        local?.menu?.BlockActionsPostMenu(); // the click on continue is not a player action
        CursorManager.SetLocked();
        SetCrosshair(true);
    }

    /// <summary>True while the first screen holds the input: the patch on PlayerSleeper keeps the full drowsy view.</summary>
    internal static bool PinsDrowsy => HoldsInput;

    /// <summary>True after continue, until the game decreased the drowsy view to zero.</summary>
    internal static bool Waking => _waking;

    // The other players see the sleep pose of the local player only from the synchronized sleep state. The game does
    // not start its sleep state while the first screen shows, thus send it here. Check it one time each second.
    private static void SendSleeping(bool sleeping, float now)
    {
        if (sleeping && now - _sleepSentAt < 1f) return;
        _sleepSentAt = now;
        var pn = DiaryHost.LocalPlayer()?.playerNetworking;
        if (pn != null && pn.isSleeping != sleeping) pn.CmdSetSleeping(sleeping);
    }

    // The canvas of the first screen stays during the wake. It becomes transparent with the drowsy view.
    private static void StartWake()
    {
        if (_canvas == null) return;
        _wakeGroup = _canvas.gameObject.GetComponent<CanvasGroup>() ?? _canvas.gameObject.AddComponent<CanvasGroup>();
        _wakeGroup.interactable = false;
        _wakeGroup.blocksRaycasts = false;
        _waking = true;
        _canvas = null; // Stop must not destroy it
    }

    /// <summary>The patch on PlayerSleeper gives the drowsy level of the game after each update during the wake.</summary>
    internal static void WakeStep(float level)
    {
        if (_wakeGroup != null) _wakeGroup.alpha = level;
        if (level > 0.01f) return;
        if (_wakeGroup != null) Object.Destroy(_wakeGroup.gameObject);
        _wakeGroup = null;
        _waking = false;
    }

    // While the world loads, the game locks the cursor. A lock moves the cursor to the middle of the screen, and the
    // player possibly uses the first screen at that time. Unlock it at once and put it back where it was.
    // ponytail: Win32 calls, thus Windows only (the game is Windows only).
    private static Win32Point _cursor;
    private static bool _cursorKnown;

    private static void KeepCursor()
    {
        if (Cursor.lockState == CursorLockMode.None)
        {
            _cursorKnown = GetCursorPos(out _cursor);
            return;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (_cursorKnown) SetCursorPos(_cursor.X, _cursor.Y);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Win32Point { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    // WorldManager.Update changes inUI at each ESC and opens or closes the world menu from it. While the first screen
    // holds the input, the menu calls are blocked, thus keep inUI false: then the first ESC after continue opens the
    // pause menu.
    private static void ClearEscState()
    {
        var w = WorldManager.instance;
        if (w != null && w.inUI) w.inUI = false;
    }

    private static void SetCrosshair(bool on)
    {
        var t = Crosshair.instance?.hideTransform;
        if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }

    private static void Stop()
    {
        if (_canvas != null && _canvas.gameObject.scene.name == "DontDestroyOnLoad") Object.Destroy(_canvas.gameObject);
        else if (_mic != null)
        {
            // No save: the first screen and the menus work as usual.
            if (_backdrop != null) Object.Destroy(_backdrop.gameObject);
            foreach (var (g, c) in _colors) if (g != null) g.color = c;
            _mic.continueButton.gameObject.SetActive(true);
        }
        SetGameSound(1f);
        _gameBuses.Clear();
        _canvas = null;
        _backdrop = null;
        _seq.End();
        EndSwitchCover();
        _switching = false;
        _hostTarget = null;
    }

    /// <summary>
    /// Start the steps of the game start again after a disconnection (#14, T3c). The host step calls
    /// <paramref name="hostTarget"/>. The <paramref name="cover"/> stays black until the first screen is black.
    /// </summary>
    // [impl->REQ-SAVE-SWITCH]
    internal static void BeginSwitch(System.Action hostTarget, GameObject cover)
    {
        _seq = new StartSequence(FadeSeconds);
        _switching = true;
        _hostTarget = hostTarget;
        _switchCover = cover;
    }

    // The first screen is black now (or the switch stopped): remove the fade-out cover and give the audio back. The
    // game buses stay at zero until the fade-in.
    private static void EndSwitchCover()
    {
        if (_switchCover == null) return;
        Object.Destroy(_switchCover);
        _switchCover = null;
        AudioListener.volume = 1f;
    }

    // In the first frame of the first screen: a black image behind it, white text and lines, no continue button,
    // and the canvas above all canvases of the game.
    private static void StyleFirstScreen()
    {
        _colors.Clear();
        _canvas = _mic.GetComponentInParent<Canvas>()?.rootCanvas;
        _music = Object.FindObjectOfType<MenuMusicPlayer>();
        if (_canvas == null)
        {
            Plugin.Logger.LogWarning("Diary: game start found no canvas for the first screen.");
            Stop();
            return;
        }
        _canvas.sortingOrder = SortingOrder;
        _mic.continueButton.gameObject.SetActive(false);

        var go = new GameObject("DiaryBackdrop");
        go.transform.SetParent(_canvas.transform, false);
        go.transform.SetAsFirstSibling();
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        _backdrop = go.AddComponent<Image>();
        _backdrop.color = Color.black;
        _backdrop.raycastTarget = false;

        foreach (var g in _mic.GetComponentsInChildren<Graphic>(true))
        {
            Color c = g.color;
            if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) >= 0.35f) continue;
            _colors.Add((g, c));
            g.color = new Color(1f, 1f, 1f, c.a);
        }
        FindGameBuses();
    }

    private static void SetMeters(bool on)
    {
        if (_mic == null) return;
        foreach (var m in _mic.GetComponentsInChildren<MenuVolumeMeter>(true)) m.enabled = on;
    }

    private static bool PlayerCameraOn()
    {
        foreach (var c in Camera.allCameras) if (c != null && c.name == "PlayerCamera") return true;
        return false;
    }
}

/// <summary>The changes to the game while the game start is active.</summary>
// [impl->REQ-GAME-START]
[HarmonyPatch]
internal static class GameStartPatches
{
    private static AudioAsset _openSound, _closeSound;

    // ESC must not open or close the world menu while the first screen holds the input.
    [HarmonyPrefix, HarmonyPatch(typeof(WorldMenuManager), nameof(WorldMenuManager.OpenMenu))]
    private static bool Open(WorldMenuManager __instance) => Mute(__instance, !GameStart.HoldsInput || GameStart.OwnMenuCall);

    // The game also closes the world menu when the world loads. That locks the cursor, which moves it to the
    // middle of the screen while the player possibly uses the first screen. Continue closes the world menu.
    [HarmonyPrefix, HarmonyPatch(typeof(WorldMenuManager), nameof(WorldMenuManager.CloseMenu))]
    private static bool Close(WorldMenuManager __instance) => Mute(__instance, !GameStart.Active || GameStart.OwnMenuCall);

    // The menu calls of the game start make no sound.
    private static bool Mute(WorldMenuManager wm, bool run)
    {
        if (run && GameStart.Active)
        {
            _openSound = wm.pauseMenuOpenSound;
            _closeSound = wm.pauseMenuCloseSound;
            wm.pauseMenuOpenSound = null;
            wm.pauseMenuCloseSound = null;
        }
        return run;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(WorldMenuManager), nameof(WorldMenuManager.OpenMenu))]
    private static void OpenDone(WorldMenuManager __instance) => Unmute(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(WorldMenuManager), nameof(WorldMenuManager.CloseMenu))]
    private static void CloseDone(WorldMenuManager __instance) => Unmute(__instance);

    private static void Unmute(WorldMenuManager wm)
    {
        if (_openSound != null) wm.pauseMenuOpenSound = _openSound;
        if (_closeSound != null) wm.pauseMenuCloseSound = _closeSound;
        _openSound = _closeSound = null;
    }

    // WorldMenuManager.Update reads ESC and changes the controls and the cursor. Skip it while the first screen holds
    // the input. Continue closes the world menu.
    [HarmonyPrefix, HarmonyPatch(typeof(WorldMenuManager), nameof(WorldMenuManager.Update))]
    private static bool NoEsc() => !GameStart.HoldsInput;

    // ESC also sets the menu state and the menu pose of the local player. Keep both closed while the first screen
    // holds the input, so that one ESC opens the pause menu after continue.
    [HarmonyPrefix, HarmonyPatch(typeof(PlayerMenu), nameof(PlayerMenu.SetMenuOpen))]
    private static bool NoMenuPose(PlayerMenu __instance) =>
        !GameStart.HoldsInput || GameStart.OwnMenuCall || __instance.playerCharacter == null || !__instance.playerCharacter.isLocalPlayer;

    // The game input stays in the menu controls until continue.
    [HarmonyPrefix, HarmonyPatch(typeof(ControlsManager), nameof(ControlsManager.SetMenuMode))]
    private static bool MenuMode(bool active) => active || !GameStart.HoldsInput || GameStart.OwnMenuCall;

    // When the world loads, the game locks the cursor, which moves it to the middle of the screen. Then the player
    // loses the place of the cursor on the first screen. Keep the cursor free until continue.
    [HarmonyPrefix, HarmonyPatch(typeof(CursorManager), nameof(CursorManager.SetLocked))]
    private static bool CursorLock() => !GameStart.Active;

    // The game stops the intro music when the world loads. The fade of the game start stops it instead.
    [HarmonyPrefix, HarmonyPatch(typeof(MenuMusicPlayer), nameof(MenuMusicPlayer.Stop))]
    private static bool MusicStop() => !GameStart.KeepsMusic;

    [HarmonyPrefix, HarmonyPatch(typeof(MenuMusicPlayer), nameof(MenuMusicPlayer.Disable))]
    private static bool MusicDisable() => !GameStart.KeepsMusic;

    // The game does not start the sleep state in the first 30 s after the game start (measured: forceSleeping is
    // true from 5.5 s, combinedIsSleeping becomes true at 30.0 s, also with no open menu). Keep the local player in
    // the full drowsy view before and after each update, so that the game does not decrease it.
    [HarmonyPrefix, HarmonyPatch(typeof(PlayerSleeper), nameof(PlayerSleeper.Update))]
    private static void DrowsyBefore(PlayerSleeper __instance) => Drowsy(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(PlayerSleeper), nameof(PlayerSleeper.Update))]
    private static void DrowsyAfter(PlayerSleeper __instance)
    {
        if (Drowsy(__instance)) PostProcessingManager.SetSleepiness(1f);
        else if (GameStart.Waking && IsLocal(__instance))
        {
            // The wake: the game decreases smoothSleepiness with its own smoothing, as after a real sleep.
            float level = __instance.smoothSleepiness;
            PostProcessingManager.SetSleepiness(level);
            GameStart.WakeStep(level);
        }
    }

    private static bool IsLocal(PlayerSleeper s) => s.playerCharacter != null && s.playerCharacter.isLocalPlayer;

    private static bool Drowsy(PlayerSleeper s)
    {
        if (!GameStart.PinsDrowsy || !IsLocal(s)) return false;
        s.localIsSleeping = true;
        s.smoothSleepiness = 1f;
        s.smoothSleepinessVelocity = 0f;
        return true;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(MicCheckMenu), nameof(MicCheckMenu.ActionContinue))]
    private static bool Continue()
    {
        if (!GameStart.HoldsInput) return true;
        GameStart.Continue();
        return false;
    }
}
