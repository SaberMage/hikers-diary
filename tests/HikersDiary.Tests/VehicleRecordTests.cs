using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

// [unit->REQ-VEHICLE-STATE]
public class VehicleRecordTests
{
    private static VehicleRecord Rec(string kind, string path, float d = 0f) => new(kind, path, 1, d, 2f, 3f);

    [Fact]
    public void FindsByKindAndPath()
    {
        VehicleRecord train = Rec("train", "World/Monorail"), driver = Rec("driver", "World/Monorail");
        var map = VehicleRecord.ByUniquePath(new[] { train, driver });

        Assert.Same(train, map[VehicleRecord.Key("train", "World/Monorail")]);
        Assert.Same(driver, map[VehicleRecord.Key("driver", "World/Monorail")]);
    }

    [Fact]
    public void DuplicatePathIsNotUsed()
    {
        var map = VehicleRecord.ByUniquePath(new[] { Rec("train", "Car", 1f), Rec("train", "Car", 2f), Rec("train", "Other") });

        Assert.False(map.ContainsKey(VehicleRecord.Key("train", "Car")));
        Assert.True(map.ContainsKey(VehicleRecord.Key("train", "Other")));
    }
}
