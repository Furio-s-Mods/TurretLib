using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TurretLib;

public static class IMountableHelpers
{
    
    /// <summary>
    /// Evaluates whether placing the entity at (x, y, z) would collide with solid world block boxes.
    /// </summary>
    private static bool IsColliding(BlockEntityTurret parentBE, EntityAgent entity, double x, double y, double z)
    {
        if (parentBE.Api?.World == null) return false;

        var blockAccessor = parentBE.Api.World.BlockAccessor;

        // Use player entity collision box (or standard player size fallback: 0.6w x 1.85h)
        Cuboidf coll = entity.CollisionBox ?? new Cuboidf(-0.3f, 0f, -0.3f, 0.3f, 1.85f, 0.3f);
        
        // Calculate world Axis-Aligned Bounding Box (AABB) for entity at (x, y, z)
        double eX1 = x + coll.X1;
        double eY1 = y + coll.Y1;
        double eZ1 = z + coll.Z1;
        double eX2 = x + coll.X2;
        double eY2 = y + coll.Y2;
        double eZ2 = z + coll.Z2;

        int minX = (int)Math.Floor(eX1);
        int maxX = (int)Math.Floor(eX2);
        int minY = (int)Math.Floor(eY1);
        int maxY = (int)Math.Floor(eY2);
        int minZ = (int)Math.Floor(eZ1);
        int maxZ = (int)Math.Floor(eZ2);

        BlockPos checkPos = parentBE.Pos.Copy();

        for (int bx = minX; bx <= maxX; bx++)
        {
            for (int by = minY; by <= maxY; by++)
            {
                for (int bz = minZ; bz <= maxZ; bz++)
                {
                    checkPos.Set(bx, by, bz);
                    Block block = blockAccessor.GetBlock(checkPos);
                    if (block == null || block.BlockId == 0) continue;

                    Cuboidf[]? blockBoxes = block.GetCollisionBoxes(blockAccessor, checkPos);
                    if (blockBoxes == null || blockBoxes.Length == 0) continue;

                    foreach (var bBox in blockBoxes)
                    {
                        double bX1 = bx + bBox.X1;
                        double bY1 = by + bBox.Y1;
                        double bZ1 = bz + bBox.Z1;
                        double bX2 = bx + bBox.X2;
                        double bY2 = by + bBox.Y2;
                        double bZ2 = bz + bBox.Z2;

                        // 3D Box overlap test
                        if (eX1 < bX2 && eX2 > bX1 &&
                            eY1 < bY2 && eY2 > bY1 &&
                            eZ1 < bZ2 && eZ2 > bZ1)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Sweeps from full eyepiece offset inward toward the block center to find a collision-free exit spot.
    /// </summary>
    public static Vec3d FindSafeUnmountPosition(BlockEntityTurret parentBE, EntityAgent entity)
    {
        float yaw = parentBE.Api?.Side == EnumAppSide.Client 
            ? parentBE.ClientVisualYaw 
            : parentBE.Yaw;

        double centerX = parentBE.Position.X;
        double centerY = parentBE.Position.Y;
        double centerZ = parentBE.Position.Z;

        // Test 8 incremental points along the radius from full distance (0.75) down to center (0.0)
        int steps = 8;
        for (int i = 0; i <= steps; i++)
        {
            double dist = parentBE.Seat.Properties.SeatDistance * (1.0 - (double)i / steps);
            double candX = centerX - Math.Sin(yaw) * dist;
            double candZ = centerZ - Math.Cos(yaw) * dist;

            if (!IsColliding(parentBE, entity, candX, centerY, candZ))
            {
                return new Vec3d(candX, centerY, candZ);
            }
        }

        // Ultimate fallback: place player standing on top of the block
        return new Vec3d(parentBE.Pos.X + 0.5, parentBE.Pos.Y + 1.0, parentBE.Pos.Z + 0.5);
    }
}