using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace TurretLib;

[HarmonyPatch(typeof(Entity), nameof(Entity.GetInteractionHelp))]
public static class Patch_Entity_GetInteractionHelp
{
    [ThreadStatic]
    private static int originalSelectionBoxIndex = -1;
    [ThreadStatic]
    private static EntitySelection? targetSelection = null;

    [HarmonyPrefix]
    public static void Prefix(Entity __instance, EntitySelection es)
    {
        originalSelectionBoxIndex = -1;
        targetSelection = null;

        if (es == null || es.SelectionBoxIndex <= 0) return;

        EntityBehaviorAttachable? attachable = __instance.GetBehavior<EntityBehaviorAttachable>();
        if (attachable == null) return;

        int currentSelectionBoxIndex = es.SelectionBoxIndex - 1;
        int clickedSlotIndex = attachable.GetSlotIndexFromSelectionBoxIndex(currentSelectionBoxIndex);
        if (clickedSlotIndex < 0) return;

        // Check if the raycast target is part of a 2x2 throne cluster
        for (int i = 0; i < attachable.Inventory.Count; i++)
        {
            ItemSlot slot = attachable.Inventory[i];
            if (slot.Empty || !slot.IsSlot2x2()) continue;

            int[]? cluster = BoatSlotResolver.Get2x2ClusterIndices(attachable, i, isPlacement: false);
            if (cluster != null && Array.IndexOf(cluster, clickedSlotIndex) >= 0)
            {
                int anchorSlotIndex = cluster[0];

                if (clickedSlotIndex != anchorSlotIndex)
                {
                    int anchorSelectionBoxIndex = GetSelectionBoxIndexFromSlotIndex(attachable, anchorSlotIndex);
                    if (anchorSelectionBoxIndex >= 0)
                    {
                        // Temporarily point the entity selection box index to the anchor slot
                        targetSelection = es;
                        originalSelectionBoxIndex = es.SelectionBoxIndex;
                        es.SelectionBoxIndex = anchorSelectionBoxIndex + 1;
                    }
                }
                break;
            }
        }
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        // Restore the original selection box index after all behaviors finish building tooltips
        if (targetSelection != null && originalSelectionBoxIndex != -1)
        {
            targetSelection.SelectionBoxIndex = originalSelectionBoxIndex;
            targetSelection = null;
            originalSelectionBoxIndex = -1;
        }
    }

    private static int GetSelectionBoxIndexFromSlotIndex(EntityBehaviorAttachable attachable, int targetSlotIndex)
    {
        for (int b = 0; b < attachable.Inventory.Count + 10; b++)
        {
            if (attachable.GetSlotIndexFromSelectionBoxIndex(b) == targetSlotIndex)
            {
                return b;
            }
        }
        return -1;
    }
}