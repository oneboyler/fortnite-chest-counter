using FortniteReplayReader.Models.NetFieldExports.Weapons;
using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace SquadChests;

// Rockets that can break chests from range. Each records who fired it (Instigator) and where it blew up (StopLocation).

[NetFieldExportGroup("/Motorboat/Weapons/B_Prj_Meatball_Missile.B_Prj_Meatball_Missile_C", minimalParseMode: ParseMode.Minimal)]
public class BoatRocketExport : BaseRocketLauncherProjectile { }

[NetFieldExportGroup("/DelilahFlavorWeaponsGameplay/Gameplay/WharfShaft_Launcher/B_Prj_WharfShaft_Rocket.B_Prj_WharfShaft_Rocket_C", minimalParseMode: ParseMode.Minimal)]
public class LauncherRocketExport : BaseRocketLauncherProjectile { }

/// <summary>Sent when a rocket explodes; lists what the blast hit.</summary>
[NetFieldExportGroup("/Script/FortniteGame.FortGameplayEffectDeliveryActor:BroadcastExplosion", minimalParseMode: ParseMode.Minimal)]
public class ExplosionExport : INetFieldExportGroup
{
    [NetFieldExport("HitActors", RepLayoutCmdType.DynamicArray)]
    public uint[] HitActors { get; set; }

    [NetFieldExport("HitResults", RepLayoutCmdType.DynamicArray)]
    public FHitResult[] HitResults { get; set; }
}

[NetFieldExportClassNetCache("B_Prj_Meatball_Missile_C_ClassNetCache", minimalParseMode: ParseMode.Minimal)]
public class BoatRocketCache
{
    [NetFieldExportRPC("BroadcastExplosion", "/Script/FortniteGame.FortGameplayEffectDeliveryActor:BroadcastExplosion", isFunction: true)]
    public ExplosionExport BroadcastExplosion { get; set; }
}

[NetFieldExportClassNetCache("B_Prj_WharfShaft_Rocket_C_ClassNetCache", minimalParseMode: ParseMode.Minimal)]
public class LauncherRocketCache
{
    [NetFieldExportRPC("BroadcastExplosion", "/Script/FortniteGame.FortGameplayEffectDeliveryActor:BroadcastExplosion", isFunction: true)]
    public ExplosionExport BroadcastExplosion { get; set; }
}
