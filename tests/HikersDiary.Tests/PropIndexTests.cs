using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

// [unit->REQ-PROP-POSE]
public class PropIndexTests
{
    private static PropRecord Rec(string guid, ulong scene, string path) =>
        new(guid, scene, path, new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f, 1f });

    [Fact]
    public void GuidWinsOverSceneIdAndPath()
    {
        PropRecord byGuid = Rec("g", 1, "a"), other = Rec("", 2, "b");
        var index = new PropIndex(new[] { byGuid, other });

        Assert.Equal((byGuid, "guid"), index.Find("g", 2, "b"));
    }

    [Fact]
    public void SceneIdIsUsedWhenThereIsNoGuid()
    {
        PropRecord r = Rec("", 7, "x");
        Assert.Equal((r, "scene"), new PropIndex(new[] { r }).Find("", 7, "other"));
    }

    [Fact]
    public void PathIsTheLastKey()
    {
        PropRecord r = Rec("", 7, "World/Cup");
        Assert.Equal((r, "path"), new PropIndex(new[] { r }).Find("", 8, "World/Cup"));
    }

    [Fact]
    public void DuplicatePathIsNotUsed()
    {
        var index = new PropIndex(new[] { Rec("", 0, "World/Rock"), Rec("", 0, "World/Rock") });
        Assert.Null(index.Find("", 0, "World/Rock"));
    }

    [Fact]
    public void MissingKeysFindNothing()
    {
        var index = new PropIndex(new[] { Rec("", 0, "") });
        Assert.Null(index.Find("", 0, ""));
        Assert.Null(index.Find(null, 0, null));
    }
}
