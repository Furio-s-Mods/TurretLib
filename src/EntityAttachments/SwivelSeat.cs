using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TurretLib;

public class SwivelSeat : EntityBoatSeat, IRenderer
{
    public const string defaultName = "swivelseat";
    public double RenderOrder => 0.5;
    public int RenderRange => 999;

    public const float SENSITIVITY_FACTOR = 0.35f;
    public static readonly float MIN_PITCH = -20f * GameMath.DEG2RAD;
    public static readonly float MAX_PITCH = 30f * GameMath.DEG2RAD;
    private Entity VehicleEntity => Entity;
    private readonly ItemSlot itemSlot;
    private readonly WearableSlotConfig slotConfig;
    private readonly EntityPos seatPosition = new();
    private readonly Vec3f itemRiderOffset;

    private ICoreClientAPI? capi;
    private float lastCameraYaw;
    private float lastCameraPitch;
    private bool wasControlledLastFrame;
    private float clientRenderYaw;
    private float clientRenderPitch;
    private float lastSentYaw;
    private float lastSentPitch;
    public const float MAX_TURN_SPEED = 1.5f;

    private ItemStack? lastCheckedStack;
    private EntityAttachmentProperties? cachedProperties;

    public EntityAttachmentProperties? CurrentProperties
    {
        get
        {
            ItemStack? currentStack = itemSlot.Itemstack;
            if (currentStack != lastCheckedStack)
            {
                lastCheckedStack = currentStack;
                cachedProperties = EntityAttachmentProperties.FromStack(currentStack);
            }
            return cachedProperties;
        }
    }

    public ItemSlot ItemSlot => itemSlot;
    public float TargetServerYaw => TurretVehicleState.GetSeatTree(VehicleEntity, Config?.SeatId ?? defaultName)?.GetFloat("yaw", 0f) ?? 0f;
    public float TargetServerPitch => TurretVehicleState.GetSeatTree(VehicleEntity, Config?.SeatId ?? defaultName)?.GetFloat("pitch", 0f) ?? 0f;   

    public float LocalYaw
    {
        get
        {
            if (Passenger != null && capi?.World?.Player?.Entity == Passenger)
            {
                return clientRenderYaw;
            }
            return TargetServerYaw;
        }
        set => clientRenderYaw = value;
    }

    public float LocalPitch
    {
        get
        {
            if (Passenger != null && capi?.World?.Player?.Entity == Passenger)
            {
                return clientRenderPitch;
            }
            return TargetServerPitch;
        }
        set => clientRenderPitch = value;
    }

    public float OrbitRadius { get; set; } = 1.0f;
    public static Vec3f pivotOffset = new(-1f, 0.05f, 1.5f);
    private SwivelSeatRenderer? renderer;

    public SwivelSeat(
        IMountable mountSupplier, 
        ItemSlot slot,
        SeatConfig config, 
        WearableSlotConfig slotConfig, 
        Vec3f? itemRiderOffset = null)
        : base(mountSupplier, config?.SeatId ?? defaultName, config)
    {
        itemSlot = slot;
        this.slotConfig = slotConfig;
        this.itemRiderOffset = itemRiderOffset ?? new Vec3f();

        if (config?.Attributes?["orbitRadius"].Exists == true)
        {
            OrbitRadius = config.Attributes["orbitRadius"].AsFloat(1.0f);
        }

        clientRenderYaw = TargetServerYaw;
        clientRenderPitch = TargetServerPitch;

        if (VehicleEntity.Api is ICoreClientAPI clientApi)
        {
            capi = clientApi;
            UpdateRendererState();
        }
    }

