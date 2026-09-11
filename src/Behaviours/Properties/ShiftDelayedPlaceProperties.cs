using Vintagestory.API.Common;

namespace TurretLib;

public class ShiftDelayedPlaceProperties : TurretPropertiesBase
{
    public float RequiredPlaceTime { get; set; } = 1f;
    public AssetLocation LoadingSound { get; set; } = null!;
    public AssetLocation DoneSound { get; set; } = null!;
    public string ActionLangCode { get; set; } = "turretlib:heldhelp-turret-place";

    public override void Validate(string collectibleCode)
    {
        if (RequiredPlaceTime <= 0f)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Collectible '{collectibleCode}' must define a positive 'RequiredPlaceTime' in ShiftDelayedPlace."
            );
        }

        RequireNotNull(LoadingSound, nameof(LoadingSound), collectibleCode);
        RequireNotNull(DoneSound, nameof(DoneSound), collectibleCode);
        RequireNotNullOrEmpty(ActionLangCode, nameof(ActionLangCode), collectibleCode);
    }
}