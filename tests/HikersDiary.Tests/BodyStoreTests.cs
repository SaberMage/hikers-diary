using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

public class BodyStoreTests
{
    // [unit->REQ-BODY-SPIKE]
    [Fact]
    public void RoundTripKeepsEveryField()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hd-bodies-{Guid.NewGuid():N}", "bodies.json");
        var body = new BodyRecord("76561198000000000", new[] { 1f, 2f, 3f }, new[] { 0f, 0.7071f, 0f, 0.7071f }, 4, 5, 6);
        BodyStore.Write(path, new[] { body });

        var read = Assert.Single(BodyStore.Read(path));
        Assert.Equal(body.Identifier, read.Identifier);
        Assert.Equal(body.Position, read.Position);
        Assert.Equal(body.Rotation, read.Rotation);
        Assert.Equal((4, 5, 6), (read.HeadColor, read.TorsoColor, read.LegsColor));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void MissingFileGivesNoBodies() =>
        Assert.Empty(BodyStore.Read(Path.Combine(Path.GetTempPath(), $"hd-none-{Guid.NewGuid():N}.json")));
}
