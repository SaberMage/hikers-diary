namespace HikersDiary.Core;

/// <summary>
/// The saved pose of one loose prop (game class <c>Prop</c>). It has three keys, best first:
/// <c>Prop.savablePropGuid</c> (empty if the prop has none), <c>NetworkIdentity.sceneId</c>
/// (0 if none), and the GameObject path.
/// </summary>
public sealed record PropRecord(string Guid, ulong SceneId, string Path, float[] Position, float[] Rotation);

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
