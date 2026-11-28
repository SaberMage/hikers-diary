using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Epic.OnlineServices.Lobby;
using HarmonyLib;
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

    private const float FirstDelay = 10f, RepeatSeconds = 30f;
    private static float _playAt = -1f, _nextAt;
    private static int _run;
    private static string _menus = "";

    internal static void Step()
    {
        if (string.IsNullOrEmpty(SearchText) && string.IsNullOrEmpty(DiaryHost.AutoJoinCode)) return;
        LogMenus();
        if (string.IsNullOrEmpty(SearchText)) return;
        if (DiaryHost.LocalPlayer() == null) { _playAt = -1f; return; }
        float now = Time.time;
        if (_playAt < 0f) { _playAt = now; _nextAt = now + FirstDelay; return; }
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

    // Runs 1 to 3: the ValueType wrappers of com.Epic.OnlineServices crash the game or give
    // InvalidParameters. Thus the spike calls the EOS C API directly (EosLobbyNative).
    private static void StartSearches()
    {
        IntPtr platform = EOSManager.Instance.GetEOSPlatformInterface().InnerHandle;
        IntPtr user = EOSManager.Instance.GetProductUserId().InnerHandle;
        Plugin.Logger.LogInfo($"  step: searches, platform {platform}, user {user}.");
        IntPtr lobby = EosLobbyNative.EOS_Platform_GetLobbyInterface(platform);
        if (lobby == IntPtr.Zero) { Plugin.Logger.LogWarning($"Lobby spike {_run}: no lobby interface."); return; }
        EosLobbyNative.Search($"{_run} by world_name", lobby, user, SearchText, false);
        if (EOSPrivateLobbyManager.IsValidCode(SearchText))
            EosLobbyNative.Search($"{_run} by lobby ID", lobby, user, SearchText, true);
    }
}

/// <summary>T3d-1 spike: the EOS lobby search through the C API of EOSSDK-Win64-Shipping.dll.
/// The offsets come from the EOS SDK headers and the Cpp2IL dump. All ApiVersion values are 1.</summary>
internal static class EosLobbyNative
{
    private const string Dll = "EOSSDK-Win64-Shipping";
    private const int Success = 0, AtBool = 0, AtInt64 = 1, AtDouble = 2, AtString = 3;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FindCallback(IntPtr info);

    [DllImport(Dll)] internal static extern IntPtr EOS_Platform_GetLobbyInterface(IntPtr platform);
    [DllImport(Dll)] private static extern int EOS_Lobby_CreateLobbySearch(IntPtr lobby, IntPtr options, out IntPtr search);
    [DllImport(Dll)] private static extern int EOS_LobbySearch_SetParameter(IntPtr search, IntPtr options);
    [DllImport(Dll)] private static extern int EOS_LobbySearch_SetLobbyId(IntPtr search, IntPtr options);
    [DllImport(Dll)] private static extern void EOS_LobbySearch_Find(IntPtr search, IntPtr options, IntPtr clientData, FindCallback callback);
    [DllImport(Dll)] private static extern uint EOS_LobbySearch_GetSearchResultCount(IntPtr search, IntPtr options);
    [DllImport(Dll)] private static extern int EOS_LobbySearch_CopySearchResultByIndex(IntPtr search, IntPtr options, out IntPtr details);
    [DllImport(Dll)] private static extern void EOS_LobbySearch_Release(IntPtr search);
    [DllImport(Dll)] private static extern int EOS_LobbyDetails_CopyInfo(IntPtr details, IntPtr options, out IntPtr info);
    [DllImport(Dll)] private static extern void EOS_LobbyDetails_Info_Release(IntPtr info);
    [DllImport(Dll)] private static extern uint EOS_LobbyDetails_GetAttributeCount(IntPtr details, IntPtr options);
    [DllImport(Dll)] private static extern int EOS_LobbyDetails_CopyAttributeByIndex(IntPtr details, IntPtr options, out IntPtr attribute);
    [DllImport(Dll)] private static extern void EOS_Lobby_Attribute_Release(IntPtr attribute);
    [DllImport(Dll)] private static extern void EOS_LobbyDetails_Release(IntPtr details);

    // Keep a reference, so that the GC does not collect the delegate while EOS holds the pointer.
    private static readonly FindCallback Callback = OnFind;
    private static readonly Dictionary<long, (IntPtr search, string what)> Pending = new();
    private static long _nextId;

    private static void Log(string s) => Plugin.Logger.LogInfo(s);

    /// <summary>A zeroed native buffer with ApiVersion 1 at offset 0. Free it with FreeHGlobal.</summary>
    private static IntPtr Options(int size)
    {
        IntPtr p = Marshal.AllocHGlobal(size);
        for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
        Marshal.WriteInt32(p, 0, 1);
        return p;
    }

