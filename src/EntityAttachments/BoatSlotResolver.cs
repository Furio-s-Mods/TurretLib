using System.Reflection;
using Vintagestory.GameContent;

namespace TurretLib;

public static class BoatSlotResolver
{
    private static readonly FieldInfo? WearableSlotsField = typeof(EntityBehaviorAttachable)
        .GetField("wearableSlots", BindingFlags.NonPublic | BindingFlags.Instance);

    public static int[]? Get2x2ClusterIndices(EntityBehaviorAttachable attachable, int slotIndex, bool isPlacement = false)
    {
        if (attachable == null) return null;

        var slots = WearableSlotsField?.GetValue(attachable) as WearableSlotConfig[];
        if (slots == null || slotIndex < 0 || slotIndex >= slots.Length) return null;

        string? code = slots[slotIndex]?.Code;
        if (string.IsNullOrEmpty(code)) return null;
        if (!code.StartsWith("Left Storage ") && !code.StartsWith("Right Storage ")) return null;

        if (!int.TryParse(code.AsSpan(code.LastIndexOf(' ') + 1), out int clickedRow)) return null;

        // CASE 1: Querying footprint of an existing seat clicked at slotIndex
        if (!isPlacement)
        {
            // Check if a 2x2 seat is anchored at clickedRow
            if (Has2x2SeatAtRow(attachable, slots, clickedRow))
            {
                return GetClusterForTopRow(slots, clickedRow);
            }
            // Check if a 2x2 seat is anchored at row above (clickedRow - 1)
            if (clickedRow > 1 && Has2x2SeatAtRow(attachable, slots, clickedRow - 1))
            {
                return GetClusterForTopRow(slots, clickedRow - 1);
            }

            return FindClusterContainingSlot(slots, slotIndex);
        }

        // CASE 2: Placement — Try Forward pair (topRow = clickedRow), then Backward pair (topRow = clickedRow - 1)

        // Forward Candidate (Occupies clickedRow and clickedRow + 1)
        if (clickedRow <= 3 && IsClusterAvailable(attachable, slots, clickedRow))
        {
            return GetClusterForTopRow(slots, clickedRow);
        }

        // Backward Candidate (Occupies clickedRow - 1 and clickedRow)
        if (clickedRow >= 2 && IsClusterAvailable(attachable, slots, clickedRow - 1))
        {
            return GetClusterForTopRow(slots, clickedRow - 1);
        }

        // Block placement if no valid 2x2 footprint exists
        return null;
    }

    private static bool IsClusterAvailable(EntityBehaviorAttachable attachable, WearableSlotConfig[] slots, int topRow)
    {
        if (topRow < 1 || topRow > 3) return false;

        // Verify all 4 target inventory slots are empty
        int[]? cluster = GetClusterForTopRow(slots, topRow);
        if (cluster == null) return false;

        foreach (int idx in cluster)
        {
            if (!attachable.Inventory[idx].Empty) return false;
        }

        // Check for 2x2 seat anchor collisions at adjacent rows
        if (topRow > 1 && Has2x2SeatAtRow(attachable, slots, topRow - 1)) return false;
        if (Has2x2SeatAtRow(attachable, slots, topRow)) return false;
        if (topRow < 3 && Has2x2SeatAtRow(attachable, slots, topRow + 1)) return false;

        return true;
    }

    private static bool Has2x2SeatAtRow(EntityBehaviorAttachable attachable, WearableSlotConfig[] slots, int row)
    {
        if (row < 1 || row > 3) return false;

        int iL = Array.FindIndex(slots, s => s?.Code == $"Left Storage {row}");
        int iR = Array.FindIndex(slots, s => s?.Code == $"Right Storage {row}");

        if (iL >= 0 && !attachable.Inventory[iL].Empty && attachable.Inventory[iL].IsSlot2x2())
            return true;

        if (iR >= 0 && !attachable.Inventory[iR].Empty && attachable.Inventory[iR].IsSlot2x2())
            return true;

        return false;
    }

    private static int[]? FindClusterContainingSlot(WearableSlotConfig[] slots, int targetSlotIdx)
    {
        for (int topRow = 1; topRow <= 3; topRow++)
        {
            int[]? cluster = GetClusterForTopRow(slots, topRow);
            if (cluster != null && Array.IndexOf(cluster, targetSlotIdx) >= 0)
            {
                return cluster;
            }
        }
        return null;
    }

    private static int[]? GetClusterForTopRow(WearableSlotConfig[] slots, int topRow)
    {
        if (topRow < 1 || topRow > 3) return null;

        string L_Top = $"Left Storage {topRow}";
        string R_Top = $"Right Storage {topRow}";
        string L_Bot = $"Left Storage {topRow + 1}";
        string R_Bot = $"Right Storage {topRow + 1}";

        int iL_Top = Array.FindIndex(slots, s => s?.Code == L_Top);
        int iR_Top = Array.FindIndex(slots, s => s?.Code == R_Top);
        int iL_Bot = Array.FindIndex(slots, s => s?.Code == L_Bot);
        int iR_Bot = Array.FindIndex(slots, s => s?.Code == R_Bot);

        if (iL_Top < 0 || iR_Top < 0 || iL_Bot < 0 || iR_Bot < 0) return null;

        return new int[] { iL_Top, iR_Top, iL_Bot, iR_Bot };
    }
}