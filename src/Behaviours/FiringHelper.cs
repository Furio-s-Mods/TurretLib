using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TurretLib;

public static class FiringHelper
{
    public static void ServerExecuteFire(this BEBehaviorTurretWeapon weaponBehaviour, BlockEntityTurret turret)
    {
        ICoreAPI api = turret.Api;
        ILogger logger = api.Logger;

        if (api?.Side != EnumAppSide.Server) return;
        if (weaponBehaviour.Properties == null) return;

        if (turret.Controller is not EntityPlayer player)
        {
            logger.Warning($"[{MainModSystem.ModId}] Firing failed: Turret at {turret.Pos} has no active EntityPlayer controller.");
            return;
        }

        BEBehaviorTurretInventory? inv = turret.InventoryBehavior;
        if (inv == null || !inv.HasProjectile)
        {
            logger.Warning($"[{MainModSystem.ModId}] Firing failed: Turret at {turret.Pos} has no ammo.");
            return;
        }

        ItemSlot? ammoSlot = inv.Inventory?[0];
        if (ammoSlot?.Itemstack?.Collectible == null)
        {
            logger.Warning($"[{MainModSystem.ModId}] Firing failed: Ammo slot at {turret.Pos} contains invalid item stack.");
            return;
        }

        ItemStack itemStackPeek = ammoSlot.Itemstack;
        JsonObject? blockAttribs = weaponBehaviour.Blockentity.Block?.Attributes;
        JsonObject? itemAttribs = itemStackPeek.ItemAttributes;
        TurretWeaponProperties props = weaponBehaviour.Properties;

        string material = itemStackPeek.Collectible.Variant?["material"] 
               ?? itemStackPeek.Collectible.Variant?["metal"] 
               ?? itemStackPeek.Collectible.Variant?["type"] 
               ?? "unknown";

        logger.Notification($"[{MainModSystem.ModId}] Firing attempt with ammo '{itemStackPeek.Collectible.Code}', extracted material key: '{material}'");

        AssetLocation entityCode = ResolveEntityCode(itemStackPeek, itemAttribs, material, props.DefaultProjectileEntityCode, logger);
        if (entityCode == null) return;

        EntityProperties? entityType = api.World.GetEntityType(entityCode);
        if (entityType == null)
        {
            logger.Error($"[{MainModSystem.ModId}] Firing failed: Projectile entity type '{entityCode}' not found in registry.");
            return;
        }

        if (api.World.ClassRegistry.CreateEntity(entityType) is not EntityProjectile projectileEntity)
        {
            logger.Error($"[{MainModSystem.ModId}] Firing failed: Entity '{entityCode}' does not extend EntityProjectile.");
            return;
        }

        ItemStack? itemStack = inv.ConsumeAmmo(1);
        if (itemStack == null)
        {
            logger.Error($"[{MainModSystem.ModId}] Firing failed: Could not consume ammo stack from inventory at {turret.Pos}.");
            return;
        }
        weaponBehaviour.Blockentity.MarkDirty(true);

        // Calculate Attributes
        float damage = GetCombinedFloat(blockAttribs, itemStack.Collectible.Attributes, "damage") * props.DamageMultiplier;
        int damageTier = GetCombinedInt(blockAttribs, itemAttribs, "damageTier");
        float propulsion = blockAttribs?["propulsionForce"]?.AsFloat(1.0f) ?? 1.0f;
        float breakChance = ResolveBreakChance(itemAttribs, material);

        // Initialize Entity Properties
        projectileEntity.World = api.World;
        IProjectile projectile = projectileEntity;
        projectile.PreInitialize();

        projectile.FiredBy = player;
        projectile.Damage = damage;
        projectile.DamageTier = damageTier;
        projectile.ProjectileStack = itemStack;
        projectile.DropOnImpactChance = 1f - breakChance;

        // Calculate Transform & Position
        Vec3d dir = CalculateDirectionVector(turret.Yaw, turret.Pitch);
        Vec3d turretPivot = turret.Pos.ToVec3d().Add(props.MuzzlePivotOffset ?? Vec3d.Zero);
        Vec3d spawnPos = turretPivot.Add(dir * props.MuzzleForwardOffset);

        projectileEntity.Pos.SetPos(spawnPos);
        projectileEntity.Pos.Motion.Set(dir * propulsion);
        projectileEntity.SetInitialRotation();

        // Spawn Entity & Play Sound
        api.World.SpawnPriorityEntity(projectileEntity);

        if (props.FireSound != null)
        {
            api.World.PlaySoundAt(
                props.FireSound,
                turretPivot.X, turretPivot.Y, turretPivot.Z,
                null, 0.7f, 32f, 2f
            );
        }

        // logger.Debug($"[{MainModSystem.ModId}] Fired projectile '{entityCode}' successfully at {spawnPos}.");

        weaponBehaviour.ServerSetState(TurretWeaponState.Idle, turret);
    }

