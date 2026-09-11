using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace TurretLib;

public class BlockBehaviorTurretMountable(Block block) : BlockBehavior(block)
{
    public TurretMountableProperties Properties { get; private set; } = null!;

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        string blockCode = block?.Code?.ToShortString() ?? "UnknownBlock";

        Properties = properties.AsObject<TurretMountableProperties>() ?? throw new InvalidOperationException(
            $"[{MainModSystem.ModId}] Block '{blockCode}' requires properties for behavior '{GetType().Name}'."
        );
        Properties.Validate(blockCode);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)
    {
        return new WorldInteraction[]
        {
            new WorldInteraction()
            {
                ActionLangCode = Properties.ActionLangCode,
                MouseButton = EnumMouseButton.Right
            }
        };
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        // Check land access permissions
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;

        if (byPlayer.Entity.Controls.Sneak)
        {
            handling = EnumHandling.PassThrough;
            return false;
        }

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorTurretInventory>() is { } inventory)
        {            
            if (inventory.IsInventorySelection(blockSel))
            {
                // inventory.Api?.Logger.Notification($"[{inventory.Api.Side}] Player '{byPlayer.PlayerName}' interacted with Ammo Box at {blockSel.Position}");
                handling = EnumHandling.PreventDefault;
                bool result = inventory.OnAmmoBoxInteract(byPlayer);
                inventory.Api?.Logger.Notification($"[{world.Side}] OnAmmoBoxInteract result: {result}");
                return result;
            }
        }

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityTurret turret) return false;

        if (turret.Controller != null)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(byPlayer, "occupied", Lang.GetIfExists(Properties.OccupiedErrorLangCode));
            }
            return false;
        }

        double distance = byPlayer.Entity.Pos.DistanceTo(blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
        if (distance > Properties.MountMaxDistance)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(byPlayer, "toofar", Lang.GetIfExists(Properties.TooFarErrorLangCode));
            }
            return false;
        }

        handling = EnumHandling.PreventDefault;
        return byPlayer.Entity.TryMount(turret.Seat);
    }
}