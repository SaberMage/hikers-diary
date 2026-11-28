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
    internal static bool FlipLookoutLight;
    internal static string PropOnPlatform;
    internal static bool StartChairlift;
    internal static bool CullTrip;
    internal static bool StartTrain;
    private static bool _trainStarted;
    private static bool _chairliftStarted;
    private static bool _propDropped;
    private static bool _movedToPole;
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
        GameStart.Step();
        SaveSwitch.Step();
        LobbySpike.Step();
        if (!NetworkServer.active)
        {
            _slotUid = null;
            _testBodyMade = false;
            if (!string.IsNullOrEmpty(AutoJoinCode)) TryAutoJoin();
            else if (AutoHost && !_autoHostTried) TryAutoHost();
            return;
        }
        LogJoins();
        EquipStep();
        BodyToCarStep();
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

        if (_platformPending.Count > 0 && Time.fixedTime >= _platformAt) RestorePlatformProps();
        if (!string.IsNullOrEmpty(PropOnPlatform) && !_propDropped && Time.fixedTime >= _platformAt + 8f) DropPropOnPlatform();
        if (_dropped != null && Time.time >= _dropCheckAt) CheckDrop();
        CarHoldPatch.Release();
        CullTripStep();
        if (Scenario) { ScenarioStep(); TrackStep(); }
        if (WaterProbe) WaterProbeStep(host);

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
            if (EquipClients) _equip.Add((pc, Time.time + 5f));
        }
    }

    // Test aid for T7: a player cannot fill its own pockets. The host gives each new client a peg tile in the
    // hand, the backpack and the holster, with the server calls of the T5 restore.
    internal static bool EquipClients;
    private static readonly List<(PlayerCharacter Pc, float At)> _equip = new();

    private static void StartAids()
    {
        if (StartChairlift && !_chairliftStarted)
        {
            // Test aid: start the chairlift before the platform props, as a save with the key in its slot does.
            _chairliftStarted = true;
            PeckDevHelper.Trigger(new PeckDevHelper.UnlockRules { chairlift = true });
            Plugin.Logger.LogInfo("Diary: test aid: chairlift started (PeckDevHelper).");
        }
        if (StartTrain && !_trainStarted)
        {
            _trainStarted = true;
            PeckDevHelper.Trigger(new PeckDevHelper.UnlockRules { train = true });
            Plugin.Logger.LogInfo("Diary: test aid: monorail started (PeckDevHelper).");
        }
    }

    // Test aid for T7: the host moves the body of a client who left onto a monorail car (the car with this path text).
    internal static string BodyToCar;
    private static readonly HashSet<int> _bodiesMoved = new();
    private static (Corpse Body, TrainCar Car, float At)? _bodyMove;

    private static void BodyToCarStep()
    {
        if (string.IsNullOrEmpty(BodyToCar)) return;
        if (_bodyMove == null)
        {
            var corpses = Corpse.allCorpses;
            for (int i = 0; corpses != null && i < corpses.Count; i++)
            {
                Corpse c = corpses[i];
                if (c == null || c.prop == null || !_bodiesMoved.Add(c.GetInstanceID())) continue;
                TrainCar car = Object.FindObjectsOfType<TrainCar>().FirstOrDefault(t => PathOf(t.transform).Contains(BodyToCar));
                if (car == null) { Plugin.Logger.LogWarning($"Diary: test aid: no car '{BodyToCar}'."); return; }
                CarHoldPatch.Hold(car, "test aid: a body goes onto it");
                _bodyMove = (c, car, Time.time + 2f);
                return;
            }
            return;
        }
        (Corpse body, TrainCar target, float when) = _bodyMove.Value;
        if (Time.time < when) return;
        _bodyMove = null;
        if (body == null || target.cullState) { Plugin.Logger.LogWarning("Diary: test aid: the body or the car is not ready."); return; }
        Transform frame = target.platformingBody.rb.transform;
        Collider col = null;
        foreach (Collider c in frame.GetComponentsInChildren<Collider>())
            if (!c.isTrigger && (col == null || c.bounds.size.sqrMagnitude > col.bounds.size.sqrMagnitude)) col = c;
        Vector3 at = col != null ? new Vector3(col.bounds.center.x, col.bounds.max.y + 0.5f, col.bounds.center.z) : frame.position + Vector3.up;
        body.prop.transform.position = at;
        if (body.prop.rb != null) { body.prop.rb.position = at; body.prop.rb.linearVelocity = target.platformingBody.rb.GetPointVelocity(at); }
        Plugin.Logger.LogInfo($"Diary: test aid: moved body {body.identifier} onto {target.name} at {at}.");
    }

    private static void EquipStep()
    {
        for (int i = _equip.Count - 1; i >= 0; i--)
        {
            (PlayerCharacter pc, float at) = _equip[i];
            if (Time.time < at) continue;
            _equip.RemoveAt(i);
            if (pc == null) continue;
            var done = new List<string>();
            foreach ((string slot, int n) in new[] { (PlayerRecord.Hand, 2), ("backpack", 3), ("holster", 4) })
            {
                Prop p = RackProp(n);
                if (p == null || !IsLoose(p)) continue;
                if (slot == PlayerRecord.Hand) pc.playerNetworking.ServerPickUpPropAutomatic(p);
                else p.ServerSetPinned(Pocket(pc, slot));
                done.Add($"{slot}: {PathOf(p.transform)}");
            }
            Plugin.Logger.LogInfo($"Diary: test aid: equipped client {pc.playerNetworking?.identifier} with [{string.Join("; ", done)}].");
        }
    }

    internal static PlayerCharacter LocalPlayer()
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
        LogWaterMap();
        if (save == null) StartAids();
        if (save == null)
        {
            Plugin.Logger.LogInfo($"Diary: slot {slotUid} has no Diary file yet.");
            return;
        }
        // The props come first, so that RestoreProps does not move an item that a body holds.
        RestoreProps(save.Props);
        RestoreBodies(save.Bodies, host);
        RestoreVehicles(save.Vehicles);
        // Test aids: start the lifts after the vehicle restore, which sets the saved speed.
        StartAids();
        // [impl->REQ-PLATFORM-PROPS] The vehicles move to their saved state in the next physics steps.
        // Then Update moves the props on the platforms (RestorePlatformProps).
        _platformAt = Time.fixedTime + 2f * Time.fixedDeltaTime;
        _platformDeadline = Time.fixedTime + 30f;
        _platformFound = _platformMissed = 0;
        RestoreTime(save.EnviroTicks);
        RestoreHost(save.Players, host);
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
            // [impl->REQ-PLATFORM-PROPS] A body on a moving platform keeps its pose relative to the platform.
            StickyPlatform sp = c.prop != null && c.prop.houseNetworkTransform != null ? c.prop.houseNetworkTransform.stickyPlatform : null;
            (string platform, float[] p, float[] r) = PoseOn(sp, sp != null ? c.prop.transform : c.transform);
            save.Bodies.Add(new BodyRecord(c.identifier, p, r, c.headColorIndex, c.torsoColorIndex, c.legsColorIndex, BodyItems(c), platform));
        }
        SaveProps(save.Props);
        LogSwitchStates();
        // T7 check: where each client and each body is. A body has a root transform and a prop (the ragdoll).
        var all = PlayerCharacter.allPlayerCharacters;
        for (int i = 0; all != null && i < all.Count; i++)
            if (all[i] != null && !all[i].isLocalPlayer)
                Plugin.Logger.LogInfo($"Diary: client {all[i].playerNetworking?.identifier} at {all[i].transform.position}, yaw {all[i].transform.eulerAngles.y:0.0}.");
        for (int i = 0; corpses != null && i < corpses.Count; i++)
            if (corpses[i] != null && corpses[i].prop != null)
                Plugin.Logger.LogInfo($"Diary: body {corpses[i].identifier} root at {corpses[i].transform.position}, yaw {corpses[i].transform.eulerAngles.y:0.0}; " +
                    $"prop {PathOf(corpses[i].prop.transform)} at {corpses[i].prop.transform.position}, yaw {corpses[i].prop.transform.eulerAngles.y:0.0}.");
        SaveVehicles(save.Vehicles);
        PlayerCharacter host = LocalPlayer();
        if (host != null)
        {
            save.Players.Add(SavePlayer(host));
            LogNearSeats(host);
        }
        save = save with { EnviroTicks = EnviroTicksNow(), PlayerCount = PlayersOf(PlayerCountSwapper.playerCount) };
        try
        {
            DiaryStore.Write(_path, save);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Diary: snapshot failed: {e.Message}");
            return;
        }
        Plugin.Logger.LogInfo($"Diary: snapshot ({reason}) of slot {_slotUid}, {save.Bodies.Count} bodies, {save.Props.Count} props ({save.Props.Count(x => x.Platform != null)} on platforms), {save.Vehicles.Count} vehicles, {save.Players.Count} players, time {(save.EnviroTicks == 0 ? "none" : new DateTime(save.EnviroTicks).ToString("s"))}.");
    }

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
            // A body is also a prop. Its BodyRecord keeps it.
            if (p.GetComponent<Corpse>() != null) continue;
            // [impl->REQ-PLATFORM-PROPS] A prop on a moving platform keeps its pose relative to the platform.
            (string platform, float[] pos, float[] rot) = PoseOn(p.houseNetworkTransform != null ? p.houseNetworkTransform.stickyPlatform : null, p.transform);
            into.Add(new PropRecord(p.savablePropGuid ?? "", p.networkIdentity != null ? p.networkIdentity.sceneId : 0,
                PathOf(p.transform), pos, rot, platform));
        }
    }

    // The props on a moving platform that wait for the vehicles (T4b), and the physics time to move them.
    // An object (a prop, a body or the host) that waits for its platform. Ready is false if a home or a
    // player took the prop. Place moves the object to its world pose on the platform.
    private sealed class Pending
    {
        internal readonly string Label;
        internal readonly PropRecord Record;
        internal readonly Func<bool> Ready;
        internal readonly Action<Vector3, Quaternion, StickyPlatform> Place;
        internal Pending(string label, PropRecord record, Func<bool> ready, Action<Vector3, Quaternion, StickyPlatform> place) =>
            (Label, Record, Ready, Place) = (label, record, ready, place);
    }
    private static readonly List<Pending> _platformPending = new();
    private static float _platformAt;
    private static float _platformDeadline;
    private static int _platformFound, _platformMissed;

    // [impl->REQ-PROP-POSE]
    private static void RestoreProps(List<PropRecord> records)
    {
        _platformPending.Clear();
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
            if (rec.Platform != null) { QueueProp(p, rec, "prop " + path); continue; }
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

    /// <summary>
    /// The transform that moves with a platform. The StickyPlatform object stays at the world origin.
    /// The rigidbody of its PlatformingBody moves (the same transform as <c>StickyPlatformer.platformTransform</c>).
    /// </summary>
    private static Transform Frame(StickyPlatform sp) =>
        sp.platformingBody != null && sp.platformingBody.rb != null ? sp.platformingBody.rb.transform : sp.transform;

    /// <summary>
    /// Moves each waiting object (a prop, a body or the host) to its saved pose on its platform. An
    /// object waits while its car is unloaded or parked. Then the host loads the car again
    /// (<see cref="CarHoldPatch"/>). When a prop touches the platform, the game attaches it again
    /// (<c>PlatformingBody.OnCollisionEnter</c> sets <c>HouseNetworkTransform.stickyPlatform</c>).
    /// </summary>
    // [impl->REQ-PLATFORM-PROPS]
    private static void RestorePlatformProps()
    {
        var platforms = new Dictionary<string, StickyPlatform>();
        var duplicate = new HashSet<string>();
        foreach (StickyPlatform sp in Object.FindObjectsOfType<StickyPlatform>())
            if (!platforms.TryAdd(PathOf(sp.transform), sp)) duplicate.Add(PathOf(sp.transform));
        // Two platforms with the same path cannot be told apart. Do not use them.
        foreach (string d in duplicate) platforms.Remove(d);
        HashSet<string> parked = PlatformPark.Parked(platforms.Select(kv =>
        {
            Vector3 v = Frame(kv.Value).position;
            return (kv.Key, v.x, v.y, v.z);
        }).ToList());
        bool late = Time.fixedTime >= _platformDeadline;
        var carOf = new Dictionary<IntPtr, TrainCar>();
        foreach (TrainCar c in Object.FindObjectsOfType<TrainCar>())
            if (c.platformingBody != null) carOf[c.platformingBody.Pointer] = c;
        var placedNow = new HashSet<string>();
        for (int i = _platformPending.Count - 1; i >= 0; i--)
        {
            Pending pend = _platformPending[i];
            PropRecord rec = pend.Record;
            // The host goes onto its platform one pass after the props on it, so that the host body does not push them off.
            if (pend.Label == "host" && placedNow.Contains(rec.Platform)) continue;
            // An unloaded car has no colliders: no player is near it, and nothing touches it. The chairlift
            // also parks it below the ground. Load the car again (CarHoldPatch). The object waits for it.
            TrainCar hold = null;
            if (platforms.TryGetValue(rec.Platform, out StickyPlatform wait) && wait.platformingBody != null)
                carOf.TryGetValue(wait.platformingBody.Pointer, out hold);
            if (!late && (parked.Contains(rec.Platform) || (hold != null && hold.cullState)))
            {
                CarHoldPatch.Hold(hold, pend.Label + " waits for it");
                continue;
            }
            _platformPending.RemoveAt(i);
            // A home or a player that took the prop after RestoreProps (a body, the host) keeps it.
            if (!pend.Ready() || parked.Contains(rec.Platform) || (hold != null && hold.cullState)
                || !platforms.TryGetValue(rec.Platform, out StickyPlatform sp))
            {
                _platformMissed++;
                continue;
            }
            Transform pt = Frame(sp);
            Vector3 pos = pt.TransformPoint(new Vector3(rec.Position[0], rec.Position[1], rec.Position[2]));
            Quaternion rot = pt.rotation * new Quaternion(rec.Rotation[0], rec.Rotation[1], rec.Rotation[2], rec.Rotation[3]);
            // ponytail: in the next physics step, the car moves before the object does. Aim at the place of the car
            // then. Measured slip without it: 7 to 9 cm at 3 m/s. A turn of the car in that step is not included.
            // Exact fix if this causes a problem: move the objects in a hook on the car movement (#10).
            Rigidbody car = sp.platformingBody != null ? sp.platformingBody.rb : null;
            if (car != null) pos += car.GetPointVelocity(pos) * Time.fixedDeltaTime;
            pend.Place(pos, rot, sp);
            placedNow.Add(rec.Platform);
            _platformFound++;
            Plugin.Logger.LogInfo($"Diary: {pend.Label} to {pos} on {rec.Platform} (platform at {pt.position}).");
        }
        _platformAt = Time.fixedTime + 0.5f;
        if (_platformPending.Count > 0) return;
        Plugin.Logger.LogInfo($"Diary: restored {_platformFound} of {_platformFound + _platformMissed} objects on moving platforms " +
            $"({platforms.Count} platforms, {parked.Count} parked, {duplicate.Count} paths with two or more platforms).");
    }

    private static void QueueProp(Prop p, PropRecord rec, string label) =>
        _platformPending.Add(new Pending(label, rec, () => p != null && IsLoose(p), (pos, rot, sp) => MoveProp(p, pos, rot, sp)));

    private static void MoveProp(Prop p, Vector3 pos, Quaternion rot, StickyPlatform sp)
    {
        p.transform.SetPositionAndRotation(pos, rot);
        if (p.rb != null)
        {
            p.rb.position = pos;
            p.rb.rotation = rot;
            if (!p.rb.isKinematic)
            {
                // The prop moves with the platform from the first step.
                Rigidbody prb = sp.platformingBody != null ? sp.platformingBody.rb : null;
                p.rb.linearVelocity = prb != null ? prb.GetPointVelocity(pos) : Vector3.zero;
                p.rb.angularVelocity = prb != null ? prb.angularVelocity : Vector3.zero;
            }
        }
        // The log shows the prop again 3 s later (CheckDrop).
        (_dropped, _droppedOn, _dropCheckAt) = (p, sp, Time.time + 3f);
    }

    /// <summary>The pose of an object relative to its platform. With no platform, the world pose.</summary>
    private static (string Platform, float[] Position, float[] Rotation) PoseOn(StickyPlatform sp, Transform t)
    {
        Vector3 pos = t.position;
        Quaternion r = t.rotation;
        if (sp == null) return (null, new[] { pos.x, pos.y, pos.z }, new[] { r.x, r.y, r.z, r.w });
        Transform pt = Frame(sp);
        pos = pt.InverseTransformPoint(pos);
        r = Quaternion.Inverse(pt.rotation) * r;
        return (PathOf(sp.transform), new[] { pos.x, pos.y, pos.z }, new[] { r.x, r.y, r.z, r.w });
    }

    /// <summary>The platform of a car, or null.</summary>
    private static StickyPlatform PlatformOf(TrainCar car) => car == null || car.platformingBody == null ? null
        : Object.FindObjectsOfType<StickyPlatform>().FirstOrDefault(sp => sp.platformingBody != null
            && sp.platformingBody.Pointer == car.platformingBody.Pointer);

    private static Prop _dropped;
    private static StickyPlatform _droppedOn;
    private static float _dropCheckAt;

    private static void CheckDrop()
    {
        Prop p = _dropped;
        _dropped = null;
        if (p == null) return;
        Transform frame = Frame(_droppedOn);
        PlatformingBody pb = p.GetComponent<PlatformingBody>();
        Plugin.Logger.LogInfo($"Diary: 3 s after the move, prop at {p.transform.position}, relative {frame.InverseTransformPoint(p.transform.position)}, " +
            $"stickyPlatform {(p.houseNetworkTransform?.stickyPlatform != null ? PathOf(p.houseNetworkTransform.stickyPlatform.transform) : "none")}, " +
            $"PlatformingBody {(pb != null ? $"yes, connected {(pb.connectedPlatform != null ? "yes" : "no")}, contacts {pb.contacts?.Count ?? -1}" : "no")}, " +
            $"kinematic {p.rb.isKinematic}, sleeping {p.rb.IsSleeping()}.");
        if (CullTrip && _tripStep == 0) { _tripProp = p; _tripOn = _droppedOn; _tripStep = 1; _tripAt = Time.time; }
    }

    // Test aid for T4b: the host goes far from a platform and back. The log shows the prop and the car at each step.
    private static Prop _tripProp;
    private static StickyPlatform _tripOn;
    private static int _tripStep;
    private static float _tripAt;
    private static Vector3 _tripHome;

    private static void CullTripStep()
    {
        if (_tripStep == 0 || _tripStep > 4 || Time.time < _tripAt) return;
        PlayerCharacter host = LocalPlayer();
        Transform frame = Frame(_tripOn);
        TrainCar car = Object.FindObjectsOfType<TrainCar>().FirstOrDefault(c => c.platformingBody == _tripOn.platformingBody);
        Prop p = _tripProp;
        Plugin.Logger.LogInfo($"Diary: test aid: trip step {_tripStep}: host at {host.transform.position}, car at {frame.position}, " +
            $"car culled {(car != null ? car.cullState.ToString() : "?")}, prop at {(p != null ? p.transform.position.ToString() : "gone")}, " +
            $"relative {(p != null ? frame.InverseTransformPoint(p.transform.position).ToString() : "-")}, " +
            $"stickyPlatform {(p?.houseNetworkTransform?.stickyPlatform != null ? "yes" : "none")}, kinematic {p?.rb?.isKinematic}.");
        var cars = Object.FindObjectsOfType<TrainCar>().Where(c => c.platformingBody != null && PathOf(c.transform).StartsWith(PathOf(_tripOn.transform).Split('/')[0])).ToList();
        var live = cars.Where(c => !c.cullState).Select(c => (c.platformingBody.rb.position - host.transform.position).magnitude).OrderBy(d => d).ToList();
        Plugin.Logger.LogInfo($"Diary: test aid: trip step {_tripStep}: {cars.Count} cars, {live.Count} not culled, distances to host [{string.Join(", ", live.Select(d => d.ToString("0")))}], " +
            $"parked {cars.Count(c => c.platformingBody.rb.position.y < -50f)}.");
        if (_tripStep == 1) { _tripHome = host.transform.position; host.grease.Teleport(_tripHome + new Vector3(400f, 50f, 0f), host.transform.rotation); }
        if (_tripStep == 3) host.grease.Teleport(_tripHome, host.transform.rotation);
        _tripStep++;
        _tripAt = Time.time + (_tripStep == 2 || _tripStep == 4 ? 15f : 1f);
    }

    /// <summary>Test aid for T4b: drops one loose prop onto a moving platform, because a single-PC test has no player input.</summary>
    private static void DropPropOnPlatform()
    {
        _propDropped = true;
        if (_scenGround == Vector3.zero) _scenGround = LocalPlayer().transform.position;
        StickyPlatform target = null;
        PlayerCharacter host = LocalPlayer();
        Vector3 h = host.transform.position;
        foreach (StickyPlatform sp in Object.FindObjectsOfType<StickyPlatform>())
        {
            // A parked chairlift car is below the ground (y = -82). Do not use it.
            if (!PathOf(sp.transform).Contains(PropOnPlatform) || Frame(sp).position.y < -50f) continue;
            // The nearest platform to the host: physics far from the host has no colliders.
            if (target == null || (Frame(sp).position - h).sqrMagnitude < (Frame(target).position - h).sqrMagnitude) target = sp;
        }
        if (target == null && !_movedToPole && PropOnPlatform.Contains("ChairLift"))
        {
            // The chairlift cars stay parked below the ground while no player is near the lift.
            _movedToPole = true;
            ChairLiftPole pole = Object.FindObjectOfType<ChairLiftPole>();
            if (pole != null)
            {
                Vector3 to = pole.transform.position + new Vector3(3f, 2f, 0f);
                host.grease.Teleport(to, host.transform.rotation);
                Plugin.Logger.LogInfo($"Diary: test aid: host moved to {to}, near pole {PathOf(pole.transform)}.");
                _propDropped = false;
                _platformAt = Time.fixedTime + 2f;
                return;
            }
        }
        if (target != null && (Frame(target).position - h).sqrMagnitude > 20f * 20f)
        {
            // Move the host onto the platform first, then drop the prop 2 s later.
            Vector3 to = Frame(target).position + Vector3.up * 1.5f;
            host.grease.Teleport(to, host.transform.rotation);
            Plugin.Logger.LogInfo($"Diary: test aid: host moved to {to}, onto {PathOf(target.transform)}.");
            _propDropped = false;
            _platformAt = Time.fixedTime + 1f;
            return;
        }
        Prop prop = null;
        var props = Prop.allProps;
        for (int i = 0; props != null && i < props.Count && prop == null; i++)
            if (props[i] != null && IsLoose(props[i]) && props[i].rb != null && !props[i].rb.isKinematic
                && props[i].GetComponent<Corpse>() == null && props[i].houseNetworkTransform?.stickyPlatform == null)
                prop = props[i];
        if (target == null || prop == null)
        {
            var seen = Object.FindObjectsOfType<StickyPlatform>().Where(sp => PathOf(sp.transform).Contains(PropOnPlatform))
                .Take(4).Select(sp => $"{Frame(sp).position} rb {(sp.platformingBody?.rb != null ? sp.platformingBody.rb.position.ToString() : "none")}");
            Plugin.Logger.LogWarning($"Diary: test aid: no platform '{PropOnPlatform}' or no loose prop (prop {prop != null}). Frames: {string.Join("; ", seen)}.");
            return;
        }
        Transform frame = Frame(target);
        Collider col = null;
        foreach (Collider c in frame.GetComponentsInChildren<Collider>())
            if (!c.isTrigger && (col == null || c.bounds.size.sqrMagnitude > col.bounds.size.sqrMagnitude)) col = c;
        Vector3 at = col != null ? new Vector3(col.bounds.center.x, col.bounds.max.y + 0.3f, col.bounds.center.z)
            : frame.position + Vector3.up;
        prop.transform.position = at;
        prop.rb.position = at;
        prop.rb.linearVelocity = Vector3.zero;
        _dropped = prop;
        _droppedOn = target;
        _dropCheckAt = Time.time + 3f;
        if (Scenario) (_scenA, _scenOn, _scenStep, _scenAt) = (prop, target, 1, Time.time + 3f);
        Plugin.Logger.LogInfo($"Diary: test aid: dropped prop {PathOf(prop.transform)} onto {PathOf(target.transform)} (frame {PathOf(frame)} at {frame.position}, collider {(col != null ? PathOf(col.transform) : "none")}) at {at}.");
    }

    // Test aid for T4b (Scenario): the host on a car, a stack of two props on the car, a test body on a
    // monorail car, and a stack of two props on the ground. After a game load, a tracker logs them.
    internal static bool Scenario;
    private static Prop _scenA;
    private static StickyPlatform _scenOn;
    private static int _scenStep, _trackStep;
    private static float _scenAt, _trackAt;
    private static Vector3 _scenGround;
    private const string ScenRack = "OpticalTelegraph/Positioner/CentralArea/PegTileRack_SetD/Pegtiles/PegTileProp";

    private static Prop RackProp(int n) => Prop.allProps?.ToArray().FirstOrDefault(p => p != null && PathOf(p.transform).EndsWith(ScenRack + n));

    private static void Put(Prop p, Vector3 at)
    {
        if (p.currentHome != null) p.ServerSetUnpinned();
        p.transform.position = at;
        p.rb.position = at;
        p.rb.linearVelocity = Vector3.zero;
    }

    private static void ScenarioStep()
    {
        if (_scenStep == 1 && Time.time >= _scenAt)
        {
            _scenStep = 2;
            PlayerCharacter host = LocalPlayer();
            Transform frame = Frame(_scenOn);
            Prop b = RackProp(3), c = RackProp(4), d = RackProp(5);
            Put(b, _scenA.transform.position + Vector3.up * 0.4f);
            Put(c, _scenGround + new Vector3(2f, 0.3f, 0f));
            Put(d, _scenGround + new Vector3(2f, 0.7f, 0f));
            // A head tilt and turn that the load must keep.
            host.head.headState = new Vector2(20f, -25f);
            host.playerNetworking.NetworkheadState = host.head.headState;
            string extra = "";
            if (PathOf(_scenOn.transform).Contains("ChairLift"))
            {
                PlayerPose pp = frame.GetComponentsInChildren<PlayerPose>().FirstOrDefault();
                if (pp != null) { host.playerNetworking.ServerEnterPoseAuto(pp.shellReference); extra = $"host sits in {PathOf(pp.transform)}"; }
            }
            else
            {
                Corpse body = Corpse.CreateAndSpawn(host, host.registry.corpsePrefab);
                body.identifier = "hikers-diary-test-body";
                Vector3 at = _scenA.transform.position + frame.right * 0.9f + Vector3.up * 0.3f;
                body.transform.position = at;
                if (body.prop?.rb != null) { body.prop.rb.position = at; body.prop.rb.linearVelocity = Vector3.zero; }
                extra = $"host stands on the car, test body at {at}";
            }
            Plugin.Logger.LogInfo($"Diary: test aid: scenario: stacked {PathOf(b.transform)} on the car prop, {PathOf(c.transform)} and {PathOf(d.transform)} on the ground at {_scenGround}; {extra}.");
        }
    }

    /// <summary>Test aid for T4b (Scenario): logs the host, the bodies and the rack props 2 to 5, with the nearest train car.</summary>
    private static void TrackStep()
    {
        if (_slotUid == null || _trackStep >= 10) return;
        if (_trackStep == 0) { _trackStep = 1; _trackAt = Time.time + 3f; return; }
        if (Time.time < _trackAt) return;
        _trackAt = Time.time + 5f;
        var cars = Object.FindObjectsOfType<TrainCar>().Where(c => c.platformingBody?.rb != null).ToList();
        string Where(Vector3 at)
        {
            TrainCar car = cars.OrderBy(c => (c.platformingBody.rb.position - at).sqrMagnitude).FirstOrDefault();
            if (car == null) return $"at {at}";
            Transform f = car.platformingBody.rb.transform;
            return $"at {at}, nearest car {car.name} {(f.position - at).magnitude:0.0} m (culled {car.cullState}, contacts {car.platformingBody.contacts?.Count ?? 0}), relative {f.InverseTransformPoint(at)}";
        }
        PlayerCharacter host = LocalPlayer();
        PlayerNetworking net = host.playerNetworking;
        PlayerPose pose = net.poseReference.shellReference.isEmpty ? null : net.poseReference.shellReference.GetPose();
        Plugin.Logger.LogInfo($"Diary: track {_trackStep}: host {Where(host.transform.position)}, sitting {net.isSitting}, pose {(pose != null ? PathOf(pose.transform) : "none")}, head {host.head.headState}.");
        TrainCar near = cars.OrderBy(c => (c.platformingBody.rb.position - host.transform.position).sqrMagnitude).FirstOrDefault();
        Camera cam = Camera.main;
        if (near != null && cam != null)
        {
            Transform f = near.platformingBody.rb.transform;
            Vector3 camDir = f.InverseTransformDirection(cam.transform.forward);
            Plugin.Logger.LogInfo($"Diary: track {_trackStep}: host yaw on car {(Quaternion.Inverse(f.rotation) * host.transform.rotation).eulerAngles.y:0.00}, " +
                $"camera direction on car ({camDir.x:0.000}, {camDir.y:0.000}, {camDir.z:0.000}), camera yaw on car {Mathf.Atan2(camDir.x, camDir.z) * Mathf.Rad2Deg:0.00}, " +
                $"look spin {host.head.runningTotalLookSpin:0.00}.");
        }
        foreach (Corpse c in Corpse.allCorpses?.ToArray() ?? Array.Empty<Corpse>())
            if (c != null) Plugin.Logger.LogInfo($"Diary: track {_trackStep}: body {c.identifier} {Where(c.transform.position)}.");
        for (int n = 2; n <= 5; n++)
        {
            Prop p = RackProp(n);
            if (p != null) Plugin.Logger.LogInfo($"Diary: track {_trackStep}: prop {n} {Where(p.transform.position)}, stickyPlatform {(p.houseNetworkTransform?.stickyPlatform != null ? "yes" : "none")}.");
        }
        _trackStep++;
    }

    // This field keeps the last switch-state line in the log. The log shows a new line only when a state changes.
    private static string _lastSwitchStates;
    private static bool _lightFlipped;

    /// <summary>
    /// For the T4 light test, this method logs the state of each saveable switch (lights, doors,
    /// radio stations, gauntlet chambers). The game save keeps these states. Compare the line from
    /// before a game load with the line from after the load.
    /// </summary>
    private static void LogSwitchStates()
    {
        var parts = new List<string>();
        foreach (TrackedPeckState s in Object.FindObjectsOfType<TrackedPeckState>())
        {
            if (s.savableSystem == SavableSystem.NotSavable) continue;
            if (FlipLookoutLight && !_lightFlipped && s.savableSystem == SavableSystem.LookoutLightRed)
            {
                _lightFlipped = true;
                int to = s.currentPeckContext.state == 0 ? 1 : 0;
                s.SetState(to);
                Plugin.Logger.LogInfo($"Diary: test aid: red lookout light set to state {to}.");
            }
            parts.Add($"{s.savableSystem}={s.currentPeckContext.state}");
        }
        parts.Sort(StringComparer.Ordinal);
        string line = string.Join(", ", parts);
        if (line == _lastSwitchStates) return;
        _lastSwitchStates = line;
        Plugin.Logger.LogInfo($"Diary: switch states ({parts.Count}): {line}.");
    }

    // The log shows the vehicle map one time for each run (T4 step 1).
    private static bool _vehicleMapLogged;

    // [impl->REQ-VEHICLE-STATE]
    private static void SaveVehicles(List<VehicleRecord> into)
    {
        bool log = !_vehicleMapLogged;
        _vehicleMapLogged = true;
        foreach (NetworkedTrain t in Object.FindObjectsOfType<NetworkedTrain>())
        {
            // [impl->REQ-VEHICLE-STATE] The cull state: the cars that are loaded now.
            var cars = t.cars;
            int[] loaded = Enumerable.Range(0, cars?.Length ?? 0).Where(i => cars[i] != null && !cars[i].cullState).ToArray();
            // The sync vars lag behind a moving train. The _current* values are the train now.
            into.Add(new VehicleRecord("train", PathOf(t.transform), t._currentSplineIndex, t._currentDistance,
                t._currentSpeed, t.NetworktargetSpeed, loaded));
            if (log)
                Plugin.Logger.LogInfo($"Diary: vehicle map: NetworkedTrain '{t.name}' path {PathOf(t.transform)}, hasCable {t.hasCable}, " +
                    $"splines {t.splines?.Length ?? 0}, cars {t.cars?.Length ?? 0}, at {t.transform.position}, fraction {t.GetDistanceFraction():0.000}.");
        }
        foreach (MovementDriver d in Object.FindObjectsOfType<MovementDriver>())
        {
            into.Add(new VehicleRecord("driver", PathOf(d.transform), 0, d._localPosition, d._localSpeed, 0f));
            if (log)
                Plugin.Logger.LogInfo($"Diary: vehicle map: MovementDriver '{d.name}' path {PathOf(d.transform)}, at {d.transform.position}, " +
                    $"position {d._localPosition}, speed {d._localSpeed}, max speed {d.maxSpeed}.");
        }
    }

    // [impl->REQ-VEHICLE-STATE]
    private static void RestoreVehicles(List<VehicleRecord> records)
    {
        if (records.Count == 0) return;
        var byKey = VehicleRecord.ByUniquePath(records);
        int found = 0;
        foreach (NetworkedTrain t in Object.FindObjectsOfType<NetworkedTrain>())
        {
            if (!byKey.TryGetValue(VehicleRecord.Key("train", PathOf(t.transform)), out VehicleRecord r)) continue;
            // On the server, FixedUpdate compares the sync vars with the _current* values. If the sync
            // vars and the _current* values are different, FixedUpdate copies the _current* values to
            // the sync vars. Thus, set the two groups of values.
            t._currentSplineIndex = r.SplineIndex;
            t._currentDistance = r.Distance;
            t._currentSpeed = r.Speed;
            // NetworkTime starts again at each host start. Thus, set syncTime to NetworkTime.time, not to the saved value.
            t.NetworksyncSplineIndex = r.SplineIndex;
            t.NetworksyncDistance = r.Distance;
            t.NetworksyncSpeed = r.Speed;
            t.NetworktargetSpeed = r.TargetSpeed;
            t.NetworksyncTime = NetworkTime.time;
            // Each car that was loaded at the snapshot stays loaded until something touches it (CarHoldPatch).
            foreach (int i in r.LoadedCars ?? Array.Empty<int>())
                if (t.cars != null && i >= 0 && i < t.cars.Length) CarHoldPatch.Hold(t.cars[i], "loaded in the snapshot");
            found++;
        }
        foreach (MovementDriver d in Object.FindObjectsOfType<MovementDriver>())
        {
            if (!byKey.TryGetValue(VehicleRecord.Key("driver", PathOf(d.transform)), out VehicleRecord r)) continue;
            // ServerSync copies the local values to the sync vars, with syncTime = NetworkTime.time.
            d._localPosition = r.Distance;
            d._localSpeed = r.Speed;
            d.ServerSync();
            found++;
        }
        Plugin.Logger.LogInfo($"Diary: restored {found} of {records.Count} vehicles.");
    }

    // Returns the Enviro date and time as DateTime ticks. If Enviro is not ready, returns 0.
    // [impl->REQ-TIME-OF-DAY]
    private static long EnviroTicksNow()
    {
        var time = Enviro.EnviroManager.instance?.Time;
        if (time == null) return 0;
        try
        {
            return new DateTime(time.years, time.months, time.days, time.hours, time.minutes, time.seconds).Ticks;
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }

    // [impl->REQ-TIME-OF-DAY]
    private static void RestoreTime(long ticks)
    {
        var time = Enviro.EnviroManager.instance?.Time;
        if (ticks == 0 || time == null) return;
        var d = new DateTime(ticks);
        time.SetDateTime(d.Second, d.Minute, d.Hour, d.Day, d.Month, d.Year);
        Plugin.Logger.LogInfo($"Diary: restored time {d:s}.");
    }

    private static PropRecord KeyOf(Prop p)
    {
        Transform t = p.transform;
        Vector3 pos = t.position;
        Quaternion r = t.rotation;
        return new PropRecord(p.savablePropGuid ?? "", p.networkIdentity != null ? p.networkIdentity.sceneId : 0,
            PathOf(t), new[] { pos.x, pos.y, pos.z }, new[] { r.x, r.y, r.z, r.w });
    }

    private static PropHome Pocket(PlayerCharacter pc, string name) => name switch
    {
        "backpack" => pc.registry.backpackPocket,
        "holster" => pc.registry.holsterPocket,
        _ => pc.registry.blindfoldPocket,
    };

    // [impl->REQ-PLAYER-STATE]
    private static PlayerRecord SavePlayer(PlayerCharacter pc)
    {
        PlayerNetworking net = pc.playerNetworking;
        Transform t = pc.transform;
        Vector3 p = t.position;
        Quaternion r = t.rotation;
        Vector2 head = pc.head.headState;
        var items = new Dictionary<string, PropRecord>();
        if (pc.hands.heldProp != null) items[PlayerRecord.Hand] = KeyOf(pc.hands.heldProp);
        foreach (string name in PlayerRecord.Pockets)
        {
            Prop pinned = Pocket(pc, name)?.pinnedProp;
            if (pinned != null) items[name] = KeyOf(pinned);
        }
        PlayerPose pose = net.poseReference.shellReference.isEmpty ? null : net.poseReference.shellReference.GetPose();
        // [impl->REQ-PLATFORM-PROPS] A player on a moving platform, or in a seat of a car, keeps the pose relative to the platform.
        StickyPlatform sp = pc.platformer?.stickyPlatform;
        if (sp == null && pose != null) sp = PlatformOf(pose.GetComponentInParent<TrainCar>());
        (string platform, float[] pos, float[] rot) = PoseOn(sp, t);
        return new PlayerRecord(net.identifier ?? "", pos, rot,
            new[] { head.x, head.y }, net.trueCrouchness, net.isSitting, pose != null ? PathOf(pose.transform) : "", items, platform);
    }

    private static bool _seatsLogged;

    /// <summary>Test aid for T5: logs the 3 seats (PlayerPose) nearest the host one time, so a test can put the host in a seat.</summary>
    private static void LogNearSeats(PlayerCharacter host)
    {
        if (_seatsLogged) return;
        _seatsLogged = true;
        Vector3 at = host.transform.position;
        var near = Object.FindObjectsOfType<PlayerPose>().OrderBy(pp => (pp.transform.position - at).sqrMagnitude).Take(3);
        foreach (PlayerPose pp in near)
            Plugin.Logger.LogInfo($"Diary: seat {PathOf(pp.transform)} at {pp.transform.position}.");
    }

    /// <summary>
    /// The host has no body, so the game does not keep its state. Move the host player to the
    /// position and rotation of the last snapshot. Then set the items, then the pose or the sit.
    /// </summary>
    // [impl->REQ-PLAYER-STATE]
    private static void RestoreHost(List<PlayerRecord> players, PlayerCharacter host)
    {
        PlayerNetworking net = host.playerNetworking;
        PlayerRecord rec = players.Find(x => x.Identifier == net.identifier);
        if (rec == null) return;
        var pos = new Vector3(rec.Position[0], rec.Position[1], rec.Position[2]);
        var rot = new Quaternion(rec.Rotation[0], rec.Rotation[1], rec.Rotation[2], rec.Rotation[3]);
        var head = new Vector2(rec.Head[0], rec.Head[1]);

        var found = new List<string>();
        foreach ((string slot, PropRecord key) in rec.Items)
        {
            Prop prop = FindProp(key);
            if (prop == null) continue;
            if (prop.currentHome != null) prop.ServerSetUnpinned();
            if (slot == PlayerRecord.Hand) net.ServerPickUpPropAutomatic(prop);
            else prop.ServerSetPinned(Pocket(host, slot));
            found.Add(slot);
        }

        // [impl->REQ-PLATFORM-PROPS] On a moving platform, the host waits for the platform. Then it goes to its place and its pose.
        if (rec.Platform != null)
        {
            // Index 0: the loop below goes from the end, so the host comes after the props.
            _platformPending.Insert(0, new Pending("host", new PropRecord("", 0, "", rec.Position, rec.Rotation, rec.Platform), () => true,
                (at, to, sp) => PlaceHost(host, rec, at, to, sp)));
            Plugin.Logger.LogInfo($"Diary: restored host items [{string.Join(", ", found)}] of {rec.Items.Count}. The host waits for its platform {rec.Platform}.");
            return;
        }
        string pose = PlaceHost(host, rec, pos, rot, null);
        Plugin.Logger.LogInfo($"Diary: restored host at {pos}, head {head}, items [{string.Join(", ", found)}] of {rec.Items.Count}, " +
            $"pose {pose}, sitting {rec.Sitting}, crouch {rec.Crouch:0.00}.");
    }

    /// <summary>Moves the host, then enters the saved pose, the sit or the crouch. Returns the pose path or "none".</summary>
    private static string PlaceHost(PlayerCharacter host, PlayerRecord rec, Vector3 pos, Quaternion rot, StickyPlatform sp)
    {
        PlayerNetworking net = host.playerNetworking;
        host.grease.Teleport(pos, rot);
        if (sp != null)
        {
            // Attach the host to its platform now, with the platform velocity. Else the host slides on a moving car
            // until the game attaches it.
            host.platformer?.SetPlatform(sp);
            Rigidbody prb = sp.platformingBody != null ? sp.platformingBody.rb : null;
            if (host.rb != null && prb != null) host.rb.linearVelocity = prb.GetPointVelocity(pos);
        }
        // [impl->REQ-PLAYER-STATE] Set the head after the teleport. The teleport resets the head tilt.
        var head = new Vector2(rec.Head[0], rec.Head[1]);
        host.head.headState = head;
        net.NetworkheadState = head;
        string pose = "none";
        if (!string.IsNullOrEmpty(rec.PosePath))
            foreach (PlayerPose pp in Object.FindObjectsOfType<PlayerPose>())
                if (PathOf(pp.transform) == rec.PosePath)
                {
                    net.ServerEnterPoseAuto(pp.shellReference);
                    pose = rec.PosePath;
                    break;
                }
        if (rec.Sitting && pose == "none") host.sitter.SetSittingLocal(true);
        // ponytail: the crouch comes from the local input in each frame. The toggle keeps it until the player crouches again.
        if (rec.Crouch > 0.5f && pose == "none") host.croucher.crouchIsToggledOn = true;
        if (rec.Platform != null) Plugin.Logger.LogInfo($"Diary: host pose {pose}, sitting {rec.Sitting}.");
        return pose;
    }

    private static Prop FindProp(PropRecord key)
    {
        var index = new PropIndex(new[] { key });
        var props = Prop.allProps;
        for (int i = 0; props != null && i < props.Count; i++)
            if (props[i] != null && index.Find(props[i].savablePropGuid,
                    props[i].networkIdentity != null ? props[i].networkIdentity.sceneId : 0, PathOf(props[i].transform)) != null)
                return props[i];
        return null;
    }

    /// <summary>The home of a body for a slot name of <see cref="PlayerRecord"/>. The hand is the grasper home.</summary>
    private static PropHome BodyHome(Corpse c, string slot) => slot switch
    {
        PlayerRecord.Hand => c.grasperHome,
        "backpack" => c.backpackHome,
        "holster" => c.holsterHome,
        _ => c.blindfoldHome,
    };

    // [impl->REQ-PLAYER-STATE]
    private static Dictionary<string, PropRecord> BodyItems(Corpse c)
    {
        var items = new Dictionary<string, PropRecord>();
        foreach (string slot in PlayerRecord.Pockets.Prepend(PlayerRecord.Hand))
        {
            Prop pinned = BodyHome(c, slot)?.pinnedProp;
            if (pinned != null) items[slot] = KeyOf(pinned);
        }
        return items;
    }

    // Test aid for T8 step 2 (#8): find where the game stops a player in deep water.
    // ponytail: diagnostic only. Remove it when T8 step 3 uses these facts.
    internal static bool WaterProbe;
    private static List<Vector3> _probe;
    private static int _probeStep;
    private static float _probeAt;

    // The IL2CPP build does not include LayerMask.GetMask and NameToLayer. It includes LayerToName.
    private static int _notPlayer;
    private static int NotPlayer
    {
        get
        {
            if (_notPlayer != 0) return _notPlayer;
            _notPlayer = ~0;
            for (int i = 0; i < 32; i++) if (LayerMask.LayerToName(i) == "Player") _notPlayer = ~(1 << i);
            return _notPlayer;
        }
    }

    private static bool GroundAt(Vector3 at, out RaycastHit hit) =>
        Physics.Raycast(new Vector3(at.x, 150f, at.z), Vector3.down, out hit, 400f, NotPlayer, QueryTriggerInteraction.Ignore);

    // This method finds the seabed. The seabed is the first hit below the float floor. The float floor supports the players.
    private static bool SeabedAt(Vector3 at, out RaycastHit bed)
    {
        bed = default;
        bool found = false;
        foreach (RaycastHit h in Physics.RaycastAll(new Vector3(at.x, 150f, at.z), Vector3.down, 400f, NotPlayer, QueryTriggerInteraction.Ignore))
            if (h.collider.name != "FloatFloorCollider" && (!found || h.distance < bed.distance)) { bed = h; found = true; }
        return found;
    }

    private static string Ground(Vector3 at) => SeabedAt(at, out RaycastHit hit)
        ? $"seabed {hit.point.y:0.00} on {PathOf(hit.collider.transform)} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)})" : "no seabed";

    private static Prop _probeProp;

    private static void WaterProbeStep(PlayerCharacter host)
    {
        var data = Resources.FindObjectsOfTypeAll<WaterDepthSampleData>().FirstOrDefault();
        if (data == null) { WaterProbe = false; Plugin.Logger.LogInfo("Diary: water probe: no depth data."); return; }
        if (_probe == null)
        {
            if (_probeAt == 0f) { _probeAt = Time.time + 10f; return; }
            if (Time.time < _probeAt) return;
            // A measurement shows that GetDepth gives the y of the point minus the y of the water surface.
            // At a point below the surface, the value is negative.
            var hist = new int[12];
            var deep = new List<(float D, Vector3 P, bool River)>();
            for (float x = data.worldMin.x; x <= data.worldMax.x; x += 4f)
                for (float z = data.worldMin.y; z <= data.worldMax.y; z += 4f)
                {
                    if (!SeabedAt(new Vector3(x, 0f, z), out RaycastHit hit)) continue;
                    float d = -data.GetDepth(hit.point, out int patch, out bool river);
                    if (d < 0.25f) continue;
                    hist[Mathf.Min(11, (int)d)]++;
                    if (d > 1f) deep.Add((d, hit.point, river));
                }
            Plugin.Logger.LogInfo($"Diary: water probe: cells (4 m) by depth at the seabed, 1 m bins from 0: [{string.Join(", ", hist)}], cells deeper than 1 m: {deep.Count}, " +
                $"river cells deeper than 1 m: {deep.Count(c => c.River)}, max river depth {(deep.Any(c => c.River) ? deep.Where(c => c.River).Max(c => c.D).ToString("0.00") : "none")}.");
            _probe = new List<Vector3>();
            // The probe tests 3 points: the deepest point, the deepest river point, and a point near 2 m.
            var pick = new List<(float D, Vector3 P, bool River)>();
            if (deep.Count > 0) pick.Add(deep.OrderByDescending(c => c.D).First());
            if (deep.Any(c => c.River)) pick.Add(deep.Where(c => c.River).OrderByDescending(c => c.D).First());
            if (deep.Count > 0) pick.Add(deep.OrderBy(c => Mathf.Abs(c.D - 2f)).First());
            foreach (var c in pick)
            {
                Plugin.Logger.LogInfo($"Diary: water probe: deep point {_probe.Count}: depth {c.D:0.00} at {c.P}, river {c.River}, {Ground(c.P)}.");
                _probe.Add(c.P);
            }
            Prop[] props = Prop.allProps == null ? new Prop[0] : Prop.allProps.ToArray();
            _probeProp = props.FirstOrDefault(p => p != null && IsLoose(p) && p.rb != null && !p.rb.isKinematic
                && p.GetComponent<Corpse>() == null && p.houseNetworkTransform?.stickyPlatform == null);
            Plugin.Logger.LogInfo($"Diary: water probe: test prop {(_probeProp != null ? PathOf(_probeProp.transform) : "none")}.");
            _probeStep = 0;
            _probeAt = Time.time;
            return;
        }
        const int Per = 4;
        if (Time.time < _probeAt || _probeStep > _probe.Count * Per) return;
        int n = _probeStep / Per, k = _probeStep % Per;
        float wait = 8f;
        if (_probeStep == _probe.Count * Per) Plugin.Logger.LogInfo("Diary: water probe: done.");
        else if (k == 0)
        {
            Vector3 to = new Vector3(_probe[n].x, 1f, _probe[n].z);
            host.grease.Teleport(to, host.transform.rotation);
            Plugin.Logger.LogInfo($"Diary: water probe: point {n}: host moved to {to}.");
            wait = 3f;
        }
        else if (k == 1)
        {
            Vector3 h = host.transform.position;
            Plugin.Logger.LogInfo($"Diary: water probe: point {n}: host at {h}, depth at host {data.GetDepth(h, out _, out _):0.00}, {Ground(h)}.");
            if (_probeProp != null)
            {
                Vector3 at = new Vector3(_probe[n].x + 1.5f, 1f, _probe[n].z);
                _probeProp.transform.position = at;
                _probeProp.rb.position = at;
                _probeProp.rb.linearVelocity = Vector3.zero;
                Plugin.Logger.LogInfo($"Diary: water probe: point {n}: dropped the test prop at {at}.");
            }
        }
        else if (_probeProp != null)
        {
            Vector3 q = _probeProp.rb.position;
            Plugin.Logger.LogInfo($"Diary: water probe: point {n}: {(k - 1) * 8} s after the drop, prop at {q}, speed {_probeProp.rb.linearVelocity.magnitude:0.00}, " +
                $"depth at prop {data.GetDepth(q, out _, out _):0.00}, {Ground(q)}, host at {host.transform.position}.");
        }
        _probeStep++;
        _probeAt = Time.time + wait;
    }

    /// <summary>T8 step 1 (#8): This method logs the water pushers, the prop zones, and the depth at each magnet point.</summary>
    // ponytail: diagnostic only. Remove it when T8 step 3 uses these facts.
    private static void LogWaterMap()
    {
        var depth = Resources.FindObjectsOfTypeAll<WaterDepthSampleData>().FirstOrDefault();
        string Depth(Vector3 at)
        {
            if (depth == null) return "no depth data";
            float d = depth.GetDepth(at, out int patch, out bool river);
            return $"depth {d:0.00} (patch {patch}, river {river})";
        }
        string Cols(Component c) => string.Join("; ", c.GetComponents<Collider>().Select(x => $"{x.GetIl2CppType().Name} {x.bounds.center} size {x.bounds.size}"));
        foreach (ZonePusher z in Resources.FindObjectsOfTypeAll<ZonePusher>())
            Plugin.Logger.LogInfo($"Diary: water map: ZonePusher {PathOf(z.transform)}, active {z.isActiveAndEnabled}, zone {(z.propZone != null ? PathOf(z.propZone.transform) : "none")}, " +
                $"magnet {z.magnetPoint} {Depth(z.magnetPoint)}, speed {z.speed}, duration {z.magnetDuration}.");
        foreach (PropZone z in Object.FindObjectsOfType<PropZone>())
            Plugin.Logger.LogInfo($"Diary: water map: PropZone {PathOf(z.transform)}, colliders [{Cols(z)}], contacts {z.contacts?.Count ?? 0}, radio dead {z.isRadioDeadZone}.");
        foreach (OceanPusher o in Object.FindObjectsOfType<OceanPusher>())
        {
            var cols = o.myColliders == null ? "" : string.Join("; ", o.myColliders.Where(x => x != null).Select(x => $"{PathOf(x.transform)} {x.bounds.center} size {x.bounds.size}"));
            Plugin.Logger.LogInfo($"Diary: water map: OceanPusher {PathOf(o.transform)}, colliders [{cols}], magnet {o.magnetPoint} {Depth(o.magnetPoint)}, speed {o.speed}.");
        }
        Plugin.Logger.LogInfo($"Diary: water map: depth data {(depth != null ? depth.name : "none")}.");
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
            // [impl->REQ-PLAYER-COLORS] The colors in the color book come before the colors of the body record.
            ColorBook book = ColorPatches.Book;
            int Color(int part, int saved) => book?.Get(b.Identifier, part) is int v and >= 0 ? v : saved;
            c.NetworkheadColorIndex = Color(0, b.HeadColor);
            c.NetworktorsoColorIndex = Color(1, b.TorsoColor);
            c.NetworklegsColorIndex = Color(2, b.LegsColor);
            NetworkServer.Spawn(go, (NetworkConnection)null);
            // [impl->REQ-PLAYER-STATE] When the player joins again, the game moves the backpack and
            // holster items to the player and drops the other items (Corpse.ExpireDueToRevival).
            var found = new List<string>();
            foreach ((string slot, PropRecord key) in b.Items ?? new Dictionary<string, PropRecord>())
            {
                Prop prop = FindProp(key);
                PropHome home = BodyHome(c, slot);
                if (prop == null || home == null) continue;
                if (prop.currentHome != null) prop.ServerSetUnpinned();
                prop.ServerSetPinned(home);
                found.Add(slot);
            }
            // [impl->REQ-PLATFORM-PROPS] A body on a platform waits for its platform, as a prop does.
            if (b.Platform != null && c.prop != null)
                QueueProp(c.prop, new PropRecord("", 0, "", b.Position, b.Rotation, b.Platform), "body " + b.Identifier);
            Plugin.Logger.LogInfo($"Diary: restored body {b.Identifier} at {(b.Platform != null ? "its platform " + b.Platform : pos.ToString())}, items [{string.Join(", ", found)}] of {b.Items?.Count ?? 0}.");
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
        HostLatestSave();
    }

    /// <summary>Host the save that was played last. Returns false if the folder has no save.</summary>
    internal static bool HostLatestSave()
    {
        var saves = SaveManager.GetAllSaveDatasInFolder();
        SaveData last = null;
        for (int i = 0; saves != null && i < saves.Count; i++)
            if (last == null || saves[i].lastPlayedTimeAsLong > last.lastPlayedTimeAsLong) last = saves[i];
        if (last == null)
        {
            Plugin.Logger.LogWarning("Host the latest save: no save in the folder. Use the menu.");
            return false;
        }
        HostSave(last, SavedPlayers(last.filenameUid));
        return true;
    }

    /// <summary>
    /// Host a save with the same steps as HostMenuConfirm.ActionStart and PlayerCountMenu.ActionProceed.
    /// </summary>
    // [impl->REQ-SAVE-SWITCH]
    internal static void HostSave(SaveData save, int players)
    {
        BaseManager.instance.saveManager.currentData = save;
        SaveManager.WriteCurrentSaveData();
        NetworkMinder.SetServerPassword(save.password);
        PlayerCountSwapper.playerCount = players switch { 3 => PlayerCount.PlayerCount3, 4 => PlayerCount.PlayerCount4, _ => PlayerCount.PlayerCount2 };
        // The host menu (MagicCode) makes the join code. The EOS lobby needs it.
        EOSPrivateLobbyManager.Instance.GenerateGameCode();
        Plugin.Logger.LogInfo($"Host save '{save.slotName}' ({save.filenameUid}), {players} players, join code {EOSPrivateLobbyManager.Instance.CurrentLobbyCode}.");
        NetworkMinder.StartHost();
    }

    /// <summary>The player count in the Diary file of a slot: 2, 3 or 4. It is 2 if the file has no count.</summary>
    internal static int SavedPlayers(string slotUid)
    {
        try
        {
            int n = DiaryStore.Read(DiaryStore.PathFor(StoreDir, slotUid))?.PlayerCount ?? 0;
            return n is >= 2 and <= 4 ? n : 2;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Diary: no player count for slot {slotUid}: {e.Message}");
            return 2;
        }
    }

    private static int PlayersOf(PlayerCount c) => c switch { PlayerCount.PlayerCount3 => 3, PlayerCount.PlayerCount4 => 4, _ => 2 };

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

/// <summary>
/// After a game load, keeps a train car loaded until the restored prop touches it (T4b, #4).
/// The game unloads a car (parks it below the ground, colliders off) when nothing touches it and no
/// player is near it. A car with a contact (a prop or a player) stays loaded.
/// </summary>
[HarmonyPatch(typeof(TrainCar), nameof(TrainCar.UpdateCullState))]
internal static class CarHoldPatch
{
    // The held cars, and the time when each hold ends at the latest.
    internal static readonly Dictionary<IntPtr, (TrainCar Car, float Until)> Held = new();

    internal static void Hold(TrainCar car, string reason)
    {
        if (car == null || Held.ContainsKey(car.Pointer)) return;
        Held[car.Pointer] = (car, Time.time + 10f);
        Plugin.Logger.LogInfo($"Diary: car hold: {car.name} ({reason}).");
    }

    // [impl->REQ-PLATFORM-PROPS]
    [HarmonyPrefix]
    private static bool Before(TrainCar trainCar, NetworkedTrain.CullMode cullMode)
    {
        if (trainCar == null || !Held.ContainsKey(trainCar.Pointer)) return true;
        if (trainCar.cullState) TrainCar.SetCullState(trainCar, false, cullMode);
        return false;
    }

    /// <summary>
    /// Ends the hold of a car when something touches it (a prop, a body or a player). Then the game
    /// keeps the car loaded. A hold also ends after 10 s. Then the game rule applies again.
    /// </summary>
    // [impl->REQ-PLATFORM-PROPS]
    internal static void Release()
    {
        if (Held.Count == 0) return;
        foreach ((IntPtr key, (TrainCar car, float until)) in Held.ToList())
        {
            int contacts = car?.platformingBody?.contacts?.Count ?? 0;
            if (contacts == 0 && Time.time < until) continue;
            Held.Remove(key);
            Plugin.Logger.LogInfo(contacts > 0
                ? $"Diary: car hold released: {car.name} has {contacts} contacts."
                : $"Diary: car hold ended after 10 s: {car?.name} has no contact. The game rule applies.");
        }
    }
}
