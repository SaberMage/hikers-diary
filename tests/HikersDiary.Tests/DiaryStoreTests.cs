using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

public class DiaryStoreTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), $"hd-diary-{Guid.NewGuid():N}");

    private static DiarySave Sample(string uid, int head = 4) => new(DiarySave.CurrentVersion, uid, new List<BodyRecord>
    {
        new("0123456789abcdef0123456789abcdef", new[] { 1f, 2f, 3f }, new[] { 0f, 0.7071f, 0f, 0.7071f }, head, 5, 6),
    });

    // [unit->REQ-BODY-SPIKE]
    // [unit->REQ-DIARY-SAVE]
    [Fact]
    public void RoundTripKeepsEveryField()
    {
        string path = DiaryStore.PathFor(NewDir(), "a1b2c3d4e5f60718");
        DiarySave save = Sample("a1b2c3d4e5f60718");
        DiaryStore.Write(path, save);

        DiarySave read = DiaryStore.Read(path)!;
        Assert.Equal(save.SlotUid, read.SlotUid);
        BodyRecord body = save.Bodies[0], got = Assert.Single(read.Bodies);
        Assert.Equal(body.Identifier, got.Identifier);
        Assert.Equal(body.Position, got.Position);
        Assert.Equal(body.Rotation, got.Rotation);
        Assert.Equal((4, 5, 6), (got.HeadColor, got.TorsoColor, got.LegsColor));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void MissingFileGivesNull() =>
        Assert.Null(DiaryStore.Read(DiaryStore.PathFor(NewDir(), "none")));

    // [unit->REQ-DIARY-SAVE]
    [Fact]
    public void EachSlotHasItsOwnFile() =>
        Assert.NotEqual(DiaryStore.PathFor("d", "aaaa"), DiaryStore.PathFor("d", "bbbb"));

    // [unit->REQ-DIARY-SAVE]
    [Theory]
    [InlineData("")]
    [InlineData("../evil")]
    [InlineData("a\\b")]
    public void SlotIdWithPathCharactersIsRefused(string uid) =>
        Assert.Throws<ArgumentException>(() => DiaryStore.PathFor("d", uid));

    // [unit->REQ-DIARY-SAVE]
    [Fact]
    public void BrokenFileFallsBackToTheBackup()
    {
        string path = DiaryStore.PathFor(NewDir(), "slot");
        DiaryStore.Write(path, Sample("slot", head: 1));
        DiaryStore.Write(path, Sample("slot", head: 2));
        File.WriteAllText(path, "{ broken");

        Assert.Equal(1, DiaryStore.Read(path)!.Bodies[0].HeadColor);
    }

    // [unit->REQ-DIARY-SAVE]
    [Fact]
    public void NoReadableFileThrows()
    {
        string path = DiaryStore.PathFor(NewDir(), "slot");
        DiaryStore.Write(path, Sample("slot"));
        File.WriteAllText(path, "{ broken");

        Assert.Throws<InvalidDataException>(() => DiaryStore.Read(path));
    }

    // [unit->REQ-DIARY-SAVE]
    [Fact]
    public void NewerVersionThrows()
    {
        string path = DiaryStore.PathFor(NewDir(), "slot");
        DiaryStore.Write(path, Sample("slot") with { Version = DiarySave.CurrentVersion + 1 });

        Assert.Throws<InvalidDataException>(() => DiaryStore.Read(path));
    }
}
