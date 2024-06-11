using BepInEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.PeerToPeer.Collaboration;
using System.Text;
using UnityEngine;
using ValheimPlus.GameClasses;
using static Minimap;

namespace ValheimPlus.RPC
{
    public class VPlusMapPinSync
    {
        public static bool ShouldSyncOnSpawn = true;

        /// <summary>
		/// Sync Pin with clients via the server
        /// </summary>
        public static void RPC_VPlusMapAddPin(long sender, ZPackage mapPinPkg)
        {            
            if (ZNet.m_isServer) // Server
            {
                int count = ValheimPlus.GameClasses.Game_Start_Patch.storedMapPins.Count;
                ValheimPlusPlugin.Logger.LogInfo($"storedMapPins has {count} in it.");

                if (mapPinPkg == null)
                {
                    ValheimPlusPlugin.Logger.LogInfo("Map Package is null.");
                    return;
                }

                ValheimPlusPlugin.Logger.LogInfo($"Sender: {sender} and Server: {ZRoutedRpc.instance.GetServerPeerID()} received pin data. {ZNet.m_isServer}");
                                  
                // Should append to sharedMapPins
                List<MapPinData> pinList = new List<MapPinData>();

                ValheimPlusPlugin.Logger.LogInfo($"Map Package Position: {mapPinPkg.GetPos()} Map Package Size: {mapPinPkg.Size()}");

                while (mapPinPkg.GetPos() < mapPinPkg.Size())
                {
                    long senderID = mapPinPkg.ReadLong();
                    string senderName = mapPinPkg.ReadString();
                    Vector3 pos = mapPinPkg.ReadVector3();
                    int pinType = mapPinPkg.ReadInt();
                    string pinName = mapPinPkg.ReadString();
                    bool keepQuiet = mapPinPkg.ReadBool();

                    ValheimPlusPlugin.Logger.LogInfo($"SenderID: {senderID}");
                    ValheimPlusPlugin.Logger.LogInfo($"SenderName: {senderName}");
                    ValheimPlusPlugin.Logger.LogInfo($"Position X, Y, Z: {pos.x}, {pos.y}, {pos.z}");
                    ValheimPlusPlugin.Logger.LogInfo($"Pin Type: {pinType}");
                    ValheimPlusPlugin.Logger.LogInfo($"Pin Name: {pinName}");
                    ValheimPlusPlugin.Logger.LogInfo($"Keep Quiet: {keepQuiet}");

                    MapPinData pinData = new MapPinData
                    {
                        SenderID = senderID,
                        SenderName = senderName,
                        Position = pos,
                        PinType = pinType,
                        PinName = pinName,
                        KeepQuiet = keepQuiet
                    };

                    // Generate unique ID for the pin based on coordinates
                    string uniqueID = pinData.GetUniqueID();

                    // Check if the pin already exists in storedMapPins
                    bool exists = ValheimPlus.GameClasses.Game_Start_Patch.storedMapPins.Any(pkg =>
                    {
                        // Reset the read position to the start of the package for accurate reading
                        pkg.SetPos(0);
                        pkg.ReadLong(); // Skip senderID
                        pkg.ReadString(); // Skip senderName
                        Vector3 storedPos = pkg.ReadVector3();
                        return $"{storedPos.x}-{storedPos.y}-{storedPos.z}" == uniqueID;
                    });

                    if (!exists)
                    {
                        pinList.Add(pinData);
                        ZPackage newPkg = new ZPackage();
                        newPkg.Write(pinData.SenderID);
                        newPkg.Write(pinData.SenderName);
                        newPkg.Write(pinData.Position);
                        newPkg.Write(pinData.PinType);
                        newPkg.Write(pinData.PinName);
                        newPkg.Write(pinData.KeepQuiet);

                        ValheimPlus.GameClasses.Game_Start_Patch.storedMapPins.Add(newPkg);
                        
                    }
                }

                try
                {
                    using (StreamWriter writer = new StreamWriter(ValheimPlus.GameClasses.Game_Start_Patch.PinDataFilePath, true, Encoding.UTF8))
                    {
                        foreach (var pin in pinList)
                        {
                            string newLine = $"{pin.SenderID},{pin.SenderName},{pin.Position.x},{pin.Position.y},{pin.Position.z},{pin.PinType},{pin.PinName},{pin.KeepQuiet}";
                            writer.WriteLine(newLine);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Handle exceptions (e.g., logging)
                    ValheimPlusPlugin.Logger.LogInfo("An error occurred while saving pins: " + ex.Message);
                }

                foreach (ZNetPeer peer in ZRoutedRpc.instance.m_peers)
                {
                    if (peer.m_uid != sender)
                        ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "VPlusMapAddPin", new object[] { mapPinPkg });
                }

                ValheimPlusPlugin.Logger.LogInfo("Sent map pin to all clients.");
                ValheimPlusPlugin.Logger.LogInfo($"storedMapPins has {count} in it.");

            }
            else //Client
            {
                if (sender != ZRoutedRpc.instance.GetServerPeerID()) return; //Only bother if it's from the server.

                if (mapPinPkg == null)
                {
                    ValheimPlusPlugin.Logger.LogWarning("Warning: Got empty map pin package from server.");
                    return;
                }    

                long pinSender = mapPinPkg.ReadLong();
                string senderName = mapPinPkg.ReadString();  // problem child

                if (senderName.IsNullOrWhiteSpace())
                { 
                    senderName = "None";
                }

                ValheimPlusPlugin.Logger.LogInfo($"SenderName: {senderName}");                

                if (senderName != Player.m_localPlayer.GetPlayerName() && pinSender != ZRoutedRpc.instance.m_id)
                {
                    ValheimPlusPlugin.Logger.LogInfo("Checking sent pin");
                    Vector3 pinPos = mapPinPkg.ReadVector3();
                    int pinType = mapPinPkg.ReadInt();
                    string pinName = mapPinPkg.ReadString();
                    bool keepQuiet = mapPinPkg.ReadBool();
                    if (!Minimap.instance.HaveSimilarPin(pinPos, (Minimap.PinType)pinType, pinName, true))
                    {
                        Minimap.PinData addedPin = Minimap.instance.AddPin(pinPos, (Minimap.PinType)pinType, pinName, true, false);
                        if(!keepQuiet)
                            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Received map pin {pinName} from {senderName}!",
                            0, Minimap.instance.GetSprite((Minimap.PinType)pinType));
                        ValheimPlusPlugin.Logger.LogInfo($"I got pin named {pinName} from {senderName}!");
                    }

                    ValheimPlusPlugin.Logger.LogInfo($"SenderID: {pinSender}");
                    ValheimPlusPlugin.Logger.LogInfo($"SenderName: {senderName}");
                    ValheimPlusPlugin.Logger.LogInfo($"Position X, Y, Z: {pinPos}");
                    ValheimPlusPlugin.Logger.LogInfo($"Pin Type: {pinType}");
                    ValheimPlusPlugin.Logger.LogInfo($"Pin Name: {pinName}");
                    ValheimPlusPlugin.Logger.LogInfo($"Keep Quiet: {keepQuiet}");
                }
                //Send Ack
                //VPlusAck.SendAck(sender);
            }
        }

