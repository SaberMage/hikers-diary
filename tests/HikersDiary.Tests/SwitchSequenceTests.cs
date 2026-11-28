using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

public class SwitchSequenceTests
{
    private static readonly ChatCommand Cmd = new(ChatCommandKind.Goto, "Lake", "", null);

    // [unit->REQ-SAVE-SWITCH]
    [Fact]
    public void SitSleepFadeOutThenDisconnect()
    {
        var s = new SwitchSequence(fadeSeconds: 4f);
        Assert.Equal(SwitchPhase.Idle, s.Step(0f, true, true)); // no command: a sleep does nothing
        s.Arm(Cmd);
        Assert.Equal(SwitchPhase.Pending, s.Step(1f, false, false));
        Assert.False(s.ShortTimeout); // the player stands
        Assert.Equal(SwitchPhase.Pending, s.Step(2f, true, false));
        Assert.True(s.ShortTimeout);
        Assert.Equal(SwitchPhase.FadeOut, s.Step(3f, true, true));
        Assert.True(s.ShortTimeout); // the initial timeout would end the sleep
        Assert.Equal(0.25f, s.Cover(4f), 3);
        Assert.Equal(SwitchPhase.FadeOut, s.Step(6.9f, true, true));
        Assert.Equal(SwitchPhase.Disconnect, s.Step(7f, true, true));
        Assert.Equal(1f, s.Cover(7f));
        Assert.Same(Cmd, s.Command);
        s.Cancel();
        Assert.Equal(SwitchPhase.Idle, s.Step(8f, true, true));
        Assert.Null(s.Command);
    }

    // [unit->REQ-SAVE-SWITCH]
    [Fact]
    public void StandAfterSitStopsTheCommand()
    {
        var s = new SwitchSequence(4f);
        s.Arm(Cmd);
        s.Step(1f, true, false);
        Assert.Equal(SwitchPhase.Idle, s.Step(2f, false, false));
        Assert.False(s.ShortTimeout);
        Assert.Null(s.Command);
    }

    // [unit->REQ-SAVE-SWITCH]
    [Fact]
    public void SleepWhileStandingDoesNotSwitch()
    {
        var s = new SwitchSequence(4f);
        s.Arm(Cmd);
        Assert.Equal(SwitchPhase.Pending, s.Step(1f, false, true));
        Assert.Equal(0f, s.Cover(1f));
    }

    // [unit->REQ-SAVE-SWITCH]
    [Fact]
    public void WakeDuringFadeOutStopsTheSwitch()
    {
        var s = new SwitchSequence(4f);
        s.Arm(Cmd);
        s.Step(1f, true, true);
        Assert.Equal(SwitchPhase.FadeOut, s.Phase);
        Assert.Equal(SwitchPhase.Idle, s.Step(2f, true, false));
        Assert.Equal(0f, s.Cover(2f));
    }

    // [unit->REQ-SAVE-SWITCH]
    [Fact]
    public void NewCommandReplacesThePrevious()
    {
        var s = new SwitchSequence(4f);
        s.Arm(Cmd);
        var other = new ChatCommand(ChatCommandKind.New, "Peak", "pw", 3);
        s.Arm(other);
        Assert.Same(other, s.Command);
        Assert.Equal(SwitchPhase.Pending, s.Phase);
    }
}
