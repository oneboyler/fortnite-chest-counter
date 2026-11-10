namespace SquadChests;

public class MatchResult
{
    public string File { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastWrite { get; set; }
    public string? Error { get; set; }
    /// <summary>
    /// The file couldn't be opened just now (locked, no access), so nothing was read. Not a result: it isn't
    /// saved, and the replay is tried again on the next check.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CouldNotOpen { get; set; }
    public bool IsLive { get; set; }

    public DateTime? Started { get; set; }
    public double LengthSeconds { get; set; }
    public string? Version { get; set; }
    public string? Playlist { get; set; }
    public int? TeamPlacement { get; set; }

    /// <summary>Bumped when the reader starts recording something new, so cached replays get re-read once.</summary>
    /// <remarks>7 was skipped: an unreleased experiment (ammo/food boxes) used it, and games saved while it was on
    /// carry that number. 8: chests are told apart by where they are, not by actor guid (a chest that came back
    /// into range was counted again), and a broken chest goes to the pawn the replay names. 9: special prizes
    /// (<see cref="SpecialPrizes"/>).</remarks>
    public const int CurrentReaderVersion = 9;
    public int ReaderVersion { get; set; }
    /// <summary>You got at least one season treasure (gizmo, jam track, car...) this game. Null = read by an older version.</summary>
    public bool? GotTreasure { get; set; }
    /// <summary>Replay time (s) of each season treasure you got. The replay doesn't say which prize. Null = older version.</summary>
    public List<double>? TreasureTimes { get; set; }
    /// <summary>Number of season treasures you got, or null if this game was read by an older version.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? Treasures => TreasureTimes?.Count ?? (GotTreasure == false ? 0 : null);
    /// <summary>
    /// Treasures that came out of a chest YOU opened or broke (one of your chests within 2 s of the prize).
    /// The rest were found by a teammate: a squad shares its prizes, so they show up in your replay too.
    /// In solo all 9 of 9 lined up with your own chests; a random moment does only 17% of the time.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? OwnTreasures => TreasureTimes == null ? Treasures
        : Squad.All(p => p.Id == OwnerId) ? TreasureTimes.Count
        : TreasureTimes.Count(t => Chests.Any(c => c.ById != null && c.ById == OwnerId && Math.Abs(c.Time - t) <= 2));
    /// <summary>Treasures a teammate found (shared with the whole squad).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? SharedTreasures => Treasures - OwnTreasures;
    /// <summary>
    /// Special (non-gizmo) prizes that came out of a chest within range, yours or anyone else's the replay shows
    /// (so far only ever seen for the owner and a teammate). Null = read by an older version. Recorded only; nothing
    /// shows them yet.
    /// </summary>
    public List<PrizeEvent>? SpecialPrizes { get; set; }
    /// <summary>Everyone in the match, players and bots. Null for matches read by an older version.</summary>
    public int? PlayerCount { get; set; }
    public int? HumanCount { get; set; }
    public int? MaxPlayers { get; set; }

    public string? OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public List<PlayerRef> Squad { get; set; } = new();
    public List<ChestRecord> Chests { get; set; } = new();
    public Dictionary<string, int> LobbyItems { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class PlayerRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsBot { get; set; }
}

/// <summary>One special-prize chest animation. The replay says who and when, not which prize.</summary>
public class PrizeEvent
{
    /// <summary>Seconds into the replay.</summary>
    public double Time { get; set; }
    /// <summary>"" for the plain animation, "Rare" for the rare one (the token was one of those).</summary>
    public string Variant { get; set; } = "";
    public int? Tier { get; set; }
    public bool? Success { get; set; }
    public string? ById { get; set; }
    public string? ByName { get; set; }
    public bool BySquad { get; set; }
}

public class ChestRecord
{
    public string Kind { get; set; } = "Chest";
    public int? Tier { get; set; }
    /// <summary>Seconds into the replay.</summary>
    public double Time { get; set; }
    public bool Opened { get; set; }
    /// <summary>Destroyed without being opened. "By" is who the replay names; a guess only when it names nobody.</summary>
    public bool Destroyed { get; set; }
    public string? ById { get; set; }
    public string? ByName { get; set; }
    public bool BySquad { get; set; }
    /// <summary>For destroyed chests: true when "By" is the closest player rather than a recorded hit.</summary>
    public bool ByIsGuess { get; set; }
    /// <summary>For guessed destroyers: how far (m) that player was from the spilled loot.</summary>
    public double? ByDistanceMeters { get; set; }
    /// <summary>For destroyed chests: "Boat rocket" / "Rocket launcher" when a rocket broke it.</summary>
    public string? DestroyedWith { get; set; }
    /// <summary>Chest was already open the first time it showed up in this replay (opened out of view). Seeing the
    /// same open chest again later is not a new record.</summary>
    public bool AlreadyOpenWhenSeen { get; set; }
    public List<string> Items { get; set; } = new();
}
