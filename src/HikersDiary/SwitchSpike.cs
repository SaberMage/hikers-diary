using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HikersDiary;

/// <summary>
/// Test aid for T3b (#14): log the chat send, the sit and sleep states, and the scenes and cameras during a
/// disconnect and a new host from the gameplay. It changes nothing in the game, except these steps after the
/// continue button: at 10 s it opens the chat input, at 25 s it sets a sleep timeout of 5 s, at 45 s it disconnects,
/// and at the next first screen it hosts the latest save again.
/// </summary>
internal static class SwitchSpike
{
    internal static bool Enabled;

    private static float _playAt = -1f;
    private static int _step;
    private static string _last = "";
    private static float _nextLog;

    internal static void Step()
    {
        if (!Enabled) return;
        float now = Time.time;
        LogChanges(now);

        PlayerCharacter local = DiaryHost.LocalPlayer();
        if (_playAt < 0f)
        {
            if (local != null && !GameStart.Active && Mirror.NetworkServer.active) { _playAt = now; Log("gameplay starts"); }
            return;
        }
        float t = now - _playAt;
        // Steps 1 and 2: normal chat goes through the send path of the game. Step 3: a command stops in the prefix.
        if (_step is 0 or 2 && t > (_step == 0 ? 10f : 16f) && TextChatInput.instance != null && local?.texter != null)
        {
            string text = _step == 0 ? "hello spike" : "goto: spike test";
            _step++;
            TextChatInput.instance.OpenInput();
            local.texter.isPlayerTextChatting = true;
            TextChatInput.instance.inputField.text = text;
            Log($"chat open {TextChatInput.instance.inputIsOpen}, text '{TextChatInput.instance.GetString()}': call TrySendTextChat");
            local.texter.TrySendTextChat();
            Log($"after TrySendTextChat: open {TextChatInput.instance.inputIsOpen}, texting {local.texter.isPlayerTextChatting}");
        }
        else if (_step is 1 && t > 13f) _step = 2;
        else if (_step == 3 && t > 25f && local?.sleeper != null)
        {
            _step = 4;
            Log($"set timeTilSleep {local.sleeper.timeTilSleep:0.0} -> 5");
            local.sleeper.timeTilSleep = 5f;
        }
        else if (_step == 4 && t > 45f) // disconnect
        {
            _step = 5;
            LogSaveEntries();
            var menus = Resources.FindObjectsOfTypeAll<DisconnectConfirmMenu>();
            Log($"disconnect: {menus.Length} DisconnectConfirmMenu");
            if (menus.Length > 0) menus[0].ActionDisconnect();
        }
        else if (_step == 5 && (Object.FindObjectOfType<MicCheckMenu>() != null || Object.FindObjectOfType<TitleMenu>() != null))
        {
            _step = 6;
            Log("first screen again: host the latest save");
            DiaryHost.HostLatestSave();
        }
    }

    // Log one line when the state changes, a maximum of 4 times each second.
    private static void LogChanges(float now)
    {
        if (now < _nextLog) return;
        _nextLog = now + 0.25f;
        PlayerCharacter local = DiaryHost.LocalPlayer();
        var scenes = new System.Text.StringBuilder();
        for (int i = 0; i < SceneManager.sceneCount; i++) scenes.Append(SceneManager.GetSceneAt(i).name).Append(' ');
        var cams = new System.Text.StringBuilder();
        foreach (Camera c in Camera.allCameras) cams.Append(c.name).Append(' ');
        string state = $"scenes [{scenes}] cameras [{cams}] server {Mirror.NetworkServer.active} client {Mirror.NetworkClient.active} " +
            $"local {local != null} chat {(TextChatInput.instance != null ? TextChatInput.instance.inputIsOpen : (bool?)null)} " +
            $"sit {local?.sitter?.isSittingLocal} sleep {local?.sleeper?.combinedIsSleeping} " +
            $"mic {Object.FindObjectOfType<MicCheckMenu>() != null} title {Object.FindObjectOfType<TitleMenu>() != null} " +
            $"save {SaveManager.instance?.currentData?.slotName}";
        if (state == _last) return;
        _last = state;
        Log(state);
    }

    private static void LogSaveEntries()
    {
        SaveData d = SaveManager.instance?.currentData;
        if (d == null) return;
        var s = new System.Text.StringBuilder();
        for (int i = 0; d.entries != null && i < d.entries.Count; i++) s.Append(d.entries[i].key).Append('=').Append(d.entries[i].value).Append(' ');
        Log($"save '{d.slotName}' password '{d.password}' entries: {s}");
    }

    internal static void Log(string text) => Plugin.Logger.LogInfo($"SwitchSpike {Time.time:0.00}: {text}");
}

[HarmonyPatch]
internal static class SwitchSpikePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(TextChatInput), nameof(TextChatInput.RecordMessage))]
    private static void Record(string message) => SwitchSpike.Log($"TextChatInput.RecordMessage '{message}'\n{System.Environment.StackTrace}");

    [HarmonyPrefix, HarmonyPatch(typeof(TextChatInput), nameof(TextChatInput.CloseInput))]
    private static void CloseInput(TextChatInput __instance) => SwitchSpike.Log($"TextChatInput.CloseInput text '{__instance.GetString()}'");

    [HarmonyPrefix, HarmonyPatch(typeof(TextChatSource), nameof(TextChatSource.AddMessage))]
    private static void Add(TextChatSource __instance, TextChatMessage message) =>
        SwitchSpike.Log($"TextChatSource.AddMessage '{message.message}' local source {__instance.isLocalPlayer}");

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerSleeper), nameof(PlayerSleeper.RecordAction))]
    private static void Action(PlayerSleeper __instance)
    {
        if (__instance.playerCharacter != null && __instance.playerCharacter.isLocalPlayer && Time.time - _actionLog > 2f)
        {
            _actionLog = Time.time;
            SwitchSpike.Log("PlayerSleeper.RecordAction (local, at most one log each 2 s)");
        }
    }
    private static float _actionLog = -10f;

    // A command does not go to the chat: close the input as the cancel of the game does.
    [HarmonyPrefix, HarmonyPatch(typeof(PlayerTexter), nameof(PlayerTexter.TrySendTextChat))]
    private static bool Send(PlayerTexter __instance)
    {
        string text = TextChatInput.instance != null ? TextChatInput.instance.GetString() : null;
        bool command = text != null && text.TrimStart().StartsWith("goto:", System.StringComparison.OrdinalIgnoreCase);
        SwitchSpike.Log($"PlayerTexter.TrySendTextChat '{text}' command {command}");
        if (!command) return true;
        __instance.TextChatCancel();
        TextChatInput.instance.inputField.text = "";
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerTexter), nameof(PlayerTexter.ReceieveMessage))]
    private static void Receive(string message) => SwitchSpike.Log($"PlayerTexter.ReceieveMessage '{message}'");

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerTexter), nameof(PlayerTexter.TextChatCancel))]
    private static void Cancel() => SwitchSpike.Log("PlayerTexter.TextChatCancel");

    [HarmonyPrefix, HarmonyPatch(typeof(DisconnectConfirmMenu), nameof(DisconnectConfirmMenu.ActionDisconnect))]
    private static void Disconnect() => SwitchSpike.Log("DisconnectConfirmMenu.ActionDisconnect");

    [HarmonyPrefix, HarmonyPatch(typeof(NetworkMinder), nameof(NetworkMinder.StartHost))]
    private static void Host() => SwitchSpike.Log("NetworkMinder.StartHost");
}
