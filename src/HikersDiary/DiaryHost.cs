using HarmonyLib;
using HikersDiary.Core;
using Mirror;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HikersDiary;

/// <summary>
/// Host side of the Diary save (#2). On the host, it saves a snapshot of the state that the game
/// save does not keep, one Diary file for each game slot. After a game load, it restores that state.
/// Only the host does this work.
/// </summary>
public sealed class DiaryHost : MonoBehaviour
{
    public DiaryHost(IntPtr ptr) : base(ptr) { }

    internal static string StoreDir;
    internal static bool MakeTestBody;
    internal static bool AutoHost;
    internal static string AutoJoinCode;
    internal static string AutoJoinPassword;

    private const float SnapshotSeconds = 10f;
    private float _nextSnapshot;
    private bool _testBodyMade;

    // The slot that is loaded now. Null when no slot is loaded.
    private static string _slotUid;
    private static string _path;
    // False if the Diary file of this slot cannot be read. Then we never overwrite it.
    private static bool _writable;

    private static bool _autoHostTried;
    private float _titleSeenAt = -1f;

    private void Update()
    {
        if (!NetworkServer.active)
        {
            _slotUid = null;
            _testBodyMade = false;
            if (!string.IsNullOrEmpty(AutoJoinCode)) TryAutoJoin();
            else if (AutoHost && !_autoHostTried) TryAutoHost();
            return;
        }
        LogJoins();
        PlayerCharacter host = LocalPlayer();
        SaveData slot = SaveManager.instance?.currentData;
        if (host == null || slot == null || string.IsNullOrEmpty(slot.filenameUid)) return;

        if (slot.filenameUid != _slotUid)
        {
            Load(slot.filenameUid, host);
            _nextSnapshot = Time.time + SnapshotSeconds;
        }

        if (MakeTestBody && !_testBodyMade)
        {
            _testBodyMade = true;
            MakeTest(host);
        }

        if (Time.time >= _nextSnapshot)
        {
            _nextSnapshot = Time.time + SnapshotSeconds;
            Snapshot();
        }
    }

    // Remote players that the log has shown. The log shows where each one spawned (T1 rejoin check).
    private static readonly HashSet<int> _seenPlayers = new();

    private static void LogJoins()
    {
        var all = PlayerCharacter.allPlayerCharacters;
        for (int i = 0; all != null && i < all.Count; i++)
        {
            PlayerCharacter pc = all[i];
            if (pc == null || pc.isLocalPlayer || !_seenPlayers.Add(pc.GetInstanceID())) continue;
            Plugin.Logger.LogInfo($"Diary: player {pc.playerNetworking?.identifier} spawned at {pc.transform.position}.");
        }
    }

    private static PlayerCharacter LocalPlayer()
    {
        var all = PlayerCharacter.allPlayerCharacters;
        if (all == null) return null;
        for (int i = 0; i < all.Count; i++)
        {
            PlayerCharacter pc = all[i];
            if (pc != null && pc.isLocalPlayer) return pc;
        }
        return null;
    }

    // [impl->REQ-DIARY-SAVE]
    private static void Load(string slotUid, PlayerCharacter host)
    {
        _slotUid = slotUid;
        _writable = false;
        DiarySave save;
        try
        {
            _path = DiaryStore.PathFor(StoreDir, slotUid);
            save = DiaryStore.Read(_path);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Diary: slot {slotUid} not saved or restored: {e.Message}");
            return;
        }
        _writable = true;
        if (save == null)
        {
            Plugin.Logger.LogInfo($"Diary: slot {slotUid} has no Diary file yet.");
            return;
        }
        RestoreBodies(save.Bodies, host);
        RestoreProps(save.Props);
    }

    /// <summary>Writes the Diary file of the loaded slot. Also called when the host stops.</summary>
    // [impl->REQ-DIARY-SAVE]
    internal static void Snapshot(string reason = "interval")
    {
        if (!NetworkServer.active || _slotUid == null || !_writable) return;
        var save = DiarySave.Empty(_slotUid);
        // [impl->REQ-BODY-SPIKE]
        var corpses = Corpse.allCorpses;
        for (int i = 0; corpses != null && i < corpses.Count; i++)
        {
            Corpse c = corpses[i];
            if (c == null || string.IsNullOrEmpty(c.identifier)) continue;
            Transform t = c.transform;
            Vector3 p = t.position;
            Quaternion r = t.rotation;
            save.Bodies.Add(new BodyRecord(c.identifier, new[] { p.x, p.y, p.z }, new[] { r.x, r.y, r.z, r.w },
                c.headColorIndex, c.torsoColorIndex, c.legsColorIndex));
        }
        SaveProps(save.Props);
        try
        {
            DiaryStore.Write(_path, save);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Diary: snapshot failed: {e.Message}");
            return;
        }
        Plugin.Logger.LogInfo($"Diary: snapshot ({reason}) of slot {_slotUid}, {save.Bodies.Count} bodies, {save.Props.Count} props.");
    }

