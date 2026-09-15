using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace TurretLib;

[HarmonyPatch(typeof(EntityBehaviorAttachable), "TryAttach")]
public static class Patch_EntityBehaviorAttachable_TryAttach
{
    [HarmonyPrefix]
    public static bool Prefix(EntityBehaviorAttachable __instance, ItemSlot itemslot, int selectionBoxIndex, ref bool __result)
    {
        int clickedSlotIndex = __instance.GetSlotIndexFromSelectionBoxIndex(selectionBoxIndex);

        // CASE 1: Attaching a 2x2 Seat
        if (itemslot.IsSlot2x2())
        {
            int[]? cluster = BoatSlotResolver.Get2x2ClusterIndices(__instance, clickedSlotIndex, isPlacement: true);
            if (cluster == null)
            {
                if (__instance.entity.World.Api is ICoreClientAPI capi)
                    capi.TriggerIngameError(__instance, "notenoughspace", "Throne must be placed on deck storage slots.");
                __result = false;
                return false;
            }

            foreach (int idx in cluster)
            {
                if (!__instance.Inventory[idx].Empty)
                {
                    if (__instance.entity.World.Api is ICoreClientAPI capi)
                        capi.TriggerIngameError(__instance, "alreadyoccupied", "Requires a clear 2x2 deck area.");
                    __result = false;
                    return false;
                }
            }

            int anchorSlotIndex = cluster[0];
            ItemSlot anchorSlot = __instance.Inventory[anchorSlotIndex];

            // 1. Perform item transfer on BOTH Client & Server for instant client prediction
            int moved = itemslot.TryPutInto(__instance.entity.World, anchorSlot, 1);
            if (moved <= 0)
            {
                anchorSlot.Itemstack = itemslot.TakeOut(1);
            }

            __instance.Inventory.MarkSlotDirty(anchorSlotIndex);
            itemslot.MarkDirty();

            // 2. Force the boat entity to immediately rebuild its attached 3D meshes on the client
            __instance.entity.MarkShapeModified();
            __instance.storeInv();

            __result = true;
            return false; // Skip native TryAttach to prevent category rejection and crash
        }

        // CASE 2: Standard placement inside a slot covered by an existing seat
        for (int i = 0; i < __instance.Inventory.Count; i++)
        {
            ItemSlot slot = __instance.Inventory[i];
            if (slot.Empty || !slot.IsSlot2x2()) continue;

            int[]? cluster = BoatSlotResolver.Get2x2ClusterIndices(__instance, i, isPlacement: false);
            if (cluster != null && Array.IndexOf(cluster, clickedSlotIndex) >= 0)
            {
                if (__instance.entity.World.Api is ICoreClientAPI capi)
                    capi.TriggerIngameError(__instance, "alreadyoccupied", "Area occupied by a 2x2 throne.");
                __result = false;
                return false;
            }
        }

        return true;
    }
}