using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HikersDiary;

/// <summary>
/// This class is a spike for T1 of M2 (#13). It logs the game start flow. The log shows the menus, the scenes,
/// the cameras, the menu music and the sleepiness of the local player when one of them changes.
/// </summary>
// ponytail: This class is only for diagnosis. When T2 (#13) uses these facts, remove this class.
internal static class StartFlow
{
    internal static bool LogFlow;
    private static string _last;
    private static Canvas _kept;
    private static float _keptAt = -1f, _keptLogAt;

    internal static void Step()
    {
        if (!LogFlow || Time.time > 240f) return;
        KeepMicCheck();
        Measure();
        // The scan below finds all MonoBehaviours in each frame. It is slow, thus do not do it while we measure.
        if (_keptAt >= 0f && Time.time > 10f && Time.time < 34f) return;
        string scenes = string.Join(",", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name));
        var mm = Object.FindObjectOfType<MainMenuManager>();
        string menus = mm == null ? "no manager" : string.Join(",", Object.FindObjectsOfType<MonoBehaviour>()
            .Where(m => m != null && m.isActiveAndEnabled && m.GetIl2CppType().Name.EndsWith("Menu")).Select(m => m.GetIl2CppType().Name).Distinct().OrderBy(n => n));
        string cams = string.Join(",", Camera.allCameras.Select(c => $"{c.name}(depth {c.depth:0}, scene {c.gameObject.scene.name})"));
        var music = Object.FindObjectOfType<MenuMusicPlayer>();
        string mus = music == null ? "none" : $"{music.CurrentPlayMode}";
        PlayerCharacter local = null;
        var all = PlayerCharacter.allPlayerCharacters;
        for (int i = 0; all != null && i < all.Count; i++) if (all[i] != null && all[i].isLocalPlayer) local = all[i];
        string sleep = local?.sleeper == null ? "none" : $"{local.sleeper.smoothSleepiness:0.0} sleeping {local.sleeper.combinedIsSleeping}";
        string now = $"scenes [{scenes}]; menus [{menus}]; cameras [{cams}]; music {mus}; player {(local != null ? "yes" : "no")}, sleepiness {sleep}";
        if (_keptAt >= 0f) now += $"; kept canvas {KeptState()}";
        if (now == _last) return;
        _last = now;
        Plugin.Logger.LogInfo($"Diary: start flow at {Time.time:0.00} s: {now}.");
    }

    // Spike 2: when the host starts, move the root canvas of MicCheckMenu to DontDestroyOnLoad.
    // Then the BaseScene unload does not destroy it. The log tells if it lives and where it draws.
    private static void KeepMicCheck()
    {
        if (_keptAt < 0f && Mirror.NetworkServer.active)
        {
            var mic = Object.FindObjectOfType<MicCheckMenu>();
            var canvas = mic == null ? null : mic.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null) return;
            canvas.transform.SetParent(null, true);
            Object.DontDestroyOnLoad(canvas.gameObject);
            _kept = canvas;
            _keptAt = Time.time;
            Plugin.Logger.LogInfo($"Diary: spike 2 kept canvas '{canvas.name}' at {Time.time:0.00} s: {KeptState()}.");
        }
        if (_keptAt >= 0f && Time.time - _keptLogAt > 1f && Time.time - _keptAt < 30f)
        {
            _keptLogAt = Time.time;
            Plugin.Logger.LogInfo($"Diary: spike 2 at {Time.time:0.00} s: {KeptState()}.");
        }
    }

    // Spike 3: frame time with the kept canvas (10 s to 20 s) and without it (22 s to 32 s).
    // Then force the drowsy view at 34 s and stop it at 46 s. The Pick log shows the sleep sounds.
    private static int _frames;
    private static float _sum, _max;
    private static int _phase;

    private static void Measure()
    {
        if (_keptAt < 0f) return;
        float t = Time.time, dt = Time.unscaledDeltaTime;
        if ((t >= 10f && t < 20f) || (t >= 22f && t < 32f)) { _frames++; _sum += dt; if (dt > _max) _max = dt; }
        if (_phase == 0 && t >= 20f) { LogFrames("with the canvas"); if (_kept != null) _kept.gameObject.SetActive(false); _phase = 1; }
        if (_phase == 1 && t >= 32f) { LogFrames("without the canvas"); _phase = 2; }
        var sleeper = LocalSleeper();
        if (_phase == 2 && t >= 34f && sleeper != null) { sleeper._wasSleeping = true; sleeper._sleepSoundToggle = true; sleeper.forceSleeping = true; Plugin.Logger.LogInfo($"Diary: spike 6 force sleep with _wasSleeping and the sound toggle set at {t:0.00} s."); _phase = 3; }
        if (_phase == 3 && t >= 46f && sleeper != null) { sleeper.forceSleeping = false; Plugin.Logger.LogInfo($"Diary: spike 3 stop sleep at {t:0.00} s."); _phase = 4; }
    }

    private static void LogFrames(string what)
    {
        Plugin.Logger.LogInfo($"Diary: spike 3 frames {what}: {_frames} frames, mean {1000f * _sum / Mathf.Max(1, _frames):0.0} ms, max {1000f * _max:0.0} ms.");
        _frames = 0; _sum = 0f; _max = 0f;
    }

    private static PlayerSleeper LocalSleeper()
    {
        var all = PlayerCharacter.allPlayerCharacters;
        for (int i = 0; all != null && i < all.Count; i++) if (all[i] != null && all[i].isLocalPlayer) return all[i].sleeper;
        return null;
    }

    internal static void LogPick(AudioPicker picker, AudioAsset result)
    {
        if (!LogFlow || picker == null || Time.time < 33f) return;
        Plugin.Logger.LogInfo($"Diary: spike 3 pick at {Time.time:0.00} s: {picker.name} -> {(result == null ? "null" : result.name)}.");
    }

    private static string KeptState()
    {
        if (_kept == null) return "destroyed";
        var cam = _kept.worldCamera;
        return $"active {_kept.isActiveAndEnabled}, mode {_kept.renderMode}, sort {_kept.sortingOrder}, camera {(cam == null ? "none" : $"{cam.name} enabled {cam.isActiveAndEnabled}")}, scene {_kept.gameObject.scene.name}";
    }
}

// ponytail: Spike 3 only. It logs each AudioPicker.Pick to find the sleep sound. Remove it with StartFlow.
[HarmonyLib.HarmonyPatch(typeof(AudioPicker))]
internal static class PickLog
{
    [HarmonyLib.HarmonyPostfix, HarmonyLib.HarmonyPatch(nameof(AudioPicker.Pick), typeof(int))]
    private static void Int(AudioPicker __instance, AudioAsset __result) => StartFlow.LogPick(__instance, __result);

    [HarmonyLib.HarmonyPostfix, HarmonyLib.HarmonyPatch(nameof(AudioPicker.Pick), typeof(float))]
    private static void Float(AudioPicker __instance, AudioAsset __result) => StartFlow.LogPick(__instance, __result);
}
