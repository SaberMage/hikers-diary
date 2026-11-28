using System.Text.Json;

namespace HikersDiary.Core;

/// <summary>
/// The Diary save of one game slot: the state that the game save does not keep.
/// Later Requests add their lists here (vehicles, players). DiaryStore reads a list that is
/// missing in the file (an older version) as an empty list.
/// </summary>
public sealed record DiarySave(int Version, string SlotUid, List<BodyRecord> Bodies, List<PropRecord> Props,
    List<VehicleRecord>? Vehicles = null, long EnviroTicks = 0, List<PlayerRecord>? Players = null)
{
    /// <summary>
    /// Version 2 added <see cref="Props"/>. Version 3 added <see cref="Vehicles"/> and
    /// <see cref="EnviroTicks"/> (the Enviro date and time as <c>DateTime</c> ticks. The value is 0
    /// if the host did not save the time). Version 4 added <see cref="Players"/>. Version 5 added
    /// <see cref="BodyRecord.Items"/>.
    /// </summary>
    public const int CurrentVersion = 5;

    public static DiarySave Empty(string slotUid) =>
        new(CurrentVersion, slotUid, new List<BodyRecord>(), new List<PropRecord>(), new List<VehicleRecord>(),
            Players: new List<PlayerRecord>());
}

/// <summary>
/// Reads and writes Diary saves, one file for each game slot (key = <c>SaveData.filenameUid</c>).
/// A write goes to a temporary file, then replaces the old file and keeps it as <c>.bak</c>.
/// A stop during the write never leaves a broken file, and a broken file falls back to the backup.
/// </summary>
public static class DiaryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The Diary file of a slot. The slot id comes from a game file, so it must be letters and digits only.</summary>
    // [impl->REQ-DIARY-SAVE]
    public static string PathFor(string dir, string slotUid)
    {
        if (string.IsNullOrEmpty(slotUid) || !slotUid.All(c => char.IsAscii(c) && char.IsLetterOrDigit(c)))
            throw new ArgumentException($"Slot id '{slotUid}' is not letters and digits only.", nameof(slotUid));
        return Path.Combine(dir, $"diary_{slotUid}.json");
    }

    // [impl->REQ-DIARY-SAVE]
    public static void Write(string path, DiarySave save) => WriteAtomic(path, JsonSerializer.Serialize(save, Options));

    /// <summary>Writes the text to a temporary file, then replaces the old file and keeps it as <c>.bak</c>.</summary>
    internal static void WriteAtomic(string path, string text)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
        else File.Move(tmp, path);
    }

    /// <summary>
    /// Returns the save, or null if the slot has no Diary file. Throws <see cref="InvalidDataException"/>
    /// if no file can be read, or if the file is from a newer mod version. Then the caller must not
    /// write to this slot, so that it does not overwrite data that it cannot read.
    /// </summary>
    // [impl->REQ-DIARY-SAVE]
    public static DiarySave? Read(string path)
    {
        string bak = path + ".bak";
        if (!File.Exists(path) && !File.Exists(bak)) return null;
        Exception? first = null;
        foreach (string p in new[] { path, bak })
        {
            if (!File.Exists(p)) continue;
            DiarySave? save;
            try
            {
                save = JsonSerializer.Deserialize<DiarySave>(File.ReadAllText(p));
            }
            catch (JsonException e)
            {
                first ??= e;
                continue;
            }
            if (save == null) continue;
            if (save.Version > DiarySave.CurrentVersion)
                throw new InvalidDataException($"{p} is version {save.Version}. This mod reads version {DiarySave.CurrentVersion} or older.");
            return save with { Bodies = save.Bodies ?? new List<BodyRecord>(), Props = save.Props ?? new List<PropRecord>(),
                Vehicles = save.Vehicles ?? new List<VehicleRecord>(), Players = save.Players ?? new List<PlayerRecord>() };
        }
        throw new InvalidDataException($"Cannot read {path} or its backup.", first);
    }
}
