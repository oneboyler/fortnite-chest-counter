using FortniteReplayReader;
using FortniteReplayReader.Models.NetFieldExports;
using Unreal.Core;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace SquadChests;

/// <summary>
/// Extends the open-source replay reader to watch chests, pickups, pawns and player states
/// and turn them into a <see cref="MatchResult"/>.
/// </summary>
public class LootReader : ReplayReader
{
    // ----- raw state collected while reading -----
    float _frameTime;                                            // replay time (s) of the frame being read
    uint? _recorderState;                                        // player-state actor of whoever recorded the replay
    readonly Dictionary<uint, uint> _pawnToState = new();        // pawn actor guid -> player-state actor guid
    readonly Dictionary<uint, string> _stateToId = new();        // player-state actor guid -> epic id / bot id
    readonly Dictionary<uint, int> _stateToTeam = new();
    readonly Dictionary<uint, PawnTrack> _pawnTracks = new();    // pawn actor guid -> location history
    readonly Dictionary<uint, ChestState> _chests = new();       // chest actor guid -> state
    readonly Dictionary<uint, PickupInfo> _pickups = new();      // pickup actor guid -> first sighting
    readonly Dictionary<uint, (uint Pawn, float T)> _lastHit = new(); // object guid -> last pawn that damaged it
    readonly Dictionary<uint, Rocket> _rockets = new();          // rocket actor guid -> who fired it / where it blew up
    readonly Dictionary<uint, (uint Rocket, float T)> _blastHits = new(); // object guid -> rocket whose explosion hit it

    public HashSet<string> UnknownChestGroups { get; } = new();
    const string TreasureHandler = "/DelilahTreasureHuntGameplay/Gameplay/Abilities/PrizeHandlers/GA_TreasureHandler_Generic";
    bool _gotTreasure;
    List<double> _treasureTimes = new();
    // First grant of each ability-list entry: (channel, element) -> ability class guid and when. Only the replay
    // owner's own list is in the replay.
    readonly Dictionary<(uint, int), (uint Ability, float T)> _grants = new();

    protected override void OnNetDeltaRead(uint channelIndex, NetDeltaUpdate update)
    {
        base.OnNetDeltaRead(channelIndex, update);
        if (update.Export is AbilitySpecExport { Ability: uint ability } && ability != 0)
            _grants.TryAdd((channelIndex, update.ElementIndex), (ability, _frameTime));
    }

    public LootReader() : base(null, ParseMode.Full) { }

    // Unknown event types are skipped (only matters in debug mode, but be safe).
    public override void ReadEvent(FArchive archive)
    {
        try { base.ReadEvent(archive); } catch (Unreal.Core.Exceptions.UnknownEventException) { }
    }

    // Peek at the frame time, then let the base class read the frame as usual.
    public override void ReadDemoFrameIntoPlaybackPackets(FArchive archive)
    {
        var pos = archive.Position;
        if (archive.NetworkVersion >= NetworkVersionHistory.HISTORY_MULTIPLE_LEVELS) archive.ReadInt32();
        _frameTime = archive.ReadSingle();
        archive.Seek(pos, SeekOrigin.Begin);
        base.ReadDemoFrameIntoPlaybackPackets(archive);
    }

