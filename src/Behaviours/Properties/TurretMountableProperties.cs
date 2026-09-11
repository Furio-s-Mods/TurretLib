using Vintagestory.API.MathTools;

namespace TurretLib;

public class TurretMountableProperties : TurretPropertiesBase
{
    public float MountMaxDistance { get; set; } = 2f;
    public bool Controllable { get; set; } = true;
    
    public float EyeHeight { get; set; } = 1.5f;
    public float SeatDistance { get; set; } = 0f;
    public float CameraDistanceOffset { get; set; } = 0.05f;

    public Vec3f RiderOffset { get; set; } = new();
    public Vec3f SeatOffset { get; set; } = new();

    public string ActionLangCode { get; set; } = "turretlib:blockhelp-turret-mount";
    public string OccupiedErrorLangCode { get; set; } = "turretlib:ingameerror-occupied";
    public string TooFarErrorLangCode { get; set; } = "turretlib:ingameerror-toofar";

    public float CameraDistance => -(SeatDistance + CameraDistanceOffset);

    public override void Validate(string blockCode)
    {
        if (MountMaxDistance <= 0f)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' must define a positive 'MountMaxDistance' in TurretMountable."
            );
        }

        RequireNotNull(RiderOffset, nameof(RiderOffset), blockCode);
        RequireNotNull(SeatOffset, nameof(SeatOffset), blockCode);
    }
}