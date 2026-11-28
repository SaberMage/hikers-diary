using System.Collections.Generic;
using HarmonyLib;
using HikersDiary.Core;
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

    internal static void Step()
    {
        if (!Enabled) return;
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
        SwitchPhase after = _seq.Step(now, sitting, sleeping);
        SetTimeout(local.sleeper, _seq.ShortTimeout);
        if (after != before)
        {
            Plugin.Logger.LogInfo($"Diary: switch {before} -> {after} at {now:0.00} s.");
            if (after == SwitchPhase.FadeOut) MakeCover();
            else if (after == SwitchPhase.Idle) RemoveCover();
            else if (after == SwitchPhase.Disconnect) Disconnect();
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
        if (Resolve(cmd, false, out _, out _) == null) return false;
        _seq ??= new SwitchSequence(GameStart.FadeSeconds);
        _seq.Arm(cmd);
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

    private static void Disconnect()
    {
        ChatCommand cmd = _seq.Command;
        _seq.Cancel();
        _initialTimeout = -1f; // the player of this session goes away with its timeout
        var menus = Resources.FindObjectsOfTypeAll<DisconnectConfirmMenu>();
        if (menus.Length == 0)
        {
            Plugin.Logger.LogWarning("Diary: switch found no DisconnectConfirmMenu. The switch stops.");
            RemoveCover();
            return;
        }
        GameStart.BeginSwitch(() => HostTarget(cmd), _coverGo);
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
        canvas.sortingOrder = 32000;
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
        List<string> titles = SaveSwitch.Titles(saves);
        ChatCommand cmd = ChatCommand.Parse(text, titles);
        if (cmd == null) return true;
        // The cancel of the game closes the input. The game does not send the message (T3b).
        __instance.TextChatCancel();
        TextChatInput.instance.inputField.text = "";
        SaveSwitch.Arm(cmd);
        return false;
    }
}
