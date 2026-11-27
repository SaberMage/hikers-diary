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

        BodySpike.StorePath = System.IO.Path.Combine(Paths.ConfigPath, "HikersDiary", "spike-bodies.json");
        BodySpike.MakeTestBody = Config.Bind("Spike", "MakeTestBody", false,
            "Host only: make one test body at the host position, to test the body save and restore on one computer.").Value;
        BodySpike.AutoHost = Config.Bind("Spike", "AutoHost", false,
            "Test aid: at the title menu, host the save that was played last, for 2 players.").Value;
        ClassInjector.RegisterTypeInIl2Cpp<BodySpike>();
        var driver = new GameObject("HikersDiary") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(driver);
        driver.AddComponent<BodySpike>();
        Logger.LogInfo($"Body spike armed. Store: {BodySpike.StorePath}");
    }
}
