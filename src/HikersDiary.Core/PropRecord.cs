namespace HikersDiary.Core;

/// <summary>
/// The saved pose of one loose prop (game class <c>Prop</c>). It has three keys, best first:
/// <c>Prop.savablePropGuid</c> (empty if the prop has none), <c>NetworkIdentity.sceneId</c>
/// (0 if none), and the GameObject path. <c>Platform</c> is the GameObject path of the moving
/// platform (<c>StickyPlatform</c>) that the prop is on, or null. If it is not null, the position and
/// the rotation are relative to that platform (#4).
/// </summary>
public sealed record PropRecord(string Guid, ulong SceneId, string Path, float[] Position, float[] Rotation,
    string? Platform = null);

/// <summary>Finds the saved record of a prop by its keys (#3).</summary>
public sealed class PropIndex
{
    private readonly Dictionary<string, PropRecord> _byGuid = new();
    private readonly Dictionary<ulong, PropRecord> _byScene = new();
    private readonly Dictionary<string, PropRecord> _byPath = new();
    private readonly HashSet<string> _duplicatePaths = new();

    public PropIndex(IEnumerable<PropRecord> records)
    {
        foreach (PropRecord r in records)
        {
            if (!string.IsNullOrEmpty(r.Guid)) _byGuid.TryAdd(r.Guid, r);
            if (r.SceneId != 0) _byScene.TryAdd(r.SceneId, r);
            if (string.IsNullOrEmpty(r.Path)) continue;
            if (!_byPath.TryAdd(r.Path, r)) _duplicatePaths.Add(r.Path);
        }
        // Two props with the same path cannot be told apart. Do not use the path for them.
        foreach (string p in _duplicatePaths) _byPath.Remove(p);
    }

    /// <summary>
    /// Returns the record and the name of the key that found it ("guid", "scene" or "path"),
    /// or null. A key that the prop does not have is not used.
    /// </summary>
    // [impl->REQ-PROP-POSE]
    public (PropRecord Record, string Key)? Find(string? guid, ulong sceneId, string? path)
    {
        if (!string.IsNullOrEmpty(guid) && _byGuid.TryGetValue(guid, out PropRecord? r)) return (r!, "guid");
        if (sceneId != 0 && _byScene.TryGetValue(sceneId, out r)) return (r!, "scene");
        if (!string.IsNullOrEmpty(path) && _byPath.TryGetValue(path, out r)) return (r!, "path");
        return null;
    }
}

/// <summary>Finds the moving platforms that are not in the world yet (T4b, #4).</summary>
public static class PlatformPark
{
    /// <summary>
    /// The game keeps the cars of a stopped chairlift at one parked position below the ground. A
    /// platform that has the same position as a different platform (within 1 cm) is parked.
    /// Returns the keys of the parked platforms.
    /// </summary>
    // [impl->REQ-PLATFORM-PROPS]
    public static HashSet<string> Parked(IReadOnlyList<(string Key, float X, float Y, float Z)> platforms)
    {
        var parked = new HashSet<string>();
        // ponytail: O(n^2), the game has 79 platforms.
        for (int i = 0; i < platforms.Count; i++)
            for (int j = i + 1; j < platforms.Count; j++)
            {
                var (a, b) = (platforms[i], platforms[j]);
                float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
                if (dx * dx + dy * dy + dz * dz > 0.0001f) continue;
                parked.Add(a.Key);
                parked.Add(b.Key);
            }
        return parked;
    }
}
