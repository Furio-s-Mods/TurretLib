using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TurretLib;

public abstract class TurretPropertiesBase
{
    public abstract void Validate(string blockCode);

    protected static void RequireNotNull<T>(T? value, string propertyName, string blockCode) where T : class
    {
        if (value == null)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' is missing required property '{propertyName}' in behavior settings."
            );
        }
    }

    protected static void RequireNotNullOrEmpty(string? value, string propertyName, string blockCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' is missing required string property '{propertyName}' in behavior settings."
            );
        }
    }

    protected static void RequireNotNull(Vec3d? vec, string propertyName, string blockCode)
    {
        if (vec == null)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' is missing required 3D vector property '{propertyName}' in behavior settings."
            );
        }
    }

    protected static void RequireNotNull(AssetLocation? asset, string propertyName, string blockCode)
    {
        if (asset == null || !asset.Valid)
        {
            throw new InvalidOperationException(
                $"[{MainModSystem.ModId}] Block '{blockCode}' specifies an invalid or missing AssetLocation for '{propertyName}'."
            );
        }
    }
}