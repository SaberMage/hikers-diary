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
            if (AutoHost && !_autoHostTried) TryAutoHost();
            return;
        }
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
        try
        {
            DiaryStore.Write(_path, save);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Diary: snapshot failed: {e.Message}");
            return;
        }
        Plugin.Logger.LogInfo($"Diary: snapshot ({reason}) of slot {_slotUid}, {save.Bodies.Count} bodies.");
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
        Plugin.Logger.LogInfo($"Auto host: save '{last.slotName}' ({last.filenameUid}), 2 players.");
        NetworkMinder.StartHost();
    }

    /// <summary>Makes a body of the host with a test identifier, so that one computer can test the save and the restore.</summary>
    private static void MakeTest(PlayerCharacter host)
    {
        Corpse c = Corpse.CreateAndSpawn(host, host.registry.corpsePrefab);
        c.identifier = "hikers-diary-test-body";
        Plugin.Logger.LogInfo($"Diary: made test body at {c.transform.position}.");
    }
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
