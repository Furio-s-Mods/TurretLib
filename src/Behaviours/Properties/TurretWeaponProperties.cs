using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TurretLib;

public class TurretWeaponProperties : TurretPropertiesBase
{
    // Fire & Performance Multipliers
    public float DamageMultiplier { get; set; } = 1f;
    public float ProjectilePropulsionForce { get; set; } = 1.0f;
    public float DefaultBreakChance { get; set; } = 0.15f;
    public int MaxAmmoConsumedPerShot { get; set; } = 1;

    // Loading & Timing
    public float LoadSpeed { get; set; }
    public string LoadAnimationCode { get; set; } = null!;

    // Audio Configuration
    public AssetLocation LoadSound { get; set; } = null!;
    public AssetLocation FireSound { get; set; } = null!;
    public float FireSoundVolume { get; set; } = 2f;
    public float FireSoundRange { get; set; } = 32f;
    public float FireSoundPitch { get; set; } = 1f;

    // Transform & Spatial Settings
    public Vec3d MuzzlePivotOffset { get; set; } = null!;
    public float MuzzleForwardOffset { get; set; }
    public float ProjectileModelYOffsetDeg { get; set; } = 0f;
    public string[] ProjectileAttachmentPoints { get; set; } = Array.Empty<string>();
    public ModelTransform ProjectileTransform { get; set; } = new()
    {
        Translation = new Vec3f(),
        Origin = new Vec3f(),
        Rotation = new Vec3f(),
        Scale = 1f
    };

    // Attribute Key Mapping Configuration
    public string[] VariantKeys { get; set; } = new[] { "material", "metal", "type" };
    public string[] EntityCodeAttributeKeys { get; set; } = new[] 
    {
        "projectileEntityCodeByType", 
        "projectileEntityCode",
        "entityCodeByType",
        "entityCode"
    };
    public string DamageAttributeKey { get; set; } = "damage";
    public string DamageTierAttributeKey { get; set; } = "damageTier";
    public string PropulsionAttributeKey { get; set; } = "propulsionForce";
    public string BreakChanceAttributeKey { get; set; } = "breakChanceOnImpactByType";
    public string DefaultProjectileEntityCode { get; set; } = null!;

    public override void Validate(string blockCode)
    {
        if (LoadSpeed <= 0f)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' must define a 'LoadSpeed' greater than 0 in TurretWeapon."
            );
        }

        if (MaxAmmoConsumedPerShot <= 0)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' must define 'MaxAmmoConsumedPerShot' greater than 0 in TurretWeapon."
            );
        }

        RequireNotNullOrEmpty(LoadAnimationCode, nameof(LoadAnimationCode), blockCode);
        RequireNotNull(LoadSound, nameof(LoadSound), blockCode);
        RequireNotNull(FireSound, nameof(FireSound), blockCode);
        RequireNotNull(MuzzlePivotOffset, nameof(MuzzlePivotOffset), blockCode);
        RequireNotNullOrEmpty(DefaultProjectileEntityCode, nameof(DefaultProjectileEntityCode), blockCode);

        if (ProjectileAttachmentPoints == null || ProjectileAttachmentPoints.Length == 0)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' must define at least one attachment point in 'ProjectileAttachmentPoints' in TurretWeapon."
            );
        }

        RequireNotNull(ProjectileTransform, nameof(ProjectileTransform), blockCode);
    }
}

public enum TurretWeaponState : byte
{
    Idle = 0,
    Loading = 1,
    Loaded = 2,
    Firing = 3,
}