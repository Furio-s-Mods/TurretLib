using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TurretLib;

public class SwivelSeatRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly Entity vehicleEntity;
    private readonly SwivelSeat seat;
    private readonly ItemSlot itemSlot;
    private readonly WearableSlotConfig slotConfig;
    private readonly AnimationUtil animUtil;
    private readonly Vec3d entityPos;
    private readonly float[] customTransformMatrix = Mat4f.Create();
    private readonly float[] ammoModelMat = Mat4f.Create();

    private readonly EntityAttachmentProperties? attachmentProps;
    private readonly TurretWeaponProperties? weaponProps;

    private ElementPose? yawPose;
    private ElementPose? pitchPose;

    private MeshRef? ammoMeshRef;
    private int ammoTextureId;
    private string currentAmmoCode = "";
    private TurretWeaponState lastRenderedState = TurretWeaponState.Idle;

    public double RenderOrder => 0.4;
    public int RenderRange => 999;

    public SwivelSeatRenderer(ICoreClientAPI capi, Entity vehicleEntity, ItemSlot itemSlot, SwivelSeat seat, WearableSlotConfig slotConfig)
    {
        this.capi = capi;
        this.vehicleEntity = vehicleEntity;
        this.itemSlot = itemSlot;
        this.seat = seat;
        this.slotConfig = slotConfig;

        this.entityPos = vehicleEntity.Pos.XYZ;
        animUtil = new AnimationUtil(capi, entityPos, vehicleEntity.Pos.Dimension);

        ItemStack? stack = itemSlot.Itemstack;
        this.attachmentProps = EntityAttachmentProperties.FromStack(stack);
        this.weaponProps = TurretWeaponProperties.FromStack(stack);

        if (stack?.Collectible != null && attachmentProps != null)
        {
            ITexPositionSource texSource = stack.Item != null
                ? capi.Tesselator.GetTextureSource(stack.Item)
                : capi.Tesselator.GetTextureSource(stack.Block);

            Shape shape = Shape.TryGet(capi, attachmentProps.ShapePath);

            if (shape != null)
            {
                animUtil.InitializeShapeAndAnimator(
                    cacheDictKey: $"swivel-turret-{stack.Collectible.Code}-{vehicleEntity.EntityId}",
                    shape: shape,
                    texSource: texSource,
                    rotation: new Vec3f(0f, 0f, 0f),
                    out MeshData meshData
                );

                animUtil.StartAnimation(new AnimationMetaData {
                    Animation = "rotation",
                    Code = "rotation",
                    AnimationSpeed = 0f,
                    Weight = 1f
                });
            }
        }

        animUtil.renderer?.ShouldRender = true;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, $"swivel-seat-renderer-{vehicleEntity.EntityId}");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Opaque && stage != EnumRenderStage.ShadowFar && stage != EnumRenderStage.ShadowNear) return;

        if (vehicleEntity == null || !vehicleEntity.Alive || itemSlot?.Itemstack == null || itemSlot.Empty || attachmentProps == null)
        {
            if (animUtil?.renderer != null)
            {
                animUtil.renderer.ShouldRender = false;
            }
            Dispose();
            return;
        }

        if (animUtil?.renderer == null) return;

        string seatId = seat.Config?.SeatId ?? SwivelSeat.defaultName;

        // 1. Sync & Update Ammo Mesh
        string ammoCode = TurretVehicleState.GetAmmo(vehicleEntity, seatId);
        UpdateAmmoMesh(ammoCode);

        // 2. Sync Weapon Animations & State
        TurretWeaponState currentState = TurretVehicleState.GetState(vehicleEntity, seatId);
        SyncWeaponState(currentState);

        // Continuous clamp for Loaded state to prevent OnFrame from resetting current frame
        if (currentState == TurretWeaponState.Loaded && weaponProps != null)
        {
            var animState = animUtil.animator?.GetAnimationState(weaponProps.LoadAnimationCode);
            if (animState != null && animState.Animation != null)
            {
                if (!animState.Active)
                {
                    SyncWeaponState(currentState);
                }
                animState.CurrentFrame = animState.Animation.QuantityFrames - 1;
                animState.EasingFactor = 1.0f;
            }
        }

        // 3. Check Load Animation Completion for Local Rider
        bool isLocalRider = seat.Passenger != null && seat.Passenger == capi.World.Player.Entity;
        if (isLocalRider && currentState == TurretWeaponState.Loading && weaponProps != null)
        {
            var animState = animUtil.animator?.GetAnimationState(weaponProps.LoadAnimationCode);
            if (animState != null && animState.AnimProgress >= 0.99f)
            {
                var modSystem = capi.ModLoader.GetModSystem<MainModSystem>();
                modSystem.SendEntityTurretInput(vehicleEntity.EntityId, seatId, TurretInputAction.CompleteLoad);
            }
        }

        // 4. Render Base Vehicle Turret Body
        if (stage == EnumRenderStage.Opaque)
        {
            entityPos.Set(vehicleEntity.Pos.X, vehicleEntity.Pos.Y, vehicleEntity.Pos.Z);
            animUtil.renderer.ShouldRender = true;

            Vec3f apOffset = SwivelSeat.GetAttachmentOffset(vehicleEntity, slotConfig?.AttachmentPointCode);

            // Vehicle transform hierarchy
            Mat4f.Identity(customTransformMatrix);
            Mat4f.RotateY(customTransformMatrix, customTransformMatrix, vehicleEntity.Pos.Yaw);
            Mat4f.RotateX(customTransformMatrix, customTransformMatrix, vehicleEntity.Pos.Pitch);
            Mat4f.RotateZ(customTransformMatrix, customTransformMatrix, vehicleEntity.Pos.Roll);
            Mat4f.Translate(customTransformMatrix, customTransformMatrix, apOffset.X, apOffset.Y, apOffset.Z);

            // Turret local base transform offset
            if (attachmentProps.BaseRotation.Y != 0) Mat4f.RotateY(customTransformMatrix, customTransformMatrix, attachmentProps.BaseRotation.Y * GameMath.DEG2RAD);
            if (attachmentProps.BaseRotation.X != 0) Mat4f.RotateX(customTransformMatrix, customTransformMatrix, attachmentProps.BaseRotation.X * GameMath.DEG2RAD);
            if (attachmentProps.BaseRotation.Z != 0) Mat4f.RotateZ(customTransformMatrix, customTransformMatrix, attachmentProps.BaseRotation.Z * GameMath.DEG2RAD);

            Mat4f.Translate(customTransformMatrix, customTransformMatrix, attachmentProps.BaseOffset.X, attachmentProps.BaseOffset.Y, attachmentProps.BaseOffset.Z);
            Mat4f.Scale(customTransformMatrix, customTransformMatrix, 1F, 1.1F, 1F);

            animUtil.renderer.CustomTransform = customTransformMatrix;

            float renderYaw = isLocalRider ? seat.LocalYaw : TurretVehicleState.GetRotation(vehicleEntity, seatId).yaw;
            float renderPitch = isLocalRider ? seat.LocalPitch : TurretVehicleState.GetRotation(vehicleEntity, seatId).pitch;

            UpdateBonePosesAndCompile(renderYaw, renderPitch, deltaTime);
            animUtil.OnRenderFrame(deltaTime, stage);
        }

        // 5. Render Loaded Projectile Mesh Attached to Animated Bone
        if (ammoMeshRef != null && weaponProps != null && currentState != TurretWeaponState.Firing)
        {
            RenderLoadedAmmo(stage);
        }
    }

    private void SyncWeaponState(TurretWeaponState currentState)
    {
        if (currentState == lastRenderedState) return;

        switch (currentState)
        {
            case TurretWeaponState.Idle:
                if (weaponProps != null) animUtil.StopAnimation(weaponProps.LoadAnimationCode);
                break;

            case TurretWeaponState.Loading:
                if (weaponProps != null)
                {
                    animUtil.StartAnimation(new AnimationMetaData
                    {
                        Animation = weaponProps.LoadAnimationCode,
                        Code = weaponProps.LoadAnimationCode,
                        AnimationSpeed = weaponProps.LoadSpeed,
                        EaseInSpeed = 10f,
                        EaseOutSpeed = 10f
                    });
                }
                break;

            case TurretWeaponState.Loaded:
                if (weaponProps != null)
                {
                    animUtil.StartAnimation(new AnimationMetaData
                    {
                        Animation = weaponProps.LoadAnimationCode,
                        Code = weaponProps.LoadAnimationCode,
                        AnimationSpeed = 1f,
                        EaseInSpeed = 9999f,
                        EaseOutSpeed = 10f
                    });

                    var animState = animUtil.animator?.GetAnimationState(weaponProps.LoadAnimationCode);
                    if (animState != null && animState.Animation != null)
                    {
                        animState.CurrentFrame = animState.Animation.QuantityFrames - 1;
                        animState.EasingFactor = 1.0f;
                    }
                }

                if (lastRenderedState == TurretWeaponState.Loading && weaponProps?.LoadSound != null)
                {
                    capi.World.PlaySoundAt(weaponProps.LoadSound, vehicleEntity);
                }
                break;

            case TurretWeaponState.Firing:
                if (weaponProps != null) animUtil.StopAnimation(weaponProps.LoadAnimationCode);
                break;
        }

        lastRenderedState = currentState;
    }

    private void RenderLoadedAmmo(EnumRenderStage stage)
    {
        if (animUtil.animator is not ClientAnimator animator || weaponProps == null) return;

        AttachmentPointAndPose? apap = null;
        string[] attachmentPoints = weaponProps.ProjectileAttachmentPoints;

        foreach (string code in attachmentPoints)
        {
            if (animator.AttachmentPointByCode.TryGetValue(code, out apap))
            {
                break;
            }
        }

        if (apap == null) return;

        Vec3d camPos = capi.World.Player.Entity.CameraPos;
        bool isShadowPass = stage == EnumRenderStage.ShadowNear || stage == EnumRenderStage.ShadowFar;
        IRenderAPI rpi = capi.Render;

        // Camera-relative vehicle base translation
        Mat4f.Identity(ammoModelMat);
        Mat4f.Translate(ammoModelMat, ammoModelMat, 
            (float)(vehicleEntity.Pos.X - camPos.X), 
            (float)(vehicleEntity.Pos.Y - camPos.Y), 
            (float)(vehicleEntity.Pos.Z - camPos.Z)
        );

        // Apply Vehicle & Base Turret Transformations
        Mat4f.Mul(ammoModelMat, ammoModelMat, customTransformMatrix);

        // Apply Animated Bone Transformation (Yaw & Pitch)
        if (apap.AnimModelMatrix != null)
        {
            Mat4f.Mul(ammoModelMat, ammoModelMat, apap.AnimModelMatrix);
        }

        AttachmentPoint ap = apap.AttachPoint;
        ModelTransform transform = weaponProps.ProjectileTransform;
        float modelYOffsetRad = weaponProps.ProjectileModelYOffsetDeg * GameMath.DEG2RAD;

        // Apply Configured Model Transforms Relative to Attachment Point
        Mat4f.Translate(ammoModelMat, ammoModelMat, 
            (float)ap.PosX / 16f + transform.Translation.X, 
            (float)ap.PosY / 16f + transform.Translation.Y, 
            (float)ap.PosZ / 16f + transform.Translation.Z
        );

        Mat4f.Translate(ammoModelMat, ammoModelMat, transform.Origin.X, transform.Origin.Y, transform.Origin.Z);
        Mat4f.Scale(ammoModelMat, ammoModelMat, transform.ScaleXYZ.X, transform.ScaleXYZ.Y, transform.ScaleXYZ.Z);

        Mat4f.RotateX(ammoModelMat, ammoModelMat, (float)(ap.RotationX + transform.Rotation.X) * GameMath.DEG2RAD);
        Mat4f.RotateY(ammoModelMat, ammoModelMat, (float)(ap.RotationY + transform.Rotation.Y) * GameMath.DEG2RAD);
        Mat4f.RotateZ(ammoModelMat, ammoModelMat, (float)(ap.RotationZ + transform.Rotation.Z) * GameMath.DEG2RAD);

        Mat4f.RotateY(ammoModelMat, ammoModelMat, modelYOffsetRad);
        Mat4f.Translate(ammoModelMat, ammoModelMat, -transform.Origin.X, -transform.Origin.Y, -transform.Origin.Z);

        // Draw Mesh
        IStandardShaderProgram? prog = null;
        if (isShadowPass)
        {
            rpi.CurrentActiveShader.BindTexture2D("tex2d", ammoTextureId, 0);
            Mat4f.Mul(ammoModelMat, rpi.CurrentShadowProjectionMatrix, ammoModelMat);
            rpi.CurrentActiveShader.UniformMatrix("mvpMatrix", ammoModelMat);
            rpi.CurrentActiveShader.Uniform("origin", new Vec3f(
                (float)(vehicleEntity.Pos.X - camPos.X), 
                (float)(vehicleEntity.Pos.Y - camPos.Y), 
                (float)(vehicleEntity.Pos.Z - camPos.Z)
            ));
        }
        else
        {
            prog = rpi.PreparedStandardShader((int)vehicleEntity.Pos.X, (int)vehicleEntity.Pos.Y, (int)vehicleEntity.Pos.Z);
            prog.ModelMatrix = ammoModelMat;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            prog.Tex2D = ammoTextureId;
            prog.AlphaTest = 0.01f;
            rpi.BindTexture2d(ammoTextureId);
        }

        rpi.GlDisableCullFace();
        rpi.RenderMesh(ammoMeshRef);
        rpi.GlEnableCullFace();

        if (!isShadowPass)
        {
            prog?.Stop();
        }
    }

    private void UpdateAmmoMesh(string ammoCode)
    {
        if (ammoCode == currentAmmoCode) return;
        currentAmmoCode = ammoCode;

        ammoMeshRef?.Dispose();
        ammoMeshRef = null;

        if (string.IsNullOrEmpty(ammoCode)) return;

        AssetLocation loc = new AssetLocation(ammoCode);
        Item? ammoItem = capi.World.GetItem(loc);
        Block? ammoBlock = capi.World.GetBlock(loc);

        MeshData? mesh = null;
        if (ammoItem != null)
        {
            capi.Tesselator.TesselateItem(ammoItem, out mesh);
            ammoTextureId = capi.ItemTextureAtlas.AtlasTextures[0].TextureId;
        }
        else if (ammoBlock != null)
        {
            capi.Tesselator.TesselateBlock(ammoBlock, out mesh);
            ammoTextureId = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
        }

        if (mesh != null)
        {
            ammoMeshRef = capi.Render.UploadMesh(mesh);
        }
    }

    private void UpdateBonePosesAndCompile(float yawRad, float pitchRad, float deltaTime)
    {
        if (animUtil?.animator == null || attachmentProps == null) return;

        yawPose ??= animUtil.animator.GetPosebyName(attachmentProps.YawBoneName);
        pitchPose ??= animUtil.animator.GetPosebyName(attachmentProps.PitchBoneName);

        if (yawPose != null)
        {
            float finalYaw = attachmentProps.InvertYaw ? -yawRad : yawRad;
            yawPose.degOffY = finalYaw * GameMath.RAD2DEG;
        }

        if (pitchPose != null)
        {
            float finalPitch = attachmentProps.InvertPitch ? -pitchRad : pitchRad;
            pitchPose.degOffX = finalPitch * GameMath.RAD2DEG;
        }

        animUtil.animator.CalculateMatrices = true;
        animUtil.animator.OnFrame(animUtil.activeAnimationsByAnimCode, deltaTime);
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);

        ammoMeshRef?.Dispose();
        ammoMeshRef = null;

        if (animUtil?.renderer != null)
        {
            animUtil.renderer.ShouldRender = false;
        }

        animUtil?.Dispose();
        GC.SuppressFinalize(this);
    }
}