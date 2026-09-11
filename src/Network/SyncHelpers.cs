using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TurretLib;

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