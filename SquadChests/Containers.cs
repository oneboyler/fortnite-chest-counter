using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models.Enums;

namespace SquadChests;

// Chest-type actors we track. Paths come from Fortnite 43.00 (Chapter 7 Season 3) replays.
// If a future season renames chests, run `SquadChests discover <file.replay>` and add the new path here.

public abstract class ContainerExport : INetFieldExportGroup
{
    [NetFieldExport("bAlreadySearched", RepLayoutCmdType.PropertyBool)]
    public bool? bAlreadySearched { get; set; }

    /// <summary>Pawn that searched (opened) the container. Replicated in recent Fortnite versions.</summary>
    [NetFieldExport("SearchingPawn", RepLayoutCmdType.PropertyObject)]
    public uint? SearchingPawn { get; set; }

    [NetFieldExport("bDestroyed", RepLayoutCmdType.PropertyBool)]
    public bool? bDestroyed { get; set; }

    [NetFieldExport("ReplicatedLootTier", RepLayoutCmdType.PropertyInt)]
    public int? ReplicatedLootTier { get; set; }

    [NetFieldExport("Health", RepLayoutCmdType.PropertyUInt16)]
    public ushort? Health { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Containers/Tiered_Chest_Athena.Tiered_Chest_Athena_C", minimalParseMode: ParseMode.Minimal)]
public class TieredChestExport : ContainerExport { }

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Containers/AlwaysSpawn_NormalChest.AlwaysSpawn_NormalChest_C", minimalParseMode: ParseMode.Minimal)]
public class AlwaysSpawnChestExport : ContainerExport { }

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Containers/AlwaysSpawn_RareChest.AlwaysSpawn_RareChest_C", minimalParseMode: ParseMode.Minimal)]
public class RareChestExport : ContainerExport { }

[NetFieldExportGroup("/RelicChest/Gameplay/B_TieredChest_Relics.B_TieredChest_Relics_C", minimalParseMode: ParseMode.Minimal)]
public class RelicChestExport : ContainerExport { }

[NetFieldExportGroup("/Game/Athena/Items/ForagedItems/BuriedChest/B_Athena_BuriedChest_Chest.B_Athena_BuriedChest_Chest_C", minimalParseMode: ParseMode.Minimal)]
public class BuriedChestExport : ContainerExport
{
    [NetFieldExport("DugUp", RepLayoutCmdType.PropertyBool)]
    public bool? DugUp { get; set; }
}

[NetFieldExportGroup("/SpritePosters/Blueprints/Items/Booty/B_SpritePosters_Booty_Chest.B_SpritePosters_Booty_Chest_C", minimalParseMode: ParseMode.Minimal)]
public class BootyChestExport : ContainerExport { }

public static class ChestKinds
{
    public static string Of(ContainerExport c) => c switch
    {
        RareChestExport => "Rare Chest",
        RelicChestExport => "Relic Chest",
        BuriedChestExport => "Buried Treasure",
        BootyChestExport => "Booty Chest",
        _ => "Chest",
    };
}

// Special pickups that use their own actor class instead of the normal FortPickupAthena.
[NetFieldExportGroup("/SpritePosters/Blueprints/Items/Booty/B_Pickup_SpritePosters_Booty.B_Pickup_SpritePosters_Booty_C", minimalParseMode: ParseMode.Minimal)]
public class BootyPickupExport : FortniteReplayReader.Models.NetFieldExports.FortPickup { }

[NetFieldExportGroup("/SpriteExtractionGameplay/Gameplay/SoonToBeMovedToExtractionCore/BP_ExtractablePickup.BP_ExtractablePickup_C", minimalParseMode: ParseMode.Minimal)]
public class ExtractablePickupExport : FortniteReplayReader.Models.NetFieldExports.FortPickup { }
