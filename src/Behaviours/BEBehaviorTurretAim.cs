using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TurretLib;

public class BEBehaviorTurretAim(BlockEntity blockentity) : BEBehaviorTurretConfigurable<TurretAimProperties>(blockentity)
{
    private bool _isLoadedFromTree;
    public float Yaw { get; set; } = 0f;
    public float Pitch { get; set; } = 0f;
    public float ClientVisualYaw { get; set; }
    public float ClientVisualPitch { get; set; }
    public float YawVelocity { get; set; } = 0f;
    
    public ElementPose? YawPose { get; private set; }
    public ElementPose? PitchPose { get; private set; }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        if (Properties == null) return;

        if (!_isLoadedFromTree)
        {
            SetYaw(Properties.DefaultYaw * GameMath.DEG2RAD);
            SetPitch(-Properties.DefaultPitch * GameMath.DEG2RAD);
        }
    }

    /// <summary>
    /// Safely sets the yaw angle in radians, applying clamping if limits exist or wrapping angle if free.
    /// </summary>
    public void SetYaw(float targetYawRad)
    {
        if (Properties == null) return;
        if (Properties.HasYawLimits)
        {
            float minRad = Properties.MinYawDeg * GameMath.DEG2RAD;
            float maxRad = Properties.MaxYawDeg * GameMath.DEG2RAD;
            Yaw = GameMath.Clamp(targetYawRad, minRad, maxRad);
        }
        else
        {
            // Free 360° rotation: normalize radians within [-PI, PI] to prevent overflow
            Yaw = GameMath.NormaliseAngleRad(targetYawRad);
        }
    }

    /// <summary>
    /// Safely sets the pitch angle in radians, applying pitch bounds.
    /// </summary>
    public void SetPitch(float targetPitchRad)
    {
        if (Properties == null) return;
        // inverted
        float maxRad = -Properties.MinPitchDeg * GameMath.DEG2RAD;
        float minRad = -Properties.MaxPitchDeg * GameMath.DEG2RAD;
        Pitch = GameMath.Clamp(targetPitchRad, minRad, maxRad);
    }

    public void SetupPoses(BEBehaviorAnimatable animatable)
    {
        if (Properties == null) return;
        if (animatable?.animUtil?.animator == null) return;

        YawPose = animatable.animUtil.animator.GetPosebyName(Properties.YawBoneName);
        PitchPose = animatable.animUtil.animator.GetPosebyName(Properties.PitchBoneName);
    }

    public void UpdatePoseAngles()
    {
        if (Properties == null) return;
        float yawDeg = (ClientVisualYaw + (Properties.ModelYawOffsetDeg * GameMath.DEG2RAD)) * GameMath.RAD2DEG;
        float pitchDeg = ClientVisualPitch * GameMath.RAD2DEG;

        YawPose?.degOffY = yawDeg;
        PitchPose?.degOffX = pitchDeg;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    { 
        base.ToTreeAttributes(tree);
        tree.SetFloat("turretPitch", Pitch);
        tree.SetFloat("turretYaw", Yaw);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        
        if (Blockentity is BlockEntityTurret turret && turret.IsLocallyControlled() && worldForResolving.Side == EnumAppSide.Client)
        {
            // this.Api.Logger.Notification($"[FromTreeAttributes] skipped aim setting locally:({turret.IsLocallyControlled()})");
            return;
        }

        _isLoadedFromTree = true;
        Pitch = tree.GetFloat("turretPitch", Pitch);
        Yaw = tree.GetFloat("turretYaw", Yaw);
    }
}