    public void UpdateRendererState()
    {
        if (capi == null) return;

        EntityAttachmentProperties? props = CurrentProperties;
        ItemStack? currentStack = itemSlot.Itemstack;

        bool hasValidItem = props != null || (currentStack?.Collectible?.Attributes?["attachableToEntity"]?["rotatable"].AsBool(false) ?? false);

        if (hasValidItem && renderer == null)
        {
            renderer = new SwivelSeatRenderer(capi, VehicleEntity, itemSlot, this, slotConfig);
        }
        else if (!hasValidItem && renderer != null)
        {
            renderer.Dispose();
            renderer = null;
        }
    }

    public override Vec3f LocalEyePos => new(0f, Config?.EyeHeight ?? 1.5f, 0f);

    public override EntityPos SeatPosition
    {
        get
        {
            string? apCode = slotConfig?.AttachmentPointCode ?? Config?.APName;
            Vec3f apOffset = GetAttachmentOffset(VehicleEntity, apCode);
            Vec3f slotOffset = Config?.RiderOffset ?? new Vec3f();
            Vec3f baseOffset = CurrentProperties?.BaseOffset ?? new Vec3f();

            float pivotX = apOffset.X + slotOffset.X;
            float pivotY = apOffset.Y + slotOffset.Y;
            float pivotZ = apOffset.Z + slotOffset.Z;

            float yaw = LocalYaw;

            float orbitOffsetX = -MathF.Sin(yaw) * OrbitRadius;
            float orbitOffsetZ = -MathF.Cos(yaw) * OrbitRadius;

            float localX = pivotX + orbitOffsetX + itemRiderOffset.X + baseOffset.X;
            float localY = pivotY + itemRiderOffset.Y + baseOffset.Y;
            float localZ = pivotZ + orbitOffsetZ + itemRiderOffset.Z + baseOffset.Z;

            Matrixf mat = new Matrixf();
            mat.Identity();
            mat.RotateY(VehicleEntity.Pos.Yaw);
            mat.RotateX(VehicleEntity.Pos.Pitch);
            mat.RotateZ(VehicleEntity.Pos.Roll);

            Vec4f worldOffset = mat.TransformVector(new Vec4f(localX, localY, localZ, 1f));

            seatPosition.X = VehicleEntity.Pos.X + worldOffset[0];
            seatPosition.Y = VehicleEntity.Pos.Y + worldOffset[1];
            seatPosition.Z = VehicleEntity.Pos.Z + worldOffset[2];

            seatPosition.Yaw = VehicleEntity.Pos.Yaw + yaw;
            seatPosition.Pitch = VehicleEntity.Pos.Pitch;
            seatPosition.Roll = VehicleEntity.Pos.Roll;

            return seatPosition;
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        UpdateRendererState();

        if (stage != EnumRenderStage.Before || capi?.World?.Player?.Entity == null) return;

        IClientPlayer localPlayer = capi.World.Player;
        bool isDriver = Passenger == localPlayer.Entity && localPlayer.Entity.MountedOn == this;

        if (isDriver && localPlayer.CameraMode != EnumCameraMode.Overhead)
        {
            EntityAttachmentProperties? props = CurrentProperties;

            if (!wasControlledLastFrame)
            {
                clientRenderYaw = TargetServerYaw;
                clientRenderPitch = TargetServerPitch;

                float initialCameraYaw = VehicleEntity.Pos.Yaw + clientRenderYaw;
                float initialCameraPitch = GameMath.PI + clientRenderPitch;

                localPlayer.CameraYaw = initialCameraYaw;
                localPlayer.CameraPitch = initialCameraPitch;

                lastCameraYaw = initialCameraYaw;
                lastCameraPitch = initialCameraPitch;
                wasControlledLastFrame = true;
            }

            float maxStep = MAX_TURN_SPEED * deltaTime;

            // 1. YAW CALCULATION
            float rawYawDelta = GameMath.AngleRadDistance(lastCameraYaw, localPlayer.CameraYaw);
            if (props?.InvertYaw == true) rawYawDelta = -rawYawDelta;

            float desiredYawDelta = rawYawDelta * SENSITIVITY_FACTOR;
            float clampedYawDelta = GameMath.Clamp(desiredYawDelta, -maxStep, maxStep);

            if (MathF.Abs(clampedYawDelta) > 0.00001f)
            {
                clientRenderYaw = GameMath.Mod(clientRenderYaw + clampedYawDelta + GameMath.PI, GameMath.TWOPI) - GameMath.PI;
            }

            float targetCameraYaw = VehicleEntity.Pos.Yaw + clientRenderYaw;
            localPlayer.CameraYaw = targetCameraYaw;
            lastCameraYaw = targetCameraYaw;

            // 2. PITCH CALCULATION
            float rawPitchDelta = localPlayer.CameraPitch - lastCameraPitch;
            if (props?.InvertPitch == true) rawPitchDelta = -rawPitchDelta;

            float desiredPitchDelta = rawPitchDelta * SENSITIVITY_FACTOR;
            float clampedPitchDelta = GameMath.Clamp(desiredPitchDelta, -maxStep, maxStep);

            if (MathF.Abs(clampedPitchDelta) > 0.00001f)
            {
                clientRenderPitch = GameMath.Clamp(clientRenderPitch + clampedPitchDelta, MIN_PITCH, MAX_PITCH);
            }

            float translatedPitch = GameMath.PI + clientRenderPitch;
            localPlayer.CameraPitch = translatedPitch;
            lastCameraPitch = translatedPitch;

            if (Passenger is EntityPlayer passenger)
            {
                passenger.Pos.Pitch = translatedPitch;
                if (localPlayer.CameraMode != EnumCameraMode.FirstPerson)
                {
                    passenger.Pos.Yaw = targetCameraYaw;
                    passenger.BodyYaw = targetCameraYaw;
                }
            }

            // 3. NETWORK SYNC
            bool yawChanged = MathF.Abs(GameMath.AngleRadDistance(lastSentYaw, clientRenderYaw)) > 0.01f;
            bool pitchChanged = MathF.Abs(lastSentPitch - clientRenderPitch) > 0.01f;

            if (yawChanged || pitchChanged)
            {
                lastSentYaw = clientRenderYaw;
                lastSentPitch = clientRenderPitch;

                var modSystem = capi.ModLoader.GetModSystem<MainModSystem>();
                modSystem?.ClientChannel?.SendPacket(new SwivelRotationPacket
                {
                    EntityId = VehicleEntity.EntityId,
                    SeatId = Config?.SeatId ?? defaultName,
                    Yaw = clientRenderYaw,
                    Pitch = clientRenderPitch
                });
            }
        }
        else
        {
            wasControlledLastFrame = false;
            clientRenderYaw = GameMath.Lerp(clientRenderYaw, TargetServerYaw, deltaTime * 8f);
            clientRenderPitch = GameMath.Lerp(clientRenderPitch, TargetServerPitch, deltaTime * 8f);
        }
    }

    public override void DidMount(EntityAgent entityAgent)
    {
        base.DidMount(entityAgent);
        entityAgent.Controls?.StopAllMovement();
        Passenger = entityAgent;

        if (VehicleEntity.Api is ICoreClientAPI capi && entityAgent == capi.World.Player.Entity)
        {
            
            capi.Event.MouseDown += OnMouseDownClient;
            capi.Event.MouseUp += OnMouseUpClient;

            clientRenderYaw = TargetServerYaw;
            clientRenderPitch = TargetServerPitch;

            float targetCameraYaw = VehicleEntity.Pos.Yaw + clientRenderYaw;
            capi.World.Player.CameraYaw = targetCameraYaw;
            capi.World.Player.CameraPitch = GameMath.PI + clientRenderPitch;
            
            lastCameraYaw = targetCameraYaw;
            lastCameraPitch = GameMath.PI + clientRenderPitch;
            wasControlledLastFrame = true;

            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "swivelseat-camera");
        }
    }

