using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

public class ColorBookTests
{
    private static string NewPath() =>
        Path.Combine(Path.GetTempPath(), $"hd-colors-{Guid.NewGuid():N}", "colors.json");

    private const string Puid = "0002bae7f1144f248dacff74f1585f9f";

    // [unit->REQ-PLAYER-COLORS]
    [Fact]
    public void SetReportsChangeAndRoundTrips()
    {
        var book = new ColorBook();
        Assert.Equal(-1, book.Get(Puid, 0));
        Assert.True(book.Set(Puid, "76561198018819420", 0, 7));
        Assert.True(book.Set(Puid, null, 2, 3));
        Assert.False(book.Set(Puid, null, 2, 3)); // same value: no write
        Assert.False(book.Set(Puid, null, 1, -1)); // -1 is not a color
        Assert.False(book.Set(Puid, null, 5, 1)); // not a part

        string path = NewPath();
        book.Write(path);
        ColorBook read = ColorBook.Read(path);
        Assert.Equal((7, -1, 3), (read.Get(Puid, 0), read.Get(Puid, 1), read.Get(Puid, 2)));
        Assert.Equal("76561198018819420", read.Players[Puid].SteamId);
    }

    // [unit->REQ-PLAYER-COLORS]
    [Fact]
    public void MissingFileIsEmptyAndBrokenFileFallsBackToBackup()
    {
        string path = NewPath();
        Assert.Empty(ColorBook.Read(path).Players);

        var book = new ColorBook();
        book.Set(Puid, null, 1, 4);
        book.Write(path);
        book.Set(Puid, null, 1, 5);
        book.Write(path); // the first file is now the .bak
        File.WriteAllText(path, "{ broken");
        Assert.Equal(4, ColorBook.Read(path).Get(Puid, 1));

        File.WriteAllText(path + ".bak", "{ broken");
        Assert.Throws<InvalidDataException>(() => ColorBook.Read(path));
    }
}
