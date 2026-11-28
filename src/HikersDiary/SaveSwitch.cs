using System.Collections.Generic;
using HarmonyLib;
using HikersDiary.Core;
using Mirror;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HikersDiary;

/// <summary>
/// The local save switch (#14, T3c). The player types a <c>new:</c> or <c>goto:</c> command in the chat and sits.
/// The sleep timeout becomes short. When the player sleeps, the view and the audio decrease to zero. Then the mod
/// disconnects, and the steps of the game start host the target save. <see cref="SwitchSequence"/> holds the order.
/// </summary>
// [impl->REQ-SAVE-SWITCH]
internal static class SaveSwitch
{
    internal static bool Enabled;
    internal static float SleepSeconds = 5f;
    internal static string TestCommand = "";

    private static SwitchSequence _seq;
    private static float _initialTimeout = -1f;
    private static GameObject _coverGo;
    private static Image _cover;
    private static string _lastState = "";
    private static float _wakeCheckAt = -1f;

    // One log line 1 s after a wake with no target: the player must stand and must not sleep.
    private static void WakeCheck()
    {
        if (_wakeCheckAt < 0f || Time.time < _wakeCheckAt) return;
        _wakeCheckAt = -1f;
        PlayerCharacter p = DiaryHost.LocalPlayer();
        Plugin.Logger.LogInfo($"Diary: switch after the wake: sit {p?.sitter?.isSittingLocal}, sleep {p?.sleeper?.combinedIsSleeping}, timeout {p?.sleeper?.timeTilSleep}.");
    }

    // A goto: command to a session (T3d): the target, and the result of the lobby check (null = no result yet).
    private const float LookupSeconds = 10f;
    private static SwitchTarget _join;
    private static bool? _found;
    private static float _lookupAt;
    private static int _lookup;
    private static PasswordBook _passwords = new();
    private static string _passwordPath;

    internal static void OpenPasswords(string storeDir)
    {
        _passwordPath = System.IO.Path.Combine(storeDir, "passwords.json");
        _passwords = PasswordBook.Read(_passwordPath);
    }

    // [impl->REQ-FRIEND-JOIN]
    internal static void KeepPassword(string code, string title, string password)
    {
        if (!_passwords.Set(code, title, password)) return;
        try { _passwords.Write(_passwordPath); }
        catch (System.Exception e) { Plugin.Logger.LogWarning($"Diary: cannot write the passwords: {e.Message}"); }
    }

    private static bool TargetReady => _join == null || _found != null || Time.time - _lookupAt >= LookupSeconds;

    internal static void Step()
    {
        if (!Enabled) return;
        SwitchJoin.Step();
        WakeCheck();
        _seq ??= new SwitchSequence(GameStart.FadeSeconds);
        TestStep();
        if (_seq.Phase == SwitchPhase.Idle) return;
        PlayerCharacter local = DiaryHost.LocalPlayer();
        if (local == null || local.sleeper == null || local.sitter == null) return;

        float now = Time.time;
        bool sitting = local.sitter.isSittingLocal, sleeping = local.sleeper.combinedIsSleeping;
        string state = $"sit {sitting} sleep {sleeping}";
        if (state != _lastState) Plugin.Logger.LogInfo($"Diary: switch {_seq.Phase}: {_lastState = state} at {now:0.00} s.");

        SwitchPhase before = _seq.Phase;
        SwitchPhase after = _seq.Step(now, sitting, sleeping, TargetReady);
        SetTimeout(local.sleeper, _seq.ShortTimeout);
        if (after != before)
        {
            Plugin.Logger.LogInfo($"Diary: switch {before} -> {after} at {now:0.00} s.");
            if (after == SwitchPhase.FadeOut) MakeCover();
            else if (after == SwitchPhase.Idle) RemoveCover();
            else if (after == SwitchPhase.Disconnect) Disconnect(local);
        }
        if (_cover != null)
        {
            float c = _seq.Cover(now);
            _cover.color = new Color(0f, 0f, 0f, c);
            AudioListener.volume = 1f - c;
        }
    }

