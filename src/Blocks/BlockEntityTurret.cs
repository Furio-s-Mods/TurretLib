using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TurretLib;

public class BlockEntityTurret : BlockEntity, IMountable
{
    public BlockBehaviorTurretMountable? MountableBehavior { get; private set; }
    public BEBehaviorTurretAim AimBehavior { get; private set; } = null!;
    public BEBehaviorTurretInventory? InventoryBehavior { get; private set; }
    public BEBehaviorTurretWeapon? WeaponBehavior { get; private set; }
    public BlockEntityAnimationUtil? AnimUtil { get; private set; }

    public float Yaw { get => AimBehavior.Yaw; }
    public float Pitch { get => AimBehavior.Pitch; }
    public float YawVelocity { get => AimBehavior.YawVelocity; }

    public float ClientVisualYaw
    {
        get => AimBehavior.ClientVisualYaw;
        set { AimBehavior.ClientVisualYaw = value; }
    }

    public float ClientVisualPitch
    {
        get => AimBehavior.ClientVisualPitch;
        set { AimBehavior.ClientVisualPitch = value; }
    }

    public bool IsLoaded => WeaponBehavior?.IsLoaded ?? false;
    public bool IsLoading => WeaponBehavior?.IsLoading ?? false;

    private TurretBlockRenderer? renderer;
    private TurretSeat internalSeat = null!;
    private IMountableSeat[]? cachedSeatsArray;
    private EntityPos entityPosition = null!;
    
    public TurretSeat Seat => internalSeat;
    public EntityPos Position => entityPosition;
    public double StepPitch => 0.0;
    public Entity? Controller => internalSeat.MountedBy;
    public Entity? OnEntity => null;
    public EntityControls ControllingControls => internalSeat.Controls;
    public IMountableSeat[] Seats => cachedSeatsArray ??= [internalSeat];

    private IClientNetworkChannel? clientChannel;
    private IServerNetworkChannel? serverChannel;
    private long physicsTickListenerId;

    public bool AnyMounted() => Controller != null;

    public bool IsLocallyControlled()
        => Api is ICoreClientAPI capi && Controller?.EntityId == capi.World.Player.Entity.EntityId;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        MountableBehavior = Block.GetBehavior<BlockBehaviorTurretMountable>();
        AimBehavior = GetBehavior<BEBehaviorTurretAim>() 
            ?? throw new InvalidOperationException($"[{MainModSystem.ModId}] BlockEntity '{Block?.Code}' is missing required entity behavior 'turretlib:TurretAim'.");
        WeaponBehavior = GetBehavior<BEBehaviorTurretWeapon>();
        InventoryBehavior = GetBehavior<BEBehaviorTurretInventory>();

        internalSeat = new TurretSeat(this);

        entityPosition = Pos != null
            ? new EntityPos(Pos.X + 0.5, Pos.Y, Pos.Z + 0.5, AimBehavior.Yaw, AimBehavior.Pitch, 0)
            : new EntityPos(0, 0, 0, 0, 0, 0);

        if (api is ICoreClientAPI capi)
        {
            clientChannel = capi.Network.GetChannel(MainModSystem.NetworkChannel);

            var animatable = GetBehavior<BEBehaviorAnimatable>();
            if (animatable != null)
            {
                AnimUtil = animatable.animUtil;

                AssetLocation shapeLocation = Block.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                Shape shape = capi.Assets.Get<Shape>(shapeLocation);

                AnimUtil.InitializeAnimator($"turret-{Pos}", shape);

                if (AnimUtil.animator != null)
                {
                    AnimUtil.StartAnimation(new AnimationMetaData { Animation = "rotation", Code = "rotation" });
                    AimBehavior.SetupPoses(animatable);
                }
            }

            renderer = new TurretBlockRenderer(capi, this);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Before, $"turret-camera-{Pos}");
        }
        else if (api is ICoreServerAPI sapi)
        {
            serverChannel = sapi.Network.GetChannel(MainModSystem.NetworkChannel);
        }

        physicsTickListenerId = RegisterGameTickListener(UpdatePhysics, 20);
    }

    public void UpdatePoseAngles() => AimBehavior.UpdatePoseAngles();

    private void UpdatePhysics(float dt)
    {
        if (Api.Side == EnumAppSide.Server || IsLocallyControlled())
        {
            BEPhysics.UpdatePhysics(dt, this);
        }
    }

    public override void OnBlockRemoved()
    {
        if (Controller != null)
        {
            internalSeat.ForceUnmount();
        }

        base.OnBlockRemoved();
        CleanUp();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        CleanUp();
    }

    private void CleanUp()
    {
        if (Api?.Side == EnumAppSide.Server && WeaponBehavior?.State == TurretWeaponState.Loading)
        {
            WeaponBehavior.ServerSetState(TurretWeaponState.Idle, this);
        }

        if (physicsTickListenerId != 0)
        {
            UnregisterGameTickListener(physicsTickListenerId);
            physicsTickListenerId = 0;
        }

        if (Api is ICoreClientAPI capi && renderer != null)
        {
            capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Before);
            renderer.Dispose();
            renderer = null;
        }
    }

    public void SendClientSync()
    {
        if (clientChannel == null) return;
        clientChannel.SendPacket(new BESyncMessage 
        { 
            Pos = Pos, 
            Yaw = Yaw, 
            Pitch = Pitch
        });
    }

    public void ReceiveNetworkSync(BESyncMessage msg)
    {
        if (msg.Pos != Pos) return;

        if (!IsLocallyControlled())
        {
            AimBehavior.Yaw = msg.Yaw;
            AimBehavior.Pitch = msg.Pitch;

            if (entityPosition != null)
            {
                entityPosition.Yaw = msg.Yaw;
                entityPosition.Pitch = msg.Pitch;
            }
        }

        if (Api?.Side == EnumAppSide.Server && serverChannel != null)
        {
            if (Controller is EntityPlayer { Player: IServerPlayer pilotPlayer })
                serverChannel.BroadcastPacket(msg, pilotPlayer);
            else
                serverChannel.BroadcastPacket(msg);
        }
    }
}