using HikersDiary.Core;
using Mirror;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HikersDiary;

/// <summary>
/// Spike for #1: on the host, save each body (game class <c>Corpse</c>) to a file, and spawn the
/// saved bodies again after a game load. Only the host does this work.
/// </summary>
public sealed class BodySpike : MonoBehaviour
{
    public BodySpike(IntPtr ptr) : base(ptr) { }

    internal static string StorePath;
    internal static bool MakeTestBody;

    private const float SnapshotSeconds = 10f;
    private float _nextSnapshot;
    private bool _restored;
    private bool _testBodyMade;

    private void Update()
    {
        if (!NetworkServer.active)
        {
            _restored = false;
            _testBodyMade = false;
            return;
        }
        PlayerCharacter host = LocalPlayer();
        if (host == null) return;

        if (!_restored)
        {
            _restored = true;
            Restore(host);
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

    // [impl->REQ-BODY-SPIKE]
    private static void Snapshot()
    {
        var corpses = Corpse.allCorpses;
        var records = new List<BodyRecord>();
        if (corpses != null)
        {
            for (int i = 0; i < corpses.Count; i++)
            {
                Corpse c = corpses[i];
                if (c == null || string.IsNullOrEmpty(c.identifier)) continue;
                Transform t = c.transform;
                Vector3 p = t.position;
                Quaternion r = t.rotation;
                records.Add(new BodyRecord(c.identifier, new[] { p.x, p.y, p.z }, new[] { r.x, r.y, r.z, r.w },
                    c.headColorIndex, c.torsoColorIndex, c.legsColorIndex));
            }
        }
        try
        {
            BodyStore.Write(StorePath, records);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Body spike: snapshot failed: {e.Message}");
            return;
        }
        Plugin.Logger.LogInfo($"Body spike: snapshot of {records.Count} bodies.");
    }

    // [impl->REQ-BODY-SPIKE]
    private static void Restore(PlayerCharacter host)
    {
        List<BodyRecord> records;
        try
        {
            records = BodyStore.Read(StorePath);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Body spike: cannot read {StorePath}: {e.Message}");
            return;
        }
        GameObject prefab = host.registry.corpsePrefab;
        foreach (BodyRecord b in records)
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
            Plugin.Logger.LogInfo($"Body spike: restored body {b.Identifier} at {pos}.");
        }
    }

    /// <summary>Makes a body of the host with a test identifier, so that one computer can test the save and the restore.</summary>
    private static void MakeTest(PlayerCharacter host)
    {
        Corpse c = Corpse.CreateAndSpawn(host, host.registry.corpsePrefab);
        c.identifier = "hikers-diary-test-body";
        Plugin.Logger.LogInfo($"Body spike: made test body at {c.transform.position}.");
    }
}
