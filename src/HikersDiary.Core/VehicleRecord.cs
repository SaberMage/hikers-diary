namespace HikersDiary.Core;

/// <summary>
/// This record contains the saved state of one vehicle (#4). <see cref="Kind"/> is "train" for a game
/// <c>NetworkedTrain</c> (spline index, distance, speed, target speed) or "driver" for a game
/// <c>MovementDriver</c> (position in <see cref="Distance"/>, speed). The key is the GameObject path.
/// </summary>
public sealed record VehicleRecord(string Kind, string Path, int SplineIndex, float Distance, float Speed, float TargetSpeed)
{
    /// <summary>
    /// Returns the records by "kind:path". The method cannot identify two vehicles of one kind that
    /// have the same path. Thus, the result does not include them.
    /// </summary>
    // [impl->REQ-VEHICLE-STATE]
    public static Dictionary<string, VehicleRecord> ByUniquePath(IEnumerable<VehicleRecord> records)
    {
        var byKey = new Dictionary<string, VehicleRecord>();
        var duplicates = new HashSet<string>();
        foreach (VehicleRecord r in records)
            if (!byKey.TryAdd(Key(r.Kind, r.Path), r)) duplicates.Add(Key(r.Kind, r.Path));
        foreach (string k in duplicates) byKey.Remove(k);
        return byKey;
    }

    public static string Key(string kind, string path) => kind + ":" + path;
}
