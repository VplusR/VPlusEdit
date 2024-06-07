using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using ValheimPlus.GameClasses;

namespace ValheimPlus.RPC
{
    public class VPlusMapPinSync
    {
        /// <summary>
		/// Sync Pin with clients via the server
        /// </summary>
        public static void RPC_VPlusMapAddPin(long sender, ZPackage mapPinPkg)
        {
            if (ZNet.m_isServer) //Server
            {
                if (mapPinPkg == null) return;

                if (sender == ZRoutedRpc.instance.GetServerPeerID())
                {
                    // should append to sharedMapPins
                    List<MapPinData> pinList = new List<MapPinData>();  
                    
                    int pinCount = mapPinPkg.ReadInt();
                    for (int i = 0; i < pinCount; i++)
                    {
                        long senderID = mapPinPkg.ReadLong();
                        string senderName = mapPinPkg.ReadString();
                        float posX = mapPinPkg.ReadSingle();
                        float posY = mapPinPkg.ReadSingle();
                        float posZ = mapPinPkg.ReadSingle();
                        int pinType = mapPinPkg.ReadInt();
                        string pinName = mapPinPkg.ReadString();
                        bool keepQuiet = mapPinPkg.ReadBool();

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

                    try
                    {
                        using (StreamWriter writer = new StreamWriter(ValheimPlus.GameClasses.Game_Start_Patch.PinDataFilePath, true, Encoding.UTF8))
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

                foreach(ZNetPeer peer in ZRoutedRpc.instance.m_peers)
                {
                    if(peer.m_uid != sender)
                        ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "VPlusMapAddPin", new object[] { mapPinPkg });
                }

                ValheimPlusPlugin.Logger.LogInfo($"Sent map pin to all clients");
                //VPlusAck.SendAck(sender);
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
                string senderName = mapPinPkg.ReadString();
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
                }
                //Send Ack
                //VPlusAck.SendAck(sender);
            }
        }

        /// <summary>
		/// Send the pin, attach client ID
        /// </summary>
        public static void SendMapPinToServer(Minimap.PinData pinData, bool keepQuiet = false)
        {
            ValheimPlusPlugin.Logger.LogInfo("-------------------- SENDING VPLUS MapPin DATA");
            ZPackage pkg = new ZPackage();

            pkg.Write(ZRoutedRpc.instance.m_id); // Sender ID
            if(keepQuiet)
                pkg.Write(""); // Loaded in
            else
                pkg.Write(Player.m_localPlayer.GetPlayerName()); // Sender Name
            pkg.Write(pinData.m_pos); // Pin position
            pkg.Write((int)pinData.m_type); // Pin type
            pkg.Write(pinData.m_name); // Pin name
            pkg.Write(keepQuiet); // Don't shout

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "VPlusMapAddPin", new object[] { pkg });

            ValheimPlusPlugin.Logger.LogInfo($"Sent map pin {pinData.m_name} to the server");

        }
    }
}
