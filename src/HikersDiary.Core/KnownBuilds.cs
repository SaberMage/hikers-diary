namespace HikersDiary.Core;

/// <summary>
/// The game builds on which we tested the mod. On a build that is not in this list,
/// the mod installs no hooks (docs/DESIGN.md, revision rule 9).
/// </summary>
public static class KnownBuilds
{
    /// <summary>Unity version strings of tested builds. Filled from measured values only.</summary>
    public static readonly IReadOnlyCollection<string> UnityVersions = new[] { "6000.3.17f1" };

    // [impl->REQ-HAZARD-UNKNOWN-BUILD]
    public static bool IsKnown(string unityVersion) =>
        unityVersion != null && UnityVersions.Contains(unityVersion);
}
