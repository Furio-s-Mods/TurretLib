using ProtoBuf;

namespace TurretLib;

public enum TurretInputAction : byte
{
    StartLoad = 0,
    CancelLoad = 1,
    CompleteLoad = 2,
    Fire = 3,
    Reload = 4,
}

[ProtoContract]
public class TurretInputPacket
{
    [ProtoMember(1)] public long PackedPos { get; set; }
    [ProtoMember(2)] public TurretInputAction Action { get; set; }
}

[ProtoContract]
public class TurretStatePacket
{
    [ProtoMember(1)] public long PackedPos { get; set; }
    [ProtoMember(2)] public TurretWeaponState State { get; set; }
}