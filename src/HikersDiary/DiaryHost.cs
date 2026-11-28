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
    private static bool _propDropped;
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

        if (_platformPending.Count > 0 && Time.fixedTime >= _platformAt) RestorePlatformProps();
        if (!string.IsNullOrEmpty(PropOnPlatform) && !_propDropped && Time.fixedTime >= _platformAt + 1f) DropPropOnPlatform();
        if (_dropped != null && Time.time >= _dropCheckAt) CheckDrop();

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
        // The props come first, so that RestoreProps does not move an item that a body holds.
        RestoreProps(save.Props);
        RestoreBodies(save.Bodies, host);
        RestoreVehicles(save.Vehicles);
        // [impl->REQ-PLATFORM-PROPS] The vehicles move to their saved state in the next physics steps.
        // Then Update moves the props on the platforms (RestorePlatformProps).
        _platformAt = Time.fixedTime + 2f * Time.fixedDeltaTime;
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
            Transform t = c.transform;
            Vector3 p = t.position;
            Quaternion r = t.rotation;
            save.Bodies.Add(new BodyRecord(c.identifier, new[] { p.x, p.y, p.z }, new[] { r.x, r.y, r.z, r.w },
                c.headColorIndex, c.torsoColorIndex, c.legsColorIndex, BodyItems(c)));
        }
        SaveProps(save.Props);
        LogSwitchStates();
        SaveVehicles(save.Vehicles);
        PlayerCharacter host = LocalPlayer();
        if (host != null)
        {
            save.Players.Add(SavePlayer(host));
            LogNearSeats(host);
        }
        save = save with { EnviroTicks = EnviroTicksNow() };
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
            Transform t = p.transform;
            Vector3 pos = t.position;
            Quaternion r = t.rotation;
            // [impl->REQ-PLATFORM-PROPS] A prop on a moving platform keeps its pose relative to the platform.
            StickyPlatform sp = p.houseNetworkTransform != null ? p.houseNetworkTransform.stickyPlatform : null;
            string platform = null;
            if (sp != null)
            {
                Transform pt = Frame(sp);
                platform = PathOf(sp.transform);
                pos = pt.InverseTransformPoint(pos);
                r = Quaternion.Inverse(pt.rotation) * r;
            }
            into.Add(new PropRecord(p.savablePropGuid ?? "", p.networkIdentity != null ? p.networkIdentity.sceneId : 0,
                PathOf(t), new[] { pos.x, pos.y, pos.z }, new[] { r.x, r.y, r.z, r.w }, platform));
        }
    }

    // The props on a moving platform that wait for the vehicles (T4b), and the physics time to move them.
    private static readonly List<(Prop Prop, PropRecord Record)> _platformPending = new();
    private static float _platformAt;

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
            if (rec.Platform != null) { _platformPending.Add((p, rec)); continue; }
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
    /// Moves each waiting prop to its saved pose on its platform. The vehicles have moved by now.
    /// When the prop touches the platform, the game attaches it again
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
        int found = 0;
        foreach ((Prop p, PropRecord rec) in _platformPending)
        {
            // A home or a player that took the prop after RestoreProps (a body, the host) keeps it.
            if (p == null || !IsLoose(p) || !platforms.TryGetValue(rec.Platform, out StickyPlatform sp)) continue;
            Transform pt = Frame(sp);
            Vector3 pos = pt.TransformPoint(new Vector3(rec.Position[0], rec.Position[1], rec.Position[2]));
            Quaternion rot = pt.rotation * new Quaternion(rec.Rotation[0], rec.Rotation[1], rec.Rotation[2], rec.Rotation[3]);
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
            found++;
            Plugin.Logger.LogInfo($"Diary: prop {PathOf(p.transform)} to {pos} on {rec.Platform} (platform at {pt.position}).");
        }
        Plugin.Logger.LogInfo($"Diary: restored {found} of {_platformPending.Count} props on moving platforms " +
            $"({platforms.Count} platforms, {duplicate.Count} paths with two or more platforms).");
        _platformPending.Clear();
    }

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
        Plugin.Logger.LogInfo($"Diary: test aid: 3 s after the drop, prop at {p.transform.position}, relative {frame.InverseTransformPoint(p.transform.position)}, " +
            $"stickyPlatform {(p.houseNetworkTransform?.stickyPlatform != null ? PathOf(p.houseNetworkTransform.stickyPlatform.transform) : "none")}, " +
            $"PlatformingBody {(pb != null ? $"yes, connected {(pb.connectedPlatform != null ? "yes" : "no")}, contacts {pb.contacts?.Count ?? -1}" : "no")}, " +
            $"kinematic {p.rb.isKinematic}, sleeping {p.rb.IsSleeping()}.");
    }

    /// <summary>Test aid for T4b: drops one loose prop onto a moving platform, because a single-PC test has no player input.</summary>
    private static void DropPropOnPlatform()
    {
        _propDropped = true;
        StickyPlatform target = null;
        foreach (StickyPlatform sp in Object.FindObjectsOfType<StickyPlatform>())
        {
        {
            if (!PathOf(sp.transform).Contains(PropOnPlatform)) continue;
            Plugin.Logger.LogInfo($"Diary: test aid: platform {PathOf(sp.transform)} frame at {Frame(sp).position}.");
            // The nearest platform to the host: physics far from the host has no colliders.
            Vector3 h = LocalPlayer().transform.position;
            if (target == null || (Frame(sp).position - h).sqrMagnitude < (Frame(target).position - h).sqrMagnitude) target = sp;
        }
        }
        Prop prop = null;
        var props = Prop.allProps;
        for (int i = 0; props != null && i < props.Count && prop == null; i++)
            if (props[i] != null && IsLoose(props[i]) && props[i].rb != null && !props[i].rb.isKinematic
                && props[i].GetComponent<Corpse>() == null && props[i].houseNetworkTransform?.stickyPlatform == null)
                prop = props[i];
        if (target == null || prop == null)
        {
            Plugin.Logger.LogWarning($"Diary: test aid: no platform '{PropOnPlatform}' or no loose prop.");
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
        Plugin.Logger.LogInfo($"Diary: test aid: dropped prop {PathOf(prop.transform)} onto {PathOf(target.transform)} (frame {PathOf(frame)} at {frame.position}, collider {(col != null ? PathOf(col.transform) : "none")}) at {at}.");
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
            into.Add(new VehicleRecord("train", PathOf(t.transform), t.NetworksyncSplineIndex, t.NetworksyncDistance,
                t.NetworksyncSpeed, t.NetworktargetSpeed));
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
        return new PlayerRecord(net.identifier ?? "", new[] { p.x, p.y, p.z }, new[] { r.x, r.y, r.z, r.w },
            new[] { head.x, head.y }, net.trueCrouchness, net.isSitting, pose != null ? PathOf(pose.transform) : "", items);
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
        host.grease.Teleport(pos, rot);
        var head = new Vector2(rec.Head[0], rec.Head[1]);
        host.head.headState = head;
        net.NetworkheadState = head;

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
        Plugin.Logger.LogInfo($"Diary: restored host at {pos}, head {head}, items [{string.Join(", ", found)}] of {rec.Items.Count}, " +
            $"pose {pose}, sitting {rec.Sitting}, crouch {rec.Crouch:0.00}.");
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
            Plugin.Logger.LogInfo($"Diary: restored body {b.Identifier} at {pos}, items [{string.Join(", ", found)}] of {b.Items?.Count ?? 0}.");
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
