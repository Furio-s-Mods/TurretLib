using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace TurretLib;

public abstract class BEBehaviorTurretConfigurable<TProps>(BlockEntity blockentity) : BlockEntityBehavior(blockentity) 
    where TProps : TurretPropertiesBase, new()
{
    public TProps? Properties { get; private set; } = null!;

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        string blockCode = Blockentity?.Block?.Code?.ToShortString() ?? "UnknownBlock";

        if (properties == null || (properties.Token != null && !properties.Token.HasValues))
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' requires properties defined for behavior '{GetType().Name}'."
            );
        }

        try
        {
            Properties = properties.AsObject<TProps>() ?? throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' deserialized to null properties for '{GetType().Name}'."
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' failed to deserialize properties for '{GetType().Name}'.", ex
            );
        }

        Properties.Validate(blockCode);
    }
}