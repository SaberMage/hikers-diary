using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

public class ChatCommandTests
{
    private static readonly string[] Saves = { "Summit Run", "Lake", "lake", "Ridge" };

    // [unit->REQ-CHAT-COMMAND]
    [Theory]
    [InlineData("new: Ridge", ChatCommandKind.New, "Ridge", "", null)]
    [InlineData("  NEW:3p: Night Hike, se,cret ", ChatCommandKind.New, "Night Hike", "se,cret", 3)]
    [InlineData("new: 5p: Odd", ChatCommandKind.New, "5p: Odd", "", null)]
    [InlineData("goto: Summit Run, pw", ChatCommandKind.Goto, "Summit Run", "pw", null)]
    [InlineData("Goto:ABC123", ChatCommandKind.Goto, "ABC123", "", null)]
    [InlineData(" summit run ", ChatCommandKind.Goto, "Summit Run", "", null)]
    public void ParsesCommands(string text, ChatCommandKind kind, string target, string password, int? players)
    {
        Assert.Equal(new ChatCommand(kind, target, password, players), ChatCommand.Parse(text, Saves));
    }

    // [unit->REQ-CHAT-COMMAND]
    [Theory]
    [InlineData("hello")]
    [InlineData("lake")] // two saves have this title: not a definite match
    [InlineData("Summit Run!")]
    [InlineData("let's goto: Ridge")]
    [InlineData("new:")]
    [InlineData("goto: , pw")]
    [InlineData("")]
    [InlineData(null)]
    public void NormalChatIsNotACommand(string text)
    {
        Assert.Null(ChatCommand.Parse(text, Saves));
    }
}
