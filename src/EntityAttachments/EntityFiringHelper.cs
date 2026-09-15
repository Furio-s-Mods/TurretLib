using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TurretLib;

public static class EntityFiringHelper
{
    public static bool ServerExecuteEntityFire(ICoreServerAPI sapi, Entity vehicle, SwivelSeat seat, IServerPlayer player)
    {
        ILogger logger = sapi.Logger;
        string? seatId = seat.Config?.SeatId;
        if (seatId == null) return false;

        // logger.Notification($"[{MainModSystem.ModId}] [Fire Attempt] VehicleId: {vehicle.EntityId} | SeatId: '{seatId}' | Player: '{player.PlayerName}'");

        // 1. Weapon Item & Properties Resolution
        ItemStack? weaponStack = seat.ItemSlot?.Itemstack;
        if (weaponStack == null)
        {
            logger.Warning($"[{MainModSystem.ModId}] [Fire Aborted] Weapon slot in seat '{seatId}' on vehicle {vehicle.EntityId} is empty.");
            return false;
        }

        TurretWeaponProperties? props = TurretWeaponProperties.FromStack(weaponStack);
        if (props == null)
        {
            logger.Warning($"[{MainModSystem.ModId}] [Fire Aborted] Stack '{weaponStack.Collectible.Code}' in seat '{seatId}' does not define TurretWeaponProperties.");
            return false;
        }

        // 2. Ammo State Resolution
        string loadedAmmoCode = TurretVehicleState.GetAmmo(vehicle, seatId);
        if (string.IsNullOrEmpty(loadedAmmoCode))
        {
            logger.Warning($"[{MainModSystem.ModId}] [Fire Aborted] Seat '{seatId}' state indicates firing, but loaded ammo string is empty.");
            return false;
        }

        CollectibleObject? ammoCollectible = sapi.World.GetItem(new AssetLocation(loadedAmmoCode));
        ammoCollectible ??= sapi.World.GetBlock(new AssetLocation(loadedAmmoCode));

        if (ammoCollectible == null)
        {
            logger.Error($"[{MainModSystem.ModId}] [Fire Failed] Ammo code '{loadedAmmoCode}' could not be resolved as an Item or Block in the world registry.");
            return false;
        }

        ItemStack ammoStack = new ItemStack(ammoCollectible, props.MaxAmmoConsumedPerShot);

        // 3. Variant & Entity Code Resolution
        JsonObject itemAttribs = ammoStack.ItemAttributes;
        JsonObject? vehicleAttribs = vehicle.Properties?.Attributes;

        string material = ExtractVariant(ammoStack, props.VariantKeys, props.DefaultVariant);
        AssetLocation entityCode = ResolveEntityCode(ammoStack, itemAttribs, material, props, logger);
        if (entityCode == null)
        {
            logger.Error($"[{MainModSystem.ModId}] [Fire Failed] Failed to derive projectile entity code for ammo '{loadedAmmoCode}'.");
            return false;
        }

        EntityProperties? entityType = sapi.World.GetEntityType(entityCode);
        if (entityType == null)
        {
            logger.Error($"[{MainModSystem.ModId}] [Fire Failed] Entity type '{entityCode}' was not found in the entity registry.");
            return false;
        }

        if (sapi.World.ClassRegistry.CreateEntity(entityType) is not EntityProjectile projectileEntity)
        {
            logger.Error($"[{MainModSystem.ModId}] [Fire Failed] Created entity '{entityCode}' does not inherit from EntityProjectile.");
            return false;
        }

        // 4. Consume Ammo & Transition State
        TurretVehicleState.SetAmmo(vehicle, seatId, "");
        TurretVehicleState.SetState(vehicle, seatId, TurretWeaponState.Idle);
        // logger.Notification($"[{MainModSystem.ModId}] Consumed ammo '{loadedAmmoCode}' from seat '{seatId}'. State reset to Idle.");

        // 5. Attribute Extraction
        string itemCodePath = ammoStack.Collectible.Code.Path;
        float BaseDamage = props.BaseDamage;
        float AmmoDamage = GetAttributeFloat(itemAttribs, vehicleAttribs, props.DamageAttributeKey, itemCodePath, material, props.DefaultAmmoDamage, logger);
        float damage = (BaseDamage + AmmoDamage) * props.DamageMultiplier;
        int damageTier = GetAttributeInt(itemAttribs, vehicleAttribs, props.DamageTierAttributeKey, itemCodePath, material, props.DefaultDamageTier, logger);
        float propulsion = GetAttributeFloat(itemAttribs, vehicleAttribs, props.PropulsionAttributeKey, itemCodePath, material, props.ProjectilePropulsionForce, logger);
        float breakChance = GetAttributeFloat(itemAttribs, vehicleAttribs, props.BreakChanceAttributeKey, itemCodePath, material, props.DefaultBreakChance, logger);

        // logger.Notification($"[{MainModSystem.ModId}] Firing Debug -> Material: '{material}' | BaseDamage: {BaseDamage} | AmmoDamage: {AmmoDamage} | Multiplier: {props.DamageMultiplier} | FinalDamage: {damage} | Propulsion: {propulsion}");

        // 6. Spatial Matrix & Trajectory Calculations
        float yaw = seat.TargetServerYaw;
        float pitch = seat.TargetServerPitch;

        // Vehicle orientation matrix (ONLY vehicle rotation)
        Matrixf vehicleMat = new Matrixf();
        vehicleMat.Identity();
        vehicleMat.RotateY(vehicle.Pos.Yaw);
        vehicleMat.RotateX(vehicle.Pos.Pitch);
        vehicleMat.RotateZ(vehicle.Pos.Roll);

        // Local aim direction vector (0 yaw = forward along vehicle)
        double cosP = Math.Cos(pitch);
        Vec3f localDir = new Vec3f(
            (float)(Math.Sin(yaw) * cosP),
            (float)(-Math.Sin(pitch)),
            (float)(Math.Cos(yaw) * cosP)
        );

        // Transform directional vector using vehicle matrix ONLY (w = 0f)
        Vec4f worldDir4 = vehicleMat.TransformVector(new Vec4f(localDir.X, localDir.Y, localDir.Z, 0f));
        Vec3d worldDir = new Vec3d(worldDir4.X, worldDir4.Y, worldDir4.Z).Normalize();

        // Fetch full turret matrix (includes BaseRotation & BaseOffset for exact 2x2 model centering)
        Matrixf turretWorldMat = seat.GetTurretWorldMatrix();

        // Transform local muzzle pivot into world space (w = 1f)
        Vec3d muzzlePivot = props.MuzzlePivotOffset ?? Vec3d.Zero;
        Vec4f worldPivot4 = turretWorldMat.TransformVector(new Vec4f((float)muzzlePivot.X, (float)muzzlePivot.Y, (float)muzzlePivot.Z, 1f));
        Vec3d worldMuzzlePos = vehicle.Pos.XYZ.Add(worldPivot4.X, worldPivot4.Y, worldPivot4.Z);

        // Apply forward muzzle offset along the aim vector
        Vec3d finalSpawnPos = worldMuzzlePos.Add(worldDir * props.MuzzleForwardOffset);

        // Calculate final velocity (Propulsion + Vehicle Motion)
        Vec3d projectileVelocity = worldDir * propulsion;
        Vec3d finalMotion = projectileVelocity.Add(vehicle.Pos.Motion);

        // logger.Notification($"[{MainModSystem.ModId}] Spatial Math Debug -> SpawnPos: {finalSpawnPos} | Final Velocity: {finalMotion}");

        // 7. Entity Initialization & World Injection
        projectileEntity.World = sapi.World;
        IProjectile projectile = projectileEntity;
        projectile.PreInitialize();

        projectile.FiredBy = player.Entity;
        projectile.Damage = damage;
        projectile.DamageTier = damageTier;
        projectile.ProjectileStack = ammoStack;
        projectile.DropOnImpactChance = Math.Clamp(1f - breakChance, 0f, 1f);

        projectileEntity.Pos.SetPos(finalSpawnPos);
        projectileEntity.Pos.Motion.Set(finalMotion);
        projectileEntity.SetInitialRotation();

        sapi.World.SpawnPriorityEntity(projectileEntity);

        // 8. Sound Effects
        if (props.FireSound != null)
        {
            sapi.World.PlaySoundAt(
                props.FireSound,
                finalSpawnPos.X, finalSpawnPos.Y, finalSpawnPos.Z,
                null, pitch: props.FireSoundPitch, range: props.FireSoundRange, volume: props.FireSoundVolume
            );
        }

        // logger.Notification($"[{MainModSystem.ModId}] Successfully spawned projectile '{entityCode}' ID: {projectileEntity.EntityId}");
        return true;
    }