    // This set holds the props on a moving platform that the log showed. The log shows each prop one time.
    private static readonly HashSet<int> _platformPropsLogged = new();

    // A prop is loose when no home (hand, pocket, slot) holds it and no player holds it.
    private static bool IsLoose(Prop p) => p.currentHome == null && p.exclusiveHolder == null;

    // [impl->REQ-PROP-POSE]
    private static void SaveProps(List<PropRecord> into)
    {
        var props = Prop.allProps;
        for (int i = 0; props != null && i < props.Count; i++)
        {
            Prop p = props[i];
            if (p == null || !IsLoose(p)) continue;
            // Out of scope for #3: a pose relative to a moving platform. The prop stays at its start position.
            if (p.houseNetworkTransform != null && p.houseNetworkTransform.stickyPlatform != null)
            {
                if (_platformPropsLogged.Add(p.GetInstanceID()))
                    Plugin.Logger.LogInfo($"Diary: prop {PathOf(p.transform)} is on a moving platform. Not saved.");
                continue;
            }
            Transform t = p.transform;
            Vector3 pos = t.position;
            Quaternion r = t.rotation;
            into.Add(new PropRecord(p.savablePropGuid ?? "", p.networkIdentity != null ? p.networkIdentity.sceneId : 0,
                PathOf(t), new[] { pos.x, pos.y, pos.z }, new[] { r.x, r.y, r.z, r.w }));
        }
    }

    // [impl->REQ-PROP-POSE]
    private static void RestoreProps(List<PropRecord> records)
    {
        if (records.Count == 0) return;
        var index = new PropIndex(records);
        var byKey = new Dictionary<string, int> { ["guid"] = 0, ["scene"] = 0, ["path"] = 0 };
        int pathDiffers = 0, unpinned = 0;
        var props = Prop.allProps;
        for (int i = 0; props != null && i < props.Count; i++)
        {
            Prop p = props[i];
            if (p == null || p.exclusiveHolder != null) continue;
            string path = PathOf(p.transform);
            var hit = index.Find(p.savablePropGuid, p.networkIdentity != null ? p.networkIdentity.sceneId : 0, path);
            if (hit == null) continue;
            (PropRecord rec, string key) = hit.Value;
            byKey[key]++;
            // Open question 2 (is sceneId stable?): count the scene matches that have a different path.
            if (key == "scene" && rec.Path != path) pathDiffers++;
            // ponytail: if the game wrote its save after our last snapshot (10 s window), the game save wins.
            // The game put a loose saveable prop in its start home. Remove it from that home first.
            if (p.currentHome != null) { p.ServerSetUnpinned(); unpinned++; }
            var pos = new Vector3(rec.Position[0], rec.Position[1], rec.Position[2]);
            var rot = new Quaternion(rec.Rotation[0], rec.Rotation[1], rec.Rotation[2], rec.Rotation[3]);
            // The server owns the prop transform. HouseNetworkTransform sends it to the clients.
            p.transform.SetPositionAndRotation(pos, rot);
            if (p.rb != null)
            {
                p.rb.position = pos;
                p.rb.rotation = rot;
                if (p.rb.isKinematic) continue;
                p.rb.linearVelocity = Vector3.zero;
                p.rb.angularVelocity = Vector3.zero;
            }
        }
        int found = byKey["guid"] + byKey["scene"] + byKey["path"];
        Plugin.Logger.LogInfo($"Diary: restored {found} of {records.Count} props (guid {byKey["guid"]}, scene {byKey["scene"]}, " +
            $"path {byKey["path"]}; scene match with other path {pathDiffers}; taken from start home {unpinned}).");
    }

    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    // [impl->REQ-BODY-SPIKE]
    private static void RestoreBodies(List<BodyRecord> bodies, PlayerCharacter host)
    {
        GameObject prefab = host.registry.corpsePrefab;
        foreach (BodyRecord b in bodies)
        {
            if (Corpse.FindMatch(b.Identifier) != null) continue;
            var pos = new Vector3(b.Position[0], b.Position[1], b.Position[2]);
            var rot = new Quaternion(b.Rotation[0], b.Rotation[1], b.Rotation[2], b.Rotation[3]);
            GameObject go = Object.Instantiate(prefab, pos, rot);
            Corpse c = go.GetComponent<Corpse>();
            c.identifier = b.Identifier;
            c.NetworkheadColorIndex = b.HeadColor;
            c.NetworktorsoColorIndex = b.TorsoColor;
            c.NetworklegsColorIndex = b.LegsColor;
            NetworkServer.Spawn(go, (NetworkConnection)null);
            Plugin.Logger.LogInfo($"Diary: restored body {b.Identifier} at {pos}.");
        }
    }

