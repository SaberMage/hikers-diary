using System.Text.Json;

namespace HikersDiary.Core;

/// <summary>The saved state of one body (game class <c>Corpse</c>) of a player who left.</summary>
public sealed record BodyRecord(
    string Identifier,
    float[] Position,
    float[] Rotation,
    int HeadColor,
    int TorsoColor,
    int LegsColor);

/// <summary>
/// Reads and writes body records as JSON. A write goes to a temporary file first and then
/// replaces the old file, so that a stop during the write never leaves a broken file.
/// </summary>
public static class BodyStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // [impl->REQ-BODY-SPIKE]
    public static void Write(string path, IReadOnlyList<BodyRecord> bodies)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(bodies, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public static List<BodyRecord> Read(string path)
    {
        if (!File.Exists(path)) return new List<BodyRecord>();
        return JsonSerializer.Deserialize<List<BodyRecord>>(File.ReadAllText(path)) ?? new List<BodyRecord>();
    }
}
