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

    private static string KeptState()
    {
        if (_kept == null) return "destroyed";
        var cam = _kept.worldCamera;
        return $"active {_kept.isActiveAndEnabled}, mode {_kept.renderMode}, sort {_kept.sortingOrder}, camera {(cam == null ? "none" : $"{cam.name} enabled {cam.isActiveAndEnabled}")}, scene {_kept.gameObject.scene.name}";
    }
}
