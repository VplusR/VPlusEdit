using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using ValheimPlus.Configurations;

namespace ValheimPlus.GameClasses
{
    [HarmonyPatch(typeof(Recipe), nameof(Recipe.GetAmount))]
    public static class Recipe_GetAmount_Transpiler
    {
        private static readonly MethodInfo Method_Player_GetFirstRequiredItem =
            AccessTools.Method(typeof(Player), nameof(Player.GetFirstRequiredItem));

        private static readonly MethodInfo Method_GetFirstRequiredItemFromNearbyChests =
            AccessTools.Method(typeof(Recipe_GetAmount_Transpiler), nameof(GetFirstRequiredItem));


        /// <summary>
        /// A fix for the fishy partial recipe bug. https://github.com/Grantapher/ValheimPlus/issues/40
        /// Adds support for recipes with multiple sets of required items.
        /// </summary>
        [UsedImplicitly]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!Configuration.Current.CraftFromChest.IsEnabled) return instructions;
            var il = instructions.ToList();
            for (int i = 0; i < il.Count; i++)
            {
                if (il[i].Calls(Method_Player_GetFirstRequiredItem))
                {
                    il[i] = new CodeInstruction(OpCodes.Call, Method_GetFirstRequiredItemFromNearbyChests);
                    return il.AsEnumerable();
                }
            }

            ValheimPlusPlugin.Logger.LogError("Couldn't transpile `Recipe.GetAmount`!");
            return il.AsEnumerable();
        }

        private static ItemDrop.ItemData GetFirstRequiredItem(Player player, Inventory inventory, Recipe recipe,
            int qualityLevel, out int amount, out int extraAmount, int craftMultiplier)
        {
            // call the old method first
            var result = player.GetFirstRequiredItem(inventory, recipe, qualityLevel, out amount, out extraAmount,
                craftMultiplier);
            if (result != null) return result; // we found items on the player

            var gameObject = Configuration.Current.CraftFromChest.checkFromWorkbench
                ? player.GetCurrentCraftingStation()?.gameObject ?? player.gameObject
                : player.gameObject;
            
            // Don't cache this. Recipe.GetAmount isn't called at any fixed interval (currently).
            var nearbyChests = InventoryAssistant.GetNearbyChests(
                gameObject,
                Helper.Clamp(Configuration.Current.CraftFromChest.range, 1, 50),
                !Configuration.Current.CraftFromChest.ignorePrivateAreaCheck);

            // try to find them inside chests.
            var requirements = recipe.m_resources;
            foreach (var chest in nearbyChests)
            {
                if (!chest) continue;
                foreach (var requirement in requirements)
                {
                    if (!requirement.m_resItem) continue;

                    int requiredAmount = requirement.GetAmount(qualityLevel) * craftMultiplier;
                    var requirementSharedItemData = requirement.m_resItem.m_itemData.m_shared;
                    for (int quality = 0; quality <= requirementSharedItemData.m_maxQuality; quality++)
                    {
                        var requirementName = requirementSharedItemData.m_name;
                        if (chest.m_inventory.CountItems(requirementName, quality) < requiredAmount) continue;

                        amount = requiredAmount;
                        extraAmount = requirement.m_extraAmountOnlyOneIngredient;
                        return chest.m_inventory.GetItem(requirementName, quality);
                    }
                }
            }

            amount = 0;
            extraAmount = 0;
            return null;
        }
    }
}