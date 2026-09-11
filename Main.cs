using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TurretLib;

public partial class MainModSystem : ModSystem
{
    private Harmony? _harmony;
    private ICoreClientAPI? _capi;
    private ICoreServerAPI? _sapi;

    public const string ModId = "turretlib";
    public const string PatchId = $"{ModId}.patches";
    public const string NetworkChannel = "turretSync";

    public IServerNetworkChannel? ServerChannel { get; private set; }
    public IClientNetworkChannel? ClientChannel { get; private set; }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        
        api.RegisterBlockClass($"{ModId}:BlockTurret", typeof(BlockTurret));
        api.RegisterBlockEntityClass($"{ModId}:BlockEntityTurret", typeof(BlockEntityTurret));

        // api.RegisterCollectibleBehaviorClass($"{ModId}:2x2Attachable", typeof(CollectibleBehavior2x2Attachable));
        api.RegisterCollectibleBehaviorClass($"{ModId}:ShiftDelayedPlace", typeof(CollectibleBehaviorShiftDelayedPlace));
        api.RegisterBlockBehaviorClass($"{ModId}:ShiftDelayedPickUp", typeof(BlockBehaviorShiftDelayedPickup));
        api.RegisterBlockEntityBehaviorClass($"{ModId}:TurretInventory", typeof(BEBehaviorTurretInventory));
        api.RegisterBlockBehaviorClass($"{ModId}:TurretMountable", typeof(BlockBehaviorTurretMountable));
        api.RegisterBlockEntityBehaviorClass($"{ModId}:TurretAim", typeof(BEBehaviorTurretAim));
        api.RegisterBlockEntityBehaviorClass($"{ModId}:TurretWeapon", typeof(BEBehaviorTurretWeapon));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        _harmony = new Harmony(PatchId);
        _harmony.PatchAll();

        ServerChannel = api.Network.RegisterChannel(NetworkChannel)
            .RegisterMessageType<BESyncMessage>()
            .RegisterMessageType<TurretInputPacket>()
            .RegisterMessageType<TurretStatePacket>()
            .SetMessageHandler<BESyncMessage>((player, message) => SyncHelper.HandleSyncRequest(message, api, player))
            .SetMessageHandler<TurretInputPacket>(OnServerInputPacket)
            // .RegisterMessageType<SwivelRotationPacket>()
            // .SetMessageHandler<SwivelRotationPacket>(OnServerSwivelRotation);
        ;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;

        ClientChannel = api.Network.RegisterChannel(NetworkChannel)
            .RegisterMessageType<BESyncMessage>()
            .RegisterMessageType<TurretInputPacket>()
            .RegisterMessageType<TurretStatePacket>()
            .SetMessageHandler<BESyncMessage>(message => SyncHelper.HandleSyncRequest(message, api, null))
            .SetMessageHandler<TurretStatePacket>(OnClientStatePacket)
            // .RegisterMessageType<SwivelRotationPacket>()
        ;

        api.Event.LevelFinalize += () =>
        {
            _ = new HudLoadProgress(api);
        };
        api.Input.RegisterHotKey($"{ModId}:TurretReload", "Reload Turret", GlKeys.R, HotkeyType.InventoryHotkeys);
        api.Input.SetHotKeyHandler($"{ModId}:TurretReload", (a) => BEBehaviorTurretWeapon.OnReloadHotKeyPressed(a, api));
    }

    private void OnServerInputPacket(IServerPlayer player, TurretInputPacket packet)
    {
        if (_sapi == null) return;

        var pos = BESyncMessage.UnpackPos(packet.PackedPos);
        var be = _sapi.World.BlockAccessor.GetBlockEntity(pos);

        if (be?.GetBehavior<BEBehaviorTurretWeapon>() is { } weapon)
        {
            weapon.HandleServerInput(packet.Action);
        }
    }

    private void OnClientStatePacket(TurretStatePacket packet)
    {
        if (_capi == null) return;

        var pos = BESyncMessage.UnpackPos(packet.PackedPos);
        var be = _capi.World.BlockAccessor.GetBlockEntity(pos);

        if (be?.GetBehavior<BEBehaviorTurretWeapon>() is { } weapon && be is BlockEntityTurret turret)
        {
            weapon.SetState(packet.State, turret);
        }
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(PatchId);
        _harmony = null;
        base.Dispose();
    }
}