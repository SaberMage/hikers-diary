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
    internal static float FadeSeconds = 4f;

    private static StartSequence _seq;
    private static MicCheckMenu _mic;
    private static Canvas _canvas;
    private static Image _backdrop;
    private static MenuMusicPlayer _music;

    /// <summary>True while the first screen controls the input of the local player.</summary>
    internal static bool HoldsInput => _seq != null && _seq.Phase is >= StartPhase.WaitView and < StartPhase.Done;

    /// <summary>Call it in each frame.</summary>
    internal static void Step()
    {
        if (!Enabled) return;
        _seq ??= new StartSequence(FadeSeconds);
        if (_seq.Phase == StartPhase.Done) return;

        float now = Time.time;
        StartPhase before = _seq.Phase;
        if (before == StartPhase.WaitMenu) _mic = Object.FindObjectOfType<MicCheckMenu>();
        PlayerCharacter local = before is StartPhase.WaitPlayer or StartPhase.WaitView ? DiaryHost.LocalPlayer() : null;
        bool playerReady = local != null && local.sleeper != null && local.menu != null;
        bool viewReady = playerReady && local.sleeper.smoothSleepiness >= 0.99f && PlayerCameraOn();
        StartPhase after = _seq.Step(now, _mic != null, NetworkServer.active, playerReady, viewReady);

        if (after != before)
        {
            Plugin.Logger.LogInfo($"Diary: game start {before} -> {after} at {now:0.00} s.");
            Enter(after, local);
        }
        if (_backdrop != null) _backdrop.color = new Color(0f, 0f, 0f, _seq.Cover(now));
        if (_seq.Phase == StartPhase.Fade && _music != null && _music._micSelectASC != null)
            _music._micSelectASC.ScriptableVolume = _seq.Cover(now);
    }

    private static void Enter(StartPhase phase, PlayerCharacter local)
    {
        switch (phase)
        {
            case StartPhase.WaitHost:
                _mic.continueButton.gameObject.SetActive(false);
                break;
            case StartPhase.Hosting:
                if (!DiaryHost.HostLatestSave()) Stop();
                break;
            case StartPhase.WaitPlayer:
                KeepFirstScreen();
                break;
            case StartPhase.WaitView:
                // Without these two values, PlayerSleeper.Update sees a new sleep state and plays the snore.
                local.sleeper._wasSleeping = true;
                local.sleeper._sleepSoundToggle = true;
                local.sleeper.forceSleeping = true;
                local.menu.SetMenuOpen(true);
                CursorManager.SetFree();
                var es = EventSystem.current;
                Plugin.Logger.LogInfo($"Diary: game start holds the input. Event system: {(es == null ? "none" : es.name)}.");
                break;
            case StartPhase.Fade:
                SetMeters(true);
                _music = Object.FindObjectOfType<MenuMusicPlayer>();
                break;
            case StartPhase.Ready:
                _music?.Stop();
                if (_mic != null)
                {
                    _mic.continueButton.gameObject.SetActive(true);
                    EventSystem.current?.SetSelectedGameObject(_mic.continueButton.gameObject);
                }
                break;
        }
    }

    /// <summary>The continue button: give the input back to the player and end the drowsy view.</summary>
    internal static void Continue()
    {
        PlayerCharacter local = DiaryHost.LocalPlayer();
        if (local != null)
        {
            local.sleeper.forceSleeping = false;
            local.sleeper.RecordAction();
            local.menu.SetMenuOpen(false);
            local.menu.BlockActionsPostMenu();
        }
        CursorManager.SetLocked();
        Plugin.Logger.LogInfo($"Diary: game start continue at {Time.time:0.00} s.");
        Stop();
    }

    private static void Stop()
    {
        if (_canvas != null) Object.Destroy(_canvas.gameObject);
        else if (_mic != null) _mic.continueButton.gameObject.SetActive(true); // no save: the menus work as usual
        _canvas = null;
        _backdrop = null;
        _seq.End();
    }

    // The game unloads BaseScene and its menus when the game scene loads. Keep the canvas of the first screen,
    // put a black image behind it, and make its text and lines white.
    private static void KeepFirstScreen()
    {
        _canvas = _mic == null ? null : _mic.GetComponentInParent<Canvas>()?.rootCanvas;
        if (_canvas == null)
        {
            Plugin.Logger.LogWarning("Diary: game start found no canvas for the first screen.");
            _seq.End();
            return;
        }
        _canvas.transform.SetParent(null, true);
        Object.DontDestroyOnLoad(_canvas.gameObject);

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
            if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.35f) g.color = new Color(1f, 1f, 1f, c.a);
        }
        // The meter throws while the game scene loads. It works again when the game view is ready.
        SetMeters(false);
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

/// <summary>While the first screen holds the input, the pause menu does not open, and continue starts the gameplay.</summary>
// [impl->REQ-GAME-START]
[HarmonyPatch]
internal static class GameStartPatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(WorldMenuManager), nameof(WorldMenuManager.OpenMenu))]
    private static bool NoPauseMenu() => !GameStart.HoldsInput;

    // The game does not start the sleep state in the first 30 s after the game start (measured: forceSleeping is
    // true from 5.5 s, combinedIsSleeping becomes true at 30.0 s, also with no open menu). The first screen hides the game view, thus put
    // the local player in the full drowsy view at once.
    [HarmonyPostfix, HarmonyPatch(typeof(PlayerSleeper), nameof(PlayerSleeper.Update))]
    private static void FullDrowsyView(PlayerSleeper __instance)
    {
        if (!GameStart.HoldsInput || __instance.playerCharacter == null || !__instance.playerCharacter.isLocalPlayer) return;
        __instance.localIsSleeping = true;
        __instance.smoothSleepiness = 1f;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(MicCheckMenu), nameof(MicCheckMenu.ActionContinue))]
    private static bool Continue()
    {
        if (!GameStart.HoldsInput) return true;
        GameStart.Continue();
        return false;
    }
}