        /// <summary>
		/// Send the pin, attach client ID
        /// </summary>
        public static void SendMapPinToServer(Vector3 pos, Minimap.PinType type, string name, bool keepQuiet = false)
        {
            ValheimPlusPlugin.Logger.LogInfo("-------------------- SENDING VPLUS MapPin DATA");
            ZPackage pkg = new ZPackage();

            pkg.Write(ZRoutedRpc.instance.m_id); // Sender ID

            if (keepQuiet)
            {
                pkg.Write(""); // when true, loads blank name to prevent shouting
            }
            
            if (!keepQuiet)
            {
                pkg.Write(Player.m_localPlayer.GetPlayerName()); // Sender Name
            }

            pkg.Write(pos); // Pin position
            pkg.Write((int)type); // Pin type
            pkg.Write(name); // Pin name
            pkg.Write(keepQuiet); // Don't shout

            ValheimPlusPlugin.Logger.LogInfo($"Sent map pin {name} to the server");
            ValheimPlusPlugin.Logger.LogInfo($"PlayerID: {ZRoutedRpc.instance.m_id}");
            ValheimPlusPlugin.Logger.LogInfo($"ServerID: {ZRoutedRpc.instance.GetServerPeerID()}");

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "VPlusMapAddPin", new object[] { pkg });
        }    
    }
}
