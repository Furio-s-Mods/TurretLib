using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace TurretLib;

public class BlockBehaviorShiftDelayedPickup(Block block) : BlockBehavior(block)
{
    public ShiftDelayedPickupProperties Properties { get; private set; } = null!;

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        string blockCode = block?.Code?.ToShortString() ?? "UnknownBlock";

        Properties = properties.AsObject<ShiftDelayedPickupProperties>() ?? throw new InvalidOperationException(
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
                MouseButton = EnumMouseButton.Right,
                HotKeyCodes = new string[] { "sneak" }
            }
        };
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (player.Entity.Controls.Sneak)
        {
            handling = EnumHandling.PreventDefault;

            if (world.Side == EnumAppSide.Server)
            {
                player.Entity.WatchedAttributes.SetFloat(HudLoadProgress.watched_key, 0f);
                player.Entity.WatchedAttributes.MarkPathDirty(HudLoadProgress.watched_key);
            }
            return true;
        }

        handling = EnumHandling.PassThrough;
        return false;
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer player, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (!player.Entity.Controls.Sneak)
        {
            handling = EnumHandling.PassThrough;
            return false;
        }

        handling = EnumHandling.PreventDefault;

        float progress = Math.Min(1f, secondsUsed / Properties.RequiredPickupTime);

        if (world.Side == EnumAppSide.Server)
        {
            player.Entity.WatchedAttributes.SetFloat(HudLoadProgress.watched_key, progress);
            player.Entity.WatchedAttributes.MarkPathDirty(HudLoadProgress.watched_key);

            if (secondsUsed % 0.25f < 0.05f)
            {
                world.PlaySoundAt(Properties.LoadingSound, blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z, player, true, 16);
            }
        }

        return secondsUsed < Properties.RequiredPickupTime;
    }

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer player, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (!player.Entity.Controls.Sneak)
        {
            handling = EnumHandling.PassThrough;
            return;
        }

        handling = EnumHandling.PreventDefault;

        if (world.Side == EnumAppSide.Server)
        {
            player.Entity.WatchedAttributes.SetFloat(HudLoadProgress.watched_key, 0f);
            player.Entity.WatchedAttributes.RemoveAttribute(HudLoadProgress.watched_key);
            player.Entity.WatchedAttributes.MarkPathDirty(HudLoadProgress.watched_key);

            if (secondsUsed >= Properties.RequiredPickupTime)
            {
                if (world.Claims != null && world.Claims.TestAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak) != EnumWorldAccessResponse.Granted)
                {
                    return;
                }

                Block targetBlock = world.BlockAccessor.GetBlock(blockSel.Position);
                ItemStack dropStack = targetBlock.OnPickBlock(world, blockSel.Position);

                if (dropStack != null && !player.InventoryManager.TryGiveItemstack(dropStack))
                {
                    world.SpawnItemEntity(dropStack, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                BlockEntity be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
                be?.OnBlockRemoved();

                world.PlaySoundAt(Properties.DoneSound, blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z, null, true, 32);
                world.BlockAccessor.SetBlock(0, blockSel.Position);
                world.BlockAccessor.TriggerNeighbourBlockUpdate(blockSel.Position);
            }
        }
    }
}