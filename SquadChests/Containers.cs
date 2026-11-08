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

// Season variants with their own class (the replay names the exact class, so each needs its own entry).
[NetFieldExportGroup("/SpritePosters/Gameplay/RelicChests/B_Delilah_TieredChest_Relics_Variant1.B_Delilah_TieredChest_Relics_Variant1_C", minimalParseMode: ParseMode.Minimal)]
public class RelicChestV1Export : ContainerExport { }

[NetFieldExportGroup("/SpritePosters/Gameplay/RelicChests/B_Delilah_TieredChest_Relics_Variant2.B_Delilah_TieredChest_Relics_Variant2_C", minimalParseMode: ParseMode.Minimal)]
public class RelicChestV2Export : ContainerExport { }

[NetFieldExportGroup("/ExtractionRelicFinder/Gameplay/Chests/B_Athena_BuriedChest_RelicFinder.B_Athena_BuriedChest_RelicFinder_C", minimalParseMode: ParseMode.Minimal)]
public class RelicFinderChestExport : ContainerExport
{
    [NetFieldExport("DugUp", RepLayoutCmdType.PropertyBool)]
    public bool? DugUp { get; set; }
}

[NetFieldExportGroup("/SpritePosters/Blueprints/Items/Booty/B_SpritePosters_Booty_Chest_Variant1.B_SpritePosters_Booty_Chest_Variant1_C", minimalParseMode: ParseMode.Minimal)]
public class BootyChestV1Export : ContainerExport { }

[NetFieldExportGroup("/SpritePosters/Blueprints/Items/Booty/B_SpritePosters_Booty_Chest_Variant2.B_SpritePosters_Booty_Chest_Variant2_C", minimalParseMode: ParseMode.Minimal)]
public class BootyChestV2Export : ContainerExport { }

// Checked and left out on purpose (Nov 2026, 102 replays):
//   Tiered_Chest_5_Child: never opened by anyone in 15 sightings, only broken -> decoration, not a lootable chest.
//   BGA_DelilahTreasureHuntAnimatedChest, BP_ExtractionSite_Chest_Relic, BGA_ExtractionPointGameplay_Chest: never searchable.
//   Loot cannons, supply drops, ammo boxes, barrels, crates: not chests.

public static class ChestKinds
{
    public static string Of(ContainerExport c) => c switch
    {
        RareChestExport => "Rare Chest",
        RelicChestExport or RelicChestV1Export or RelicChestV2Export => "Relic Chest",
        BuriedChestExport or RelicFinderChestExport => "Buried Treasure",
        BootyChestExport or BootyChestV1Export or BootyChestV2Export => "Booty Chest",
        _ => "Chest",
    };
}

// Special pickups that use their own actor class instead of the normal FortPickupAthena.
[NetFieldExportGroup("/SpritePosters/Blueprints/Items/Booty/B_Pickup_SpritePosters_Booty.B_Pickup_SpritePosters_Booty_C", minimalParseMode: ParseMode.Minimal)]
public class BootyPickupExport : FortniteReplayReader.Models.NetFieldExports.FortPickup { }

[NetFieldExportGroup("/SpriteExtractionGameplay/Gameplay/SoonToBeMovedToExtractionCore/BP_ExtractablePickup.BP_ExtractablePickup_C", minimalParseMode: ParseMode.Minimal)]
public class ExtractablePickupExport : FortniteReplayReader.Models.NetFieldExports.FortPickup { }
