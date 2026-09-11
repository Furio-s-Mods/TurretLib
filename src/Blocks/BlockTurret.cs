using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TurretLib;

public class BlockTurret : Block
{
    private MultiTextureMeshRef? inventoryMeshRef;

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        if (inventoryMeshRef == null)
        {
            AssetLocation shapePath = Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            Shape baseShape = capi.Assets.Get<Shape>(shapePath);
            capi.Tesselator.TesselateShape(this, baseShape, out MeshData combinedMesh);
            inventoryMeshRef = capi.Render.UploadMultiTextureMesh(combinedMesh);
        }
        renderinfo.ModelRef = inventoryMeshRef;
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Block[] chunkExtBlocks, int extIndex3d)
    {
        sourceMesh.Clear();
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        if (api.Side == EnumAppSide.Client)
        {
            inventoryMeshRef?.Dispose();
        }
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        failureCode = "tryplaceholdtosneak";
        return false;
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        Cuboidf[] baseBoxes = base.GetCollisionBoxes(blockAccessor, pos) ?? [];
        if (blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorTurretInventory>() is { } inv)
        {
            return inv.GetCollisionBoxes(baseBoxes);
        }
        return baseBoxes;
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        Cuboidf[] baseBoxes = base.GetSelectionBoxes(blockAccessor, pos) ?? []; 
        if (blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorTurretInventory>() is { } inv)
        {
            return inv.GetSelectionBoxes(baseBoxes);
        }
        return baseBoxes;
    }
}