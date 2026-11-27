using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HikersDiary.Core;
using UnityEngine;

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
    }
}
