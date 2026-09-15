using HarmonyLib;
using Vintagestory.GameContent;

namespace TurretLib;

[HarmonyPatch(typeof(EntityBehaviorAttachable), "TryRemoveAttachment")]
public static class Patch_EntityBehaviorAttachable_TryDetach
{
    [HarmonyPostfix]
    public static void Postfix(EntityBehaviorAttachable __instance, int selectionBoxIndex, ref bool __result)
    {
        if (!__result) return;

        int slotIndex = __instance.GetSlotIndexFromSelectionBoxIndex(selectionBoxIndex);
        if (slotIndex < 0) return;

        // 1. Reset rotation attributes on boat entity
        string seatKey = $"swivelYaw_seat{slotIndex}";
        if (__instance.entity.WatchedAttributes.HasAttribute(seatKey))
        {
            __instance.entity.WatchedAttributes.SetFloat(seatKey, 0f);
            __instance.entity.WatchedAttributes.MarkPathDirty(seatKey);
        }

        // 2. Dispose SwivelSeat and revert back to standard EntityBoatSeat
        var seatable = __instance.entity.GetBehavior<EntityBehaviorSeatable>();
        var wearableSlots = Traverse.Create(__instance).Field("wearableSlots").GetValue<WearableSlotConfig[]>();

        if (seatable != null && wearableSlots != null && slotIndex < wearableSlots.Length)
        {
            WearableSlotConfig slotConfig = wearableSlots[slotIndex];
            if (slotConfig?.SeatConfig != null)
            {
                int seatIndex = Array.FindIndex(seatable.Seats, s => s.Config?.SeatId == slotConfig.SeatConfig.SeatId);
                if (seatIndex >= 0 && seatable.Seats[seatIndex] is SwivelSeat swivelSeat)
                {
                    swivelSeat.Dispose(); 
                    seatable.Seats[seatIndex] = new EntityBoatSeat(seatable, slotConfig.SeatConfig.SeatId, slotConfig.SeatConfig);
                }
            }
        }
    }
}