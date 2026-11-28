using HikersDiary.Core;
using Xunit;

namespace HikersDiary.Tests;

public class SwitchTargetTests
{
    private static readonly string[] Local = { "Lake", "Ridge", "Twin", "twin " };
    private static readonly FriendSession[] Friends = { new("abc123", "Ridge"), new("QQQ111", "Summit") };
    private static bool IsCode(string s) => s.Length == 6 && s.All(char.IsLetterOrDigit);

    private static SwitchTarget R(string t, FriendSession[]? f = null) => SwitchTarget.Resolve(t, Local, f ?? Friends, IsCode);

    // [unit->REQ-FRIEND-JOIN]
    [Fact]
    public void FriendTitleComesBeforeLocalTitle()
    {
        Assert.Equal(new SwitchTarget(TargetKind.Join, Code: "ABC123", Title: "Ridge"), R(" ridge "));
        Assert.Equal(new SwitchTarget(TargetKind.Join, Code: "QQQ111", Title: "Summit"), R("SUMMIT"));
        Assert.Equal(new SwitchTarget(TargetKind.Local, 0, Title: "Lake"), R("lake"));
    }

    // [unit->REQ-FRIEND-JOIN]
    [Fact]
    public void SessionIdJoinsInUpperCase()
    {
        Assert.Equal(new SwitchTarget(TargetKind.Join, Code: "QQQ111", Title: "Summit"), R("qqq111"));
        Assert.Equal(new SwitchTarget(TargetKind.Join, Code: "ZZZ999"), R("zzz999"));
        Assert.Equal(TargetKind.None, R("nothing here").Kind);
    }

    // [unit->REQ-FRIEND-JOIN]
    [Fact]
    public void TwoMatchesAreNotValid()
    {
        Assert.Equal(TargetKind.Ambiguous, R("twin").Kind);
        Assert.Equal(TargetKind.Ambiguous, R("Hill", new[] { new FriendSession("AAA111", "Hill"), new FriendSession("BBB222", "hill") }).Kind);
    }

    // [unit->REQ-FRIEND-JOIN]
    [Fact]
    public void ChatTitlesAddOnlyNewFriendTitles()
    {
        var titles = SwitchTarget.ChatTitles(Local, Friends);
        Assert.Equal(new[] { "Lake", "Ridge", "Twin", "twin ", "Summit" }, titles);
        // A friend title that is also a local title stays a definite match for rule 2.
        Assert.Equal(ChatCommandKind.Goto, ChatCommand.Parse("Ridge", titles)?.Kind);
    }

    // [unit->REQ-FRIEND-JOIN]
    [Fact]
    public void PasswordBookKeepsTheMostRecentPassword()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hd-pw-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "passwords.json");
        try
        {
            var book = PasswordBook.Read(path);
            Assert.Equal("", book.Get("ABC123", "Ridge"));
            Assert.True(book.Set("abc123", "Ridge", "pw1"));
            Assert.False(book.Set("ABC123", " ridge", "pw1"));
            Assert.True(book.Set("XYZ789", "Ridge", "pw2"));
            book.Write(path);
            book = PasswordBook.Read(path);
            Assert.Equal("pw1", book.Get("abc123", "Ridge")); // the session ID comes first
            Assert.Equal("pw2", book.Get("NEW000", "RIDGE")); // then the title
            Assert.Equal("pw2", book.Get(null, "Ridge"));
            File.WriteAllText(path, "{ broken");
            Assert.Empty(PasswordBook.Read(path).Passwords); // no backup yet: an empty book
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    // [unit->REQ-FRIEND-JOIN]
    [Fact]
    public void FadeOutWaitsForTheTarget()
    {
        var s = new SwitchSequence(2f);
        s.Arm(new ChatCommand(ChatCommandKind.Goto, "ABC123", "", null));
        s.Step(0f, true, true);
        Assert.Equal(SwitchPhase.FadeOut, s.Step(5f, true, true, targetReady: false));
        Assert.Equal(1f, s.Cover(5f));
        Assert.Equal(SwitchPhase.Disconnect, s.Step(6f, true, true, targetReady: true));
    }
}
