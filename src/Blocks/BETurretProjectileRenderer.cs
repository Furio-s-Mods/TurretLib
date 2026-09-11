using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TurretLib;

public class BETurretProjectileRenderer(ICoreClientAPI capi, BEBehaviorTurretWeapon weapon) : IRenderer, IDisposable
{
    private readonly ICoreClientAPI _capi = capi;
    private readonly BEBehaviorTurretWeapon _weapon = weapon;
    
    private MeshRef? _projectileMeshRef;
    private int _projectileTextureId;
    private readonly float[] _modelMat = Mat4f.Create();

    public double RenderOrder => 0.4;
    public int RenderRange => 32;

    /// <summary>
    /// Re-tessellates and uploads the projectile mesh when ammo changes.
    /// </summary>
    public void UpdateMesh(ItemStack? stack)
    {
        _projectileMeshRef?.Dispose();
        _projectileMeshRef = null;

        if (stack == null) return;

        MeshData mesh;
        if (stack.Class == EnumItemClass.Item)
        {
            _capi.Tesselator.TesselateItem(stack.Item, out mesh);
            _projectileTextureId = _capi.ItemTextureAtlas.AtlasTextures[0].TextureId;
        }
        else
        {
            _capi.Tesselator.TesselateBlock(stack.Block, out mesh);
            _projectileTextureId = _capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
        }

        if (mesh != null)
        {
            _projectileMeshRef = _capi.Render.UploadMesh(mesh);
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_projectileMeshRef == null || _weapon.Blockentity is not BlockEntityTurret turret) return;

        if (turret.InventoryBehavior == null) {
            // this._capi.Logger.Notification($"[ONRENDERFRAME 1] InventoryBehavior warning");
            return;
        }
        if (_weapon.Properties == null) {
            // this._capi.Logger.Notification($"[ONRENDERFRAME 2] attachment Properties warning");
            return;
        }
        if (!turret.InventoryBehavior.HasProjectile && !_weapon.IsLoaded && !_weapon.IsLoading) {
            // this._capi.Logger.Notification($"[ONRENDERFRAME 3] {!turret.InventoryBehavior.HasProjectile}, {!_weapon.IsLoaded}, {!_weapon.IsLoading}");
            return;
        }

        bool isShadowPass = stage == EnumRenderStage.ShadowNear || stage == EnumRenderStage.ShadowFar;
        IRenderAPI rpi = _capi.Render;

        if (turret.AnimUtil?.animator is not ClientAnimator animator) return;

        // Dynamic Attachment Point Resolution
        AttachmentPointAndPose? apap = null;
        string[]? attachmentPoints = _weapon.Properties.ProjectileAttachmentPoints;

        if (attachmentPoints != null)
        {
            foreach (string code in attachmentPoints)
            {
                if (animator.AttachmentPointByCode.TryGetValue(code, out apap))
                {
                    break;
                }
            }
        }

        if (apap == null) {
            _capi.Logger.Notification($"[{MainModSystem.ModId}][ONRENDERFRAME] attachment point warning");
            return;
        }

        Vec3d camPos = _capi.World.Player.Entity.CameraPos;
        BlockPos pos = _weapon.Blockentity.Pos;

        // Camera-relative base translation
        Mat4f.Identity(_modelMat);
        Mat4f.Translate(_modelMat, _modelMat, (float)(pos.X - camPos.X), (float)(pos.Y - camPos.Y), (float)(pos.Z - camPos.Z));

        // Apply Bone Transformation Matrix
        if (apap.AnimModelMatrix != null)
        {
            Mat4f.Mul(_modelMat, _modelMat, apap.AnimModelMatrix);
        }

        AttachmentPoint ap = apap.AttachPoint;
        ModelTransform transform = _weapon.Properties.ProjectileTransform;
        float modelYOffsetRad = _weapon.Properties.ProjectileModelYOffsetDeg * GameMath.DEG2RAD;

        // Apply Configured Model Transforms Relative to Attachment Point
        Mat4f.Translate(_modelMat, _modelMat, 
            (float)ap.PosX / 16f + transform.Translation.X, 
            (float)ap.PosY / 16f + transform.Translation.Y, 
            (float)ap.PosZ / 16f + transform.Translation.Z
        );

        Mat4f.Translate(_modelMat, _modelMat, transform.Origin.X, transform.Origin.Y, transform.Origin.Z);
        Mat4f.Scale(_modelMat, _modelMat, transform.ScaleXYZ.X, transform.ScaleXYZ.Y, transform.ScaleXYZ.Z);

        Mat4f.RotateX(_modelMat, _modelMat, (float)(ap.RotationX + transform.Rotation.X) * GameMath.DEG2RAD);
        Mat4f.RotateY(_modelMat, _modelMat, (float)(ap.RotationY + transform.Rotation.Y) * GameMath.DEG2RAD);
        Mat4f.RotateZ(_modelMat, _modelMat, (float)(ap.RotationZ + transform.Rotation.Z) * GameMath.DEG2RAD);

        // Mesh facing correction factor (must execute AFTER RotateZ and BEFORE -Origin)
        Mat4f.RotateY(_modelMat, _modelMat, modelYOffsetRad);

        Mat4f.Translate(_modelMat, _modelMat, -transform.Origin.X, -transform.Origin.Y, -transform.Origin.Z);


        // Draw Mesh
        IStandardShaderProgram? prog = null;
        if (isShadowPass)
        {
            rpi.CurrentActiveShader.BindTexture2D("tex2d", _projectileTextureId, 0);
            Mat4f.Mul(_modelMat, rpi.CurrentShadowProjectionMatrix, _modelMat);
            rpi.CurrentActiveShader.UniformMatrix("mvpMatrix", _modelMat);
            rpi.CurrentActiveShader.Uniform("origin", new Vec3f((float)(pos.X - camPos.X), (float)(pos.Y - camPos.Y), (float)(pos.Z - camPos.Z)));
        }
        else
        {
            prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            prog.ModelMatrix = _modelMat;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            prog.Tex2D = _projectileTextureId;
            prog.AlphaTest = 0.01f;
            rpi.BindTexture2d(_projectileTextureId);
        }

        rpi.GlDisableCullFace();
        rpi.RenderMesh(_projectileMeshRef);
        rpi.GlEnableCullFace();

        if (!isShadowPass)
        {
            prog?.Stop();
        }
    }

    public void Dispose()
    {
        _projectileMeshRef?.Dispose();
        _projectileMeshRef = null;

        GC.SuppressFinalize(this);
    }
}