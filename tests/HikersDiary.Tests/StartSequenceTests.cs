using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

public class StartSequenceTests
{
    // [unit->REQ-GAME-START]
    [Fact]
    public void GoesThroughEachPhaseInOrder()
    {
        var s = new StartSequence(fadeSeconds: 4f);
        Assert.Equal(StartPhase.WaitMenu, s.Step(0f, false, false, false, false));
        Assert.Equal(StartPhase.WaitHost, s.Step(1f, true, false, false, false));
        Assert.Equal(StartPhase.WaitHost, s.Step(3.9f, true, false, false, false)); // the host delay is 3 s
        Assert.Equal(StartPhase.Hosting, s.Step(4f, true, false, false, false));
        Assert.Equal(StartPhase.Hosting, s.Step(5f, false, false, true, true)); // the host did not start yet
        Assert.Equal(StartPhase.WaitPlayer, s.Step(6f, false, true, false, false));
        Assert.Equal(StartPhase.WaitView, s.Step(7f, false, true, true, false));
        Assert.Equal(1f, s.Cover(7f));
        Assert.Equal(StartPhase.Fade, s.Step(8f, false, true, true, true));
        Assert.Equal(0.75f, s.Cover(9f), 3);
        Assert.Equal(StartPhase.Fade, s.Step(11.9f, false, true, true, true));
        Assert.Equal(StartPhase.Ready, s.Step(12f, false, true, true, true));
        Assert.Equal(0f, s.Cover(12f));
        s.End();
        Assert.Equal(StartPhase.Done, s.Step(13f, true, true, true, true));
    }

    // [unit->REQ-GAME-START]
    [Fact]
    public void EndStopsTheStartAtAnyPhase()
    {
        var s = new StartSequence(fadeSeconds: 4f);
        s.Step(0f, true, false, false, false);
        s.Step(3f, true, false, false, false);
        s.End(); // no save to host
        Assert.Equal(StartPhase.Done, s.Step(4f, true, true, true, true));
        Assert.Equal(0f, s.Cover(4f));
    }
}