    public override void DidUnmount(EntityAgent entityAgent)
    {
        base.DidUnmount(entityAgent);

        if (VehicleEntity.Api is ICoreClientAPI capi && entityAgent == capi.World.Player.Entity)
        {
            capi.Event.MouseDown -= OnMouseDownClient;
            capi.Event.MouseUp -= OnMouseUpClient;
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        }

        Passenger = null;
        wasControlledLastFrame = false;

        if (entityAgent != null)
        {
            Vec3d safePos = entityAgent.Pos.XYZ;
            entityAgent.Pos.SetPos(safePos.X, safePos.Y, safePos.Z);
            entityAgent.Pos.Motion.Set(0, 0, 0);
            entityAgent.Controls?.StopAllMovement();
        }
    }

    public void Dispose()
    {
        capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        renderer?.Dispose();
        renderer = null;

        GC.SuppressFinalize(this);
    }

    public static Vec3f GetAttachmentOffset(Entity entity, string? apCode)
    {
        if (string.IsNullOrEmpty(apCode)) return new Vec3f(pivotOffset.X, pivotOffset.Y, pivotOffset.Z);

        var apap = entity.AnimManager?.Animator?.GetAttachmentPointPose(apCode);

        if (apap != null && apap.AnimModelMatrix != null)
        {
            float[] m = apap.AnimModelMatrix;

            float posX = 0f;          // Keep player centered left-to-right
            float posY = m[13];       // Maintain height (Y)
            float posZ = -m[12];       // Redirect the shifting slot value to depth (Z)

            return new Vec3f(posX + pivotOffset.X, posY + pivotOffset.Y, posZ + pivotOffset.Z);
        }

        Shape? entityShape = entity.Properties?.Client?.LoadedShape;
        if (entityShape?.Elements != null)
        {
            AttachmentPoint? ap = FindAttachmentPointRecursive(entityShape.Elements, apCode);
            if (ap != null)
            {
                float posX = (float)(ap.PosX / 16.0) - 0.5f;
                float posY = (float)(ap.PosY / 16.0);
                float posZ = (float)(ap.PosZ / 16.0) - 0.5f;

                return new Vec3f(posX + pivotOffset.X, posY + pivotOffset.Y, posZ + pivotOffset.Z);
            }
        }

        return new Vec3f(0, 0, 0);
    }

