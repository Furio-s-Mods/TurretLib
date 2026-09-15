using ProtoBuf;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace TurretLib;

[ProtoContract]
public class SwivelRotationPacket
{
    [ProtoMember(1)]
    public long EntityId;

    [ProtoMember(2)]
    public string SeatId = "";

    [ProtoMember(3)]
    public float Yaw;

    [ProtoMember(4)]
    public float Pitch;
}

public partial class MainModSystem
{
    private void OnServerSwivelRotation(IServerPlayer player, SwivelRotationPacket packet)
    {
        if (player?.Entity?.World == null || string.IsNullOrEmpty(packet.SeatId)) return;

        Entity vehicle = player.Entity.World.GetEntityById(packet.EntityId);
        if (vehicle == null) return;

        TurretVehicleState.SetRotation(vehicle, packet.SeatId, packet.Yaw, packet.Pitch);
    }
}