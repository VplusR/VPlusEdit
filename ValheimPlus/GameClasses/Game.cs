using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UIElements;
using ValheimPlus;
using ValheimPlus.Configurations;
using ValheimPlus.Configurations.Sections;
using ValheimPlus.RPC;
using static Minimap;
using static ValheimPlus.RPC.VPlusMapPinSync;
using BepInEx;

namespace ValheimPlus.GameClasses
{
    /// <summary>
    /// Sync server config to clients
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.Start))]
    public static class Game_Start_Patch
    {
        public static string PinDataFilePath = "mapPins.csv";
        public static List<ZPackage> storedMapPins = new List<ZPackage>();        

        [UsedImplicitly]
        private static void Prefix()
        {
            ZRoutedRpc.instance.Register<ZPackage>("VPlusConfigSync", VPlusConfigSync.RPC_VPlusConfigSync);
            ZRoutedRpc.instance.Register<ZPackage>("VPlusMapSync", VPlusMapSync.RPC_VPlusMapSync);
            ZRoutedRpc.instance.Register<ZPackage>("VPlusMapAddPin", VPlusMapPinSync.RPC_VPlusMapAddPin);            
            ZRoutedRpc.instance.Register("VPlusAck", VPlusAck.RPC_VPlusAck);
        }

        private static void Postfix()
        {
            LoadPinsFromFile();
        }

        public static List<MapPinData> LoadPinsFromFile()
        {
            List<MapPinData> pinDataList = new List<MapPinData>();            

            if (ZNet.instance.IsServer())
            {
                if (!File.Exists(PinDataFilePath))
                {
                    // If the file doesn't exist, create it
                    using (File.Create(PinDataFilePath)) { }
                }

                try
                {
                    using (StreamReader reader = new StreamReader(PinDataFilePath))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string[] parts = line.Split(',');
                            if (parts.Length == 8)
                            {
                                try
                                {
                                    long senderID = long.Parse(parts[0]);
                                    string senderName = parts[1];
                                    float positionX = float.Parse(parts[2]);
                                    float positionY = float.Parse(parts[3]);
                                    float positionZ = float.Parse(parts[4]);
                                    int pinType = int.Parse(parts[5]);
                                    string pinName = parts[6];
                                    bool keepQuiet = bool.Parse(parts[7]);

                                    if (senderName.IsNullOrWhiteSpace())
                                    {
                                        senderName = string.Empty;
                                    }

                                    MapPinData pinData = new MapPinData
                                    {
                                        SenderID = senderID,
                                        SenderName = senderName,
                                        Position = new Vector3(positionX, positionY, positionZ),
                                        PinType = pinType,
                                        PinName = pinName,
                                        KeepQuiet = keepQuiet
                                    };

                                    ValheimPlusPlugin.Logger.LogInfo($"pinSender: {senderID}");
                                    ValheimPlusPlugin.Logger.LogInfo($"senderName: {senderName}");
                                    ValheimPlusPlugin.Logger.LogInfo($"PosX: {positionX}, PosY {positionY}, PosZ {positionZ}");
                                    ValheimPlusPlugin.Logger.LogInfo($"Type: {pinType}");
                                    ValheimPlusPlugin.Logger.LogInfo($"PinName: {pinName}");
                                    ValheimPlusPlugin.Logger.LogInfo($"Shout: {keepQuiet}");

                                    pinDataList.Add(pinData);
                                }
                                catch (Exception ex)
                                {
                                    ValheimPlusPlugin.Logger.LogError($"Failed to parse map pin data from line: {line}. Error: {ex.Message}");
                                }                                
                            }
                        }
                    }

                    // clears the list before adding pin data to memory
                    storedMapPins.Clear();

                    // Populate storedMapPins with the loaded data
                    foreach (var mappinData in pinDataList)
                    {
                        ZPackage pkg = new ZPackage();
                        pkg.Write(mappinData.SenderID);
                        pkg.Write(mappinData.SenderName);
                        pkg.Write(mappinData.Position);
                        pkg.Write(mappinData.PinType);
                        pkg.Write(mappinData.PinName);
                        pkg.Write(mappinData.KeepQuiet);

                        ValheimPlusPlugin.Logger.LogInfo($"pinSender: {pkg.ReadLong()}");
                        ValheimPlusPlugin.Logger.LogInfo($"senderName: {pkg.ReadString()}");
                        ValheimPlusPlugin.Logger.LogInfo($"Position: {pkg.ReadVector3()}");
                        ValheimPlusPlugin.Logger.LogInfo($"Type: {pkg.ReadInt()}");
                        ValheimPlusPlugin.Logger.LogInfo($"PinName: {pkg.ReadString()}");
                        ValheimPlusPlugin.Logger.LogInfo($"Shout: {pkg.ReadBool()}");

                        storedMapPins.Add(pkg);
                    }

                    int numberOfPackages = ValheimPlus.GameClasses.Game_Start_Patch.storedMapPins.Count;

                    ValheimPlusPlugin.Logger.LogInfo("Loaded map pins from file.");
                    ValheimPlusPlugin.Logger.LogInfo($"storedMapPins has {numberOfPackages} in it.");
                }
                catch (Exception ex)
                {
                    ValheimPlusPlugin.Logger.LogError($"Failed to load map pins from file: {ex.Message}");
                }
            }
            return pinDataList;
        }
    }

    /*[HarmonyPatch(typeof(Game), nameof(Game.UpdateSaving))]
    public static class PinSave_patch
    {
        private static void Postfix(Game __instance)
        {            
            if ((bool)ZNet.instance)
            {
                ValheimPlusPlugin.Logger.LogInfo("Saving Map Pins.");

                List<MapPinData> pinList = new List<MapPinData>();
                List<ZPackage> zPackages = ValheimPlus.GameClasses.Game_Start_Patch.storedMapPins;

                if (ZRoutedRpc.instance.GetServerPeerID() == ZRoutedRpc.instance.m_id && Configuration.Current.Map.shareAllPins)
                {
                    foreach (var zPackage in zPackages)
                    {
                        int pinCount = zPackage.ReadInt();
                        for (int i = 0; i < pinCount; i++)
                        {
                            long senderID = zPackage.ReadLong();
                            string senderName = zPackage.ReadString();
                            float posX = zPackage.ReadSingle();
                            float posY = zPackage.ReadSingle();
                            float posZ = zPackage.ReadSingle();
                            int pinType = zPackage.ReadInt();
                            string pinName = zPackage.ReadString();
                            bool keepQuiet = zPackage.ReadBool();

                            MapPinData pinData = new MapPinData
                            {
                                SenderID = senderID,
                                SenderName = senderName,
                                Position = new Vector3(posX, posY, posZ),
                                PinType = pinType,
                                PinName = pinName,
                                KeepQuiet = keepQuiet
                            };

                            pinList.Add(pinData);
                        }
                    }

                    try
                    {
                        using (StreamWriter writer = new StreamWriter(ValheimPlus.GameClasses.Game_Start_Patch.PinDataFilePath, false, Encoding.UTF8))
                        {
                            foreach (var pin in pinList)
                            {
                                string newLine = $"{pin.SenderID},{pin.SenderName},{pin.Position.x},{pin.Position.y},{pin.Position.z},{pin.PinType},{pin.PinName},{pin.KeepQuiet}";
                                writer.WriteLine(newLine);
                            }

                            ValheimPlusPlugin.Logger.LogInfo("Saving Completed.");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle exceptions (e.g., logging)
                        ValheimPlusPlugin.Logger.LogInfo("An error occurred while saving pins: " + ex.Message);
                    }
                }
            }
        }
    }*/

    // Saves Map Pin Data to disk
    [HarmonyPatch(typeof(Game), nameof(Game.OnApplicationQuit))]
    public static class MapPinSave_patch
    {
        private static void Prefix(Game __instance)
        {
            List<MapPinData> pinList = new List<MapPinData>();
            List<ZPackage> zPackages = ValheimPlus.GameClasses.Game_Start_Patch.storedMapPins;

            ValheimPlusPlugin.Logger.LogInfo("Game Quitting. Saving Pins.");

            if (ZRoutedRpc.instance.GetServerPeerID() == ZRoutedRpc.instance.m_id && Configuration.Current.Map.shareAllPins)
            {

                try
                {
                    using (FileStream fileStream = new FileStream(ValheimPlus.GameClasses.Game_Start_Patch.PinDataFilePath, FileMode.Create, FileAccess.Write))
                    {
                        using (StreamWriter writer = new StreamWriter(fileStream, Encoding.UTF8))
                        {
                            foreach (var zPackage in zPackages)
                            {
                                // Reset the read position to the start of the ZPackage
                                zPackage.SetPos(0);

                                long senderID = zPackage.ReadLong();
                                string senderName = zPackage.ReadString();
                                Vector3 pos = zPackage.ReadVector3();
                                int pinType = zPackage.ReadInt();
                                string pinName = zPackage.ReadString();
                                bool keepQuiet = zPackage.ReadBool();

                                float posX = pos.x;
                                float posY = pos.y;
                                float posZ = pos.z;

                                // Construct the line with the delimiter
                                string line = $"{senderID},{senderName},{posX},{posY},{posZ},{pinType},{pinName},{keepQuiet}";
                                ValheimPlusPlugin.Logger.LogInfo($"String Line: {line}");

                                // Write the line to the file
                                writer.WriteLine(line);
                            }
                        }
                    }
                    ValheimPlusPlugin.Logger.LogInfo("Map pins saved successfully.");
                }
                catch (Exception ex)
                {
                    // Handle exceptions (e.g., logging)
                    ValheimPlusPlugin.Logger.LogError("An error occurred while saving pins: " + ex.Message);
                }
            }
        }        
    }

    /// <summary>
    /// Alter game difficulty damage scale.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.GetDifficultyDamageScalePlayer))]
    public static class Game_GetDifficultyDamageScale_Patch
    {
        [UsedImplicitly]
        private static void Prefix(Game __instance)
        {
            var config = Configuration.Current.Game;
            if (!config.IsEnabled) return;
            __instance.m_damageScalePerPlayer = config.gameDifficultyDamageScale / 100f;
        }
    }

    /// <summary>
    /// Alter game difficulty health scale for enemies.
    /// 
    /// Although the underlying game code seems to just scale the damage down,
    /// in game this results in the same damage but higher enemy health.
    /// Not sure how that is converted in the game code, however. 
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.GetDifficultyDamageScaleEnemy))]
    public static class Game_GetDifficultyHealthScale_Patch
    {
        [UsedImplicitly]
        private static void Prefix(Game __instance)
        {
            var config = Configuration.Current.Game;
            if (!config.IsEnabled) return;
            __instance.m_healthScalePerPlayer = config.gameDifficultyHealthScale / 100f;
        }
    }

    /// <summary>
    /// Disable the "I have arrived" message on spawn.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.UpdateRespawn))]
    public static class Game_UpdateRespawn_Patch
    {
        [UsedImplicitly]
        private static void Prefix(ref Game __instance, float dt)
        {
            var config = Configuration.Current.Player;
            if (!config.IsEnabled || config.iHaveArrivedOnSpawn) return;
            __instance.m_firstSpawn = false;
        }
    }

    /// <summary>
    /// Alter player difficulty scale
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.GetPlayerDifficulty))]
    public static class Game_GetPlayerDifficulty_Patch
    {
        private static readonly FieldInfo Field_M_DifficultyScaleRange =
            AccessTools.Field(typeof(Game), nameof(Game.m_difficultyScaleRange));

        /// <summary>
        /// Patches the range used to check the number of players around.
        /// </summary>
        [UsedImplicitly]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!Configuration.Current.Game.IsEnabled) return instructions;

            float range = Math.Min(Configuration.Current.Game.difficultyScaleRange, 2);

            var il = instructions.ToList();
            for (int i = 0; i < il.Count; i++)
            {
                if (il[i].LoadsField(Field_M_DifficultyScaleRange))
                {
                    il.RemoveAt(i - 1); // remove "this"
                    // replace field with our range as a constant
                    il[i - 1] = new CodeInstruction(OpCodes.Ldc_R4, range);
                    return il.AsEnumerable();
                }
            }

            ValheimPlusPlugin.Logger.LogError("Failed to apply Game_GetPlayerDifficulty_Patch.Transpiler");

            return il;
        }

        [UsedImplicitly]
        private static void Postfix(ref int __result)
        {
            var config = Configuration.Current.Game;
            if (!config.IsEnabled) return;
            if (config.setFixedPlayerCountTo > 0) __result = config.setFixedPlayerCountTo;
            __result += config.extraPlayerCountNearby;
        }
    }

    public class MapPinData
    {
        public long SenderID { get; set; }
        public string SenderName { get; set; }
        public Vector3 Position { get; set; }
        public int PinType { get; set; }
        public string PinName { get; set; }
        public bool KeepQuiet { get; set; }
        public string GetUniqueID()
        {
            return $"{Position.x}-{Position.y}-{Position.z}";
        }
    }
}
