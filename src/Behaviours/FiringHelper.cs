using Newtonsoft.Json.Linq;
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

        ItemSlot? ammoSlot = inv.MainAmmoSlot;
        if (ammoSlot?.Itemstack?.Collectible == null)
        {
            logger.Warning($"[{MainModSystem.ModId}] Firing failed: Ammo slot at {turret.Pos} contains invalid item stack.");
            return;
        }

        TurretWeaponProperties props = weaponBehaviour.Properties;
        ItemStack itemStackPeek = ammoSlot.Itemstack;
        JsonObject? blockAttribs = weaponBehaviour.Blockentity.Block?.Attributes;
        JsonObject? itemAttribs = itemStackPeek.ItemAttributes;

        // Resolve Variant / Material
        string material = ExtractVariant(itemStackPeek, props.VariantKeys, props.DefaultVariant);

        // Resolve Entity Code
        AssetLocation entityCode = ResolveEntityCode(itemStackPeek, itemAttribs, material, props, logger);
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

        // Consume Ammo
        ItemStack? itemStack = inv.ConsumeAmmo(props.MaxAmmoConsumedPerShot);
        if (itemStack == null)
        {
            logger.Error($"[{MainModSystem.ModId}] Firing failed: Could not consume ammo stack from inventory at {turret.Pos}.");
            return;
        }
        weaponBehaviour.Blockentity.MarkDirty(true);

        // Calculate Abstracted Attributes
        string itemCodePath = itemStackPeek.Collectible.Code.Path;

        float BaseDamage = props.BaseDamage;
        float AmmoDamage = GetAttributeFloat(itemAttribs, blockAttribs, props.DamageAttributeKey, itemCodePath, material, props.DefaultAmmoDamage, logger);
        float damage = (BaseDamage + AmmoDamage) * props.DamageMultiplier;
        int damageTier = GetAttributeInt(itemAttribs, blockAttribs, props.DamageTierAttributeKey, itemCodePath, material, props.DefaultDamageTier, logger);
        float propulsion = GetAttributeFloat(itemAttribs, blockAttribs, props.PropulsionAttributeKey, itemCodePath, material, props.ProjectilePropulsionForce, logger);
        float breakChance = GetAttributeFloat(itemAttribs, blockAttribs, props.BreakChanceAttributeKey, itemCodePath, material, props.DefaultBreakChance, logger);

        // logger.Notification($"[{MainModSystem.ModId}] Firing Debug -> Material: '{material}' | BaseDamage: {BaseDamage} | AmmoDamage: {AmmoDamage} | Multiplier: {props.DamageMultiplier} | FinalDamage: {damage} | Propulsion: {propulsion}");

        // Initialize Entity Properties
        projectileEntity.World = api.World;
        IProjectile projectile = projectileEntity;
        projectile.PreInitialize();

        projectile.FiredBy = player;
        projectile.Damage = damage;
        projectile.DamageTier = damageTier;
        projectile.ProjectileStack = itemStack;
        projectile.DropOnImpactChance = Math.Clamp(1f - breakChance, 0f, 1f);

        // Calculate Transform & Spawn Position
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
                null, pitch: props.FireSoundPitch, range: props.FireSoundRange, volume: props.FireSoundVolume
            );
        }

        weaponBehaviour.ServerSetState(TurretWeaponState.Idle, turret);
    }

    #region Helper Methods

    private static float GetAttributeFloat(
        JsonObject? itemAttr, 
        JsonObject? blockAttr, 
        string attrKey, 
        string itemCodePath, 
        string material, 
        float defaultValue, 
        ILogger logger)
    {
        if (string.IsNullOrEmpty(attrKey)) return defaultValue;

        // Check primary key on item attributes, then block attributes
        if (TryExtractValue(itemAttr, attrKey, itemCodePath, material, out float itemVal)) return itemVal;
        if (TryExtractValue(blockAttr, attrKey, itemCodePath, material, out float blockVal)) return blockVal;

        // Dynamic fallback: toggle "ByType" suffix
        string fallbackKey = attrKey.EndsWith("ByType", StringComparison.OrdinalIgnoreCase)
            ? attrKey.Substring(0, attrKey.Length - 6)
            : attrKey + "ByType";

        if (TryExtractValue(itemAttr, fallbackKey, itemCodePath, material, out float fbItemVal)) return fbItemVal;
        if (TryExtractValue(blockAttr, fallbackKey, itemCodePath, material, out float fbBlockVal)) return fbBlockVal;

        // Log warning when all extraction steps fail
        logger.Warning($"[{MainModSystem.ModId}] Attribute key '{attrKey}' (and fallback '{fallbackKey}') could not be resolved for '{itemCodePath}' [material: '{material}']. Falling back to default value: {defaultValue}");

        return defaultValue;
    }

    private static int GetAttributeInt(
        JsonObject? itemAttr, 
        JsonObject? blockAttr, 
        string attrKey, 
        string itemCodePath, 
        string material, 
        int defaultValue, 
        ILogger logger)
    {
        return (int)GetAttributeFloat(itemAttr, blockAttr, attrKey, itemCodePath, material, defaultValue, logger);
    }

    private static string ExtractVariant(ItemStack itemStack, string[] variantKeys, string defaultVariant)
    {
        if (itemStack?.Collectible?.Variant == null) return defaultVariant;

        foreach (string key in variantKeys)
        {
            if (itemStack.Collectible.Variant.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return defaultVariant;
    }

    private static AssetLocation ResolveEntityCode(ItemStack itemStack, JsonObject? itemAttribs, string material, TurretWeaponProperties props, ILogger logger)
    {
        AssetLocation ammoItemCode = itemStack.Collectible.Code;

        if (itemAttribs != null && props.EntityCodeAttributeKeys != null)
        {
            foreach (string attrKey in props.EntityCodeAttributeKeys)
            {
                JsonObject token = itemAttribs[attrKey];
                if (token.Exists)
                {
                    string? rawPath = token[$"*-{material}"].AsString()
                        ?? token[material].AsString()
                        ?? token["*"].AsString()
                        ?? token.AsString();

                    if (!string.IsNullOrEmpty(rawPath))
                    {
                        string path = rawPath
                            .Replace("{ammoMaterial}", material)
                            .Replace("{material}", material);

                        return path.Contains(':') ? new AssetLocation(path) : new AssetLocation(ammoItemCode.Domain, path);
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(props.DefaultProjectileEntityCode))
        {
            string defaultPath = props.DefaultProjectileEntityCode
                .Replace("{ammoMaterial}", material)
                .Replace("{material}", material);

            return defaultPath.Contains(':') ? new AssetLocation(defaultPath) : new AssetLocation(ammoItemCode.Domain, defaultPath);
        }

        return ammoItemCode;
    }

    private static bool TryExtractValue(JsonObject? attributes, string key, string itemCodePath, string material, out float value)
    {
        value = 0f;
        if (attributes == null || !attributes.KeyExists(key)) return false;

        JsonObject token = attributes[key];
        if (!token.Exists) return false;

        // Handle single primitive scalar values
        if (token.Token is JValue)
        {
            value = token.AsFloat(0f);
            return true;
        }

        // Handle dictionary objects using strict resolution hierarchy
        if (token.Token is JObject jObj)
        {
            // Hierarchy 1: Exact Item Code Path match (e.g. "modid-steel")
            JToken? pathToken = jObj.GetValue(itemCodePath, StringComparison.OrdinalIgnoreCase);
            if (pathToken != null)
            {
                value = new JsonObject(pathToken).AsFloat(0f);
                return true;
            }

            // Hierarchy 2: Wildcard pattern match (e.g. "*-steel")
            JToken? wildcardToken = jObj.GetValue($"*-{material}", StringComparison.OrdinalIgnoreCase);
            if (wildcardToken != null)
            {
                value = new JsonObject(wildcardToken).AsFloat(0f);
                return true;
            }

            // Hierarchy 3: Direct material name match (e.g. "steel")
            JToken? materialToken = jObj.GetValue(material, StringComparison.OrdinalIgnoreCase);
            if (materialToken != null)
            {
                value = new JsonObject(materialToken).AsFloat(0f);
                return true;
            }

            // Hierarchy 4: Global wildcard fallback (e.g. "*")
            JToken? starToken = jObj.GetValue("*");
            if (starToken != null)
            {
                value = new JsonObject(starToken).AsFloat(0f);
                return true;
            }
        }

        return false;
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