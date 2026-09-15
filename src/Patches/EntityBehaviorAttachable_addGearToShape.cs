using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace TurretLib;

/*
```text
1. addGearToShape() executes
   └── Merges item elements into entity elements
2. Harmony Postfix executes
   └── Modify ShapeElement objects inside __result
3. Engine calls Tesselator.TesselateShape(__result)
   └── Reads From, To, and Faces to build GPU MeshData
4. Mesh uploaded to GPU and rendered
```
*/

[HarmonyPatch(typeof(EntityBehaviorAttachable), "addGearToShape")]
public class Patch_EntityBehaviorAttachable_addGearToShape
{
    [HarmonyPrefix]
    public static bool Prefix(
        EntityBehaviorAttachable __instance, 
        Shape entityShape, 
        ref Shape __result, 
        ItemSlot gearslot
    )
    {
        if (gearslot?.Itemstack?.Collectible?.Attributes == null) return true;

        // Check if the item has rotatable set to true
        bool isRotatable = gearslot.Itemstack.Collectible.Attributes["attachableToEntity"]?["rotatable"].AsBool(false) ?? false;

        if (isRotatable)
        {
            __result = entityShape;
            return false; 
        }

        return true; 
    }

    [HarmonyPostfix]
    public static void Postfix(
        EntityBehaviorAttachable __instance,
        Shape __result,
        ItemSlot gearslot)
    {
        // 1. Guard against null behavior, entity, or API
        if (__instance?.entity?.Api == null) return;
        ILogger logger = __instance.entity.Api.Logger;

        // Only run on the client side
        if (__instance.entity.Api is not ICoreClientAPI capi) return;

        bool isRotatable = gearslot.Itemstack?.Collectible.Attributes["attachableToEntity"]?["rotatable"].AsBool(false) ?? false;
        if (isRotatable) return;
        try
        {
            // 2. Validate input parameters and slot content
            if (__result == null)
            {
                logger.Warning("[{MainModSystem.ModId}] Postfix received a null __result Shape for entity '{0}'. Skipping transform.", __instance.entity.Code);
                return;
            }

            if (__result.Elements == null)
            {
                logger.Warning("[{MainModSystem.ModId}] __result Shape has null Elements for entity '{0}'. Skipping transform.", __instance.entity.Code);
                return;
            }

            if (gearslot == null)
            {
                logger.Warning("[{MainModSystem.ModId}] Postfix received a null gearslot for entity '{0}'. Skipping transform.", __instance.entity.Code);
                return;
            }

            ItemStack? stack = gearslot.Itemstack;
            if (stack == null)
            {
                // Empty slot is expected during normal gameplay
                return;
            }

            if (!gearslot.IsSlot2x2())
            {
                // Not a 2x2 custom slot, ignore silently
                return;
            }

            // 3. Locate inventory slot index
            if (__instance.Inventory == null)
            {
                logger.Warning("[{MainModSystem.ModId}] EntityBehaviorAttachable inventory is null on entity '{0}'.", __instance.entity.Code);
                return;
            }

            var inv = __instance.Inventory.AsEnumerable();
            if (inv == null)
            {
                logger.Warning("[{MainModSystem.ModId}] Failed to enumerate inventory on entity '{0}'.", __instance.entity.Code);
                return;
            }

            int slotIndex = inv.IndexOf((e) => e == gearslot);
            if (slotIndex < 0)
            {
                logger.Warning("[{MainModSystem.ModId}] Gearslot for item '{0}' was not found in EntityBehaviorAttachable inventory for entity '{1}'.", stack.GetName(), __instance.entity.Code);
                return;
            }

            // 4. Read wearableSlots configuration array via reflection
            object? wearableSlotsObj = Traverse.Create(__instance).Field("wearableSlots").GetValue();
            if (wearableSlotsObj is not Array wearableSlots)
            {
                logger.Warning("[{MainModSystem.ModId}] Field 'wearableSlots' could not be retrieved or is not an Array on entity '{0}'.", __instance.entity.Code);
                return;
            }

            if (slotIndex >= wearableSlots.Length)
            {
                logger.Warning("[{MainModSystem.ModId}] Slot index {0} is out of bounds for wearableSlots (Length: {1}) on entity '{2}'.", slotIndex, wearableSlots.Length, __instance.entity.Code);
                return;
            }

            object? slotConfig = wearableSlots.GetValue(slotIndex);
            if (slotConfig == null)
            {
                logger.Warning("[{MainModSystem.ModId}] SlotConfig at index {0} is null on entity '{1}'.", slotIndex, __instance.entity.Code);
                return;
            }

            // 5. Extract AttachmentPointCode
            string? apCode = Traverse.Create(slotConfig).Field("AttachmentPointCode").GetValue<string>()
                        ?? Traverse.Create(slotConfig).Property("AttachmentPointCode").GetValue<string>();

            if (string.IsNullOrEmpty(apCode))
            {
                logger.Warning("[{MainModSystem.ModId}] AttachmentPointCode for slot index {0} on entity '{1}' is null or empty.", slotIndex, __instance.entity.Code);
                return;
            }

            // 6. Find the ShapeElement that owns this AttachmentPointCode
            ShapeElement? apElement = FindElementByAttachmentPointCode(__result.Elements, apCode);

            // Fallback: Check entity's loaded base shape if __result stripped attachment point metadata
            if (apElement == null)
            {
                Shape? entityShape = __instance.entity.Properties?.Client?.LoadedShape;
                if (entityShape?.Elements != null)
                {
                    ShapeElement? origElement = FindElementByAttachmentPointCode(entityShape.Elements, apCode);
                    if (origElement?.Name != null)
                    {
                        apElement = FindElementByName(__result.Elements, origElement.Name);
                    }
                }
            }

            if (apElement == null)
            {
                logger.Warning("[{MainModSystem.ModId}] Could not find AttachmentPointCode '{0}' in shape or loaded base shape for entity '{1}'.", apCode, __instance.entity.Code);
                return;
            }

            if (apElement.Children == null)
            {
                logger.Warning("[{MainModSystem.ModId}] Attachment point element '{0}' has null Children array on entity '{1}'.", apElement.Name ?? apCode, __instance.entity.Code);
                return;
            }

            // 7. Resolve gear item shape base (works safely for both Item and Block collectibles)
            AssetLocation? shapeBase;
            CollectibleObject? collectible = stack.Collectible;
            if (stack.Class == EnumItemClass.Block)
            {
                shapeBase = stack.Block.Shape.Base;
            } else
            {
                shapeBase = stack.Item.Shape.Base;
            }
            if (shapeBase == null)
            {
                logger.Warning("[{MainModSystem.ModId}] Collectible '{0}' has no defined Shape.Base path.", collectible.Code);
                return;
            }

            AssetLocation shapeLoc = shapeBase.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");

            IAsset? asset = capi.Assets.TryGet(shapeLoc);
            if (asset == null)
            {
                logger.Warning("[{MainModSystem.ModId}] Asset file not found at path '{0}' for collectible '{1}'.", shapeLoc, collectible.Code);
                return;
            }

            Shape? baseShape = asset.ToObject<Shape>();
            if (baseShape?.Elements == null || baseShape.Elements.Length == 0)
            {
                logger.Warning("[{MainModSystem.ModId}] Loaded base shape at '{0}' is null or contains no elements.", shapeLoc);
                return;
            }

            string? targetElementName = baseShape.Elements[0].Name;
            if (string.IsNullOrEmpty(targetElementName))
            {
                logger.Warning("[{MainModSystem.ModId}] Root element of shape '{0}' has a null or empty name.", shapeLoc);
                return;
            }

            // 8. Search for the root element inside apElement.Children and transform
            ShapeElement? seatRoot = FindElementInChildren(apElement.Children, targetElementName);
            if (seatRoot == null)
            {
                logger.Warning("[{MainModSystem.ModId}] Target root element '{0}' not found under attachment point '{1}' for entity '{2}'.", targetElementName, apElement.Name ?? apCode, __instance.entity.Code);
                return;
            }

            TransformSubtree(seatRoot, scale: 2.0f, translateX: 0, translateY: 0.0, translateZ: 1);
        }
        catch (Exception ex)
        {
            // Catches any unexpected internal engine exceptions to prevent tessellation crashes and invisible entities
            logger.Error("[{MainModSystem.ModId}] Unhandled exception in addGearToShape Postfix for entity '{0}':\n{1}", __instance?.entity?.Code, ex);
        }
    }

