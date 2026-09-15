using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TurretLib;

[HarmonyPatch(typeof(EntityBehaviorAttachable), "updateSeats")]
public class Patch_EntityBehaviorAttachable_UpdateSeats
{
    [HarmonyPostfix]
    public static void Postfix(EntityBehaviorAttachable __instance)
    {
        var seatable = __instance.entity.GetBehavior<EntityBehaviorSeatable>();
        if (seatable == null) return;

        var wearableSlots = Traverse.Create(__instance).Field("wearableSlots").GetValue<WearableSlotConfig[]>();
        if (wearableSlots == null) return;

        for (int i = 0; i < wearableSlots.Length; i++)
        {
            WearableSlotConfig slotConfig = wearableSlots[i];
            if (slotConfig.SeatConfig == null) continue;

            int seatIndex = Array.FindIndex(seatable.Seats, s => s.Config?.SeatId == slotConfig.SeatConfig.SeatId);
            if (seatIndex < 0) continue;

            ItemSlot slot = __instance.Inventory[i];
            bool isRotatable = !slot.Empty && (slot.Itemstack?.Collectible?.Attributes?["attachableToEntity"]?["rotatable"].AsBool(false) ?? false);

            // CASE 1: Slot is empty or item is NOT rotatable, but seat is still a SwivelSeat
            if (!isRotatable && seatable.Seats[seatIndex] is SwivelSeat orphanedSwivel)
            {
                // Dispose orphaned renderer and revert to standard boat seat
                orphanedSwivel.Dispose();
                seatable.Seats[seatIndex] = new EntityBoatSeat(seatable, slotConfig.SeatConfig?.SeatId, slotConfig.SeatConfig);
                continue;
            }

            // CASE 2: Rotatable item attached
            if (isRotatable && slot.Itemstack?.Collectible?.HasBehavior<CollectibleBehavior2x2Attachable>() == true)
            {
                // Skip if ALREADY initialized
                if (seatable.Seats[seatIndex] is SwivelSeat) continue;

                // Dispose any previous seat before replacing
                (seatable.Seats[seatIndex] as SwivelSeat)?.Dispose();

                EntityAgent? passenger = seatable.Seats[seatIndex]?.Passenger as EntityAgent;
                Vec3f? itemRiderOffset = slot.Itemstack.ItemAttributes?["attachableToEntity"]?["seatConfig"]?["riderOffset"]?.AsObject<Vec3f>();

                var newSeat = new SwivelSeat(
                    seatable, 
                    slot,
                    slotConfig.SeatConfig, 
                    slotConfig, 
                    itemRiderOffset
                );

                if (passenger != null)
                {
                    passenger.TryUnmount();
                    seatable.Seats[seatIndex] = newSeat;
                    passenger.TryMount(newSeat);
                }
                else
                {
                    seatable.Seats[seatIndex] = newSeat;
                }
            }
        }
    }
}