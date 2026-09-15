using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace TurretLib;

public class BEBehaviorTurretWeapon(BlockEntity blockentity) : BEBehaviorTurretConfigurable<TurretWeaponProperties>(blockentity)
{
    public TurretWeaponState State { get; private set; } = TurretWeaponState.Idle;
    public bool IsLoaded => State == TurretWeaponState.Loaded;
    public bool IsLoading => State == TurretWeaponState.Loading;

    private long _weaponTickListenerId;
    private bool _animNeedsSync = true;

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        _weaponTickListenerId = Blockentity.RegisterGameTickListener(OnWeaponTick, 20);
    }

    public void OnPullInput(bool isHeld)
    {
        if (Api?.Side != EnumAppSide.Client) return;
        if (Blockentity is not BlockEntityTurret turret || !turret.IsLocallyControlled()) return;

        var inv = turret.InventoryBehavior;
        if (inv == null || !inv.HasProjectile) return;

        if (isHeld && State == TurretWeaponState.Idle)
        {
            SendInputPacket(TurretInputAction.StartLoad);
        }
        else if (!isHeld && State == TurretWeaponState.Loading)
        {
            SendInputPacket(TurretInputAction.CancelLoad);
        }
    }

    public void OnFireInput()
    {
        if (Api?.Side != EnumAppSide.Client) return;
        if (Blockentity is not BlockEntityTurret turret || !turret.IsLocallyControlled()) return;

        if (State == TurretWeaponState.Loaded)
        {
            SendInputPacket(TurretInputAction.Fire);
        }
    }

    private void SendInputPacket(TurretInputAction action)
    {
        var system = Api?.ModLoader.GetModSystem<MainModSystem>();
        system?.ClientChannel?.SendPacket(new TurretInputPacket
        {
            PackedPos = BESyncMessage.PackPos(Blockentity.Pos),
            Action = action
        });
    }

    public void ServerSetState(TurretWeaponState newState, BlockEntityTurret turret)
    {
        State = newState;
        Blockentity.MarkDirty(true);

        var system = Api?.ModLoader.GetModSystem<MainModSystem>();
        var sapi = Api as ICoreServerAPI;

        IPlayer[]? players = sapi?.World.GetPlayersAround(Blockentity.Pos.ToVec3d(), 32, 32);
        IServerPlayer[]? serverPlayers = players != null 
            ? Array.ConvertAll(players, x => (IServerPlayer)x) 
            : null;

        system?.ServerChannel?.SendPacket(
            new TurretStatePacket 
            { 
                PackedPos = BESyncMessage.PackPos(Blockentity.Pos), 
                State = newState 
            },
            serverPlayers
        );
    }

    private void OnWeaponTick(float dt)
    {
        if (Api?.Side != EnumAppSide.Client) return;
        if (Blockentity is not BlockEntityTurret turret) return;
        if (Properties == null) return;

        if (_animNeedsSync && turret.AnimUtil?.animator != null)
        {
            _animNeedsSync = false;
            ApplyStateAnimation(turret, State);
        }

        // CONTINUOUS FRAME HOLD FOR LOADED STATE
        if (State == TurretWeaponState.Loaded && turret.AnimUtil?.animator != null)
        {
            var animState = turret.AnimUtil.animator.GetAnimationState(Properties.LoadAnimationCode);
            if (animState != null && animState.Animation != null)
            {
                if (!animState.Active)
                {
                    ApplyStateAnimation(turret, State);
                }
                // Continuously force the frame to the end every tick so animator.OnFrame() cannot reset it
                animState.CurrentFrame = animState.Animation.QuantityFrames - 1;
            }
        }

        if (State != TurretWeaponState.Loading || !turret.IsLocallyControlled()) return;

        var loadingAnimState = turret.AnimUtil?.animator?.GetAnimationState(Properties.LoadAnimationCode);
        if (loadingAnimState != null && loadingAnimState.AnimProgress >= 0.99f)
        {
            SendInputPacket(TurretInputAction.CompleteLoad);
        }
    }

    public void ApplyStateAnimation(BlockEntityTurret turret, TurretWeaponState state)
    {
        var animUtil = turret.AnimUtil;
        if (animUtil?.animator == null || Properties == null) return;

        switch (state)
        {
            case TurretWeaponState.Idle:
                animUtil.StopAnimation(Properties.LoadAnimationCode);
                break;

            case TurretWeaponState.Loading:
                animUtil.StartAnimation(new AnimationMetaData
                {
                    Animation = Properties.LoadAnimationCode,
                    Code = Properties.LoadAnimationCode,
                    AnimationSpeed = Properties.LoadSpeed,
                    EaseInSpeed = 10f,
                    EaseOutSpeed = 10f
                });
                break;

            case TurretWeaponState.Loaded:
                Api?.World.PlaySoundAt(
                    Properties.LoadSound, 
                    Blockentity.Pos.X + 0.5, Blockentity.Pos.Y + 0.5, Blockentity.Pos.Z + 0.5, 
                    null, false, 20f, 1f
                );

                // Use normal speed so Active/Running remain true; OnWeaponTick clamps the frame
                animUtil.StartAnimation(new AnimationMetaData
                {
                    Animation = Properties.LoadAnimationCode,
                    Code = Properties.LoadAnimationCode,
                    AnimationSpeed = 1f,
                    EaseInSpeed = 9999f,
                    EaseOutSpeed = 10f
                });
                break;

            case TurretWeaponState.Firing:
                animUtil.StopAnimation(Properties.LoadAnimationCode);
                break;
        }
    }

    public static bool OnReloadHotKeyPressed(KeyCombination comb, ICoreClientAPI capi)
    {
        IClientPlayer player = capi.World.Player;

        if (player.Entity.MountedOn is TurretSeat seat && seat.MountSupplier is BlockEntityTurret turret)
        {
            if (turret.WeaponBehavior != null)
            {
                turret.WeaponBehavior.SendInputPacket(TurretInputAction.Reload);
                return true;
            }
        }

        return false;
    }

    public void SetState(TurretWeaponState newState, BlockEntityTurret turret)
    {
        if (State == newState) return;

        State = newState;
        ApplyStateAnimation(turret, newState);
    }

    public void HandleServerInput(TurretInputAction action)
    {
        if (Api?.Side != EnumAppSide.Server) return;
        if (Blockentity is not BlockEntityTurret turret) return;

        switch (action)
        {
            case TurretInputAction.StartLoad when State == TurretWeaponState.Idle:
                ServerSetState(TurretWeaponState.Loading, turret);
                break;

            case TurretInputAction.CancelLoad when State == TurretWeaponState.Loading:
                ServerSetState(TurretWeaponState.Idle, turret);
                break;

            case TurretInputAction.CompleteLoad when State == TurretWeaponState.Loading:
                ServerSetState(TurretWeaponState.Loaded, turret);
                break;

            case TurretInputAction.Fire when State == TurretWeaponState.Loaded:
                this.ServerExecuteFire(turret);
                break;

            case TurretInputAction.Reload when State == TurretWeaponState.Idle || State == TurretWeaponState.Loaded:
                if (turret.Controller is EntityPlayer playerEntity)
                {
                    turret.InventoryBehavior?.OnAmmoBoxInteract(playerEntity.Player);
                }
                break;
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("weaponState", (int)State);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        State = (TurretWeaponState)tree.GetInt("weaponState", 0);
        _animNeedsSync = true;
    }

    public override void OnBlockUnloaded()
    { base.OnBlockUnloaded(); CleanUp(); }

    public override void OnBlockRemoved()
    { base.OnBlockRemoved(); CleanUp(); }

    private void CleanUp()
    {
        if (_weaponTickListenerId != 0)
        {
            Blockentity.UnregisterGameTickListener(_weaponTickListenerId);
            _weaponTickListenerId = 0;
        }
    }
}