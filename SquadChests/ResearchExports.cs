using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models.Enums;

namespace SquadChests;

// The replay owner's granted abilities. Only the owner's own list is in the replay (it replicates to the owning
// player only). Every season treasure from a chest grants a new GA_TreasureHandler_Generic entry, so counting
// those entries counts the owner's treasures. The entry doesn't say which prize it was.

[NetFieldExportClassNetCache("FortAbilitySystemComponentAthena_ClassNetCache", minimalParseMode: ParseMode.Full)]
public class AbilitySystemCache
{
    // enablePropertyChecksum is passed on inverted by the parser: true here = no checksum bit, which is what these elements use.
    [NetFieldExportRPC("ActivatableAbilities", "/Script/FortniteGame.FortAbilitySystemComponentAthena", enablePropertyChecksum: true)]
    public AbilitySpecExport[] ActivatableAbilities { get; set; }
}

[NetFieldExportGroup("/Script/FortniteGame.FortAbilitySystemComponentAthena", minimalParseMode: ParseMode.Full)]
public class AbilitySpecExport : INetFieldExportGroup
{
    [NetFieldExport("HandleValue", RepLayoutCmdType.Ignore)]
    public object? HandleValue { get; set; }

    [NetFieldExport("Handle", RepLayoutCmdType.PropertyInt)]
    public int? Handle { get; set; }

    [NetFieldExport("ability", RepLayoutCmdType.PropertyObject)]
    public uint? Ability { get; set; }

    [NetFieldExport("Level", RepLayoutCmdType.PropertyInt)]
    public int? Level { get; set; }

    [NetFieldExport("InputID", RepLayoutCmdType.PropertyInt)]
    public int? InputID { get; set; }

    [NetFieldExport("SourceObject", RepLayoutCmdType.PropertyObject)]
    public uint? SourceObject { get; set; }
}
