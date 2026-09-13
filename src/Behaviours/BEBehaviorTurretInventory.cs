using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace TurretLib;

public class BEBehaviorTurretInventory(BlockEntity blockentity) : BEBehaviorTurretConfigurable<TurretInventoryProperties>(blockentity)
{
    public BETurretProjectileRenderer? ProjectileRenderer { get; private set; }
    public InventoryGeneric Inventory { get; private set; } = null!;

    public const int MainAmmoIndex = 0;
    public ItemSlot? MainAmmoSlot => Inventory?[MainAmmoIndex];
    public bool HasProjectile => MainAmmoSlot != null && !MainAmmoSlot.Empty;

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        string invKey = $"turretlib-inventory-{Blockentity.Pos}";
        if (Inventory == null)
        {
            Inventory = new InventoryGeneric(Properties?.SlotCount ?? 1, invKey, api);
        }
        else
        {
            Inventory.LateInitialize(invKey, api);
        }
        
        if (api is ICoreClientAPI capi)
        {
            ProjectileRenderer = new BETurretProjectileRenderer(capi, Blockentity.GetBehavior<BEBehaviorTurretWeapon>());
            capi.Event.RegisterRenderer(ProjectileRenderer, EnumRenderStage.Opaque);
            capi.Event.RegisterRenderer(ProjectileRenderer, EnumRenderStage.ShadowFar);
            capi.Event.RegisterRenderer(ProjectileRenderer, EnumRenderStage.ShadowNear);

            NotifyInventoryChanged(MainAmmoIndex);
            Inventory.SlotModified += NotifyInventoryChanged;
            ProjectileRenderer.UpdateMesh(Inventory[MainAmmoIndex].Itemstack);
        }
    }

    public void NotifyInventoryChanged(int slotId)
    {
        if (Api?.Side == EnumAppSide.Client)
        {
            ItemStack? ammoStack = Inventory?[slotId].Itemstack;
            // Api.Logger?.Notification($"[NotifyInventoryChanged] ammostack: {ammoStack}");
            ProjectileRenderer?.UpdateMesh(ammoStack);
        }
    }

    public Cuboidf[] GetCollisionBoxes(Cuboidf[] baseBoxes)
    {
        if (Properties == null) return [.. baseBoxes];
        return [.. baseBoxes, Properties.AmmoBoxCuboid];
    }

    public Cuboidf[] GetSelectionBoxes(Cuboidf[] baseBoxes)
    {
        if (Properties == null) return [.. baseBoxes];
        return [.. baseBoxes, Properties.AmmoBoxCuboid, Properties.InteractionCuboid];
    }

    public ItemStack? ConsumeAmmo(int count = 1)
    {
        ItemSlot? slot = MainAmmoSlot;
        if (slot == null) return null;

        ItemStack consumed = slot.TakeOut(count);
        slot.MarkDirty();
        Blockentity.MarkDirty(true);
        
        NotifyInventoryChanged(MainAmmoIndex);
        return consumed;
    }

    public bool IsValidAmmo(ItemStack? stack)
    {
        if (Properties == null) return false;
        if (stack?.Collectible?.Code == null)
        {
            Api?.Logger.Warning($"[{Api.Side}] [Ammo Check] Stack or Item Code is NULL");
            return false;
        }

        string domain = stack.Collectible.Code.Domain;
        string prefix = stack.Collectible.FirstCodePart();

        bool domainMatch = domain == Properties.AmmoDomain;
        bool prefixMatch = prefix == Properties.AmmoCodePrefix;

        // Api?.Logger.Notification($"[{Api.Side}] [Ammo Check] Held Item: {stack.Collectible.Code} | Domain: '{domain}' vs Required '{Properties.AmmoDomain}' (Match: {domainMatch}) | Prefix: '{prefix}' vs Required '{Properties.AmmoCodePrefix}' (Match: {prefixMatch})");

        return domainMatch && prefixMatch;
    }

    public bool OnAmmoBoxInteract(IPlayer player)
    {
        if (Api == null || player == null || Inventory == null) return false;

        if (!Api.World.Claims.TryAccess(player, Blockentity.Pos, EnumBlockAccessFlags.Use))
        {
            player.InventoryManager?.ActiveHotbarSlot?.MarkDirty();
            return false;
        }

        ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null) return false;

        if (HasProjectile)
        {
            if (Api.Side == EnumAppSide.Server)
            {
                ItemSlot? ammoSlot = MainAmmoSlot;
                if (ammoSlot != null && !ammoSlot.Empty && player.InventoryManager != null)
                {
                    ItemStack stackToGive = ammoSlot.TakeOut(1);
                    ammoSlot.MarkDirty();
                    Blockentity.MarkDirty(true);

                    if (!player.InventoryManager.TryGiveItemstack(stackToGive, true))
                    {
                        Api.World.SpawnItemEntity(stackToGive, Blockentity.Pos.ToVec3d().Add(0.5, 1.0, 0.5));
                    }

                    NotifyInventoryChanged(MainAmmoIndex);
                }
            }
            return true;
        }
        else
        {
            ItemStack? heldStack = activeSlot.Itemstack;
            if (IsValidAmmo(heldStack))
            {
                if (Api.Side == EnumAppSide.Server)
                {
                    Inventory[MainAmmoIndex].Itemstack = activeSlot.TakeOut(1);
                    Inventory[MainAmmoIndex].MarkDirty();
                    activeSlot.MarkDirty();
                    Blockentity.MarkDirty(true);

                    NotifyInventoryChanged(MainAmmoIndex);
                }
                return true;
            }
        }

        return false;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (Inventory != null)
        {
            ITreeAttribute invTree = new TreeAttribute();
            Inventory.ToTreeAttributes(invTree);
            tree["inventory"] = invTree;
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        if (Inventory == null && worldForResolving.Api != null)
        {
            int slotCount = Properties?.SlotCount ?? 1;
            Inventory = new InventoryGeneric(slotCount, $"turretlib-inventory-{Blockentity.Pos}", worldForResolving.Api);
        }

        base.FromTreeAttributes(tree, worldForResolving);

        if (Inventory != null && tree.HasAttribute("inventory"))
        {
            Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
            Inventory.ResolveBlocksOrItems();

            if (worldForResolving.Side == EnumAppSide.Client)
            {
                ProjectileRenderer?.UpdateMesh(Inventory[MainAmmoIndex].Itemstack);
            }
        }
    }

    public override void OnBlockUnloaded()
    { base.OnBlockUnloaded(); CleanUp(); }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        if (Api?.Side == EnumAppSide.Server && Inventory != null)
        {
            Inventory.DropAll(Blockentity.Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        CleanUp();
    }

    private void CleanUp()
    {
        Inventory?.SlotModified -= NotifyInventoryChanged;

        if (Api?.Side == EnumAppSide.Client && ProjectileRenderer != null)
        {
            var capi = (ICoreClientAPI)Api;
            capi.Event.UnregisterRenderer(ProjectileRenderer, EnumRenderStage.Opaque);
            capi.Event.UnregisterRenderer(ProjectileRenderer, EnumRenderStage.ShadowFar);
            capi.Event.UnregisterRenderer(ProjectileRenderer, EnumRenderStage.ShadowNear);
            ProjectileRenderer.Dispose();
            ProjectileRenderer = null;
        }
    }

    public bool IsInventorySelection(BlockSelection blockSel)
    {
        if (blockSel == null || Api?.World == null) return false;

        Cuboidf[]? boxes = Api.World.BlockAccessor.GetBlock(blockSel.Position)?.GetSelectionBoxes(Api.World.BlockAccessor, blockSel.Position);
        
        if (boxes == null)
        {
            Api.Logger.Notification($"[Turret Debug] GetSelectionBoxes returned null");
            return false;
        }

        if (blockSel.SelectionBoxIndex < 0 || blockSel.SelectionBoxIndex >= boxes.Length)
        {
            Api.Logger.Notification($"[Turret Debug] Index out of bounds: {blockSel.SelectionBoxIndex}, Total boxes: {boxes.Length}");
            return false;
        }

        bool indexIsAmmoBox = blockSel.SelectionBoxIndex > 0; // Ammo box is appended after base box (index 0)
        return indexIsAmmoBox;
    }
}