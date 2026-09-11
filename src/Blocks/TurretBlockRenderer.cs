using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TurretLib;

public class TurretBlockRenderer(ICoreClientAPI capi, BlockEntityTurret be) : IRenderer
{
    public double RenderOrder => 0.5;
    public int RenderRange => 32;

    public const float MODEL_YAW_OFFSET = GameMath.PIHALF;
    public const float SENSITIVITY_FACTOR = 0.35f;

    private readonly ICoreClientAPI capi = capi;
    private readonly BlockEntityTurret blockEntity = be;

    private float syncAccumulator = 0f;
    private float lastCameraYaw = 0f;
    private float lastCameraPitch = 0f;
    private float lastSentYaw = -999f;
    private float lastSentPitch = -999f;
    private bool wasControlledLastFrame = false;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (blockEntity == null) return;

        if (stage == EnumRenderStage.Before)
        {
            RenderStage1(deltaTime);
        }
    }

    private void RenderStage1(float deltaTime)
    {
        var localPlayer = capi.World.Player;
        bool isOverhead = localPlayer?.CameraMode == EnumCameraMode.Overhead;

        if (blockEntity.IsLocallyControlled() && localPlayer?.Entity != null && !isOverhead)
        {
            if (!wasControlledLastFrame)
            {
                localPlayer.CameraYaw = blockEntity.ClientVisualYaw;
                localPlayer.CameraPitch = GameMath.PI + blockEntity.ClientVisualPitch;
                lastCameraYaw = localPlayer.CameraYaw;
                lastCameraPitch = localPlayer.CameraPitch;
                wasControlledLastFrame = true;
            }

            float rawYawDelta = GameMath.AngleRadDistance(lastCameraYaw, localPlayer.CameraYaw);
            float rawPitchDelta = localPlayer.CameraPitch - lastCameraPitch;

            float scaledYawDelta = rawYawDelta * SENSITIVITY_FACTOR;
            float scaledPitchDelta = rawPitchDelta * SENSITIVITY_FACTOR;

            if (MathF.Abs(scaledYawDelta) > 0.00001f || MathF.Abs(scaledPitchDelta) > 0.00001f)
            {
                BEPhysics.ApplyMouseInput(blockEntity, scaledYawDelta, scaledPitchDelta);
            }

            blockEntity.ClientVisualYaw = blockEntity.YawClamped();
            blockEntity.ClientVisualPitch = blockEntity.Pitch;

            float translatedPitch = GameMath.PI + blockEntity.ClientVisualPitch;
            localPlayer.CameraYaw = blockEntity.ClientVisualYaw;
            localPlayer.CameraPitch = translatedPitch;

            lastCameraYaw = localPlayer.CameraYaw;
            lastCameraPitch = localPlayer.CameraPitch;

            if (blockEntity.Controller is EntityPlayer passenger)
            {
                passenger.Pos.Pitch = translatedPitch;
                
                if (localPlayer.CameraMode != EnumCameraMode.FirstPerson)
                {
                    passenger.Pos.Yaw = blockEntity.ClientVisualYaw;
                    passenger.BodyYaw = blockEntity.ClientVisualYaw;
                }
            }

            // Throttled network synchronization (20 Hz)
            syncAccumulator += deltaTime;
            if (syncAccumulator >= 0.05f)
            {
                syncAccumulator = 0f;
                if (MathF.Abs(blockEntity.Yaw - lastSentYaw) > 0.001f || MathF.Abs(blockEntity.Pitch - lastSentPitch) > 0.001f)
                {
                    lastSentYaw = blockEntity.Yaw;
                    lastSentPitch = blockEntity.Pitch;
                    blockEntity.SendClientSync();
                }
            }
        }
        else
        {
            // Reset flag so re-entering 1st/3rd person seamlessly re-seeds camera angles
            wasControlledLastFrame = false;

            float yawDiff = GameMath.AngleRadDistance(blockEntity.ClientVisualYaw, blockEntity.Yaw);
            blockEntity.ClientVisualYaw += yawDiff * GameMath.Clamp(deltaTime * 15f, 0f, 1f);

            float pitchDiff = blockEntity.Pitch - blockEntity.ClientVisualPitch;
            blockEntity.ClientVisualPitch += pitchDiff * GameMath.Clamp(deltaTime * 15f, 0f, 1f);

            if (blockEntity.Controller is EntityPlayer passenger)
            {
                passenger.Pos.Yaw = blockEntity.ClientVisualYaw;
                passenger.BodyYaw = blockEntity.ClientVisualYaw;
                passenger.Pos.Pitch = GameMath.PI + blockEntity.ClientVisualPitch;
            }
        }

        blockEntity.Position.Yaw = blockEntity.ClientVisualYaw;
        blockEntity.Position.Pitch = 0f;

        blockEntity.UpdatePoseAngles();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}