    uint? ActorOf(uint channelIndex) => Channels[channelIndex]?.Actor?.ActorNetGUID.Value;

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {
        base.OnExportRead(channelIndex, exportGroup);
        if (exportGroup == null) return;

        switch (exportGroup)
        {
            case GameState gs:
                if (gs.RecorderPlayerState?.Value is uint rec && rec != 0) _recorderState ??= rec;
                break;

            case FortPlayerState ps when ActorOf(channelIndex) is uint stateGuid:
                var id = ps.UniqueId ?? ps.UniqueID ?? ps.BotUniqueId;
                if (!string.IsNullOrEmpty(id)) _stateToId[stateGuid] = id;
                if (ps.TeamIndex is int team) _stateToTeam[stateGuid] = team;
                break;

            // Damage dealt by a pawn (multicast on the attacker's channel). Used to see who broke a chest.
            case FortniteReplayReader.Models.NetFieldExports.RPC.BatchedDamageCues dmg when ActorOf(channelIndex) is uint attacker:
                if (dmg.NonPlayerHitActor is uint np && np != 0) _lastHit[np] = (attacker, _frameTime);
                if (dmg.HitActor is uint ha && ha != 0) _lastHit[ha] = (attacker, _frameTime);
                break;

            // Boat rockets / rocket launcher: who fired it and where it exploded.
            case FortniteReplayReader.Models.NetFieldExports.Weapons.BaseRocketLauncherProjectile pr when ActorOf(channelIndex) is uint rocketGuid:
                if (!_rockets.TryGetValue(rocketGuid, out var rocket))
                {
                    _rockets[rocketGuid] = rocket = new Rocket { Kind = pr is BoatRocketExport ? "Boat rocket" : "Rocket launcher" };
                }
                if (pr.Instigator != 0) rocket.Instigator = pr.Instigator;
                if (pr.ReplicatedMovement.Location is FVector rl) rocket.LastPos = rl;
                if (pr.StopLocation is FVector stop && (stop.X != 0 || stop.Y != 0)) { rocket.StopPos = stop; rocket.ExplodedAt ??= _frameTime; }
                if (pr.bHasExploded) rocket.ExplodedAt ??= _frameTime;
                break;

            case ExplosionExport ex when ActorOf(channelIndex) is uint blastRocket:
                if (_rockets.TryGetValue(blastRocket, out var br)) br.ExplodedAt ??= _frameTime;
                foreach (var a in ex.HitActors ?? []) if (a != 0) _blastHits[a] = (blastRocket, _frameTime);
                foreach (var h in ex.HitResults ?? []) if (h?.Actor is uint ha2 && ha2 != 0) _blastHits[ha2] = (blastRocket, _frameTime);
                break;

            case PlayerPawn pawn when ActorOf(channelIndex) is uint pawnGuid:
                if (pawn.PlayerState is uint st && st != 0) _pawnToState[pawnGuid] = st;
                if (pawn.ReplicatedMovement?.Location is FVector loc)
                {
                    if (!_pawnTracks.TryGetValue(pawnGuid, out var track)) _pawnTracks[pawnGuid] = track = new PawnTrack();
                    track.Add(_frameTime, loc);
                }
                break;

            case ContainerExport c when ActorOf(channelIndex) is uint chestGuid:
                if (!_chests.TryGetValue(chestGuid, out var chest))
                {
                    chest = new ChestState { Kind = ChestKinds.Of(c), FirstSeen = _frameTime, FirstSeenSearched = c.bAlreadySearched == true };
                    _chests[chestGuid] = chest;
                }
                if (c.ReplicatedLootTier is int tier) chest.Tier = tier;
                if (c.SearchingPawn is uint sp && sp != 0) chest.SearchingPawn = sp;
                if (c.bAlreadySearched == true && chest.SearchedAt == null) chest.SearchedAt = _frameTime;
                if (c.bDestroyed == true && chest.DestroyedAt == null) chest.DestroyedAt = _frameTime;
                break;

            case FortPickup pk when ActorOf(channelIndex) is uint pickupGuid:
                if (!_pickups.TryGetValue(pickupGuid, out var info))
                {
                    info = new PickupInfo
                    {
                        Time = _frameTime,
                        Prefix = pk switch { BootyPickupExport => "[Booty] ", ExtractablePickupExport => "[Relic] ", _ => "" },
                    };
                    _pickups[pickupGuid] = info;
                }
                if (pk.ItemDefinition?.Name is string item && info.Item == null) info.Item = info.Prefix + item;
                if (pk.bTossedFromContainer == true) info.FromContainer = true;
                if (pk.PawnWhoDroppedPickup is uint dp && dp != 0) info.DroppedByPawn = dp;
                if (info.Pos is null)
                {
                    if (pk.LootInitialPosition is FVector li && (li.X != 0 || li.Y != 0)) info.Pos = li;
                    else if (Channels[channelIndex]?.Actor?.Location is FVector al) info.Pos = al;
                }
                break;
        }
    }