    private static AttachmentPoint? FindAttachmentPointRecursive(ShapeElement[]? elements, string apCode)
    {
        if (elements == null) return null;

        for (var i = 0; i < elements.Length; i++)
        {
            ShapeElement elem = elements[i];
            if (elem.AttachmentPoints != null)
            {
                for (var j = 0; j < elem.AttachmentPoints.Length; j++)
                {
                    if (elem.AttachmentPoints[j].Code == apCode) return elem.AttachmentPoints[j];
                }
            }

            if (elem.Children != null)
            {
                AttachmentPoint? childAp = FindAttachmentPointRecursive(elem.Children, apCode);
                if (childAp != null) return childAp;
            }
        }

        return null;
    }

    public override AnimationMetaData? SuggestedAnimation => null;
    public override Matrixf RenderTransform { get; } = new Matrixf().Identity();
    public override float FpHandPitchFollow => 0f;

    private void OnMouseDownClient(MouseEvent e)
    {
        if (Passenger == null || VehicleEntity.Api is not ICoreClientAPI capi) return;

        string seatId = Config?.SeatId ?? defaultName;
        TurretWeaponState state = TurretVehicleState.GetState(VehicleEntity, seatId);
        string ammo = TurretVehicleState.GetAmmo(VehicleEntity, seatId);
        var modSystem = capi.ModLoader.GetModSystem<MainModSystem>();

        if (e.Button == EnumMouseButton.Left) // Pull / Load Mechanism
        {
            if (state == TurretWeaponState.Idle && !string.IsNullOrEmpty(ammo))
            {
                modSystem.SendEntityTurretInput(VehicleEntity.EntityId, seatId, TurretInputAction.StartLoad);
                e.Handled = true;
            }
        }
        else if (e.Button == EnumMouseButton.Right) // Fire
        {
            if (state == TurretWeaponState.Loaded)
            {
                modSystem.SendEntityTurretInput(VehicleEntity.EntityId, seatId, TurretInputAction.Fire);
                e.Handled = true;
            }
        }
    }

