using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using HarmonyLib;
using Il2CppInterop.Runtime;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace HikersDiary;

/// <summary>
/// T3d-1 spike (#14): logs the friend sessions, the lobby of this session and the EOS lobby search results.
/// Test aid <c>Spike.LobbySearch</c>: the search text (a save title, or a 6-character join code).
/// Empty = off. Remove this file after T3d-2.
/// </summary>
internal static class LobbySpike
{
    internal static string SearchText = "";

    private const float FirstDelay = 10f, RepeatSeconds = 30f, ResultDelay = 5f;
    private static float _playAt = -1f, _nextAt, _resultAt = -1f;
    private static LobbySearch _byName, _byId;
    private static int _run;
    private static string _menus = "";

    // The managed callback. Keep a reference, so that the GC does not collect it.
    private delegate void FindCallback(ref LobbySearchFindCallbackInfo info);
    private static LobbySearchOnFindCallback _callback;
    private static bool _callbackFailed;

    internal static void Step()
    {
        if (string.IsNullOrEmpty(SearchText) && string.IsNullOrEmpty(DiaryHost.AutoJoinCode)) return;
        LogMenus();
        if (string.IsNullOrEmpty(SearchText)) return;
        if (DiaryHost.LocalPlayer() == null) { _playAt = -1f; return; }
        float now = Time.time;
        if (_playAt < 0f) { _playAt = now; _nextAt = now + FirstDelay; return; }
        if (_resultAt > 0f && now >= _resultAt) { _resultAt = -1f; LogResults(); }
        if (now < _nextAt) return;
        _nextAt = now + RepeatSeconds;
        _run++;
        try
        {
            LogFriends();
            LogOwnLobby();
            StartSearches();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Lobby spike {_run}: {e}"); }
    }

    // The menus that are active now. Log each change, for the failed join test.
    private static void LogMenus()
    {
        string m = "";
        if (Active<MicCheckMenu>()) m += " MicCheckMenu";
        if (Active<TitleMenu>()) m += " TitleMenu";
        if (Active<JoinMenu>()) m += " JoinMenu";
        if (Active<ConnectingMenu>()) m += " ConnectingMenu";
        if (Active<PasswordMenu>()) m += " PasswordMenu";
        if (m == _menus) return;
        _menus = m;
        Plugin.Logger.LogInfo($"Lobby spike: menus [{m.Trim()}] at {Time.time:0.00} s.");
    }

    private static bool Active<T>() where T : UnityEngine.Object => UnityEngine.Object.FindObjectOfType<T>() != null;

    private static void LogFriends()
    {
        BasePresence presence = FriendGamePresenceManager.Presence;
        if (presence == null) { Plugin.Logger.LogInfo($"Lobby spike {_run}: no presence."); return; }
        var list = presence.CurrentLobbies?.TryCast<Il2CppSystem.Collections.Generic.List<BigWalkLobbyData>>();
        if (list == null) { Plugin.Logger.LogInfo($"Lobby spike {_run}: CurrentLobbies is null or not a List ({presence.CurrentLobbies?.Cast<Il2CppSystem.Object>().GetIl2CppType().FullName})."); return; }
        Plugin.Logger.LogInfo($"Lobby spike {_run}: {list.Count} friend sessions ({presence.GetIl2CppType().Name}).");
        for (int i = 0; i < list.Count; i++)
            Plugin.Logger.LogInfo($"  friend session: code '{list[i].lobbyCode}', user '{list[i].userName}', room '{list[i].roomName}'.");
    }

    private static void LogOwnLobby()
    {
        var mgr = EOSPrivateLobbyManager.Instance;
        Plugin.Logger.LogInfo($"Lobby spike {_run}: own code '{mgr?.CurrentLobbyCode}', hosting {mgr?.IsHostingLobby}.");
        LobbyInfo info = mgr?.GetJoinedLobbyInfo();
        if (info == null) { Plugin.Logger.LogInfo("  own lobby: none."); return; }
        Plugin.Logger.LogInfo($"  own lobby: world '{info.worldName}', owner '{info.WorldOwnerUserName}', code '{info.joinCode}', crossplay {info.crossplay}.");
        // Runs 1 and 2 crashed the game in LobbyDetails calls and in CopyLobbyDetailsHandle (native, no exception).
        // The copied LobbyDetailsInfo of the game needs no native call.
        LobbyDetailsInfo i = info.detailsInfo;
        if (i == null) { Plugin.Logger.LogInfo("  own lobby: no detailsInfo."); return; }
        Plugin.Logger.LogInfo($"  own lobby: id '{(string)i.LobbyId}', permission {i.PermissionLevel}, bucket '{(string)i.BucketId}', join by id {i.AllowJoinById}, presence {i.PresenceEnabled}, slots {i.AvailableSlots}/{i.MaxMembers}.");
    }

    // A log line before each native call, so that a crash shows the last step.
    private static void Step(string what) => Plugin.Logger.LogInfo($"  step: {what}.");

    private static void StartSearches()
    {
        LobbyInterface lobbies = EOSManager.Instance.GetEOSLobbyInterface();
        Step("searches");
        _callback ??= MakeCallback();
        _byName = NewSearch(lobbies);
        if (_byName != null)
        {
            var p = new LobbySearchSetParameterOptions
            {
                Parameter = new Il2CppSystem.Nullable<AttributeData>(new AttributeData { Key = "world_name", Value = SearchText }),
                ComparisonOp = ComparisonOp.Equal,
            };
            Result r = _byName.SetParameter(ref p);
            Find("by world_name", _byName, r);
        }
        _byId = null;
        if (EOSPrivateLobbyManager.IsValidCode(SearchText))
        {
            _byId = NewSearch(lobbies);
            var o = new LobbySearchSetLobbyIdOptions { LobbyId = SearchText };
            Result r = _byId.SetLobbyId(ref o);
            Find("by lobby ID", _byId, r);
        }
        _resultAt = Time.time + ResultDelay;
    }

    private static LobbySearch NewSearch(LobbyInterface lobbies)
    {
        var o = new CreateLobbySearchOptions { MaxResults = 10 };
        Result r = lobbies.CreateLobbySearch(ref o, out LobbySearch s);
        if (r != Result.Success) Plugin.Logger.LogWarning($"Lobby spike {_run}: CreateLobbySearch {r}.");
        return r == Result.Success ? s : null;
    }

    private static void Find(string what, LobbySearch s, Result setResult)
    {
        Plugin.Logger.LogInfo($"Lobby spike {_run}: search {what} '{SearchText}', set {setResult}, callback {(_callback != null ? "yes" : "no")}.");
        if (_callback == null) return;
        var o = new LobbySearchFindOptions { LocalUserId = EOSManager.Instance.GetProductUserId() };
        Step($"Find {what}");
        s.Find(ref o, null, _callback);
    }

    private static LobbySearchOnFindCallback MakeCallback()
    {
        if (_callbackFailed) return null;
        try
        {
            return DelegateSupport.ConvertDelegate<LobbySearchOnFindCallback>(new FindCallback(OnFind));
        }
        catch (Exception e)
        {
            _callbackFailed = true;
            Plugin.Logger.LogWarning($"Lobby spike: ConvertDelegate failed: {e.Message}");
            return null;
        }
    }

    private static void OnFind(ref LobbySearchFindCallbackInfo info)
    {
        Plugin.Logger.LogInfo($"Lobby spike {_run}: find callback {info.ResultCode} at {Time.time:0.00} s.");
    }

    private static void LogResults()
    {
        try
        {
            LogResult("by world_name", _byName);
            LogResult("by lobby ID", _byId);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Lobby spike {_run} results: {e}"); }
    }

    private static void LogResult(string what, LobbySearch s)
    {
        if (s == null) return;
        var c = new LobbySearchGetSearchResultCountOptions();
        Step($"GetSearchResultCount {what}");
        uint n = s.GetSearchResultCount(ref c);
        Plugin.Logger.LogInfo($"Lobby spike {_run}: {what}: {n} results.");
        for (uint i = 0; i < n; i++)
        {
            var o = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
            Result r = s.CopySearchResultByIndex(ref o, out LobbyDetails d);
            if (r == Result.Success) LogDetails($"result {i}", d);
            else Plugin.Logger.LogInfo($"  result {i}: copy {r}.");
            d?.Release();
        }
        s.Release();
    }

    private static void LogDetails(string what, LobbyDetails d)
    {
        if (d == null) { Plugin.Logger.LogInfo($"  {what}: no details."); return; }
        var io = new LobbyDetailsCopyInfoOptions();
        Step($"{what}: CopyInfo");
        if (d.CopyInfo(ref io, out Il2CppSystem.Nullable<LobbyDetailsInfo> ni) == Result.Success && ni.HasValue)
        {
            LobbyDetailsInfo i = ni.Value;
            Plugin.Logger.LogInfo($"  {what}: id '{(string)i.LobbyId}', permission {i.PermissionLevel}, bucket '{(string)i.BucketId}', join by id {i.AllowJoinById}, presence {i.PresenceEnabled}, slots {i.AvailableSlots}/{i.MaxMembers}.");
        }
        var ac = new LobbyDetailsGetAttributeCountOptions();
        Step($"{what}: GetAttributeCount");
        uint n = d.GetAttributeCount(ref ac);
        for (uint k = 0; k < n; k++)
        {
            var ao = new LobbyDetailsCopyAttributeByIndexOptions { AttrIndex = k };
            Step($"{what}: CopyAttributeByIndex {k}");
            if (d.CopyAttributeByIndex(ref ao, out Il2CppSystem.Nullable<Epic.OnlineServices.Lobby.Attribute> na) != Result.Success || !na.HasValue) continue;
            var a = na.Value;
            if (!a.Data.HasValue) continue;
            AttributeData data = a.Data.Value;
            AttributeDataValue v = data.Value;
            string val = v.ValueType switch
            {
                AttributeType.String => (string)v.AsUtf8,
                AttributeType.Int64 => v.AsInt64.HasValue ? v.AsInt64.Value.ToString() : "?",
                AttributeType.Boolean => v.AsBool.HasValue ? v.AsBool.Value.ToString() : "?",
                _ => v.AsDouble.HasValue ? v.AsDouble.Value.ToString() : "?",
            };
            Plugin.Logger.LogInfo($"    attribute '{(string)data.Key}' = '{val}' ({a.Visibility}).");
        }
    }
}

/// <summary>T3d-1 spike: logs each error menu of the game, for the failed join test.</summary>
[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ShowErrorMenu))]
internal static class LobbySpikeErrorLog
{
    private static void Prefix(ErrorUtils.ErrorType errorType) =>
        Plugin.Logger.LogInfo($"Lobby spike: error menu {errorType} at {Time.time:0.00} s.");
}
