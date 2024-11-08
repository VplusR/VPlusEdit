﻿using GUIFramework;
using HarmonyLib;
using System.Reflection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValheimPlus.Configurations;
using ValheimPlus.RPC;
using ValheimPlus.Utility;
using static Minimap;
using static System.Net.Mime.MediaTypeNames;
using Random = UnityEngine.Random;
using BepInEx;
using static Mono.Security.X509.X520;

// ToDo add packet system to convey map markers
namespace ValheimPlus.GameClasses
{
    /// <summary>
    /// Hooks base explore method
    /// </summary>
    [HarmonyPatch(typeof(Minimap))]
    public class HookExplore
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(Minimap), "Explore", new Type[] { typeof(Vector3), typeof(float) })]
        public static void call_Explore(object instance, Vector3 p, float radius) => throw new NotImplementedException();
    }

    /// <summary>
    /// Update exploration for all players
    /// </summary>
    [HarmonyPatch(typeof(Minimap), "UpdateExplore")]
    public static class ChangeMapBehavior
    {
        private static void Prefix(ref float dt, ref Player player, ref Minimap __instance, ref float ___m_exploreTimer, ref float ___m_exploreInterval)
        {
            if (Configuration.Current.Map.exploreRadius > 10000) Configuration.Current.Map.exploreRadius = 10000;

            if (!Configuration.Current.Map.IsEnabled) return;

            if (Configuration.Current.Map.shareMapProgression)
            {
                float explorerTime = ___m_exploreTimer;
                explorerTime += Time.deltaTime;
                if (explorerTime > ___m_exploreInterval)
                {
                    if (ZNet.instance.m_players.Any())
                    {
                        foreach (ZNet.PlayerInfo m_Player in ZNet.instance.m_players)
                        {
                            HookExplore.call_Explore(__instance, m_Player.m_position, Configuration.Current.Map.exploreRadius);
                        }
                    }
                }
            }

            // Always reveal for your own, we do this non the less to apply the potentially bigger exploreRadius
            HookExplore.call_Explore(__instance, player.transform.position, Configuration.Current.Map.exploreRadius);
        }
    }

    [HarmonyPatch(typeof(Minimap), "Awake")]
    public static class MinimapAwake
    {
        private static void Postfix()
        {
            if (ZNet.m_isServer && Configuration.Current.Map.IsEnabled && Configuration.Current.Map.shareMapProgression)
            {
                //Init map array
                VPlusMapSync.ServerMapData = new bool[Minimap.instance.m_textureSize * Minimap.instance.m_textureSize];

                //Load map data from disk
                VPlusMapSync.LoadMapDataFromDisk();

                //Start map data save timer
                ValheimPlusPlugin.MapSyncSaveTimer.Start();
            }
        }
    }

    // Triggers sending map pins to players when a pin is created
    public static class MapPinEditor_Patches
    {
        // Gets name of the pin
        [HarmonyPatch(typeof(Minimap), "OnPinTextEntered")]
        public static class Minimap_PinNameData_CreateMapNamePin_Patch
        {
            private static void Prefix(string t, ref Minimap __instance)
            {
                if (__instance.m_nameInput == null) return;

                string pinName = __instance.m_nameInput.text;

                if (pinName == null) return;

                // original handling of the method
                pinName = pinName.Replace('$', ' ').Replace('<', ' ').Replace('>', ' ');

                if (string.IsNullOrEmpty(pinName)) return;

                var namePin = __instance.m_namePin; // Accessing m_namePin

                if (namePin == null) return;

                if (!Configuration.Current.Map.IsEnabled || !Configuration.Current.Map.shareAllPins) return;

                if (new List<Minimap.PinType> { Minimap.PinType.Icon0, Minimap.PinType.Icon1, Minimap.PinType.Icon2, Minimap.PinType.Icon3, Minimap.PinType.Icon4 }.Contains(namePin.m_type))
                {
                    Vector3 pos = namePin.m_pos; // Assuming m_pos is a field in namePin
                    Minimap.PinType type = namePin.m_type; // Assuming m_type is a field in namePin

                    // ValheimPlusPlugin.Logger.LogInfo($"Pin Text: {pinName}"); // This triggers even when clients connect to the server and server sends stored pins

                    if (!string.IsNullOrEmpty(pinName))
                    {
                        if (__instance.m_mode != Minimap.MapMode.Large)
                        {
                            VPlusMapPinSync.SendMapPinToServer(pos, type, pinName, true);
                        }
                        else
                        {
                            VPlusMapPinSync.SendMapPinToServer(pos, type, pinName);
                        }
                    }
                }
            }
        }

        // Handles deleting of pins by owner
        [HarmonyPatch(typeof(Minimap), "OnMapRightClick")]
        public static class miniMap_pinDelete_patch
        {
            private static void Postfix()
            {
                //ValheimPlusPlugin.Logger.LogFatal("Setting up pin data for deletion");
                string playerName = Player.m_localPlayer.GetPlayerName();
                Vector3 pos = Minimap.instance.ScreenToWorldPoint(ZInput.mousePosition);
                ZPackage zpkg = new ZPackage();

                if (!(ZNet.m_isServer))
                {
                    zpkg.Write(playerName);
                    zpkg.Write(pos);

                    //ValheimPlusPlugin.Logger.LogFatal($"Sending pin data for deletion: { playerName}");
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "VPlusMapDeletePin", new object[] { zpkg });
                }
            }
        }
    }

    /// <summary>
    /// Show boats and carts on map
    /// </summary>
    public class displayCartsAndBoatsOnMap
    {
        static Dictionary<ZDO, Minimap.PinData> customPins = new Dictionary<ZDO, Minimap.PinData>();
        static Dictionary<int, Sprite> icons = new Dictionary<int, Sprite>();
        static int CartHashcode = "Cart".GetStableHashCode();
        static int RaftHashcode = "Raft".GetStableHashCode();
        static int KarveHashcode = "Karve".GetStableHashCode();
        static int LongshipHashcode = "VikingShip".GetStableHashCode();
        static int hammerHashCode = "Hammer".GetStableHashCode();
        static float updateInterval = 5.0f;

        // clear dictionary if the user logs out
        [HarmonyPatch(typeof(Minimap), "OnDestroy")]
        public static class Minimap_OnDestroy_Patch
        {
            private static void Postfix()
            {
                customPins.Clear();
                icons.Clear();
            }
        }

        [HarmonyPatch(typeof(Minimap), "UpdateMap")]
        public static class Minimap_UpdateMap_Patch
        {
            static float timeCounter = updateInterval;

            private static void FindIcons()
            {
                GameObject hammer = ObjectDB.instance.m_itemByHash[hammerHashCode];
                if (!hammer)
                    return;
                ItemDrop hammerDrop = hammer.GetComponent<ItemDrop>();
                if (!hammerDrop)
                    return;
                PieceTable hammerPieceTable = hammerDrop.m_itemData.m_shared.m_buildPieces;
                foreach (GameObject piece in hammerPieceTable.m_pieces)
                {
                    Piece p = piece.GetComponent<Piece>();
                    icons.Add(p.name.GetStableHashCode(), p.m_icon);
                }
            }

            private static bool CheckPin(Minimap __instance, Player player, ZDO zdo, int hashCode, string pinName)
            {
                if (zdo.m_prefab != hashCode)
                    return false;

                Minimap.PinData customPin;
                bool pinWasFound = customPins.TryGetValue(zdo, out customPin);

                // turn off associated pin if player controlled ship is in that position
                Ship controlledShip = player.GetControlledShip();
                if (controlledShip && Vector3.Distance(controlledShip.transform.position, zdo.m_position) < 0.01f)
                {
                    if (pinWasFound)
                    {
                        __instance.RemovePin(customPin);
                        customPins.Remove(zdo);
                    }
                    return true;
                }

                if (!pinWasFound)
                {
                    customPin = __instance.AddPin(zdo.m_position, Minimap.PinType.Death, pinName, false, false);

                    Sprite sprite;
                    if (icons.TryGetValue(hashCode, out sprite))
                        customPin.m_icon = sprite;

                    customPin.m_doubleSize = true;
                    customPins.Add(zdo, customPin);
                } 
                else
                    customPin.m_pos = zdo.m_position;

                return true;
            }

            public static void Postfix(ref Minimap __instance, Player player, float dt, bool takeInput)
            {
                timeCounter += dt;

                if (timeCounter < updateInterval || !Configuration.Current.Map.IsEnabled || !Configuration.Current.Map.displayCartsAndBoats)
                    return;

                timeCounter -= updateInterval;

                if (icons.Count == 0)
                    FindIcons();

                // search zones for ships and carts
                foreach (List<ZDO> zdoarray in ZDOMan.instance.m_objectsBySector)
                {
                    if (zdoarray != null)
                    {
                        foreach (ZDO zdo in zdoarray)
                        {
                            if (CheckPin(__instance, player, zdo, CartHashcode, "Cart"))
                                continue;
                            if (CheckPin(__instance, player, zdo, RaftHashcode, "Raft"))
                                continue;
                            if (CheckPin(__instance, player, zdo, KarveHashcode, "Karve"))
                                continue;
                            if (CheckPin(__instance, player, zdo, LongshipHashcode, "Longship"))
                                continue;
                        }
                    }
                }

                // clear pins for destroyed objects
                foreach (KeyValuePair<ZDO, Minimap.PinData> pin in customPins)
                {
                    if (!pin.Key.IsValid())
                    {
                        __instance.RemovePin(pin.Value);
                        customPins.Remove(pin.Key);
                    }
                }
            }
        }
    }
}
