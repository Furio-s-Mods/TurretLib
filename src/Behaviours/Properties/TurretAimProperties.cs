using System;

namespace TurretLib;

public class TurretAimProperties : TurretPropertiesBase
{
    public string YawBoneName { get; set; } = null!;
    public string PitchBoneName { get; set; } = null!;
    public float DefaultYaw { get; set; } = 0f;
    public float DefaultPitch { get; set; } = 0f;
    public float MinPitchDeg { get; set; } = -90f;
    public float MaxPitchDeg { get; set; } = 90f;
    public float MinYawDeg { get; set; } = 0f;
    public float MaxYawDeg { get; set; } = 0f;
    public float ModelYawOffsetDeg { get; set; } = 0f;

    // Equal bounds (e.g. 0 and 0) mean yaw is unlimited/free
    public bool HasYawLimits => MinYawDeg != MaxYawDeg;

    public override void Validate(string blockCode)
    {
        RequireNotNullOrEmpty(YawBoneName, nameof(YawBoneName), blockCode);
        RequireNotNullOrEmpty(PitchBoneName, nameof(PitchBoneName), blockCode);

        if (MinPitchDeg >= MaxPitchDeg)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' has invalid pitch limits in TurretAim (MinPitchDeg '{MinPitchDeg}' must be less than MaxPitchDeg '{MaxPitchDeg}')."
            );
        }

        if (HasYawLimits && MinYawDeg > MaxYawDeg)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' has invalid yaw limits in TurretAim (MinYawDeg '{MinYawDeg}' cannot be greater than MaxYawDeg '{MaxYawDeg}')."
            );
        }
    }
}