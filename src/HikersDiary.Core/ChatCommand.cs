namespace HikersDiary.Core;

public enum ChatCommandKind { New, Goto }

/// <summary>
/// A save switch command from the chat (#14, docs/DESIGN.md spec 9 and revision rule 2).
/// The syntax is <c>new: [&lt;2|3|4&gt;p: ]&lt;title&gt;[, &lt;password&gt;]</c> or <c>goto: &lt;title|session ID&gt;[, &lt;password&gt;]</c>.
/// A text that is a definite match to the title of only one known save is a <c>goto:</c> to that save.
/// All other text is normal chat. The title cannot contain a comma. The first comma starts the password.
/// </summary>
// [impl->REQ-CHAT-COMMAND]
public sealed record ChatCommand(ChatCommandKind Kind, string Target, string Password, int? Players)
{
    /// <summary>This method returns the command in the text, or null if the text is normal chat.</summary>
    public static ChatCommand? Parse(string? text, IEnumerable<string>? knownTitles)
    {
        if (text == null) return null;
        string t = text.Trim();
        if (StartsWith(t, "new:", out string rest))
        {
            int? players = null;
            if (rest.Length >= 3 && rest[0] is '2' or '3' or '4' && (rest[1] is 'p' or 'P') && rest[2] == ':')
            {
                players = rest[0] - '0';
                rest = rest.Substring(3).TrimStart();
            }
            return Make(ChatCommandKind.New, rest, players);
        }
        if (StartsWith(t, "goto:", out rest)) return Make(ChatCommandKind.Goto, rest, null);

        // Rule 2: a title alone starts a switch only if it is a definite match. If two saves have this title, the title is not a definite match.
        var titles = (knownTitles ?? Enumerable.Empty<string>()).ToList();
        int i = MatchIndex(t, titles);
        return i >= 0 ? new ChatCommand(ChatCommandKind.Goto, titles[i], "", null) : null;
    }

    /// <summary>
    /// The index of the only title that matches the target (no case, no spaces at the two ends). Returns -1 if no
    /// title matches, and -2 if two or more titles match.
    /// </summary>
    public static int MatchIndex(string? target, IList<string> titles)
    {
        string t = (target ?? "").Trim();
        int match = -1;
        for (int i = 0; t.Length > 0 && i < titles.Count; i++)
        {
            if (titles[i] == null || !string.Equals(titles[i].Trim(), t, StringComparison.OrdinalIgnoreCase)) continue;
            if (match >= 0) return -2;
            match = i;
        }
        return match;
    }

    private static bool StartsWith(string t, string prefix, out string rest)
    {
        bool yes = t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        rest = yes ? t.Substring(prefix.Length).TrimStart() : "";
        return yes;
    }

    private static ChatCommand? Make(ChatCommandKind kind, string rest, int? players)
    {
        int comma = rest.IndexOf(',');
        string target = (comma < 0 ? rest : rest.Substring(0, comma)).Trim();
        string password = comma < 0 ? "" : rest.Substring(comma + 1).Trim();
        return target.Length == 0 ? null : new ChatCommand(kind, target, password, players);
    }
}