    /// <summary>A command from the local chat. Returns false if the target is not valid. Then the switch does not start.</summary>
    internal static bool Arm(ChatCommand cmd)
    {
        SwitchTarget join = null;
        if (cmd.Kind == ChatCommandKind.Goto)
        {
            SwitchTarget t = ResolveGoto(cmd.Target);
            if (t.Kind is TargetKind.None or TargetKind.Ambiguous)
            {
                Plugin.Logger.LogWarning($"Diary: switch command not done: '{cmd.Target}' is {(t.Kind == TargetKind.None ? "not a save, a friend session or a session ID" : "the title of two or more saves or friend sessions")}.");
                return false;
            }
            if (t.Kind == TargetKind.Join) join = t;
        }
        else if (Resolve(cmd, false, out _, out _) == null) return false;
        _seq ??= new SwitchSequence(GameStart.FadeSeconds);
        _seq.Arm(cmd);
        _join = join;
        if (join != null) StartLookup(join);
        Plugin.Logger.LogInfo($"Diary: switch command {cmd.Kind} '{cmd.Target}' ({cmd.Players?.ToString() ?? "no"} player count). Sit and sleep to switch.");
        return true;
    }

    // While the player sits with a command, the game uses the short sleep timeout. Then the initial value again.
    private static void SetTimeout(PlayerSleeper s, bool shortTimeout)
    {
        if (shortTimeout)
        {
            if (_initialTimeout < 0f) _initialTimeout = s.timeTilSleep;
            s.timeTilSleep = SleepSeconds;
        }
        else if (_initialTimeout >= 0f)
        {
            s.timeTilSleep = _initialTimeout;
            _initialTimeout = -1f;
        }
    }

    // [impl->REQ-FRIEND-JOIN]
    private static SwitchTarget ResolveGoto(string target) =>
        SwitchTarget.Resolve(target, Titles(SaveManager.GetAllSaveDatasInFolder()), Friends(), IsCode);

    private static bool IsCode(string code) => EOSPrivateLobbyManager.IsValidCode(code, 6);

    /// <summary>The friend sessions of the game (rich presence). The list is also there during the gameplay (T3d-1).</summary>
    internal static List<FriendSession> Friends()
    {
        var list = new List<FriendSession>();
        try
        {
            var lobbies = FriendGamePresenceManager.Presence?.CurrentLobbies?.TryCast<Il2CppSystem.Collections.Generic.List<BigWalkLobbyData>>();
            for (int i = 0; lobbies != null && i < lobbies.Count; i++)
                if (!string.IsNullOrEmpty(lobbies[i].lobbyCode) && !string.IsNullOrEmpty(lobbies[i].roomName))
                    list.Add(new FriendSession(lobbies[i].lobbyCode, lobbies[i].roomName));
        }
        catch (System.Exception e) { Plugin.Logger.LogWarning($"Diary: no friend sessions: {e.Message}"); }
        return list;
    }

    // The lobby check of a join: does the session exist, and what is its title? A new command makes an old result stale.
    // [impl->REQ-FRIEND-JOIN]
    private static void StartLookup(SwitchTarget t)
    {
        int id = ++_lookup;
        _found = null;
        _lookupAt = Time.time;
        bool started = EosLobby.FindById(t.Code, (found, title) =>
        {
            if (id != _lookup) return;
            _found = found;
            if (title.Length > 0) _join = new SwitchTarget(_join.Kind, _join.Index, _join.Code, title); // no "with": Nullable is off here
        });
        if (!started) _found = false;
    }