    #region Shared Extraction Helpers

    private static float GetAttributeFloat(JsonObject? itemAttr, JsonObject? vehicleAttr, string attrKey, string itemCodePath, string material, float defaultValue, ILogger logger)
    {
        if (string.IsNullOrEmpty(attrKey)) return defaultValue;

        if (TryExtractValue(itemAttr, attrKey, itemCodePath, material, out float itemVal)) return itemVal;
        if (TryExtractValue(vehicleAttr, attrKey, itemCodePath, material, out float vVal)) return vVal;

        string fallbackKey = attrKey.EndsWith("ByType", StringComparison.OrdinalIgnoreCase)
            ? attrKey.Substring(0, attrKey.Length - 6)
            : attrKey + "ByType";

        if (TryExtractValue(itemAttr, fallbackKey, itemCodePath, material, out float fbItemVal)) return fbItemVal;
        if (TryExtractValue(vehicleAttr, fallbackKey, itemCodePath, material, out float fbVVal)) return fbVVal;

        logger.Warning($"[{MainModSystem.ModId}] Attribute key '{attrKey}' not found for '{itemCodePath}'. Fallback default used: {defaultValue}");
        return defaultValue;
    }

    private static int GetAttributeInt(JsonObject? itemAttr, JsonObject? vehicleAttr, string attrKey, string itemCodePath, string material, int defaultValue, ILogger logger)
    {
        return (int)GetAttributeFloat(itemAttr, vehicleAttr, attrKey, itemCodePath, material, defaultValue, logger);
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
                        string path = rawPath.Replace("{ammoMaterial}", material).Replace("{material}", material);
                        return path.Contains(':') ? new AssetLocation(path) : new AssetLocation(ammoItemCode.Domain, path);
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(props.DefaultProjectileEntityCode))
        {
            string defaultPath = props.DefaultProjectileEntityCode.Replace("{ammoMaterial}", material).Replace("{material}", material);
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

        if (token.Token is JValue)
        {
            value = token.AsFloat(0f);
            return true;
        }

        if (token.Token is JObject jObj)
        {
            JToken? pathToken = jObj.GetValue(itemCodePath, StringComparison.OrdinalIgnoreCase);
            if (pathToken != null) { value = new JsonObject(pathToken).AsFloat(0f); return true; }

            JToken? wildcardToken = jObj.GetValue($"*-{material}", StringComparison.OrdinalIgnoreCase);
            if (wildcardToken != null) { value = new JsonObject(wildcardToken).AsFloat(0f); return true; }

            JToken? materialToken = jObj.GetValue(material, StringComparison.OrdinalIgnoreCase);
            if (materialToken != null) { value = new JsonObject(materialToken).AsFloat(0f); return true; }

            JToken? starToken = jObj.GetValue("*");
            if (starToken != null) { value = new JsonObject(starToken).AsFloat(0f); return true; }
        }

        return false;
    }

    #endregion
}