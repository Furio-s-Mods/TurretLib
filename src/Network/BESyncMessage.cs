using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TurretLib;

[ProtoContract]
public class BESyncMessage
{
    [ProtoMember(1)] public long PackedPos { get; set; }
    [ProtoMember(2)] public uint PackedAngles { get; set; }

    public BlockPos Pos
    {
        get => UnpackPos(PackedPos);
        set => PackedPos = PackPos(value);
    }

    public static long PackPos(BlockPos pos)
    {
        return ((long)(pos.X & 0x3FFFFFF) << 38) |
               ((long)(pos.Y & 0xFFF) << 26) |
               ((long)(pos.Z & 0x3FFFFFF));
    }

    public static BlockPos UnpackPos(long packed)
    {
        int x = (int)((packed >> 38) & 0x3FFFFFF);
        int y = (int)((packed >> 26) & 0xFFF);
        int z = (int)(packed & 0x3FFFFFF);

        if ((x & 0x2000000) != 0) x |= ~0x3FFFFFF;
        if ((z & 0x2000000) != 0) z |= ~0x3FFFFFF;

        return new BlockPos(x, y, z);
    }

    public float Yaw
    {
        get => ((PackedAngles >> 16) / 65535f) * MathF.Tau;
        set
        {
            float norm = (value % MathF.Tau + MathF.Tau) % MathF.Tau;
            ushort yaw16 = (ushort)(norm / MathF.Tau * 65535f);
            PackedAngles = (PackedAngles & 0x0000FFFF) | ((uint)yaw16 << 16);
        }
    }

    public float Pitch
    {
        get => (((PackedAngles & 0xFFFF) / 65535f) - 0.5f) * MathF.PI;
        set
        {
            float clamped = Math.Clamp(value, -MathF.PI / 2f, MathF.PI / 2f);
            ushort pitch16 = (ushort)(((clamped / MathF.PI) + 0.5f) * 65535f);
            PackedAngles = (PackedAngles & 0xFFFF0000) | pitch16;
        }
    }
}

class SyncHelper {    
    public static void HandleSyncRequest(BESyncMessage msg, ICoreAPI api, IServerPlayer? sender)
    {
        if (!ValidateSyncMessage(msg, api, sender, out var be)) return;

        OnSyncMessageReceived(msg, be!);
    }

    /// <summary>
    /// Performs structural, numeric, and server security validation on incoming sync messages.
    /// </summary>
    private static bool ValidateSyncMessage(BESyncMessage? msg, ICoreAPI api, IServerPlayer? sender, out BlockEntityTurret? turret)
    {
        turret = null;

        if (msg == null || msg.Pos == null) return false;

        // Float Sanity (Prevent NaN / Infinity crash vectors)
        if (float.IsNaN(msg.Yaw) || float.IsInfinity(msg.Yaw)) return false;
        if (float.IsNaN(msg.Pitch) || float.IsInfinity(msg.Pitch)) return false;

        if (api.World.BlockAccessor.GetBlockEntity(msg.Pos) is not BlockEntityTurret be)
        {
            return false;
        }

        if (api.Side == EnumAppSide.Server)
        {
            if (sender?.Entity == null) return false;
            if (be.Controller == null) return false;

            if (be.Controller.EntityId != sender.Entity.EntityId) return false;

            // Distance Check: Reject if player is > 10 blocks away (100.0 sq distance)
            double distSq = sender.Entity.Pos.SquareDistanceTo(msg.Pos.X + 0.5, msg.Pos.Y, msg.Pos.Z + 0.5);
            if (distSq > 100.0) return false;
        }

        turret = be;
        return true;
    }

    /// <summary>
    /// Executes the actual state sync onto a validated BlockEntityTurret instance.
    /// </summary>
    private static void OnSyncMessageReceived(BESyncMessage msg, BlockEntityTurret be)
    {
        be.ReceiveNetworkSync(msg);
    }
}