    private void OnMouseUpClient(MouseEvent e)
    {
        if (Passenger == null || VehicleEntity.Api is not ICoreClientAPI capi) return;

        if (e.Button == EnumMouseButton.Left)
        {
            string? seatId = Config?.SeatId;
            if (seatId == null) return;

            TurretWeaponState state = TurretVehicleState.GetState(VehicleEntity, seatId);

            if (state == TurretWeaponState.Loading)
            {
                var modSystem = capi.ModLoader.GetModSystem<MainModSystem>();
                modSystem.SendEntityTurretInput(VehicleEntity.EntityId, seatId, TurretInputAction.CancelLoad);
            }
        }
    }

    // public Vec3d GetLocalPivotPosition(Vec3d? muzzlePivotOffset = null)
    // {
    //     string? apCode = slotConfig?.AttachmentPointCode ?? Config?.APName;
    //     Vec3f apOffset = GetAttachmentOffset(VehicleEntity, apCode);
    //     Vec3f slotOffset = Config?.RiderOffset ?? new Vec3f();
    //     Vec3f baseOffset = CurrentProperties?.BaseOffset ?? new Vec3f();

    //     float yaw = LocalYaw;
    //     float orbitOffsetX = -MathF.Sin(yaw) * OrbitRadius;
    //     float orbitOffsetZ = -MathF.Cos(yaw) * OrbitRadius;

    //     double localX = apOffset.X + slotOffset.X + baseOffset.X + itemRiderOffset.X + orbitOffsetX + (muzzlePivotOffset?.X ?? 0);
    //     double localY = apOffset.Y + slotOffset.Y + baseOffset.Y + itemRiderOffset.Y + (muzzlePivotOffset?.Y ?? 0);
    //     double localZ = apOffset.Z + slotOffset.Z + baseOffset.Z + itemRiderOffset.Z + orbitOffsetZ + (muzzlePivotOffset?.Z ?? 0);

    //     return new Vec3d(localX, localY, localZ);
    // }

    /// <summary>
    /// Computes the exact 3D transformation matrix for the turret base in world space,
    /// matching SwivelSeatRenderer.
    /// </summary>
    public Matrixf GetTurretWorldMatrix()
    {
        Matrixf mat = new Matrixf();
        mat.Identity();

        // Vehicle World Rotation
        mat.RotateY(VehicleEntity.Pos.Yaw);
        mat.RotateX(VehicleEntity.Pos.Pitch);
        mat.RotateZ(VehicleEntity.Pos.Roll);

        // Attachment Point Offset on Vehicle
        string? apCode = slotConfig?.AttachmentPointCode;
        Vec3f apOffset = GetAttachmentOffset(VehicleEntity, apCode);
        mat.Translate(apOffset.X, apOffset.Y, apOffset.Z);

        // Attachment Base Rotation & Base Offset (Centers 2x2 model over the slot)
        EntityAttachmentProperties? attachProps = CurrentProperties;
        if (attachProps != null)
        {
            if (attachProps.BaseRotation.Y != 0) mat.RotateY(attachProps.BaseRotation.Y * GameMath.DEG2RAD);
            if (attachProps.BaseRotation.X != 0) mat.RotateX(attachProps.BaseRotation.X * GameMath.DEG2RAD);
            if (attachProps.BaseRotation.Z != 0) mat.RotateZ(attachProps.BaseRotation.Z * GameMath.DEG2RAD);

            mat.Translate(attachProps.BaseOffset.X, attachProps.BaseOffset.Y, attachProps.BaseOffset.Z);
        }

        return mat;
    }
}