    #region Helper Methods

    private static float GetCombinedFloat(JsonObject? blockAttr, JsonObject? itemAttr, string key)
    {
        float val = 0f;
        if (blockAttr?[key] != null) val += blockAttr[key].AsFloat(0f);
        if (itemAttr?[key] != null) val += itemAttr[key].AsFloat(0f);
        return val;
    }

    private static int GetCombinedInt(JsonObject? blockAttr, JsonObject? itemAttr, string key)
    {
        int val = 0;
        if (blockAttr?[key] != null) val += blockAttr[key].AsInt(0);
        if (itemAttr?[key] != null) val += itemAttr[key].AsInt(0);
        return val;
    }

    private static AssetLocation ResolveEntityCode(ItemStack itemStack, JsonObject? itemAttribs, string material, string? defaultPattern, ILogger logger)
    {
        AssetLocation ammoItemCode = itemStack.Collectible.Code;
        string? path = null;
        string source = "none";

        if (itemAttribs != null)
        {
            JsonObject? byType = itemAttribs["projectileEntityCodeByType"];

            if (byType != null && byType.Exists)
            {
                path = byType[$"*-{material}"]?.AsString()
                    ?? byType[material]?.AsString()
                    ?? byType["*"]?.AsString();

                if (!string.IsNullOrEmpty(path)) source = "itemAttribs.byType";
            }

            if (string.IsNullOrEmpty(path) && itemAttribs.KeyExists("projectileEntityCode"))
            {
                path = itemAttribs["projectileEntityCode"].AsString();
                source = "itemAttribs.projectileEntityCode";
            }
        }

        AssetLocation entityCode;

        if (!string.IsNullOrEmpty(path))
        {
            if (path.Contains("{material}")) path = path.Replace("{material}", material);
            
            entityCode = path.Contains(':') 
                ? new AssetLocation(path) 
                : new AssetLocation(ammoItemCode.Domain, path);
        }
        else
        {
            logger.Notification($"[{MainModSystem.ModId}][ResolveEntityCode] fallback -> entityCode = ammoItemCode");
            entityCode = ammoItemCode;
            source = "ammo collectible code";
        }

        logger.Notification($"[{MainModSystem.ModId}] Resolved entity code: '{entityCode}' (Source: {source})");

        return entityCode;
    }

    private static float ResolveBreakChance(JsonObject? itemAttribs, string material, float defaultChance = 0.15f)
    {
        if (itemAttribs?["breakChanceOnImpactByType"] != null)
        {
            return itemAttribs["breakChanceOnImpactByType"][$"*-{material}"]?.AsFloat(defaultChance) 
                ?? itemAttribs["breakChanceOnImpactByType"]["*"]?.AsFloat(defaultChance) 
                ?? defaultChance;
        }
        return defaultChance;
    }

    private static Vec3d CalculateDirectionVector(double yaw, double pitch)
    {
        double cosPitch = Math.Cos(pitch);
        return new Vec3d(
            Math.Sin(yaw) * cosPitch,
            -Math.Sin(pitch),
            Math.Cos(yaw) * cosPitch
        ).Normalize();
    }

    #endregion
}