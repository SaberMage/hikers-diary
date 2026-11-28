using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace HikersDiary;

/// <summary>
/// The EOS lobby search by lobby ID (#14, T3d), through the C API of EOSSDK-Win64-Shipping.dll. The ValueType wrappers
/// of com.Epic.OnlineServices.dll crash the game (T3d-1). The offsets come from the EOS SDK headers and the Cpp2IL
/// dump. All ApiVersion values are 1.
/// </summary>
// [impl->REQ-FRIEND-JOIN]
internal static class EosLobby
{
    private const string Dll = "EOSSDK-Win64-Shipping";
    private const int Success = 0, AtString = 3;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FindCallback(IntPtr info);

    [DllImport(Dll)] private static extern IntPtr EOS_Platform_GetLobbyInterface(IntPtr platform);
    [DllImport(Dll)] private static extern int EOS_Lobby_CreateLobbySearch(IntPtr lobby, IntPtr options, out IntPtr search);
    [DllImport(Dll)] private static extern int EOS_LobbySearch_SetLobbyId(IntPtr search, IntPtr options);
    [DllImport(Dll)] private static extern void EOS_LobbySearch_Find(IntPtr search, IntPtr options, IntPtr clientData, FindCallback callback);
    [DllImport(Dll)] private static extern uint EOS_LobbySearch_GetSearchResultCount(IntPtr search, IntPtr options);
    [DllImport(Dll)] private static extern int EOS_LobbySearch_CopySearchResultByIndex(IntPtr search, IntPtr options, out IntPtr details);
    [DllImport(Dll)] private static extern void EOS_LobbySearch_Release(IntPtr search);
    [DllImport(Dll)] private static extern uint EOS_LobbyDetails_GetAttributeCount(IntPtr details, IntPtr options);
    [DllImport(Dll)] private static extern int EOS_LobbyDetails_CopyAttributeByIndex(IntPtr details, IntPtr options, out IntPtr attribute);
    [DllImport(Dll)] private static extern void EOS_Lobby_Attribute_Release(IntPtr attribute);
    [DllImport(Dll)] private static extern void EOS_LobbyDetails_Release(IntPtr details);

    // Keep a reference, so that the GC does not collect the delegate while EOS holds the pointer.
    private static readonly FindCallback Callback = OnFind;
    private static readonly Dictionary<long, (IntPtr search, Action<bool, string> done)> Pending = new();
    private static long _nextId;

    /// <summary>A zeroed native buffer with ApiVersion 1 at offset 0. Free it with FreeHGlobal.</summary>
    private static IntPtr Options(int size)
    {
        IntPtr p = Marshal.AllocHGlobal(size);
        for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
        Marshal.WriteInt32(p, 0, 1);
        return p;
    }

    /// <summary>
    /// Search the lobby with this ID. EOS calls <paramref name="done"/> later in a frame, with "found" and the
    /// <c>WORLD_NAME</c> attribute (the save title). If the search cannot start, the method returns false.
    /// </summary>
    internal static bool FindById(string code, Action<bool, string> done)
    {
        try
        {
            IntPtr platform = EOSManager.Instance.GetEOSPlatformInterface().InnerHandle;
            IntPtr user = EOSManager.Instance.GetProductUserId().InnerHandle;
            IntPtr lobby = EOS_Platform_GetLobbyInterface(platform);
            if (lobby == IntPtr.Zero || user == IntPtr.Zero) return Fail("no lobby interface or no user");

            IntPtr o = Options(8);
            Marshal.WriteInt32(o, 4, 1); // MaxResults
            int r = EOS_Lobby_CreateLobbySearch(lobby, o, out IntPtr search);
            Marshal.FreeHGlobal(o);
            if (r != Success) return Fail($"CreateLobbySearch result {r}");

            IntPtr str = Marshal.StringToCoTaskMemUTF8(code);
            o = Options(16);
            Marshal.WriteIntPtr(o, 8, str); // LobbyId
            r = EOS_LobbySearch_SetLobbyId(search, o);
            Marshal.FreeHGlobal(o);
            Marshal.FreeCoTaskMem(str);
            if (r != Success) { EOS_LobbySearch_Release(search); return Fail($"SetLobbyId result {r}"); }

            long id = ++_nextId;
            Pending[id] = (search, done);
            o = Options(16);
            Marshal.WriteIntPtr(o, 8, user); // LocalUserId
            EOS_LobbySearch_Find(search, o, new IntPtr(id), Callback);
            Marshal.FreeHGlobal(o);
            return true;
        }
        catch (Exception e) { return Fail(e.Message); }
    }

    private static bool Fail(string why)
    {
        Plugin.Logger.LogWarning($"Diary: lobby search did not start: {why}.");
        return false;
    }

    // EOS_LobbySearch_FindCallbackInfo: ResultCode at 0, ClientData at 8. EOS calls it in EOS_Platform_Tick.
    private static void OnFind(IntPtr info)
    {
        try
        {
            int code = Marshal.ReadInt32(info, 0);
            long id = Marshal.ReadIntPtr(info, 8).ToInt64();
            if (!Pending.Remove(id, out var p)) return;
            IntPtr o = Options(8);
            uint n = code == Success ? EOS_LobbySearch_GetSearchResultCount(p.search, o) : 0;
            Marshal.FreeHGlobal(o);
            string title = "";
            if (n > 0)
            {
                o = Options(8); // LobbyIndex 0
                if (EOS_LobbySearch_CopySearchResultByIndex(p.search, o, out IntPtr d) == Success)
                {
                    title = WorldName(d);
                    EOS_LobbyDetails_Release(d);
                }
                Marshal.FreeHGlobal(o);
            }
            EOS_LobbySearch_Release(p.search);
            Plugin.Logger.LogInfo($"Diary: lobby search result {code}, {n} lobbies, title '{title}', at {Time.time:0.00} s.");
            p.done(n > 0, title);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Diary: lobby search callback: {e}"); }
    }

    // EOS_Lobby_Attribute: Data at 8. EOS_Lobby_AttributeData: Key 8, Value 16, ValueType 24. EOS gives the keys in upper case.
    private static string WorldName(IntPtr d)
    {
        IntPtr o = Options(8);
        uint n = EOS_LobbyDetails_GetAttributeCount(d, o);
        Marshal.FreeHGlobal(o);
        for (uint k = 0; k < n; k++)
        {
            o = Options(8);
            Marshal.WriteInt32(o, 4, (int)k); // AttrIndex
            int r = EOS_LobbyDetails_CopyAttributeByIndex(d, o, out IntPtr a);
            Marshal.FreeHGlobal(o);
            if (r != Success) continue;
            IntPtr data = Marshal.ReadIntPtr(a, 8);
            string value = null;
            if (data != IntPtr.Zero && Marshal.ReadInt32(data, 24) == AtString &&
                string.Equals(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data, 8)), "world_name", StringComparison.OrdinalIgnoreCase))
                value = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data, 16)) ?? "";
            EOS_Lobby_Attribute_Release(a);
            if (value != null) return value;
        }
        return "";
    }
}
