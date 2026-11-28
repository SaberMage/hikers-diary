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
        DiaryHost.CullTrip = Config.Bind("Spike", "CullTrip", false,
            "Host-only test aid for T4b: after a PropOnPlatform drop, move the host 400 m away and back, and log the prop and the car.").Value;
        DiaryHost.StartTrain = Config.Bind("Spike", "StartTrain", false,
            "Host-only test aid for T4b: at the game load, start the monorail (the game dev helper PeckDevHelper).").Value;
        DiaryHost.Scenario = Config.Bind("Spike", "Scenario", false,
            "Host-only test aid for T4b: with PropOnPlatform, put the host, a stack of two props and a test body on a car, and a stack of two props on the ground. Always: log them after the game load.").Value;
        DiaryHost.StartChairlift = Config.Bind("Spike", "StartChairlift", false,
            "Host-only test aid for T4b: after the game load, start the chairlift (the game dev helper PeckDevHelper, as if the key is in its slot).").Value;
        DiaryHost.PropOnPlatform = Config.Bind("Spike", "PropOnPlatform", "",
            "Host-only test aid for T4b: after the game load, drop one loose prop onto the first moving platform whose path contains this text. Empty = off.").Value;
        DiaryHost.EquipClients = Config.Bind("Spike", "EquipClients", false,
            "Host-only test aid for T7: 5 s after a client spawns, put a peg tile in its hand, its backpack and its holster.").Value;
        DiaryHost.BodyToCar = Config.Bind("Spike", "BodyToCar", "",
            "Host-only test aid for T7: move the body of a client who left onto the first train car whose path contains this text. Empty = off.").Value;
        DiaryHost.WaterProbe = Config.Bind("Spike", "WaterProbe", false,
            "Host-only test aid for T8. After the game loads, it logs the water depth at the ground. Then it moves the host to the 6 deepest points and logs where the host stops.").Value;
        GameStart.Enabled = Config.Bind("Start", "GameStart", true,
            "At the first screen, load the latest save behind it. The player starts in the drowsy view, and the continue button starts the gameplay (#13). False = the menus of the game.").Value;
        GameStart.FadeSeconds = Config.Bind("Start", "FadeSeconds", 5f,
            "The time in seconds in which the black image of the first screen becomes transparent and the intro music decreases to zero.").Value;
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
        if (GameStart.Enabled) harmony.PatchAll(typeof(GameStartPatches));
        ColorPatches.Open(DiaryHost.StoreDir);
        harmony.PatchAll(typeof(ColorPatches));
        harmony.PatchAll(typeof(CarHoldPatch));
        Logger.LogInfo($"Diary: {harmony.GetPatchedMethods().Count()} hooks installed.");
        Logger.LogInfo($"Diary armed. Store: {DiaryHost.StoreDir}");
    }
}