    protected override void Cleanup()
    {
        // Season treasure (gizmo, jam track, car...): every treasure you get hands you a new prize-handler ability.
        // The replay only shows that you got one, not which.
        _gotTreasure = _netGuidCache.NetGuidToPathName.Values.Any(p => p.Contains(TreasureHandler, StringComparison.Ordinal));
        _treasureTimes = _grants.Values
            .Where(g => _netGuidCache.NetGuidToPathName.TryGetValue(g.Ability, out var p) && p.Contains("GA_TreasureHandler_Generic", StringComparison.Ordinal))
            .Select(g => Math.Round(g.T, 1)).OrderBy(t => t).ToList();

        // Before the cache is wiped: note chest-like groups we don't know yet (helps after a season change).

        foreach (var kv in _netGuidCache.NetFieldExportGroupMap)
        {
            if (kv.Key.Contains("Chest", StringComparison.OrdinalIgnoreCase)
                && kv.Value.NetFieldExports.Any(f => f?.Name == "bAlreadySearched")
                && !_netFieldParser.WillReadType(kv.Key))
            {
                UnknownChestGroups.Add(kv.Key);
            }
        }
        base.Cleanup();
    }

    // ----- turning raw state into a result -----

    public static MatchResult Read(string path)
    {
        var fi = new FileInfo(path);
        var result = new MatchResult { File = fi.Name, Size = fi.Length, LastWrite = fi.LastWriteTimeUtc };
        try
        {
            // Fortnite may still have the file open, so allow shared access.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            ms.Position = 0;

            var reader = new LootReader();
            var replay = reader.ReadReplay(ms);
            reader.Fill(result, replay);
        }
        catch (Exception ex)
        {
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
        }
        return result;
    }

