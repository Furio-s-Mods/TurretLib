using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TurretLib;

public class EntityAttachmentProperties : TurretPropertiesBase
{
    public AssetLocation ShapePath { get; set; } = null!;
    public string YawBoneName { get; set; } = null!;
    public string PitchBoneName { get; set; } = null!;

    public Vec3f BaseOffset { get; set; } = new();
    public Vec3f BaseRotation { get; set; } = new();

    public bool InvertYaw { get; set; }
    public bool InvertPitch { get; set; }

    public override void Validate(string code)
    {
        RequireNotNull(ShapePath, nameof(ShapePath), code);
        RequireNotNullOrEmpty(YawBoneName, nameof(YawBoneName), code);
        RequireNotNullOrEmpty(PitchBoneName, nameof(PitchBoneName), code);
        RequireNotNull(BaseOffset, nameof(BaseOffset), code);
        RequireNotNull(BaseRotation, nameof(BaseRotation), code);
    }

    public static EntityAttachmentProperties? FromStack(ItemStack? stack)
    {
        if (stack?.Collectible?.Attributes?.KeyExists("turretProps") != true)
        {
            return null;
        }

        string itemCode = stack.Collectible.Code?.ToShortString() ?? "UnknownItem";
        EntityAttachmentProperties? props = stack.Collectible.Attributes["turretProps"].AsObject<EntityAttachmentProperties>();
        
        props?.Validate(itemCode);
        return props;
    }
}