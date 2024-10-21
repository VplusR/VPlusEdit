using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using ValheimPlus.Configurations;
using ValheimPlus.Utility;

namespace ValheimPlus.GameClasses
{
    internal static class FireplaceFuel
    {
        /// <summary>
        /// When fire source is loaded, set its fuel to infinite if necessary.
        /// </summary>
        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Awake))]
        public static class Fireplace_Awake_Patch
        {
            [UsedImplicitly]
            private static void Postfix(ref Fireplace __instance)
            {
                var config = Configuration.Current.FireSource;
                if (!config.IsEnabled) return;

                var isTorch = IsTorch(__instance.m_nview.GetPrefabName());
                if ((isTorch && !config.torches) || (!isTorch && !config.fires)) return;

                __instance.m_infiniteFuel = true;
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.UpdateFireplace))]
        public static class Fireplace_UpdateFireplace_Transpiler
        {
            private static readonly MethodInfo Method_Zdo_SetFloat =
                AccessTools.Method(typeof(ZDO), nameof(ZDO.GetFloat), new[] { typeof(int), typeof(float) });

            private static readonly MethodInfo Method_AddFuelFromNearbyChests =
                AccessTools.Method(typeof(Fireplace_UpdateFireplace_Transpiler), nameof(AddFuelFromNearbyChests));

            [HarmonyTranspiler]
            [UsedImplicitly]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var config = Configuration.Current.FireSource;
                if (!config.IsEnabled || !config.autoFuel) return instructions;

                var il = instructions.ToList();

                for (int i = 0; i < il.Count; i++)
                {
                    // replace
                    // ```
                    // this.m_nview.GetZDO().Set(ZDOVars.s_fuel, num3)
                    // ```
                    // with
                    // ```
                    // this.m_nview.GetZDO().Set(ZDOVars.s_fuel, AddFuelFromNearbyChests(this) + num3)
                    // ```
                    // where num3 is the new amount of fuel to set (normally just the normal decay of fuel)
                    if (!il[i].Calls(Method_Zdo_SetFloat)) continue;

                    i -= 2;
                    il.Insert(++i, new CodeInstruction(OpCodes.Ldarg_0));
                    il.Insert(++i, new CodeInstruction(OpCodes.Call, Method_AddFuelFromNearbyChests));
                    i++; // this is the stloc for num3 
                    il.Insert(++i, new CodeInstruction(OpCodes.Add));

                    return il;
                }

                ValheimPlusPlugin.Logger.LogError("Failed to apply Fireplace_UpdateFireplace_Transpiler");

                return il;
            }

            private static float AddFuelFromNearbyChests(Fireplace __instance)
            {
                var config = Configuration.Current.FireSource;
                var isTorch = IsTorch(__instance.m_nview.GetPrefabName());
                // if they are already infinite fuel, don't bother to search for more fuel.
                if ((isTorch && config.torches) || (!isTorch && config.fires)) return 0;

                int currentFuel = (int)Math.Ceiling(__instance.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel));
                int toMaxFuel = (int)__instance.m_maxFuel - currentFuel;
                if (toMaxFuel <= 0) return 0;

                var stopwatch = GameObjectAssistant.GetStopwatch(__instance.gameObject);
                if (stopwatch.IsRunning && stopwatch.ElapsedMilliseconds < 1000) return 0;

                stopwatch.Restart();

                var fuelItemData = __instance.m_fuelItem.m_itemData;
                var range = Helper.Clamp(config.autoRange, 1, 50);
                int addedFuel = InventoryAssistant.RemoveItemInAmountFromAllNearbyChests(
                    __instance.gameObject, range, fuelItemData, toMaxFuel, !config.ignorePrivateAreaCheck);

                if (addedFuel <= 0) return 0;

                // Only make the call if we're actually adding fuel,
                // otherwise the fuel adding animation plays every second.
                __instance.m_nview.InvokeRPC("RPC_AddFuelAmount", (float)addedFuel);
                ValheimPlusPlugin.Logger.LogInfo(
                    $"Added {addedFuel} fuel({fuelItemData.m_shared.m_name}) in {__instance.m_name}");

                return addedFuel;
            }
        }

        private static bool IsTorch(string itemName) => TorchItemNames.Contains(itemName);

        private static readonly HashSet<string> TorchItemNames = new()
        {
            "piece_groundtorch_wood", // standing wood torch
            "piece_groundtorch", // standing iron torch
            "piece_groundtorch_green", // standing green torch
            "piece_groundtorch_blue", // standing blue torch
            "piece_walltorch", // sconce torch
            "piece_brazierceiling01", // ceiling brazier
            "piece_brazierfloor01", // standing brazier
            "piece_brazierfloor02", // Blue standing brazier
            "piece_jackoturnip" // Jack-o-turnip
        };
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
    public static class Fireplace_Interact_Transpiler
    {
        // TODO memory leak
        private static readonly Dictionary<float, List<Container>> NearbyChestsDictionary = new();

        private static readonly MethodInfo Method_Inventory_HaveItem =
            AccessTools.Method(typeof(Inventory), nameof(Inventory.HaveItem), new[] { typeof(string), typeof(bool) });

        private static readonly MethodInfo Method_ReplaceInventoryRefByChest = AccessTools.Method(
            typeof(Fireplace_Interact_Transpiler), nameof(ReplaceInventoryRefByChest));

        /// <summary>
        /// Patches out the code that looks for fuel item.
        /// When no fuel item has been found in the player inventory, check inside nearby chests.
        /// If found, replace the reference to the player Inventory by the one from the chest.
        /// </summary>
        [HarmonyTranspiler]
        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!Configuration.Current.CraftFromChest.IsEnabled) return instructions;

            var il = instructions.ToList();

            for (int i = 0; i < il.Count; i++)
            {
                if (!il[i].Calls(Method_Inventory_HaveItem)) continue;

                // replace call to `inventory.HaveItem(this.m_fuelItem.m_itemData.m_shared.m_name, true)`
                // with call to `Fireplace_Interact_Transpiler.ReplaceInventoryRefByChest(ref inventory, this)`.
                il[i - 7] = new CodeInstruction(OpCodes.Ldloca_S, 1).MoveLabelsFrom(il[i - 7]);
                il[i] = new CodeInstruction(OpCodes.Call, Method_ReplaceInventoryRefByChest);
                il.RemoveRange(i - 5, 5);
                return il.AsEnumerable();
            }

            ValheimPlusPlugin.Logger.LogError("Failed to apply Fireplace_Interact_Transpiler");

            return il;
        }

        private static bool ReplaceInventoryRefByChest(ref Inventory inventory, Fireplace fireplace)
        {
            string itemName = fireplace.m_fuelItem.m_itemData.m_shared.m_name;
            if (inventory.HaveItem(itemName)) return true; // original inventory suffices, no need to search further.

            var config = Configuration.Current.CraftFromChest;
            var fireplaceGameObject = fireplace.gameObject;
            var stopwatch = GameObjectAssistant.GetStopwatch(fireplaceGameObject);
            var hash = GameObjectAssistant.GetGameObjectPositionHash(fireplaceGameObject);
            if (NearbyChestsDictionary.TryGetValue(hash, out var nearbyChests))
            {
                int lookupInterval = Helper.Clamp(config.lookupInterval, 1, 10) * 1000;
                if (!stopwatch.IsRunning || stopwatch.ElapsedMilliseconds > lookupInterval) UpdateNearbyChests();
            }
            else
            {
                UpdateNearbyChests();
            }

            var maybeInventory = nearbyChests
                .Select(container => container.GetInventory())
                .FirstOrDefault(inv => inv.HaveItem(itemName));

            if (inventory != null) inventory = maybeInventory;
            return maybeInventory != null;

            void UpdateNearbyChests()
            {
                var range = Helper.Clamp(config.range, 1, 50);
                nearbyChests =
                    InventoryAssistant.GetNearbyChests(fireplace.gameObject, range, !config.ignorePrivateAreaCheck);
                stopwatch.Restart();
                NearbyChestsDictionary[hash] = nearbyChests;
            }
        }
    }
}