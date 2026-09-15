using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TurretLib;

public static class ItemSlotExtensions
{
    public static bool IsSlot2x2(this ItemSlot slot)
    {
        return slot.Itemstack?.Collectible?.HasBehavior<CollectibleBehavior2x2Attachable>() ?? false;
    }
}

public class CollectibleBehavior2x2Attachable(CollectibleObject coll) : CollectibleBehavior(coll), IAttachedInteractions, IAttachedListener
{
    public int Width { get; private set; } = 2;
    public int Length { get; private set; } = 2;

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        Width = properties["width"].AsInt(2);
        Length = properties["length"].AsInt(2);
    }

    public bool OnTryAttach(ItemSlot itemslot, int slotIndex, Entity entity)
    {
        ILogger logger = entity.World.Logger;
        string side = entity.World.Side.ToString();
        // logger.Notification($"[{MainModSystem.ModId} {side}] --- OnTryAttach START --- slotIndex: {slotIndex}, Item: {itemslot.Itemstack?.Collectible?.Code}");

        EntityBehaviorAttachable? attachable = entity.GetBehavior<EntityBehaviorAttachable>();
        if (attachable == null)
        {
            logger.Warning($"[{MainModSystem.ModId} {side}] Entity missing EntityBehaviorAttachable!");
            return false;
        }

        int[]? cluster = BoatSlotResolver.Get2x2ClusterIndices(attachable, slotIndex, isPlacement: true);
        // logger.Notification($"[{MainModSystem.ModId} {side}] 2x2 Cluster Check Result: {(cluster != null ? string.Join(",", cluster) : "null")}");

        return cluster != null;
    }

    public void OnAttached(ItemSlot slot, int slotIndex, Entity entity, EntityAgent byEntity)
    {
        // ILogger logger = entity.World.Logger;
        // string side = entity.World.Side.ToString();
        // logger.Notification($"[{MainModSystem.ModId} {side}] === OnAttached FIRED === slotIndex: {slotIndex}, Item: {slot.Itemstack?.Collectible?.Code}");

        // LogVehicleSeatState(entity, slotIndex, slot);
    }

    public bool OnTryDetach(ItemSlot itemslot, int slotIndex, Entity toEntity)
    {
        // ILogger logger = toEntity.World.Logger;
        // string side = toEntity.World.Side.ToString();
        // logger.Notification($"[{MainModSystem.ModId} {side}] --- OnTryDetach FIRED --- slotIndex: {slotIndex}");
        return true;
    }

    public void OnDetached(ItemSlot slot, int slotIndex, Entity entity, EntityAgent byEntity)
    {
        // ILogger logger = entity.World.Logger;
        // string side = entity.World.Side.ToString();
        // logger.Notification($"[{MainModSystem.ModId} {side}] === OnDetached FIRED === slotIndex: {slotIndex}, Item: {slot.Itemstack?.Collectible?.Code}");

        // LogVehicleSeatState(entity, slotIndex, slot);

        string seatId = ResolveSeatId(entity, slotIndex, slot);
        string currentAmmo = TurretVehicleState.GetAmmo(entity, seatId);
        // logger.Notification($"[{MainModSystem.ModId} {side}] Resolved SeatId: '{seatId}', Found Ammo Code in Vehicle Attributes: '{currentAmmo}'");

        if (entity.World.Api is ICoreServerAPI sapi)
        {
            if (!string.IsNullOrEmpty(currentAmmo))
            {
                AssetLocation itemLoc = new AssetLocation(currentAmmo);
                Item? ammoItem = sapi.World.GetItem(itemLoc);
                // logger.Notification($"[{MainModSystem.ModId} Server] Attempting ammo refund for item '{currentAmmo}' (Found in registry: {ammoItem != null})");
                
                if (ammoItem != null)
                {
                    ItemStack giveStack = new ItemStack(ammoItem, 1);
                    if (byEntity is EntityPlayer entityPlayer && entityPlayer.Player is IServerPlayer player)
                    {
                        bool given = player.InventoryManager.TryGiveItemstack(giveStack, true);
                        // logger.Notification($"[{MainModSystem.ModId} Server] Refund directly to player inventory success: {given}");
                        if (!given)
                        {
                            sapi.World.SpawnItemEntity(giveStack, entity.Pos.XYZ);
                            // logger.Notification($"[{MainModSystem.ModId} Server] Player inventory full. Spawned ammo entity in world.");
                        }
                    }
                    else
                    {
                        sapi.World.SpawnItemEntity(giveStack, entity.Pos.XYZ);
                        // logger.Notification($"[{MainModSystem.ModId} Server] Detached by non-player agent. Spawned ammo entity in world.");
                    }
                }
            }
            else
            {
                // logger.Warning($"[{MainModSystem.ModId} Server] No ammo was found in vehicle attributes under seatId '{seatId}' to refund.");
            }

            TurretVehicleState.ClearSeat(entity, seatId);
            // logger.Notification($"[{MainModSystem.ModId} Server] Cleared seat '{seatId}' state from vehicle.");
        }
    }

    private static void LogVehicleSeatState(Entity entity, int slotIndex, ItemSlot targetSlot)
    {
        ILogger logger = entity.World.Logger;
        string side = entity.World.Side.ToString();

        var seatable = entity.GetBehavior<EntityBehaviorSeatable>();
        if (seatable == null)
        {
            logger.Warning($"[{MainModSystem.ModId} {side}] Vehicle missing EntityBehaviorSeatable.");
            return;
        }

        logger.Notification($"[{MainModSystem.ModId} {side}] --- Total Seats on Vehicle: {seatable.Seats?.Length ?? 0} ---");
        if (seatable.Seats != null)
        {
            for (int i = 0; i < seatable.Seats.Length; i++)
            {
                var seat = seatable.Seats[i];
                string seatType = seat?.GetType().Name ?? "null";
                string seatId = seat?.Config?.SeatId ?? SwivelSeat.defaultName;
                
                ItemSlot? swivelSlot = (seat is SwivelSeat swivel) ? swivel.ItemSlot : null;
                bool slotMatches = swivelSlot == targetSlot;

                logger.Notification($"[{MainModSystem.ModId} {side}] Seat[{i}]: Type={seatType}, SeatId='{seatId}', SlotMatchesTarget={slotMatches}");
            }
        }
    }

    private static string ResolveSeatId(Entity entity, int slotIndex, ItemSlot targetSlot)
    {
        var seatable = entity.GetBehavior<EntityBehaviorSeatable>();
        if (seatable?.Seats != null)
        {
            foreach (var seat in seatable.Seats)
            {
                if (seat is SwivelSeat swivel && swivel.ItemSlot == targetSlot)
                {
                    if (!string.IsNullOrEmpty(swivel.Config?.SeatId)) return swivel.Config.SeatId;
                }
            }

            if (slotIndex >= 0 && slotIndex < seatable.Seats.Length)
            {
                string? configSeatId = seatable.Seats[slotIndex]?.Config?.SeatId;
                if (!string.IsNullOrEmpty(configSeatId)) return configSeatId;
            }
        }

        return $"seat-{slotIndex}";
    }

    public void OnInteract(ItemSlot itemslot, int slotIndex, Entity onEntity, EntityAgent byEntity, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled, Action onRequireSave) { }
    public void OnEntityDespawn(ItemSlot itemslot, int slotIndex, Entity onEntity, EntityDespawnData despawn) { }
    public void OnEntityDeath(ItemSlot itemslot, int slotIndex, Entity onEntity, DamageSource damageSourceForDeath) { }
    public void OnReceivedClientPacket(ItemSlot itemslot, int slotIndex, Entity onEntity, IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled, Action onRequireSave) { }
}