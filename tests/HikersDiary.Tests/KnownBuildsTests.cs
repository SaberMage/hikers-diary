using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

public class KnownBuildsTests
{
    // [unit->REQ-HAZARD-UNKNOWN-BUILD]
    [Fact]
    public void UnknownBuildIsRefused()
    {
        Assert.False(KnownBuilds.IsKnown("6000.9.99f1"));
        Assert.False(KnownBuilds.IsKnown(null!));
    }

    [Fact]
    public void TestedBuildIsAccepted() => Assert.True(KnownBuilds.IsKnown("6000.3.17f1"));
}
