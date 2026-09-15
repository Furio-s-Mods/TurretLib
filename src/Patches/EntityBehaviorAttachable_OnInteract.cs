using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace TurretLib;

[HarmonyPatch(typeof(EntityBehaviorAttachable), "OnInteract")]
public static class Patch_EntityBehaviorAttachable_OnInteract
{
    [ThreadStatic]
    private static int originalSelectionBoxIndex = -1;
    [ThreadStatic]
    private static EntitySelection? targetEntitySelection = null;

    [HarmonyPrefix]
    public static void Prefix(EntityBehaviorAttachable __instance, EntityAgent byEntity)
    {
        originalSelectionBoxIndex = -1;
        targetEntitySelection = null;

        if (byEntity is not EntityPlayer entityPlayer || entityPlayer.EntitySelection == null) return;

        int currentNum = entityPlayer.EntitySelection.SelectionBoxIndex;
        if (currentNum <= 0) return;

        int currentSelectionBoxIndex = currentNum - 1;
        int clickedSlotIndex = __instance.GetSlotIndexFromSelectionBoxIndex(currentSelectionBoxIndex);
        if (clickedSlotIndex < 0) return;

        // Check if the clicked slot belongs to an active 2x2 throne cluster
        for (int i = 0; i < __instance.Inventory.Count; i++)
        {
            ItemSlot slot = __instance.Inventory[i];
            if (slot.Empty || !slot.IsSlot2x2()) continue;

            int[]? cluster = BoatSlotResolver.Get2x2ClusterIndices(__instance, i, isPlacement: false);
            if (cluster != null && Array.IndexOf(cluster, clickedSlotIndex) >= 0)
            {
                int anchorSlotIndex = cluster[0];

                if (clickedSlotIndex != anchorSlotIndex)
                {
                    int anchorSelectionBoxIndex = GetSelectionBoxIndexFromSlotIndex(__instance, anchorSlotIndex);
                    if (anchorSelectionBoxIndex >= 0)
                    {
                        // Save original SelectionBoxIndex to restore in Postfix
                        targetEntitySelection = entityPlayer.EntitySelection;
                        originalSelectionBoxIndex = currentNum;

                        // Redirect player selection to the anchor slot (+1 offset required by OnInteract)
                        targetEntitySelection.SelectionBoxIndex = anchorSelectionBoxIndex + 1;
                    }
                }
                break;
            }
        }
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        // Restore original player raycast selection box index after interaction finishes
        if (targetEntitySelection != null && originalSelectionBoxIndex != -1)
        {
            targetEntitySelection.SelectionBoxIndex = originalSelectionBoxIndex;
            targetEntitySelection = null;
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