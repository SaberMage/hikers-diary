using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HikersDiary.Core;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HikersDiary;

[BepInPlugin(Guid, Name, VersionString)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "com.sabermage.hikersdiary";
    public const string Name = "Hikers' Diary";
    public const string VersionString = "0.1.0";

    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        string unity = Application.unityVersion;
        Logger.LogInfo($"{Name} {VersionString} loaded. Unity={unity}");

        // Unknown game build: no hooks at all (docs/DESIGN.md, revision rule 9).
        if (!KnownBuilds.IsKnown(unity))
        {
            Logger.LogError($"{Name} disabled: game build {unity} is not a known build.");
            return;
        }

        DiaryHost.StoreDir = System.IO.Path.Combine(Paths.ConfigPath, "HikersDiary");
        DiaryHost.MakeTestBody = Config.Bind("Spike", "MakeTestBody", false,
            "Host only: make one test body at the host position, to test the body save and restore on one computer.").Value;
        DiaryHost.FlipLookoutLight = Config.Bind("Spike", "FlipLookoutLight", false,
            "Host-only test aid: at the first snapshot, change the state of the red lookout light. Use it to make sure that the game load keeps the light state.").Value;
        DiaryHost.AutoHost = Config.Bind("Spike", "AutoHost", false,
            "Test aid: at the title menu, host the save that was played last, for 2 players.").Value;
        DiaryHost.AutoJoinCode = Config.Bind("Spike", "AutoJoinCode", "",
            "Test aid for a second client: at the title menu, join the session with this join code.").Value.Trim();
        DiaryHost.AutoJoinPassword = Config.Bind("Spike", "AutoJoinPassword", "",
            "Test aid for a second client: the password of the session in AutoJoinCode.").Value;
        ClassInjector.RegisterTypeInIl2Cpp<DiaryHost>();
        var driver = new GameObject("HikersDiary") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(driver);
        driver.AddComponent<DiaryHost>();
        var harmony = new HarmonyLib.Harmony(Guid);
        harmony.PatchAll(typeof(SessionStopPatches));
        harmony.PatchAll(typeof(BodyMatchLog));
        ColorPatches.Open(DiaryHost.StoreDir);
        harmony.PatchAll(typeof(ColorPatches));
        Logger.LogInfo($"Diary: {harmony.GetPatchedMethods().Count()} hooks installed.");
        Logger.LogInfo($"Diary armed. Store: {DiaryHost.StoreDir}");
    }
}