    private static void TransformSubtree(ShapeElement element, float scale, double translateX, double translateY, double translateZ)
    {
        if (element == null) return;

        if (element.From != null && element.To != null && element.From.Length >= 3 && element.To.Length >= 3)
        {
            element.From[0] = (element.From[0] * scale) + translateX;
            element.From[1] = (element.From[1] * scale) + translateY;
            element.From[2] = (element.From[2] * scale) + translateZ;

            element.To[0] = (element.To[0] * scale) + translateX;
            element.To[1] = (element.To[1] * scale) + translateY;
            element.To[2] = (element.To[2] * scale) + translateZ;
        }

        // Cascade transform down to all child cubes
        if (element.Children != null)
        {
            foreach (var child in element.Children)
            {
                TransformSubtree(child, scale, translateX, translateY, translateZ);
            }
        }
    }

    private static ShapeElement? FindElementByAttachmentPointCode(ShapeElement[]? elements, string apCode)
    {
        if (elements == null || string.IsNullOrEmpty(apCode)) return null;

        foreach (var el in elements)
        {
            if (el == null) continue;

            if (el.AttachmentPoints != null)
            {
                foreach (var ap in el.AttachmentPoints)
                {
                    if (ap?.Code == apCode) return el;
                }
            }

            if (el.Children != null)
            {
                var match = FindElementByAttachmentPointCode(el.Children, apCode);
                if (match != null) return match;
            }
        }
        return null;
    }

    private static ShapeElement? FindElementByName(ShapeElement[]? elements, string name)
    {
        if (elements == null || string.IsNullOrEmpty(name)) return null;

        foreach (var el in elements)
        {
            if (el == null) continue;

            if (el.Name == name) return el;

            if (el.Children != null)
            {
                var match = FindElementByName(el.Children, name);
                if (match != null) return match;
            }
        }
        return null;
    }

    private static ShapeElement? FindElementInChildren(ShapeElement[]? children, string targetName)
    {
        if (children == null || string.IsNullOrEmpty(targetName)) return null;

        foreach (var el in children)
        {
            if (el == null) continue;

            if (el.Name != null && (el.Name == targetName || el.Name.EndsWith(targetName))) 
                return el;

            if (el.Children != null)
            {
                var childMatch = FindElementInChildren(el.Children, targetName);
                if (childMatch != null) return childMatch;
            }
        }
        return null;
    }
}