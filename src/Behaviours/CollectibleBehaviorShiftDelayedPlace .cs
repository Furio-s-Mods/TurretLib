using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace TurretLib;

public class CollectibleBehaviorShiftDelayedPlace(CollectibleObject collectible) : CollectibleBehavior(collectible)
{
    public ShiftDelayedPlaceProperties Properties { get; private set; } = null!;

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        string collectibleCode = collObj?.Code?.ToShortString() ?? "UnknownCollectible";

        Properties = properties.AsObject<ShiftDelayedPlaceProperties>() ?? throw new InvalidOperationException(
            $"[{MainModSystem.ModId}] Collectible '{collectibleCode}' requires properties for behavior '{GetType().Name}'."
        );
        Properties.Validate(collectibleCode);
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
    {
        handling = EnumHandling.PassThrough;
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

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        EnumAppSide side = byEntity.World.Side;

        if (blockSel == null) return;

        if (byEntity is EntityPlayer player && !player.Player.Entity.Controls.Sneak)
        {
            handling = EnumHandling.PreventDefault;
            return;
        }

        handHandling = EnumHandHandling.PreventDefault;
        handling = EnumHandling.PreventDefault;

        if (side == EnumAppSide.Server && byEntity is EntityPlayer sPlayer)
        {
            sPlayer.Player.Entity.Attributes.SetFloat(HudLoadProgress.watched_key, 0f);
            sPlayer.Player.Entity.WatchedAttributes.MarkPathDirty(HudLoadProgress.watched_key);
        }
    }

    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
    {
        EnumAppSide side = byEntity.World.Side;

        if (blockSel == null) return false;

        if (byEntity is EntityPlayer player && !player.Player.Entity.Controls.Sneak)
        {
            handling = EnumHandling.PreventDefault;
            return false;
        }

        handling = EnumHandling.PreventDefault;
        float progress = Math.Min(1f, secondsUsed / Properties.RequiredPlaceTime);

        if (side == EnumAppSide.Server && byEntity is EntityPlayer sPlayer)
        {
            sPlayer.Player.Entity.WatchedAttributes.SetFloat(HudLoadProgress.watched_key, progress);
            sPlayer.Player.Entity.WatchedAttributes.MarkPathDirty(HudLoadProgress.watched_key);

            if (secondsUsed % 0.20f < 0.05f)
            {
                byEntity.World.PlaySoundAt(Properties.LoadingSound, blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z, sPlayer.Player, true, 16);
            }
        }

        if (secondsUsed >= Properties.RequiredPlaceTime)
        {
            if (side == EnumAppSide.Server && byEntity is EntityPlayer serverPlayer)
            {
                IWorldAccessor world = byEntity.World;
                BlockPos targetPos = blockSel.Position.AddCopy(blockSel.Face);

                if (world.Claims != null && world.Claims.TestAccess(serverPlayer.Player, targetPos, EnumBlockAccessFlags.BuildOrBreak) != EnumWorldAccessResponse.Granted)
                {
                    ResetProgressHUD(serverPlayer.Player);
                    return false;
                }

                if (slot.Itemstack?.Collectible is Block blockToPlace)
                {
                    world.BlockAccessor.SetBlock(blockToPlace.Id, targetPos);
                    blockToPlace.OnBlockPlaced(world, targetPos, slot.Itemstack);
                    world.BlockAccessor.TriggerNeighbourBlockUpdate(targetPos);
                    world.PlaySoundAt(Properties.DoneSound, targetPos.X, targetPos.Y, targetPos.Z, null, true, 32);

                    slot.TakeOut(1);
                    slot.MarkDirty();
                }

                ResetProgressHUD(serverPlayer.Player);
            }

            return false;
        }

        return true;
    }

    public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
    {
        handling = EnumHandling.PreventDefault;
        if (byEntity is EntityPlayer player) ResetProgressHUD(player.Player);
    }

    public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason, ref EnumHandling handled)
    {
        handled = EnumHandling.PreventDefault;
        if (byEntity is EntityPlayer player) ResetProgressHUD(player.Player);
        return true;
    }

    private static void ResetProgressHUD(IPlayer player)
    {
        if (player?.Entity?.World?.Side == EnumAppSide.Server)
        {
            player.Entity.WatchedAttributes.SetFloat(HudLoadProgress.watched_key, 0f);
            player.Entity.WatchedAttributes.RemoveAttribute(HudLoadProgress.watched_key);
            player.Entity.WatchedAttributes.MarkPathDirty(HudLoadProgress.watched_key);
        }
    }
}