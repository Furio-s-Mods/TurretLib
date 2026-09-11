using Vintagestory.API.MathTools;

namespace TurretLib;

public class TurretInventoryProperties : TurretPropertiesBase
{
    public int SlotCount { get; set; } = 1;
    public string AmmoDomain { get; set; } = null!;
    public string AmmoCodePrefix { get; set; } = null!;
    public Cuboidf InteractionCuboid { get; set; } = new Cuboidf(0f, 0f, 0f, 1f, 1f, 1f);
    public Cuboidf AmmoBoxCuboid { get; set; } = new Cuboidf(0f, 1f, 0f, 1f, 1.1f, 1f);

    public override void Validate(string blockCode)
    {
        if (SlotCount <= 0)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' must specify a 'SlotCount' greater than 0 in TurretInventory."
            );
        }

        RequireNotNullOrEmpty(AmmoDomain, nameof(AmmoDomain), blockCode);
        RequireNotNullOrEmpty(AmmoCodePrefix, nameof(AmmoCodePrefix), blockCode);
        RequireNotNull(InteractionCuboid, nameof(InteractionCuboid), blockCode);
        RequireNotNull(AmmoBoxCuboid, nameof(AmmoBoxCuboid), blockCode);
    }
}