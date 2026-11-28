using System.Text.Json;

namespace HikersDiary.Core;

public enum TargetKind { None, Ambiguous, Local, Join }

/// <summary>A friend session: the join code and the save title of the host.</summary>
public sealed record FriendSession(string Code, string Title);

/// <summary>
/// The target of a <c>goto:</c> command (#14, T3d): a friend session, a local save or a session ID.
/// <see cref="Index"/> is the index of the local save. <see cref="Code"/> and <see cref="Title"/> are for a join.
/// </summary>
// [impl->REQ-FRIEND-JOIN]
public sealed record SwitchTarget(TargetKind Kind, int Index = -1, string Code = "", string Title = "")
{
    /// <summary>
    /// The order: the title of one friend session, the title of one local save, then a session ID. A title of two
    /// friend sessions or two local saves is not valid. <paramref name="isCode"/> gets the target in upper case.
    /// </summary>
    public static SwitchTarget Resolve(string target, IList<string> localTitles, IList<FriendSession> friends, Func<string, bool> isCode)
    {
        int f = ChatCommand.MatchIndex(target, friends.Select(s => s.Title).ToList());
        if (f >= 0) return new(TargetKind.Join, Code: friends[f].Code.ToUpperInvariant(), Title: friends[f].Title);
        if (f == -2) return new(TargetKind.Ambiguous);
        int l = ChatCommand.MatchIndex(target, localTitles);
        if (l >= 0) return new(TargetKind.Local, l, Title: localTitles[l]);
        if (l == -2) return new(TargetKind.Ambiguous);
        string code = (target ?? "").Trim().ToUpperInvariant();
        if (!isCode(code)) return new(TargetKind.None);
        FriendSession? s = friends.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        return new(TargetKind.Join, Code: code, Title: s?.Title ?? "");
    }

    /// <summary>The titles for rule 2: the local titles, and the friend titles that are not also local titles.</summary>
    public static List<string> ChatTitles(IList<string> localTitles, IList<FriendSession> friends)
    {
        var titles = localTitles.ToList();
        foreach (FriendSession s in friends)
            if (ChatCommand.MatchIndex(s.Title, localTitles) == -1) titles.Add(s.Title);
        return titles;
    }
}

/// <summary>The most recent password of each session ID and each session title (#14, T3d). One file for all saves.</summary>
public sealed class PasswordBook
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public Dictionary<string, string> Passwords { get; init; } = new();

    private static string CodeKey(string code) => "code:" + code.Trim().ToUpperInvariant();
    private static string TitleKey(string title) => "title:" + title.Trim().ToLowerInvariant();

    /// <summary>The password of the session ID, else of the title, else empty.</summary>
    // [impl->REQ-FRIEND-JOIN]
    public string Get(string? code, string? title)
    {
        if (!string.IsNullOrWhiteSpace(code) && Passwords.TryGetValue(CodeKey(code), out string? p)) return p;
        if (!string.IsNullOrWhiteSpace(title) && Passwords.TryGetValue(TitleKey(title), out p)) return p;
        return "";
    }

    /// <summary>Keeps the password for the session ID and the title. Returns false if nothing changed.</summary>
    // [impl->REQ-FRIEND-JOIN]
    public bool Set(string? code, string? title, string password)
    {
        bool changed = false;
        if (!string.IsNullOrWhiteSpace(code)) changed |= Put(CodeKey(code), password);
        if (!string.IsNullOrWhiteSpace(title)) changed |= Put(TitleKey(title), password);
        return changed;
    }

    private bool Put(string key, string password)
    {
        if (Passwords.TryGetValue(key, out string? old) && old == password) return false;
        Passwords[key] = password;
        return true;
    }

    public void Write(string path) => DiaryStore.WriteAtomic(path, JsonSerializer.Serialize(this, Options));

    /// <summary>The book, or an empty book if there is no file or the file and its backup cannot be read.</summary>
    public static PasswordBook Read(string path)
    {
        foreach (string p in new[] { path, path + ".bak" })
        {
            if (!File.Exists(p)) continue;
            try
            {
                PasswordBook? book = JsonSerializer.Deserialize<PasswordBook>(File.ReadAllText(p));
                if (book != null) return new PasswordBook { Passwords = book.Passwords ?? new() };
            }
            catch (JsonException) { }
        }
        return new PasswordBook();
    }
}
