using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace TurretLib;

[HarmonyPatch(typeof(EntityBehaviorSeatable), "OnInteract")]
public static class Patch_EntityBehaviorSeatable_OnInteract
{
    [ThreadStatic]
    private static int originalSelectionBoxIndex;
    [ThreadStatic]
    private static EntitySelection? targetEntitySelection;

    [HarmonyPrefix]
    public static void Prefix(EntityBehaviorSeatable __instance, EntityAgent byEntity)
    {
        originalSelectionBoxIndex = -1;
        targetEntitySelection = null;

        if (byEntity is not EntityPlayer entityPlayer || entityPlayer.EntitySelection == null) return;

        EntityBehaviorAttachable? attachable = __instance.entity.GetBehavior<EntityBehaviorAttachable>();
        if (attachable == null) return;

        int currentNum = entityPlayer.EntitySelection.SelectionBoxIndex;
        if (currentNum <= 0) return;

        int currentSelectionBoxIndex = currentNum - 1;
        int clickedSlotIndex = attachable.GetSlotIndexFromSelectionBoxIndex(currentSelectionBoxIndex);
        if (clickedSlotIndex < 0) return;

        // Find if the clicked slot belongs to a 2x2 throne cluster
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
                        // Temporarily redirect selection box index to the anchor seat for seatable resolution
                        targetEntitySelection = entityPlayer.EntitySelection;
                        originalSelectionBoxIndex = currentNum;
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
        // Restore player selection box index post-interaction
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