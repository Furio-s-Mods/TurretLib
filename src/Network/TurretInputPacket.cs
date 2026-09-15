using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

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

[ProtoContract]
public class EntityTurretInputPacket
{
    [ProtoMember(1)] public long EntityId { get; set; }
    [ProtoMember(2)] public string SeatId { get; set; } = "";
    [ProtoMember(3)] public TurretInputAction Action { get; set; }
}

public partial class MainModSystem
{
    
    #region Block Entity Network Handlers

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

    #endregion

    #region Entity Attachment Network Handlers

    private void OnServerEntityInputPacket(IServerPlayer player, EntityTurretInputPacket packet)
    {
        if (_sapi == null) return;

        Entity vehicle = _sapi.World.GetEntityById(packet.EntityId);
        if (vehicle == null) return;

        if ((player.Entity.MountedOn as SwivelSeat)?.Entity != vehicle) return;

        HandleEntityTurretInput(vehicle, packet.SeatId, packet.Action, player);
    }

    private void HandleEntityTurretInput(Entity vehicle, string seatId, TurretInputAction action, IServerPlayer player)
    {
        if (_sapi == null) return;

        TurretWeaponState currentState = TurretVehicleState.GetState(vehicle, seatId);
        string currentAmmo = TurretVehicleState.GetAmmo(vehicle, seatId);

        switch (action)
        {
            case TurretInputAction.StartLoad when currentState == TurretWeaponState.Idle && !string.IsNullOrEmpty(currentAmmo):
                TurretVehicleState.SetState(vehicle, seatId, TurretWeaponState.Loading);
                break;

            case TurretInputAction.CancelLoad when currentState == TurretWeaponState.Loading:
                TurretVehicleState.SetState(vehicle, seatId, TurretWeaponState.Idle);
                break;

            case TurretInputAction.CompleteLoad when currentState == TurretWeaponState.Loading:
                TurretVehicleState.SetState(vehicle, seatId, TurretWeaponState.Loaded);
                break;

            case TurretInputAction.Fire when currentState == TurretWeaponState.Loaded:
                var seatable = vehicle.GetBehavior<EntityBehaviorSeatable>();
                SwivelSeat? targetSeat = seatable?.Seats.OfType<SwivelSeat>().FirstOrDefault(s => s.Config?.SeatId == seatId);

                if (targetSeat != null)
                {
                    EntityFiringHelper.ServerExecuteEntityFire(_sapi, vehicle, targetSeat, player);
                }
                else
                {
                    _sapi.Logger.Error($"[{ModId}] Firing failed: SwivelSeat with ID '{seatId}' was not found on vehicle entity {vehicle.EntityId}.");
                }
                break;

            case TurretInputAction.Reload:
                HandleAmmoInventoryToggle(vehicle, seatId, player);
                break;
        }
    }

    private void HandleAmmoInventoryToggle(Entity vehicle, string seatId, IServerPlayer player)
    {
        if (_sapi == null) return;

        var seatable = vehicle.GetBehavior<EntityBehaviorSeatable>();
        if (seatable == null) return;

        SwivelSeat? targetSeat = null;
        foreach (var seat in seatable.Seats)
        {
            if (seat is SwivelSeat swivel && swivel.Config?.SeatId == seatId)
            {
                targetSeat = swivel;
                break;
            }
        }

        if (targetSeat?.ItemSlot?.Itemstack == null) return;

        ItemStack turretStack = targetSeat.ItemSlot.Itemstack;
        TurretInventoryProperties? invProps = TurretInventoryProperties.FromStack(turretStack);
        string ammoDomain = invProps?.AmmoDomain ?? "game";
        string? ammoCodePrefix = invProps?.AmmoCodePrefix;

        string currentAmmo = TurretVehicleState.GetAmmo(vehicle, seatId);

        // UNLOAD
        if (!string.IsNullOrEmpty(currentAmmo))
        {
            TurretVehicleState.SetAmmo(vehicle, seatId, "");
            TurretVehicleState.SetState(vehicle, seatId, TurretWeaponState.Idle);


            AssetLocation loc = new AssetLocation(currentAmmo);
            CollectibleObject? ammo;
            ammo = _sapi.World.GetItem(loc);
            ammo ??=  _sapi.World.GetBlock(loc);

            if (ammo != null)
            {
                ItemStack giveStack = new ItemStack(ammo, 1);
                if (!player.InventoryManager.TryGiveItemstack(giveStack, true))
                {
                    _sapi.World.SpawnItemEntity(giveStack, vehicle.Pos.XYZ);
                }
            }
        }
        // LOAD
        else
        {
            ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
            ItemStack? heldStack = activeSlot?.Itemstack;

            if (heldStack == null)
            {
                // player.SendIngameError("noammoheld", "Hold ammo in your active hotbar slot to load.");
                return;
            }

            string heldCode = heldStack.Collectible.Code.ToShortString();

            bool isValidAmmo = TurretAmmoHelper.IsValidAmmo(heldStack, ammoDomain, ammoCodePrefix);

            if (isValidAmmo)
            {
                activeSlot!.TakeOut(1);
                activeSlot.MarkDirty();

                TurretVehicleState.SetAmmo(vehicle, seatId, heldCode);
            }
        }
    }

    #endregion
    
    #region Helper Methods

    public void SendEntityTurretInput(long entityId, string seatId, TurretInputAction action)
    {
        ClientChannel?.SendPacket(new EntityTurretInputPacket
        {
            EntityId = entityId,
            SeatId = seatId,
            Action = action
        });
    }

    private bool OnReloadHotKeyPressed(KeyCombination keyCombo)
    {
        if (_capi == null) return false;

        IMountableSeat? mounted = _capi.World.Player.Entity.MountedOn;
        if (mounted == null) return false;

        if (mounted is TurretSeat seat && seat.MountSupplier is BlockEntityTurret turret)
        {
            if (turret.WeaponBehavior != null)
            {
                ClientChannel?.SendPacket(new TurretInputPacket
                {
                    PackedPos = BESyncMessage.PackPos(turret.WeaponBehavior.Blockentity.Pos),
                    Action = TurretInputAction.Reload
                });
                return true;
            }
        }
        else if (mounted is SwivelSeat swivelSeat)
        {
            SendEntityTurretInput(
                swivelSeat.Entity.EntityId, 
                swivelSeat.Config?.SeatId ?? SwivelSeat.defaultName, 
                TurretInputAction.Reload
            );
            return true;
        }

        return false;
    }

    #endregion
}