    private static void Disconnect(PlayerCharacter local)
    {
        ChatCommand cmd = _seq.Command;
        System.Action host = () => HostTarget(cmd);
        // [impl->REQ-FRIEND-JOIN]
        if (_join != null)
        {
            SwitchTarget join = _join;
            bool typed = cmd.Password.Length > 0;
            if (_found == true)
            {
                string pw = typed ? cmd.Password : _passwords.Get(join.Code, join.Title);
                host = () => SwitchJoin.Start(join, pw, () => { if (typed || !HostCopy(join.Title)) HostLatestOrMenus(); });
            }
            else if (!typed && HasCopy(join.Title))
            {
                Plugin.Logger.LogInfo($"Diary: switch found no lobby {join.Code}. Host the local copy '{join.Title}'.");
                host = () => { if (!HostCopy(join.Title)) HostLatestOrMenus(); };
            }
            else
            {
                Plugin.Logger.LogWarning($"Diary: switch found no lobby {join.Code} ({(_found == null ? "no result" : "no lobby")}), and {(typed ? "the command has a password" : "there is no local copy")}. The player wakes.");
                WakeAndStand(local);
                return;
            }
        }
        _seq.Cancel();
        _initialTimeout = -1f; // the player of this session goes away with its timeout
        var menus = Resources.FindObjectsOfTypeAll<DisconnectConfirmMenu>();
        if (menus.Length == 0)
        {
            Plugin.Logger.LogWarning("Diary: switch found no DisconnectConfirmMenu. The switch stops.");
            RemoveCover();
            return;
        }
        GameStart.BeginSwitch(host, _coverGo);
        _coverGo = null;
        _cover = null;
        Plugin.Logger.LogInfo("Diary: switch disconnects.");
        menus[0].ActionDisconnect();
    }

    // After the disconnection, the first screen shows again. Host the target. If it is not valid now, host the latest save.
    private static void HostTarget(ChatCommand cmd)
    {
        SaveData save = Resolve(cmd, true, out int players, out string why);
        if (save != null) DiaryHost.HostSave(save, players);
        else
        {
            Plugin.Logger.LogWarning($"Diary: switch target not valid after the disconnection ({why}). Host the latest save.");
            DiaryHost.HostLatestSave();
        }
    }

    // No session for the switch: the cover goes away, and the player wakes and stands.
    // [impl->REQ-FRIEND-JOIN]
    private static void WakeAndStand(PlayerCharacter local)
    {
        _seq.Cancel();
        RemoveCover();
        SetTimeout(local.sleeper, false);
        local.sleeper.RecordAction();
        local.sitter.SetSittingLocal(false);
        _wakeCheckAt = Time.time + 1f;
    }

    private static int CopyIndex(Il2CppSystem.Collections.Generic.List<SaveData> saves, string title) =>
        string.IsNullOrEmpty(title) ? -1 : ChatCommand.MatchIndex(title, Titles(saves));

    private static bool HasCopy(string title) => CopyIndex(SaveManager.GetAllSaveDatasInFolder(), title) >= 0;

    /// <summary>Host the local copy of a session: the only local save with its title. Returns false if there is none.</summary>
    private static bool HostCopy(string title)
    {
        var saves = SaveManager.GetAllSaveDatasInFolder();
        int i = CopyIndex(saves, title);
        if (i < 0) return false;
        DiaryHost.HostSave(saves[i], DiaryHost.SavedPlayers(saves[i].filenameUid));
        return true;
    }

    private static void HostLatestOrMenus()
    {
        if (DiaryHost.HostLatestSave()) return;
        Plugin.Logger.LogWarning("Diary: switch has no save to host. The menus of the game come back.");
        GameStart.Stop();
    }

