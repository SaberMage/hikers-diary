using System.Text.Json;

namespace HikersDiary.Core;

/// <summary>The colors of one player (game look identifiers). The value -1 means that the player has no color.</summary>
public sealed record PlayerColors(string? SteamId, int Head = -1, int Torso = -1, int Legs = -1)
{
    /// <summary>Returns the color of a part. The key is the game <c>LookPart</c> value (0 head, 1 torso, 2 legs).</summary>
    public int Get(int part) => part switch { 0 => Head, 1 => Torso, 2 => Legs, _ => -1 };

    public PlayerColors With(int part, int color) => part switch
    {
        0 => this with { Head = color },
        1 => this with { Torso = color },
        2 => this with { Legs = color },
        _ => this,
    };
}

/// <summary>
/// The color book: one color set for each player, in one file that all save slots share (#6).
/// The key is the game identifier (<c>PlayerNetworking.identifier</c>).
/// </summary>
public sealed class ColorBook
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public Dictionary<string, PlayerColors> Players { get; init; } = new();

    // [impl->REQ-PLAYER-COLORS]
    public int Get(string identifier, int part) =>
        Players.TryGetValue(identifier, out PlayerColors? c) ? c.Get(part) : -1;

    /// <summary>Sets one color. If the book already has this value, returns false. Then do not write the file.</summary>
    // [impl->REQ-PLAYER-COLORS]
    public bool Set(string identifier, string? steamId, int part, int color)
    {
        if (color < 0 || part is < 0 or > 2) return false;
        PlayerColors old = Players.TryGetValue(identifier, out PlayerColors? c) ? c : new PlayerColors(steamId);
        PlayerColors next = old.With(part, color);
        if (!string.IsNullOrEmpty(steamId)) next = next with { SteamId = steamId };
        if (next == c) return false;
        Players[identifier] = next;
        return true;
    }

    // [impl->REQ-PLAYER-COLORS]
    public void Write(string path) => DiaryStore.WriteAtomic(path, JsonSerializer.Serialize(this, Options));

    /// <summary>
    /// Returns the book, or an empty book if there is no file. If the method cannot read the file or its
    /// backup, it throws <see cref="InvalidDataException"/>. Then the caller must not write the book.
    /// </summary>
    // [impl->REQ-PLAYER-COLORS]
    public static ColorBook Read(string path)
    {
        string bak = path + ".bak";
        if (!File.Exists(path) && !File.Exists(bak)) return new ColorBook();
        foreach (string p in new[] { path, bak })
        {
            if (!File.Exists(p)) continue;
            try
            {
                ColorBook? book = JsonSerializer.Deserialize<ColorBook>(File.ReadAllText(p));
                if (book != null) return new ColorBook { Players = book.Players ?? new() };
            }
            catch (JsonException) { }
        }
        throw new InvalidDataException($"Cannot read {path} or its backup.");
    }
}
