using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

public class DiaryStoreTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), $"hd-diary-{Guid.NewGuid():N}");

    private static DiarySave Sample(string uid, int head = 4) => new(DiarySave.CurrentVersion, uid, new List<BodyRecord>
    {
        new("0123456789abcdef0123456789abcdef", new[] { 1f, 2f, 3f }, new[] { 0f, 0.7071f, 0f, 0.7071f }, head, 5, 6),
    }, new List<PropRecord>
    {
        new("g-1", 0x1234UL, "World/Props/Lantern", new[] { 4f, 5f, 6f }, new[] { 0f, 0f, 0f, 1f }),
    }, new List<VehicleRecord>
    {
        new("train", "World/Monorail", 2, 15.5f, 3f, 4f),
    }, 638_000_000_000_000_000L);

    // [unit->REQ-BODY-SPIKE]
    // [unit->REQ-DIARY-SAVE]
    // [unit->REQ-TIME-OF-DAY]
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
        PropRecord prop = save.Props[0], gotProp = Assert.Single(read.Props);
        Assert.Equal((prop.Guid, prop.SceneId, prop.Path), (gotProp.Guid, gotProp.SceneId, gotProp.Path));
        Assert.Equal(prop.Position, gotProp.Position);
        Assert.Equal(prop.Rotation, gotProp.Rotation);
        Assert.Equal(save.Vehicles![0], Assert.Single(read.Vehicles!));
        Assert.Equal(save.EnviroTicks, read.EnviroTicks);
        Assert.False(File.Exists(path + ".tmp"));
    }

    // [unit->REQ-PLATFORM-PROPS]
    [Fact]
    public void PlatformOfABodyAndOfAPlayerRoundTrips()
    {
        string path = DiaryStore.PathFor(NewDir(), "pp");
        DiarySave save = DiarySave.Empty("pp");
        save.Bodies.Add(new BodyRecord("b", new[] { 0.5f, 1f, 0f }, new[] { 0f, 0f, 0f, 1f }, 1, 2, 3, null, "World/Monorail/Car"));
        save.Players!.Add(new PlayerRecord("h", new[] { 0f, 1.2f, 0f }, new[] { 0f, 0f, 0f, 1f }, new[] { 0f, 0f }, 0f, false,
            "World/Lift/Car/Pose", new Dictionary<string, PropRecord>(), "World/Lift/Car"));
        DiaryStore.Write(path, save);
        DiarySave read = DiaryStore.Read(path)!;
        Assert.Equal("World/Monorail/Car", Assert.Single(read.Bodies).Platform);
        Assert.Equal("World/Lift/Car", Assert.Single(read.Players!).Platform);
    }

    // [unit->REQ-VEHICLE-STATE]
    [Fact]
    public void LoadedCarsRoundTripAndAreNullInAVersion6File()
    {
        string path = DiaryStore.PathFor(NewDir(), "v7");
        DiarySave save = DiarySave.Empty("v7");
        save.Vehicles!.Add(new VehicleRecord("train", "World/Monorail", 0, 10f, 3f, 3f, new[] { 2, 5 }));
        DiaryStore.Write(path, save);
        Assert.Equal(new[] { 2, 5 }, Assert.Single(DiaryStore.Read(path)!.Vehicles!).LoadedCars);

        File.WriteAllText(path, "{\"Version\":6,\"SlotUid\":\"v7\",\"Bodies\":[],\"Props\":[],\"Vehicles\":[{\"Kind\":\"train\"," +
            "\"Path\":\"P\",\"SplineIndex\":0,\"Distance\":1,\"Speed\":0,\"TargetSpeed\":0}]}");
        Assert.Null(Assert.Single(DiaryStore.Read(path)!.Vehicles!).LoadedCars);
    }

    // [unit->REQ-PLATFORM-PROPS]
    [Fact]
    public void PlatformOfAPropRoundTripsAndIsNullInAVersion5File()
    {
        string path = DiaryStore.PathFor(NewDir(), "p6");
        DiarySave save = DiarySave.Empty("p6");
        save.Props.Add(new PropRecord("g-2", 0, "World/Monorail/Car/Crate", new[] { 0.5f, 1f, -2f }, new[] { 0f, 0f, 0f, 1f },
            "World/Monorail/Car"));
        DiaryStore.Write(path, save);
        Assert.Equal("World/Monorail/Car", Assert.Single(DiaryStore.Read(path)!.Props).Platform);

        File.WriteAllText(path, "{\"Version\":5,\"SlotUid\":\"p6\",\"Bodies\":[],\"Props\":[{\"Guid\":\"g\",\"SceneId\":0," +
            "\"Path\":\"P\",\"Position\":[0,0,0],\"Rotation\":[0,0,0,1]}]}");
        Assert.Null(Assert.Single(DiaryStore.Read(path)!.Props).Platform);
    }

    // [unit->REQ-VEHICLE-STATE]
    // [unit->REQ-TIME-OF-DAY]
    [Fact]
    public void Version2FileReadsWithNoVehiclesAndNoTime()
    {
        string path = DiaryStore.PathFor(NewDir(), "v2");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"Version\":2,\"SlotUid\":\"v2\",\"Bodies\":[],\"Props\":[]}");

        DiarySave read = DiaryStore.Read(path)!;
        Assert.Empty(read.Vehicles!);
        Assert.Equal(0, read.EnviroTicks);
    }

    // [unit->REQ-PLAYER-STATE]
    [Fact]
    public void PlayerStateRoundTrips()
    {
        string path = DiaryStore.PathFor(NewDir(), "p4");
        var lantern = new PropRecord("g-1", 7UL, "World/Lantern", new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f, 1f });
        var player = new PlayerRecord("puid", new[] { 1f, 2f, 3f }, new[] { 0f, 1f, 0f, 0f }, new[] { -10f, 90f }, 0.8f, true,
            "World/Bench/Seat", new Dictionary<string, PropRecord> { [PlayerRecord.Hand] = lantern, ["holster"] = lantern });
        DiaryStore.Write(path, DiarySave.Empty("p4") with { Players = new List<PlayerRecord> { player } });

        PlayerRecord got = Assert.Single(DiaryStore.Read(path)!.Players!);
        Assert.Equal((player.Identifier, player.Crouch, player.Sitting, player.PosePath), (got.Identifier, got.Crouch, got.Sitting, got.PosePath));
        Assert.Equal(player.Position, got.Position);
        Assert.Equal(player.Rotation, got.Rotation);
        Assert.Equal(player.Head, got.Head);
        Assert.Equal(new[] { "hand", "holster" }, got.Items.Keys.OrderBy(k => k));
        Assert.Equal(lantern.Guid, got.Items["holster"].Guid);
    }

    // [unit->REQ-PLAYER-STATE]
    [Fact]
    public void BodyItemsRoundTripAndVersion4BodyHasNoItems()
    {
        string path = DiaryStore.PathFor(NewDir(), "b5");
        var map = new PropRecord("g-2", 9UL, "World/Map", new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f, 1f });
        var body = new BodyRecord("puid", new[] { 1f, 2f, 3f }, new[] { 0f, 0f, 0f, 1f }, 1, 2, 3,
            new Dictionary<string, PropRecord> { ["backpack"] = map, [PlayerRecord.Hand] = map });
        DiaryStore.Write(path, DiarySave.Empty("b5") with { Bodies = new List<BodyRecord> { body } });

        BodyRecord got = Assert.Single(DiaryStore.Read(path)!.Bodies);
        Assert.Equal(new[] { "backpack", "hand" }, got.Items!.Keys.OrderBy(k => k));
        Assert.Equal(map.Guid, got.Items["backpack"].Guid);

        File.WriteAllText(path, "{\"Version\":4,\"SlotUid\":\"b5\",\"Bodies\":[{\"Identifier\":\"puid\",\"Position\":[0,0,0]," +
            "\"Rotation\":[0,0,0,1],\"HeadColor\":0,\"TorsoColor\":0,\"LegsColor\":0}],\"Props\":[]}");
        Assert.Null(Assert.Single(DiaryStore.Read(path)!.Bodies).Items);
    }

    // [unit->REQ-PLAYER-STATE]
    [Fact]
    public void Version3FileReadsWithNoPlayers()
    {
        string path = DiaryStore.PathFor(NewDir(), "v3");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"Version\":3,\"SlotUid\":\"v3\",\"Bodies\":[],\"Props\":[],\"Vehicles\":[]}");

        Assert.Empty(DiaryStore.Read(path)!.Players!);
    }

    // [unit->REQ-PROP-POSE]
    [Fact]
    public void Version1FileReadsWithNoProps()
    {
        string path = DiaryStore.PathFor(NewDir(), "old");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"Version\":1,\"SlotUid\":\"old\",\"Bodies\":[]}");

        Assert.Empty(DiaryStore.Read(path)!.Props);
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
