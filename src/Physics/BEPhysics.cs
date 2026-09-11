using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TurretLib;

public static class BEPhysics
{
    public static void UpdatePhysics(float dt, BlockEntityTurret BE)
    {
        if (BE.Api?.Side == EnumAppSide.Server)
        {
            BE.Position.Yaw = BE.YawClamped();
            BE.Position.Pitch = BE.Pitch;
        }
    }

    public static void ApplyMouseInput(BlockEntityTurret BE, float yawDelta, float pitchDelta)
    {
        BE.AimBehavior.SetYaw(BE.Yaw + yawDelta);
        BE.AimBehavior.SetPitch(BE.Pitch + pitchDelta);
        BE.AimBehavior.YawVelocity = yawDelta;

        if (BE.Position != null)
        {
            BE.Position.Yaw = BE.Yaw;
            BE.Position.Pitch = BE.Pitch;
        }
    }

    public static float YawClamped(this BlockEntityTurret BE) 
        => GameMath.Mod(BE.Yaw, GameMath.TWOPI);
}