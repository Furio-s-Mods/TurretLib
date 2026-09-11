using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace TurretLib;

public class TurretSeat : IMountableSeat
{
    private static readonly TurretMountableProperties FallbackProperties = new();

    private readonly BlockEntityTurret parentBE;
    public EntityAgent? MountedBy;

    private readonly EntityPos seatPosition = new();
    public SeatConfig Config { get; set; } = new();

    public TurretMountableProperties Properties => parentBE.MountableBehavior?.Properties ?? FallbackProperties;

    public string SeatId { get; set; } = "turret_seat";
    public long PassengerEntityIdForInit { get; set; }
    public bool DoTeleportOnUnmount { get; set; }
    public Entity? Entity => null;
    public Entity? Passenger => MountedBy;
    public IMountable MountSupplier => parentBE;
    public bool CanControl => Properties.Controllable;
    public EnumMountAngleMode AngleMode => EnumMountAngleMode.Unaffected;

    private static readonly AnimationMetaData stableMountAnimIdle = new()
    {
        Code = "be_mount_sneak_idle", 
        Animation = "sneakidle", 
        Weight = 1f, 
        BlendMode = EnumAnimationBlendMode.Average, 
        AnimationSpeed = 1f
    };

    private static readonly AnimationMetaData stableMountAnimMoving = new()
    {
        Code = "be_mount_sneak_walk", 
        Animation = "sneakwalk", 
        Weight = 1f, 
        BlendMode = EnumAnimationBlendMode.Average, 
        AnimationSpeed = 1f
    };

    public AnimationMetaData? SuggestedAnimation => (Math.Abs(parentBE.YawVelocity) > 0.005f) ? stableMountAnimMoving : stableMountAnimIdle;
    public bool SkipIdleAnimation => true;
    public float FpHandPitchFollow => 0f;

    public Vec3f LocalEyePos
    {
        get
        {
            float yaw = parentBE.Api?.Side == EnumAppSide.Client 
                ? parentBE.ClientVisualYaw 
                : parentBE.Yaw;

            float distance = Properties.CameraDistance;

            float eyeOffsetX = (float)(-Math.Sin(yaw) * distance);
            float eyeOffsetZ = (float)(-Math.Cos(yaw) * distance);

            return new Vec3f(eyeOffsetX, Properties.EyeHeight, eyeOffsetZ);
        }
    }

    public EntityPos SeatPosition
    {
        get
        {
            float yaw = parentBE.Api?.Side == EnumAppSide.Client 
                ? parentBE.ClientVisualYaw 
                : parentBE.Yaw;

            float distance = Properties.SeatDistance;
            Vec3f offset = Properties.SeatOffset;

            double offsetX = (-Math.Sin(yaw) * distance) + offset.X;
            double offsetY = offset.Y;
            double offsetZ = (-Math.Cos(yaw) * distance) + offset.Z;

            seatPosition.X = parentBE.Position.X + offsetX;
            seatPosition.Y = parentBE.Position.Y + offsetY;
            seatPosition.Z = parentBE.Position.Z + offsetZ;
            
            seatPosition.Yaw = 0f;
            seatPosition.Pitch = 0f;
            
            return seatPosition;
        }
    }

    public Matrixf RenderTransform { get; } = new();
    public EntityControls Controls { get; } = new();

    public TurretSeat(BlockEntityTurret be)
    {
        parentBE = be;
        Controls.OnAction = OnControls;
        Config.RiderOffset = Properties.RiderOffset;
    }

    public void OnControls(EnumEntityAction action, bool on, ref EnumHandling handled)
    {
        if (parentBE == null) return;

        if (action == EnumEntityAction.Sneak && on)
        {
            ForceUnmount();
            MountedBy?.Controls?.StopAllMovement();
            handled = EnumHandling.Handled;
        }
    }

    public void ForceUnmount()
    {
        MountedBy?.TryUnmount();
        MountedBy = null;
    }

    public bool CanMount(EntityAgent entityAgent) => MountedBy == null;
    public bool CanUnmount(EntityAgent entityAgent) => true;

    public void DidMount(EntityAgent entityAgent)
    {
        MountedBy = entityAgent;
        entityAgent?.Controls?.StopAllMovement();
        RenderTransform.Identity();

        Controls.RightMouseDown = false;
        Controls.LeftMouseDown = false;

        if (parentBE.Api is ICoreClientAPI capi && entityAgent == capi.World.Player.Entity)
        {
            capi.Event.MouseDown += OnMouseDownClient;
            capi.Event.MouseUp += OnMouseUpClient;
            capi.World.Player.CameraYaw = parentBE.ClientVisualYaw;
            capi.World.Player.CameraPitch = GameMath.PI + parentBE.ClientVisualPitch;
        }
    }

    public void DidUnmount(EntityAgent entityAgent)
    {
        if (parentBE.Api is ICoreClientAPI capi && entityAgent == capi.World.Player.Entity)
        {
            capi.Event.MouseDown -= OnMouseDownClient;
            capi.Event.MouseUp -= OnMouseUpClient;
        }

        entityAgent?.TryStopHandAction(true, EnumItemUseCancelReason.ReleasedMouse);
        if (entityAgent?.Controls != null)
        {
            entityAgent.Controls.RightMouseDown = false;
            entityAgent.Controls.LeftMouseDown = false;
        }

        entityAgent?.Controls?.StopAllMovement();

        if (entityAgent != null)
        {
            Vec3d safePos = IMountableHelpers.FindSafeUnmountPosition(parentBE, entityAgent);

            entityAgent.Pos.SetPos(safePos.X, safePos.Y, safePos.Z);
            entityAgent.Pos.Motion.Set(0, 0, 0);
        }

        MountedBy = null;
    }

    private void OnMouseDownClient(MouseEvent e)
    {
        if (MountedBy == null) return;

        if (e.Button == EnumMouseButton.Right)
        {
            if (parentBE.WeaponBehavior?.State == TurretWeaponState.Loaded)
            {
                parentBE.WeaponBehavior.OnFireInput();
            }

            e.Handled = true; 
        }
        else if (e.Button == EnumMouseButton.Left)
        {
            parentBE.WeaponBehavior?.OnPullInput(true);
            e.Handled = true; 
        }
    }

    private void OnMouseUpClient(MouseEvent e)
    {
        if (MountedBy == null) return;

        if (e.Button == EnumMouseButton.Left)
        {
            parentBE.WeaponBehavior?.OnPullInput(false);
        }
    }

    public void MountableToTreeAttributes(TreeAttribute tree) => tree.SetString("className", "turretSeat");
}