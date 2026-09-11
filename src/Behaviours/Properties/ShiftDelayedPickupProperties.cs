using Vintagestory.API.Common;

namespace TurretLib;

public class ShiftDelayedPickupProperties : TurretPropertiesBase
{
    public float RequiredPickupTime { get; set; } = 1f;
    public AssetLocation LoadingSound { get; set; } = null!;
    public AssetLocation DoneSound { get; set; } = null!;
    public string ActionLangCode { get; set; } = "turretlib:blockhelp-turret-pickup";

    public override void Validate(string blockCode)
    {
        if (RequiredPickupTime <= 0f)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' must define a positive 'RequiredPickupTime' in ShiftDelayedPickup."
            );
        }

        RequireNotNull(LoadingSound, nameof(LoadingSound), blockCode);
        RequireNotNull(DoneSound, nameof(DoneSound), blockCode);
        RequireNotNullOrEmpty(ActionLangCode, nameof(ActionLangCode), blockCode);
    }
}