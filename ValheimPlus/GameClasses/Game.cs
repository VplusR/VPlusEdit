using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
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

namespace ValheimPlus.GameClasses
{
    /// <summary>
    /// Sync server config to clients
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.Start))]
    public static class Game_Start_Patch
    {
        public static readonly string PinDataFilePath = "mapPins.csv";
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
                                long senderID = long.Parse(parts[0]);
                                string senderName = parts[1];
                                float positionX = float.Parse(parts[2]);
                                float positionY = float.Parse(parts[3]);
                                float positionZ = float.Parse(parts[4]);
                                int pinType = int.Parse(parts[5]);
                                string pinName = parts[6];
                                bool keepQuiet = bool.Parse(parts[7]);

                                MapPinData pinData = new MapPinData
                                {
                                    SenderID = senderID,
                                    SenderName = senderName,
                                    Position = new Vector3(positionX, positionY, positionZ),                                    
                                    PinType = pinType,
                                    PinName = pinName,
                                    KeepQuiet = keepQuiet
                                };

                                pinDataList.Add(pinData);
                            }
                        }
                    }

                    // Populate storedMapPins with the loaded data
                    foreach (var pinData in pinDataList)
                    {
                        ZPackage pkg = new ZPackage();
                        pkg.Write(pinData.SenderID);
                        pkg.Write(pinData.SenderName);
                        pkg.Write(pinData.Position);
                        pkg.Write(pinData.PinType);
                        pkg.Write(pinData.PinName);
                        pkg.Write(pinData.KeepQuiet);

                        storedMapPins.Add(pkg);
                    }

                    ValheimPlusPlugin.Logger.LogInfo("Loaded map pins from file.");
                }
                catch (Exception ex)
                {
                    ValheimPlusPlugin.Logger.LogError($"Failed to load map pins from file: {ex.Message}");
                }
            }
            return pinDataList;
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    public class ZNet_OnNewConnection_Patch
    {
        public static void Postfix(ZNetPeer peer)
        {
            SendStoredMapPinsToClient(peer);
        }

        private static void SendStoredMapPinsToClient(ZNetPeer peer)
        {
            ValheimPlusPlugin.Logger.LogInfo("Sending stored map pins to client...");

            if (ZNet.instance.IsServer())
            {
                long serverID = ZRoutedRpc.instance.GetServerPeerID();

                foreach (var pinDataPackage in Game_Start_Patch.storedMapPins)
                {
                    ZPackage packageToSend = new ZPackage();

                    packageToSend.Write(pinDataPackage); // Write the original package data

                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ReceiveMapPins", packageToSend);
                }

                ValheimPlusPlugin.Logger.LogInfo("All stored map pins sent to client.");
            }
        }

        private static void ReceiveMapPins(ZRpc rpc, ZPackage pkg)
        {
            long senderID = pkg.ReadLong();
            long serverID = ZRoutedRpc.instance.GetServerPeerID();
            byte[] pinDataArray = pkg.ReadByteArray();

            // Only process the package if it's from the server
            if (senderID != serverID) return;

            if (pkg == null)
            {
                ValheimPlusPlugin.Logger.LogError("Received empty package."); // only for debugging
                return;
            }

            ValheimPlusPlugin.Logger.LogInfo($"Received package from sender ID: {senderID}, expected server ID: {serverID}");

            ZPackage pinDataPackage = new ZPackage(pinDataArray);

            // Extracts pin data from pkg
            List<MapPinData> pinDataList = DeserializePinData(pinDataPackage);

            int pinDataCount = pinDataPackage.ReadInt();

            for (int i = 0; i < pinDataCount; i++)
            {
                // Read each pin data entry
                long pinSenderID = pinDataPackage.ReadLong();
                string pinSenderName = pinDataPackage.ReadString();
                Vector3 pinPosition = pinDataPackage.ReadVector3();
                int pinType = pinDataPackage.ReadInt();
                string pinName = pinDataPackage.ReadString();
                bool keepQuiet = pinDataPackage.ReadBool();

                // Create a MapPinData object from the read data
                MapPinData pinData = new MapPinData
                {
                    SenderID = pinSenderID,
                    SenderName = pinSenderName,
                    Position = pinPosition,
                    PinType = pinType,
                    PinName = pinName,
                    KeepQuiet = keepQuiet
                };

                // Add the pin data to the list
                pinDataList.Add(pinData);
            }

            // Add pins to map
            foreach (MapPinData pinData in pinDataList)
            {
                Minimap.PinData pin = Minimap.instance.AddPin(
                    pinData.Position,
                    (Minimap.PinType)pinData.PinType,
                    pinData.PinName,
                    true,
                    true
                );

                ValheimPlusPlugin.Logger.LogInfo($"Received pin: {pinData.PinName} at position: {pinData.Position}");
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Received pin: {pinData.PinName}", 0, null);
            }
        }

        private static List<MapPinData> DeserializePinData(ZPackage pkg)
        {
            List<MapPinData> pinDataList = new List<MapPinData>();

            int pinDataCount = pkg.ReadInt();

            for (int i = 0; i < pinDataCount; i++)
            {
                // Read each pin data entry
                long pinSenderID = pkg.ReadLong();
                string pinSenderName = pkg.ReadString();
                Vector3 pinPosition = pkg.ReadVector3();
                int pinType = pkg.ReadInt();
                string pinName = pkg.ReadString();
                bool keepQuiet = pkg.ReadBool();

                // Create a MapPinData object from the read data
                MapPinData pinData = new MapPinData
                {
                    SenderID = pinSenderID,
                    SenderName = pinSenderName,
                    Position = pinPosition,                    
                    PinType = pinType,
                    PinName = pinName,
                    KeepQuiet = keepQuiet
                };

                // Add the pin data to the list
                pinDataList.Add(pinData);
            }
            return pinDataList;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.UpdateSaving))]
    public static class PinSave_patch
    {
        private static void Postfix(Game __instance)
        {            
            if ((bool)ZNet.instance)
            {
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
                            writer.WriteLine("SenderID,SenderName,PositionX,PositionY,PositionZ,PinType,PinName,KeepQuiet");

                            foreach (var pin in pinList)
                            {
                                var newLine = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}",
                                                            pin.SenderID,
                                                            pin.SenderName,
                                                            pin.Position.x,
                                                            pin.Position.y,
                                                            pin.Position.z,
                                                            pin.PinType,
                                                            pin.PinName,
                                                            pin.KeepQuiet);
                                writer.WriteLine(newLine);
                            }
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
    }

    // Saves Map Pin Data to disk
    [HarmonyPatch(typeof(Game), nameof(Game.Shutdown))]
    public static class MapPinSave_patch
    {
        private static void Prefix(Game __instance)
        {
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
                        writer.WriteLine("SenderID,SenderName,PositionX,PositionY,PositionZ,PinType,PinName,KeepQuiet");

                        foreach (var pin in pinList)
                        {
                            var newLine = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}",
                                                        pin.SenderID,
                                                        pin.SenderName,
                                                        pin.Position.x,
                                                        pin.Position.y,
                                                        pin.Position.z,
                                                        pin.PinType,
                                                        pin.PinName,
                                                        pin.KeepQuiet);
                            writer.WriteLine(newLine);
                        }
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
    }
}
