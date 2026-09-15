using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace TurretLib;

// // ```text
// // vehicle.WatchedAttributes
// // └── "turrets" (ITreeAttribute)
// //     ├── "seat-0" (ITreeAttribute)
// //     │   ├── "yaw" (float)
// //     │   ├── "pitch" (float)
// //     │   └── "ammoCode" (string)
// //     └── "seat-1" (ITreeAttribute)
// //         ├── "yaw" (float)
// //         └── ...
// // ```

public static class TurretVehicleState
{
    public const string RootKey = "turrets";

    public static ITreeAttribute? GetSeatTree(Entity vehicle, string seatId, bool createIfMissing = false)
    {
        var root = vehicle.WatchedAttributes.GetOrAddTreeAttribute(RootKey);
        if (createIfMissing && !root.HasAttribute(seatId))
        {
            root[seatId] = new TreeAttribute();
        }
        return root.GetTreeAttribute(seatId);
    }

    public static (float yaw, float pitch) GetRotation(Entity vehicle, string seatId)
    {
        var seatTree = GetSeatTree(vehicle, seatId);
        if (seatTree == null) return (0f, 0f);
        return (seatTree.GetFloat("yaw", 0f), seatTree.GetFloat("pitch", 0f));
    }

    public static void SetRotation(Entity vehicle, string seatId, float yaw, float pitch)
    {
        var seatTree = GetSeatTree(vehicle, seatId, true);
        if (seatTree == null) return;
        
        seatTree.SetFloat("yaw", yaw);
        seatTree.SetFloat("pitch", pitch);
        vehicle.WatchedAttributes.MarkPathDirty(RootKey);
    }

    public static string GetAmmo(Entity vehicle, string seatId)
    {
        // Direct key match
        string ammo = GetSeatTree(vehicle, seatId)?.GetString("ammoCode", "") ?? "";
        if (!string.IsNullOrEmpty(ammo)) return ammo;

        // Fallback search across all seat sub-trees on this vehicle
        var root = vehicle.WatchedAttributes.GetTreeAttribute(RootKey);
        if (root != null)
        {
            foreach (var val in root)
            {
                if (val.Value is ITreeAttribute seatTree)
                {
                    string candidate = seatTree.GetString("ammoCode", "");
                    if (!string.IsNullOrEmpty(candidate)) return candidate;
                }
            }
        }

        return "";
    }

    public static void SetAmmo(Entity vehicle, string seatId, string ammoCode)
    {
        var seatTree = GetSeatTree(vehicle, seatId, true);
        seatTree?.SetString("ammoCode", ammoCode);
        vehicle.WatchedAttributes.MarkPathDirty(RootKey);
    }

    public static TurretWeaponState GetState(Entity vehicle, string seatId)
    {
        var seatTree = GetSeatTree(vehicle, seatId);
        return (TurretWeaponState)(seatTree?.GetInt("state", 0) ?? 0);
    }

    public static void SetState(Entity vehicle, string seatId, TurretWeaponState state)
    {
        var seatTree = GetSeatTree(vehicle, seatId, true);
        if (seatTree == null) return;

        seatTree.SetInt("state", (int)state);
        vehicle.WatchedAttributes.MarkPathDirty(RootKey);
    }

    public static void ClearSeat(Entity vehicle, string seatId)
    {
        ITreeAttribute root = vehicle.WatchedAttributes.GetTreeAttribute(RootKey);
        if (root != null)
        {
            // Snapshot keys before removing to avoid modifying collection while iterating
            var keys = root.Select(kvp => kvp.Key).ToList();
            foreach (var key in keys)
            {
                root.RemoveAttribute(key);
            }
            vehicle.WatchedAttributes.MarkPathDirty(RootKey);
        }

        // Clean legacy top-level watched attributes
        vehicle.WatchedAttributes.RemoveAttribute($"turretAmmo_{seatId}");
        vehicle.WatchedAttributes.RemoveAttribute($"swivelYaw_{seatId}");
        vehicle.WatchedAttributes.RemoveAttribute($"swivelPitch_{seatId}");
    }
}