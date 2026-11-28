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

    internal static void Step()
    {
        if (!LogFlow || Time.time > 240f) return;
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
        if (now == _last) return;
        _last = now;
        Plugin.Logger.LogInfo($"Diary: start flow at {Time.time:0.00} s: {now}.");
    }
}