    void Fill(MatchResult r, FortniteReplayReader.Models.FortniteReplay replay)
    {
        r.IsLive = replay.Info?.IsLive ?? false;
        // The replay stores local time (it matches the time in the file name), so keep it as-is.
        r.Started = replay.Info?.Timestamp is DateTime ts ? DateTime.SpecifyKind(ts, DateTimeKind.Local) : null;
        r.LengthSeconds = (replay.Info?.LengthInMs ?? 0) / 1000.0;
        r.Version = replay.Header?.Branch;
        r.Playlist = replay.GameData?.CurrentPlaylist;
        r.TeamPlacement = replay.TeamStats?.Position is uint pos ? (int)pos : null;
        r.ReaderVersion = MatchResult.CurrentReaderVersion;
        r.GotTreasure = _gotTreasure;
        r.TreasureTimes = _treasureTimes;
        var lobby = (replay.PlayerData ?? []).Where(p => (p.EpicId ?? p.BotId) != null).ToList();
        r.PlayerCount = lobby.Count;
        r.HumanCount = lobby.Count(p => !p.IsBot);
        r.MaxPlayers = replay.GameData?.MaxPlayers;
        foreach (var u in UnknownChestGroups) r.Warnings.Add($"Unknown chest type not counted: {u}");

        // Names from the base reader's player list (keyed by epic id / bot id).
        var names = new Dictionary<string, (string Name, bool Bot)>();
        foreach (var p in replay.PlayerData ?? [])
        {
            var pid = p.EpicId ?? p.BotId;
            if (pid != null) names[pid] = (p.PlayerName ?? p.StreamerModeName ?? "(unknown)", p.IsBot);
        }

        string? PlayerOfPawn(uint? pawn) =>
            pawn is uint pw && _pawnToState.TryGetValue(pw, out var st) && _stateToId.TryGetValue(st, out var pid) ? pid : null;
        int? TeamOfPawn(uint? pawn) =>
            pawn is uint pw && _pawnToState.TryGetValue(pw, out var st) && _stateToTeam.TryGetValue(st, out var t) ? t : null;
        FVector? PawnPosAt(uint? pawn, float t) =>
            pawn is uint pw && _pawnTracks.TryGetValue(pw, out var tr) ? tr.At(t, 3f) : null;

        // Who recorded it and who was in their squad.
        int? ownerTeam = null;
        if (_recorderState is uint rs && _stateToId.TryGetValue(rs, out var ownerId))
        {
            r.OwnerId = ownerId;
            ownerTeam = _stateToTeam.TryGetValue(rs, out var ot) ? ot : null;
        }
        if (ownerTeam != null)
        {
            foreach (var (st, team) in _stateToTeam)
            {
                if (team != ownerTeam || !_stateToId.TryGetValue(st, out var pid)) continue;
                var n = names.GetValueOrDefault(pid, (Name: "(unknown)", Bot: false));
                r.Squad.Add(new PlayerRef { Id = pid, Name = n.Name, IsBot = n.Bot });
            }
        }
        r.OwnerName = r.Squad.FirstOrDefault(s => s.Id == r.OwnerId)?.Name;

        // Opened chests: the opener's position at that moment stands in for the chest's position
        // (map-placed chests don't send their own location).
        foreach (var c in _chests.Values)
        {
            if (c.SearchedAt is float st) c.Pos = PawnPosAt(c.SearchingPawn, st);
        }

        // Link loot that flew out of a container to the chest it came from:
        // a chest opened/destroyed just before the pickup appeared, near where the loot started flying.
        var events = new List<LootEvent>();
        foreach (var c in _chests.Values)
        {
            bool seenLive(float t) => !(c.FirstSeenSearched && t == c.FirstSeen);
            if (c.SearchedAt is float so && seenLive(so)) events.Add(new LootEvent { Chest = c, T = so, Pos = c.Pos, Destroy = false });
            else if (c.DestroyedAt is float sd) events.Add(new LootEvent { Chest = c, T = sd, Pos = null, Destroy = true });
        }
        foreach (var pk in _pickups.Values.Where(p => p.FromContainer && p.Item != null && p.DroppedByPawn == null).OrderBy(p => p.Time))
        {
            LootEvent? best = null; double bestScore = double.MaxValue;
            foreach (var e in events)
            {
                var dt = pk.Time - e.T;
                if (dt < -0.25 || dt > (e.Destroy ? 0.75 : 2.5)) continue;
                double dist;
                if (e.Pos is FVector cp && pk.Pos is FVector pp) dist = Dist(cp, pp);
                else if (e.Destroy && e.Pos is null) dist = 400;   // first loot of a destroyed chest defines where it was
                else continue;
                if (dist > (e.Destroy ? 500 : 800)) continue;
                var score = dist + dt * 100;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            if (best == null) continue;
            best.Chest.Items.Add(pk.Item!);
            if (best.Destroy && best.Pos is null) best.Pos = best.Chest.Pos = pk.Pos;
        }

        // Destroyed chests have no "who" in the replay, so work it out, most reliable first:
        //   1. a recorded damage hit on the chest            -> that player
        //   2. a rocket explosion that lists the chest as hit -> whoever fired the rocket
        //   3. a rocket that blew up within 8 m of the chest just before it broke -> whoever fired it
        //   4. otherwise the closest player within 6 m (a guess)
        foreach (var (guid, c) in _chests.Where(kv => kv.Value.DestroyedAt != null && kv.Value.SearchedAt == null))
        {
            var t = c.DestroyedAt!.Value;
            if (_lastHit.TryGetValue(guid, out var hit) && hit.T >= t - 5 && hit.T <= t + 1)
            {
                c.DestroyerPawnGuess = hit.Pawn;
                c.DestroyerExact = true;
                continue;
            }
            if (_blastHits.TryGetValue(guid, out var blast) && blast.T >= t - 3 && blast.T <= t + 1
                && _rockets.TryGetValue(blast.Rocket, out var hitBy) && hitBy.Instigator is uint inst1)
            {
                c.DestroyerPawnGuess = inst1;
                c.DestroyerExact = true;
                c.DestroyedWith = hitBy.Kind;
                continue;
            }
            if (c.Pos is not null)
            {
                Rocket? near = null; double nearD = 800;   // 8 m
                foreach (var rk in _rockets.Values)
                {
                    if (rk.ExplodedAt is not float et || et < t - 3 || et > t + 1 || rk.Instigator == null) continue;
                    if ((rk.StopPos ?? rk.LastPos) is not FVector rp) continue;
                    var d = Dist(rp, c.Pos);
                    if (d < nearD) { nearD = d; near = rk; }
                }
                if (near != null)
                {
                    c.DestroyerPawnGuess = near.Instigator;
                    c.DestroyerExact = true;
                    c.DestroyedWith = near.Kind;
                    continue;
                }
            }
            if (c.Pos is null) continue;
            uint? bestPawn = null; double bestD = 600;    // 6 m: about melee range plus loot spread
            foreach (var (pawn, track) in _pawnTracks)
            {
                if (track.At(t, 5f) is not FVector p) continue;
                var d = Dist(p, c.Pos!);
                if (d < bestD) { bestD = d; bestPawn = pawn; }
            }
            c.DestroyerPawnGuess = bestPawn;
            if (bestPawn != null) c.DestroyerDistance = Math.Round(bestD / 100, 1);
        }

        foreach (var c in _chests.Values)
        {
            if (c.SearchedAt == null && c.DestroyedAt == null) continue;
            var opened = c.SearchedAt != null;
            var byPawn = opened ? c.SearchingPawn : c.DestroyerPawnGuess;
            var byId = PlayerOfPawn(byPawn);
            r.Chests.Add(new ChestRecord
            {
                Kind = c.Kind,
                Tier = c.Tier,
                Time = Math.Round((c.SearchedAt ?? c.DestroyedAt)!.Value, 1),
                Opened = opened,
                Destroyed = !opened,
                ById = byId,
                ByName = byId != null && names.TryGetValue(byId, out var nm) ? nm.Name : null,
                BySquad = ownerTeam != null && TeamOfPawn(byPawn) == ownerTeam,
                AlreadyOpenWhenSeen = c.FirstSeenSearched,
                ByIsGuess = !opened && !c.DestroyerExact,
                ByDistanceMeters = opened || c.DestroyerExact ? null : c.DestroyerDistance,
                DestroyedWith = opened ? null : c.DestroyedWith,
                Items = c.Items,
            });
        }
        r.Chests.Sort((a, b) => a.Time.CompareTo(b.Time));

        // Every distinct item that appeared anywhere in the lobby (as seen by this replay).
        r.LobbyItems = _pickups.Values.Where(p => p.Item != null)
            .GroupBy(p => p.Item!).ToDictionary(g => g.Key, g => g.Count());
    }

    static double Dist(FVector a, FVector b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    class PawnTrack
    {
        readonly List<float> _t = new();
        readonly List<FVector> _p = new();

        public void Add(float t, FVector p) { _t.Add(t); _p.Add(p); }
        public int Count => _t.Count;

        /// <summary>Location closest in time to <paramref name="t"/>, if one exists within <paramref name="maxGap"/> seconds.</summary>
        public FVector? At(float t, float maxGap)
        {
            if (_t.Count == 0) return null;
            var i = _t.BinarySearch(t);
            if (i < 0) i = ~i;
            int best = -1; float bestGap = float.MaxValue;
            for (int j = Math.Max(0, i - 1); j <= Math.Min(_t.Count - 1, i); j++)
            {
                var gap = Math.Abs(_t[j] - t);
                if (gap < bestGap) { bestGap = gap; best = j; }
            }
            return best >= 0 && bestGap <= maxGap ? _p[best] : null;
        }
    }

    class ChestState
    {
        public string Kind = "Chest";
        public int? Tier;
        public float FirstSeen;
        public bool FirstSeenSearched;
        public uint? SearchingPawn;
        public float? SearchedAt;
        public float? DestroyedAt;
        public uint? DestroyerPawnGuess;
        public bool DestroyerExact;
        public double? DestroyerDistance;
        public string? DestroyedWith;
        public FVector? Pos;
        public List<string> Items = new();
    }

    class LootEvent
    {
        public ChestState Chest = null!;
        public float T;
        public FVector? Pos;
        public bool Destroy;
    }

    class Rocket
    {
        public string Kind = "Rocket";
        public uint? Instigator;     // pawn that fired it
        public FVector? StopPos;     // where it exploded
        public FVector? LastPos;     // last known position, if it never reported StopLocation
        public float? ExplodedAt;
    }

    class PickupInfo
    {
        public float Time;
        public string Prefix = "";
        public string? Item;
        public bool FromContainer;
        public uint? DroppedByPawn;
        public FVector? Pos;
    }
}