    /// <summary>
    /// Test aid: when the title menu shows, host the save that was played last, for 2 players,
    /// with the same steps as HostMenuConfirm.ActionStart and PlayerCountMenu.ActionProceed.
    /// </summary>
    private void TryAutoHost()
    {
        // The first menu is MicCheckMenu (welcome and microphone check), then TitleMenu.
        if (Object.FindObjectOfType<TitleMenu>() == null && Object.FindObjectOfType<MicCheckMenu>() == null)
        {
            _titleSeenAt = -1f;
            return;
        }
        if (_titleSeenAt < 0f) { _titleSeenAt = Time.time; return; }
        if (Time.time - _titleSeenAt < 3f) return;
        _autoHostTried = true;

        var saves = SaveManager.GetAllSaveDatasInFolder();
        SaveData last = null;
        for (int i = 0; saves != null && i < saves.Count; i++)
            if (last == null || saves[i].lastPlayedTimeAsLong > last.lastPlayedTimeAsLong) last = saves[i];
        if (last == null)
        {
            Plugin.Logger.LogWarning("Auto host: no save in the folder. Use the menu.");
            return;
        }
        BaseManager.instance.saveManager.currentData = last;
        SaveManager.WriteCurrentSaveData();
        NetworkMinder.SetServerPassword(last.password);
        PlayerCountSwapper.playerCount = PlayerCount.PlayerCount2;
        // The host menu (MagicCode) makes the join code. The EOS lobby needs it.
        EOSPrivateLobbyManager.Instance.GenerateGameCode();
        Plugin.Logger.LogInfo($"Auto host: save '{last.slotName}' ({last.filenameUid}), 2 players, join code {EOSPrivateLobbyManager.Instance.CurrentLobbyCode}.");
        NetworkMinder.StartHost();
    }

    private bool _joinSent;
    private bool _passwordSent;

    /// <summary>
    /// Test aid for a second client: at the first menu, connect with the join code through
    /// JoinMenu.ConnectTo, the call of the join menu. If the password menu shows, type the password.
    /// </summary>
    private void TryAutoJoin()
    {
        if (NetworkClient.active)
        {
            PasswordMenu pm = _passwordSent ? null : Object.FindObjectOfType<PasswordMenu>();
            if (pm != null)
            {
                _passwordSent = true;
                pm.passwordField.text = AutoJoinPassword;
                pm.SubmitPassword();
                Plugin.Logger.LogInfo("Auto join: password sent.");
            }
            return;
        }
        if (_joinSent) return;
        // The first menu is MicCheckMenu (welcome and microphone check), then TitleMenu.
        if (Object.FindObjectOfType<TitleMenu>() == null && Object.FindObjectOfType<MicCheckMenu>() == null)
        {
            _titleSeenAt = -1f;
            return;
        }
        if (_titleSeenAt < 0f) { _titleSeenAt = Time.time; return; }
        if (Time.time - _titleSeenAt < 3f) return;
        // The join menu is not active yet. Find it with the inactive objects.
        var menus = Resources.FindObjectsOfTypeAll<JoinMenu>();
        if (menus == null || menus.Length == 0) return;
        _joinSent = true;
        NetworkMinder.SetServerPassword(AutoJoinPassword);
        Plugin.Logger.LogInfo($"Auto join: code {AutoJoinCode}.");
        menus[0].ConnectTo(AutoJoinCode);
    }

    /// <summary>Makes a body of the host with a test identifier, so that one computer can test the save and the restore.</summary>
    private static void MakeTest(PlayerCharacter host)
    {
        Corpse c = Corpse.CreateAndSpawn(host, host.registry.corpsePrefab);
        c.identifier = "hikers-diary-test-body";
        Plugin.Logger.LogInfo($"Diary: made test body at {c.transform.position}.");
    }
}

/// <summary>T1 check: logs each body lookup of the game rejoin path and its result.</summary>
[HarmonyPatch(typeof(Corpse), nameof(Corpse.FindMatch))]
internal static class BodyMatchLog
{
    [HarmonyPostfix]
    private static void After(string identifier, Corpse __result) =>
        Plugin.Logger.LogInfo($"Diary: Corpse.FindMatch({identifier}) -> {(__result == null ? "none" : "body at " + __result.transform.position)}.");
}

/// <summary>Saves a last snapshot before the host stops, while the bodies still exist.</summary>
[HarmonyPatch(typeof(HouseNetworkManager))]
internal static class SessionStopPatches
{
    // [impl->REQ-DIARY-SAVE]
    [HarmonyPrefix, HarmonyPatch(nameof(HouseNetworkManager.OnStopHost))]
    private static void BeforeStopHost() => DiaryHost.Snapshot("host stop");

    [HarmonyPrefix, HarmonyPatch(nameof(HouseNetworkManager.OnApplicationQuit))]
    private static void BeforeQuit() => DiaryHost.Snapshot("quit");
}
