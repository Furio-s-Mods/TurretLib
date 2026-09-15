using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace TurretLib;

public static class TurretAmmoHelper
{
    private const string AmmoCodeKey = "turretlib_ammoCode";
    private const string AmmoCountKey = "turretlib_ammoCount";

    /// <summary>
    /// Validates whether a candidate item stack matches the required ammo rules.
    /// </summary>
    public static bool IsValidAmmo(ItemStack? stack, string ammoDomain, string? ammoCodePrefix)
    {
        if (stack?.Collectible?.Code == null) return false;

        bool validDomain = stack.Collectible.Code.Domain == ammoDomain;
        bool validPrefix = string.IsNullOrEmpty(ammoCodePrefix) || stack.Collectible.Code.Path.StartsWith(ammoCodePrefix);

        return validDomain && validPrefix;
    }

    #region ITreeAttribute Helpers

    public static string? GetLoadedAmmoCode(ITreeAttribute tree)
        => tree.GetString(AmmoCodeKey);

    public static int GetLoadedAmmoCount(ITreeAttribute tree)
        => tree.GetInt(AmmoCountKey, 0);

    public static bool HasAmmo(ITreeAttribute tree)
        => GetLoadedAmmoCount(tree) > 0;

    public static void SetLoadedAmmo(ITreeAttribute tree, string ammoCode, int count)
    {
        tree.SetString(AmmoCodeKey, ammoCode);
        tree.SetInt(AmmoCountKey, count);
    }

    public static void ClearAmmo(ITreeAttribute tree)
    {
        tree.RemoveAttribute(AmmoCodeKey);
        tree.RemoveAttribute(AmmoCountKey);
    }

    public static bool TryAddAmmo(ITreeAttribute tree, ItemStack candidateStack, string ammoDomain, string ammoCodePrefix, int maxCapacity, out int addedCount)
    {
        addedCount = 0;
        if (!IsValidAmmo(candidateStack, ammoDomain, ammoCodePrefix)) return false;

        string currentCode = GetLoadedAmmoCode(tree) ?? string.Empty;
        int currentCount = GetLoadedAmmoCount(tree);

        string candidateCode = candidateStack.Collectible.Code.ToShortString();

        // Prevent mixing different ammo types if already loaded
        if (currentCount > 0 && currentCode != candidateCode) return false;

        int spaceAvailable = maxCapacity - currentCount;
        if (spaceAvailable <= 0) return false;

        addedCount = System.Math.Min(candidateStack.StackSize, spaceAvailable);
        SetLoadedAmmo(tree, candidateCode, currentCount + addedCount);
        return true;
    }

    public static bool TryConsumeAmmo(ITreeAttribute tree, out string? consumedAmmoCode)
    {
        consumedAmmoCode = null;
        int currentCount = GetLoadedAmmoCount(tree);
        if (currentCount <= 0) return false;

        consumedAmmoCode = GetLoadedAmmoCode(tree);
        int newCount = currentCount - 1;

        if (newCount <= 0)
        {
            ClearAmmo(tree);
        }
        else
        {
            tree.SetInt(AmmoCountKey, newCount);
        }

        return true;
    }

    #endregion
}