    /// <summary>
    /// The save of the command, or null if the command has no valid target. With <paramref name="apply"/>, a
    /// <c>new:</c> command changes the save: a new save, or the player count and the password of the save.
    /// </summary>
    private static SaveData Resolve(ChatCommand cmd, bool apply, out int players, out string why)
    {
        players = 2;
        var saves = SaveManager.GetAllSaveDatasInFolder();
        List<string> titles = SaveSwitch.Titles(saves);
        int index = ChatCommand.MatchIndex(cmd.Target, titles);
        why = index == -2 ? $"two or more saves have the title '{cmd.Target}'" : $"no local save has the title '{cmd.Target}'";
        if (index == -2 || (index == -1 && cmd.Kind == ChatCommandKind.Goto))
        {
            if (!apply) Plugin.Logger.LogWarning($"Diary: switch command not done: {why}.");
            return null;
        }
        SaveData save;
        if (index >= 0)
        {
            save = saves[index];
            players = cmd.Players ?? DiaryHost.SavedPlayers(save.filenameUid);
            if (apply && cmd.Kind == ChatCommandKind.New && cmd.Password.Length > 0) save.password = cmd.Password;
            return save;
        }
        // A new save, with the steps of HostMenuConfirm.ActionStart.
        players = cmd.Players ?? 2;
        if (!apply) return new SaveData();
        save = new SaveData
        {
            entries = new Il2CppSystem.Collections.Generic.List<SaveEntry>(),
            stringEntries = new Il2CppSystem.Collections.Generic.List<SaveEntryString>(),
            inventory = new Il2CppSystem.Collections.Generic.List<string>(),
            filenameUid = System.Guid.NewGuid().ToString("N").Substring(0, 16),
            slotName = cmd.Target,
            password = cmd.Password,
        };
        Plugin.Logger.LogInfo($"Diary: switch makes the new save '{save.slotName}' ({save.filenameUid}).");
        return save;
    }

    internal static List<string> Titles(Il2CppSystem.Collections.Generic.List<SaveData> saves)
    {
        var titles = new List<string>();
        for (int i = 0; saves != null && i < saves.Count; i++) titles.Add(saves[i].slotName);
        return titles;
    }

    // The fade-out cover: a black image above all canvases of the game. It stays while the scenes change.
    private static void MakeCover()
    {
        _coverGo = new GameObject("DiarySwitchCover");
        Object.DontDestroyOnLoad(_coverGo);
        var canvas = _coverGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32001; // above the first screen: the menus of a join are not visible
        var img = new GameObject("Black");
        img.transform.SetParent(_coverGo.transform, false);
        var rect = img.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        _cover = img.AddComponent<Image>();
        _cover.color = Color.clear;
        _cover.raycastTarget = false;
    }

    private static void RemoveCover()
    {
        if (_coverGo != null) Object.Destroy(_coverGo);
        _coverGo = null;
        _cover = null;
        AudioListener.volume = 1f;
    }

    // Test aid (Spike.SwitchCommand): 10 s after the gameplay starts, send the command through the chat and sit.
    private static float _playAt = -1f;
    private static bool _testSent;

    private static void TestStep()
    {
        if (string.IsNullOrEmpty(TestCommand) || _testSent) return;
        PlayerCharacter local = DiaryHost.LocalPlayer();
        if (local == null || GameStart.Active) { _playAt = -1f; return; }
        if (_playAt < 0f) { _playAt = Time.time; return; }
        if (Time.time - _playAt < 10f || TextChatInput.instance == null || local.texter == null || local.sitter == null) return;
        _testSent = true;
        TextChatInput.instance.OpenInput();
        local.texter.isPlayerTextChatting = true;
        TextChatInput.instance.inputField.text = TestCommand;
        Plugin.Logger.LogInfo($"Diary: test aid sends '{TestCommand}' and sits.");
        local.texter.TrySendTextChat();
        local.sitter.SetSittingLocal(true);
    }
}

/// <summary>A command from the local chat does not go to the chat. It starts the switch.</summary>
// [impl->REQ-SAVE-SWITCH]
[HarmonyPatch]
internal static class SaveSwitchPatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(PlayerTexter), nameof(PlayerTexter.TrySendTextChat))]
    private static bool Send(PlayerTexter __instance)
    {
        string text = TextChatInput.instance != null ? TextChatInput.instance.GetString() : null;
        var saves = SaveManager.GetAllSaveDatasInFolder();
        List<string> titles = SwitchTarget.ChatTitles(SaveSwitch.Titles(saves), SaveSwitch.Friends());
        ChatCommand cmd = ChatCommand.Parse(text, titles);
        if (cmd == null) return true;
        // The cancel of the game closes the input. The game does not send the message (T3b).
        __instance.TextChatCancel();
        TextChatInput.instance.inputField.text = "";
        SaveSwitch.Arm(cmd);
        return false;
    }
}
