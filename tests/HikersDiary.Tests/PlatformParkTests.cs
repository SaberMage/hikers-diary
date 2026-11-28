using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

public class PlatformParkTests
{
    // [unit->REQ-PLATFORM-PROPS]
    [Fact]
    public void PlatformsAtOnePositionAreParkedAndOthersAreNot()
    {
        var parked = PlatformPark.Parked(new[]
        {
            ("lift/0", 133.05f, -82.35f, -64.08f),
            ("lift/1", 133.05f, -82.35f, -64.085f),
            ("lift/2", -206f, 112.8f, -203f),
            ("train/0", 7.26f, 70.63f, -153.77f),
        });
        Assert.Equal(new HashSet<string> { "lift/0", "lift/1" }, parked);
    }
}
