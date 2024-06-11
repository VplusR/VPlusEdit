using GUIFramework;
using HarmonyLib;
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

    public static class MapPinEditor_Patches
    {       
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.AddPin))]
        public static class Minimap_AddPin_Patch
        {
            private static void Postfix(ref Minimap __instance, ref Minimap.PinData __result)
            {     
                if (Configuration.Current.Map.IsEnabled && Configuration.Current.Map.shareAllPins)
                {
                    if (new List<Minimap.PinType> { Minimap.PinType.Icon0, Minimap.PinType.Icon1, Minimap.PinType.Icon2, Minimap.PinType.Icon3, Minimap.PinType.Icon4 }.Contains(__result.m_type))
                    {
                        Vector3 pos = __result.m_pos;
                        Minimap.PinType type = __result.m_type;
                        string name = __result.m_name;

                        ValheimPlusPlugin.Logger.LogInfo($"Pin Text: {name} or {__result.m_name}");

                        if (__instance.m_mode != Minimap.MapMode.Large)
                        {
                            ValheimPlusPlugin.Logger.LogInfo("Sent to server with bool");
                            VPlusMapPinSync.SendMapPinToServer(pos, type, name, true);                                
                        }
                        else
                        {
                            ValheimPlusPlugin.Logger.LogInfo("Sent to server without bool");
                            VPlusMapPinSync.SendMapPinToServer(pos, type, name);                                
                        }                        
                    }
                }
            }            
        }   

        /// <summary>
        /// Below is the code for the retired Vplus pin share system, i (nx) plan on working with this a little more when i get to it and have a plan on how i would like to change this.
        /// This will require some more days/weeks until i get to it, sorry.
        /// </summary>

        /*[HarmonyPatch(typeof(Minimap), "Awake")]
        public static class MapPinEditor_Patches_Awake
        {
            private static void Postfix(ref Minimap __instance)
            {
                // Ensure the InputField is properly initialized
                if (__instance.m_nameInput == null)
                {
                    GameObject inputFieldObj = new GameObject("PinNameInputField");
                    inputFieldObj.transform.SetParent(__instance.transform);

                    InputField inputField = inputFieldObj.AddComponent<InputField>();
                    RectTransform rectTransform = inputFieldObj.GetComponent<RectTransform>();
                    rectTransform.sizeDelta = new Vector2(200, 30);
                    rectTransform.anchoredPosition = Vector2.zero;

                    // Create separate Text components for text and placeholder
                    Text textComponent = inputFieldObj.AddComponent<Text>();
                    Text placeholderComponent = new GameObject("Placeholder").AddComponent<Text>();
                    placeholderComponent.transform.SetParent(inputFieldObj.transform);

                    // Assign the fonts and colors
                    textComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    textComponent.color = Color.black;
                    placeholderComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    placeholderComponent.color = Color.gray;

                    // Assign placeholder text
                    placeholderComponent.text = "Enter Pin Name";

                    // Configure the InputField
                    inputField.textComponent = textComponent;
                    inputField.placeholder = placeholderComponent;

                    __instance.m_nameInput = Minimap.m_instance.m_nameInput;
                }
            }
        }*/

        /*[HarmonyPatch(typeof(Minimap), "OnMapDblClick")]
        public static class MapPinEditor_Patches_OnMapDblClick
        {
            private static bool Prefix(ref Minimap __instance)
            {
                if (Configuration.Current.Map.IsEnabled)
                {
                    // Ensure shareablePins are set
                    Minimap_AddPin_Patch.shareablePins = new List<Minimap.PinType>()
                    {
                        Minimap.PinType.Icon0, Minimap.PinType.Icon1, Minimap.PinType.Icon2,
                        Minimap.PinType.Icon3, Minimap.PinType.Icon4
                    };

                    var nameInputFieldField = typeof(Minimap).GetField("m_nameInput", BindingFlags.NonPublic | BindingFlags.Instance);
                    InputField inputField = nameInputFieldField?.GetValue(__instance) as InputField;

                    if (inputField == null)
                    {
                        ValheimPlusPlugin.Logger.LogInfo("m_nameInput is null");
                        return true;
                    }

                    if (!__instance.gameObject.activeInHierarchy)
                    {
                        ValheimPlusPlugin.Logger.LogInfo("Minimap gameObject is not active in hierarchy");
                        return true;
                    }

                    inputField.gameObject.SetActive(true);
                    inputField.ActivateInputField();
                    EventSystem.current.SetSelectedGameObject(inputField.gameObject);

                    // Set the input field as the selected game object
                    if (!inputField.isFocused)
                    {
                        EventSystem.current.SetSelectedGameObject(inputField.gameObject);
                    }

                    // Store the pin position
                    pinPos = __instance.ScreenToWorldPoint(Input.mousePosition);
                    __instance.m_wasFocused = true;

                    return false; // Skip the original method
                }
                return true; // Run the original method if the map is not enabled
            }
        }*/

        [HarmonyPatch(typeof(Minimap), "UpdateNameInput")]
        public static class MapPinEditor_Patches_UpdateNameInput
        {
            private static bool Prefix(ref Minimap __instance)
            {
                if (Configuration.Current.Map.IsEnabled)
                {
                    // Break out of this unnecessary thing
                    return false;
                }
                return true;
            }
        }

        
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.InTextInput))]
        public static class MapPinEditor_InTextInput_Patch
        {
            private static bool Prefix(ref bool __result)
            {
                if (Configuration.Current.Map.IsEnabled)
                {
                    __result = Minimap.m_instance.m_mode == Minimap.MapMode.Large && Minimap.m_instance.m_wasFocused;
                    return false;
                }
                return true;
            }
        }
        
        /*[HarmonyPatch(typeof(Minimap), nameof(Minimap.Update))]
        public static class MapPinEditor_Update_Patch
        {
            private static void Postfix(ref Minimap __instance)
            {
                if (Configuration.Current.Map.IsEnabled)
                {
                    if (Minimap.InTextInput())
                    {
                        if (Input.GetKeyDown(KeyCode.Escape))
                        {
                            Minimap.instance.m_wasFocused = false;                             
                        } 
                        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                        {
                            AddPin(ref __instance);
                        }
                    }
                }
            }
        }

        public static void AddPin(ref Minimap __instance)
        {
            Minimap.PinType pintype = __instance.m_selectedType;
            Minimap.PinData addedPin = __instance.AddPin(pinPos, pintype, __instance.m_nameInput.text, true, false);
            if (!Configuration.Current.Map.shareAllPins)
                VPlusMapPinSync.SendMapPinToServer(addedPin);
            __instance.m_nameInput.gameObject.SetActive(false);
            __instance.m_wasFocused = false;
        }*/
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