    internal static void Search(string what, IntPtr lobby, IntPtr user, string text, bool byId)
    {
        IntPtr o = Options(8);
        Marshal.WriteInt32(o, 4, 10); // MaxResults
        Log($"  step: CreateLobbySearch {what}.");
        int r = EOS_Lobby_CreateLobbySearch(lobby, o, out IntPtr search);
        Marshal.FreeHGlobal(o);
        if (r != Success) { Log($"Lobby spike {what}: CreateLobbySearch result {r}."); return; }

        IntPtr str = Marshal.StringToCoTaskMemUTF8(text), key = IntPtr.Zero, data = IntPtr.Zero;
        if (byId)
        {
            o = Options(16);
            Marshal.WriteIntPtr(o, 8, str); // LobbyId
            Log("  step: SetLobbyId.");
            r = EOS_LobbySearch_SetLobbyId(search, o);
        }
        else
        {
            key = Marshal.StringToCoTaskMemUTF8("world_name");
            data = Options(32);                 // EOS_Lobby_AttributeData
            Marshal.WriteIntPtr(data, 8, key);  // Key
            Marshal.WriteIntPtr(data, 16, str); // Value.AsUtf8
            Marshal.WriteInt32(data, 24, AtString);
            o = Options(24);                    // EOS_LobbySearch_SetParameterOptions
            Marshal.WriteIntPtr(o, 8, data);    // Parameter
            Marshal.WriteInt32(o, 16, 0);       // EOS_CO_EQUAL
            Log("  step: SetParameter.");
            r = EOS_LobbySearch_SetParameter(search, o);
        }
        Marshal.FreeHGlobal(o);
        if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
        if (key != IntPtr.Zero) Marshal.FreeCoTaskMem(key);
        Marshal.FreeCoTaskMem(str);
        Log($"Lobby spike {what}: search '{text}', set result {r}.");
        if (r != Success) { EOS_LobbySearch_Release(search); return; }

        long id = ++_nextId;
        Pending[id] = (search, what);
        o = Options(16);
        Marshal.WriteIntPtr(o, 8, user); // LocalUserId
        Log($"  step: Find {what}.");
        EOS_LobbySearch_Find(search, o, new IntPtr(id), Callback);
        Marshal.FreeHGlobal(o);
    }

    // EOS_LobbySearch_FindCallbackInfo: ResultCode at 0, ClientData at 8. EOS calls it in EOS_Platform_Tick.
    private static void OnFind(IntPtr info)
    {
        try
        {
            int code = Marshal.ReadInt32(info, 0);
            long id = Marshal.ReadIntPtr(info, 8).ToInt64();
            if (!Pending.Remove(id, out var p)) { Log($"Lobby spike: find callback {code} for unknown search {id}."); return; }
            IntPtr o = Options(8);
            uint n = code == Success ? EOS_LobbySearch_GetSearchResultCount(p.search, o) : 0;
            Marshal.FreeHGlobal(o);
            Log($"Lobby spike {p.what}: find result {code}, {n} results, at {Time.time:0.00} s.");
            for (uint i = 0; i < n; i++)
            {
                o = Options(8);
                Marshal.WriteInt32(o, 4, (int)i); // LobbyIndex
                int r = EOS_LobbySearch_CopySearchResultByIndex(p.search, o, out IntPtr d);
                Marshal.FreeHGlobal(o);
                if (r != Success) { Log($"  result {i}: copy result {r}."); continue; }
                LogDetails($"result {i}", d);
                EOS_LobbyDetails_Release(d);
            }
            EOS_LobbySearch_Release(p.search);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Lobby spike find callback: {e}"); }
    }

    private static string Utf8(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p);

    // EOS_LobbyDetails_Info: LobbyId 8, owner 16, PermissionLevel 24, AvailableSlots 28, MaxMembers 32,
    // bAllowInvites 36, BucketId 40, bAllowHostMigration 48, bRTCRoomEnabled 52, bAllowJoinById 56,
    // bRejoinAfterKickRequiresInvite 60, bPresenceEnabled 64.
    private static void LogDetails(string what, IntPtr d)
    {
        IntPtr o = Options(8);
        int r = EOS_LobbyDetails_CopyInfo(d, o, out IntPtr info);
        Marshal.FreeHGlobal(o);
        if (r == Success)
        {
            Log($"  {what}: api {Marshal.ReadInt32(info, 0)}, id '{Utf8(Marshal.ReadIntPtr(info, 8))}', permission {Marshal.ReadInt32(info, 24)}, " +
                $"slots {Marshal.ReadInt32(info, 28)}/{Marshal.ReadInt32(info, 32)}, bucket '{Utf8(Marshal.ReadIntPtr(info, 40))}', " +
                $"join by id {Marshal.ReadInt32(info, 56)}, presence {Marshal.ReadInt32(info, 64)}.");
            EOS_LobbyDetails_Info_Release(info);
        }
        else Log($"  {what}: CopyInfo result {r}.");

        o = Options(8);
        uint n = EOS_LobbyDetails_GetAttributeCount(d, o);
        Marshal.FreeHGlobal(o);
        for (uint k = 0; k < n; k++)
        {
            o = Options(8);
            Marshal.WriteInt32(o, 4, (int)k); // AttrIndex
            r = EOS_LobbyDetails_CopyAttributeByIndex(d, o, out IntPtr a);
            Marshal.FreeHGlobal(o);
            if (r != Success) { Log($"    attribute {k}: copy result {r}."); continue; }
            // EOS_Lobby_Attribute: Data 8, Visibility 16. EOS_Lobby_AttributeData: Key 8, Value 16, ValueType 24.
            IntPtr data = Marshal.ReadIntPtr(a, 8);
            if (data != IntPtr.Zero)
            {
                int type = Marshal.ReadInt32(data, 24);
                string val = type switch
                {
                    AtString => Utf8(Marshal.ReadIntPtr(data, 16)),
                    AtInt64 => Marshal.ReadInt64(data, 16).ToString(),
                    AtBool => (Marshal.ReadInt32(data, 16) != 0).ToString(),
                    AtDouble => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, 16)).ToString(),
                    _ => $"type {type}",
                };
                Log($"    attribute '{Utf8(Marshal.ReadIntPtr(data, 8))}' = '{val}' (visibility {Marshal.ReadInt32(a, 16)}).");
            }
            EOS_Lobby_Attribute_Release(a);
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
