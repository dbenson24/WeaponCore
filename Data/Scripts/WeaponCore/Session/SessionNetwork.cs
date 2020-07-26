﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using WeaponCore.Support;
using static WeaponCore.Session;

namespace WeaponCore
{
    public partial class Session
    {
        private void StringReceived(byte[] rawData)
        {
            try
            {
                var message = System.Text.Encoding.UTF8.GetString(rawData, 0, rawData.Length); 
                if (string.IsNullOrEmpty(message)) return;
                var firstChar = message[0];
                int logId;
                if (!int.TryParse(firstChar.ToString(), out logId))
                    return;
                message = message.Substring(1);

                switch (logId) {
                    case 0: {
                        Log.LineShortDate(message);
                        break;
                    }
                    case 1: {
                        Log.LineShortDate(message, "perf");
                        break;
                    }
                    case 2: {
                        Log.LineShortDate(message, "stats");
                        break;
                    }
                    case 3: { 
                        Log.LineShortDate(message, "net");
                        break;
                    }
                    case 4: {
                        Log.LineShortDate(message);
                        break;
                    }
                    default:
                        Log.LineShortDate(message);
                        break;
                }
            }
            catch (Exception ex) { Log.Line($"Exception in StringReceivedPacket: {ex}"); }
        }

        #region NewClientSwitch
        private void ClientReceivedPacket(byte[] rawData)
        {
            try
            {
                var packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(rawData);
                if (packet == null)
                {
                    Log.Line($"ClientReceivedPacket null packet");
                    return;
                }

                var packetSize = rawData.Length;
                var report = Reporter.ReportPool.Get();
                report.Receiver = NetworkReporter.Report.Received.Client;
                report.PacketSize = packetSize;
                Reporter.ReportData[packet.PType].Add(report);
                var packetObj = PacketObjPool.Get();
                packetObj.ErrorPacket.RecievedTick = Tick;
                packetObj.ErrorPacket.Packet = packet;
                packetObj.Packet = packet; packetObj.PacketSize = packetSize; packetObj.Report = report;

                ProccessClientPacket(packetObj);
            }
            catch (Exception ex) { Log.Line($"Exception in ClientReceivedPacket: {ex}"); }
        }

        private bool ProccessClientPacket(PacketObj packetObj, bool firstRun = true)
        {
            try {
                var invalidType = false;
                switch (packetObj.Packet.PType) {

                    case PacketType.FakeTargetUpdate: 
                    {
                            ClientFakeTargetUpdate(packetObj);
                            break;
                    }
                    case PacketType.PlayerIdUpdate: 
                    {
                            ClientPlayerIdUpdate(packetObj); 
                            break;
                    }
                    case PacketType.ClientMouseEvent: 
                    {
                            ClientClientMouseEvent(packetObj);
                            break;
                    }
                    case PacketType.ConstructGroups:
                    {
                        ClientConstructGroups(packetObj);
                        break;
                    }
                    case PacketType.ConstructFoci:
                    {
                        ClientConstructFoci(packetObj);
                        break;
                    }
                    case PacketType.AiData: 
                    {
                        ClientAiDataUpdate(packetObj);
                        break;
                    }
                    case PacketType.CompState:
                    case PacketType.StateReload:
                    case PacketType.StateNoAmmo:
                    {
                        ClientStateUpdate(packetObj);
                        break;
                    }
                    case PacketType.CompData:
                    {
                        ClientCompData(packetObj);
                        break;
                    }
                    case PacketType.RequestSetRof:
                    case PacketType.RequestSetGuidance:
                    case PacketType.RequestSetOverload:
                    case PacketType.RequestSetRange:
                    case PacketType.RequestSetDps:
                    {
                        ClientUpdateSetting(packetObj);
                        break;
                    }
                    case PacketType.TargetChange:
                    {
                        ClientTargetUpdate(packetObj);
                        break;
                    }
                    case PacketType.TargetExpireUpdate: 
                    {
                            ClientTargetExpireUpdate(packetObj);
                            break;
                    }
                    case PacketType.FullMouseUpdate: 
                    {
                            ClientFullMouseUpdate(packetObj);
                            break;
                    }
                    case PacketType.SendSingleShot: 
                    {
                            ClientSendSingleShot(packetObj);
                            break;
                    }
                    case PacketType.ProblemReport: 
                    {
                        ClientSentReport(packetObj);
                        break;
                    }
                    case PacketType.Invalid:
                    {
                        Log.Line($"invalid packet: {packetObj.PacketSize} - {packetObj.Packet.PType}");
                        break;
                    }
                    default:
                        if (!packetObj.ErrorPacket.Retry) Reporter.ReportData[PacketType.Invalid].Add(packetObj.Report);
                        Log.LineShortDate($"        [BadClientPacket] Type:{packetObj.Packet.PType} - Size:{packetObj.PacketSize}", "net");
                        invalidType = true;
                        packetObj.Report.PacketValid = false;
                        break;
                }
                if (!packetObj.Report.PacketValid && !invalidType && !packetObj.ErrorPacket.Retry && !packetObj.ErrorPacket.NoReprocess)
                {
                    if (packetObj.ErrorPacket.PType != PacketType.Invalid)
                    {
                        if (!ClientSideErrorPkt.Contains(packetObj.ErrorPacket))
                            ClientSideErrorPkt.Add(packetObj.ErrorPacket);
                        else
                        {
                            //this only works because hashcode override in ErrorPacket
                            Log.Line($"readding bad packet: {packetObj.ErrorPacket.RetryAttempt}");
                            ClientSideErrorPkt.Remove(packetObj.ErrorPacket);
                            ClientSideErrorPkt.Add(packetObj.ErrorPacket);
                        }
                    }

                    Log.Line($"ErrorPacket has invalid type: packetObjIsNull: {packetObj.Packet == null}");
                }
                else if (packetObj.Report.PacketValid && ClientSideErrorPkt.Contains(packetObj.ErrorPacket))
                    ClientSideErrorPkt.Remove(packetObj.ErrorPacket);

                if (firstRun) 
                    ClientSideErrorPkt.ApplyChanges();

                if (packetObj.Report.PacketValid) {
                    PacketObjPool.Return(packetObj);
                    return true;
                }
                if (!ClientSideErrorPkt.Contains(packetObj.ErrorPacket)) PacketObjPool.Return(packetObj);
            }
            catch (Exception ex) { Log.Line($"Exception in ProccessClientPacket: {ex} - packetSize:{packetObj?.PacketSize} - pObjNull:{packetObj == null} - packetNull:{packetObj?.Packet == null} - error:{packetObj?.ErrorPacket == null} - report:{packetObj?.Report == null}"); }
            return false;
        }
        #endregion

        #region NewServerSwitch
        internal void ProccessClientPacketsForServer()
        {
            if (!IsClient || !MpActive)
            {
                Log.Line($"trying to process client packets on a non-client");
                PacketsToServer.Clear();
                return;
            }

            for (int i = 0; i < PacketsToServer.Count; i++)
                MyModAPIHelper.MyMultiplayer.Static.SendMessageToServer(ServerPacketId, MyAPIGateway.Utilities.SerializeToBinary(PacketsToServer[i]), true);

            PacketsToServer.Clear();
        }

        private void ProccessServerPacket(byte[] rawData)
        {
            var packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(rawData);
            if (packet == null) return;

            var packetSize = rawData.Length;

            var report = Reporter.ReportPool.Get();
            report.Receiver = NetworkReporter.Report.Received.Server;
            report.PacketSize = packetSize;
            Reporter.ReportData[packet.PType].Add(report);

            var packetObj = PacketObjPool.Get();
            packetObj.ErrorPacket.RecievedTick = Tick;
            packetObj.ErrorPacket.Packet = packet;
            packetObj.ErrorPacket.PType = packet.PType;

            packetObj.Packet = packet; packetObj.PacketSize = packetSize; packetObj.Report = report;

            switch (packetObj.Packet.PType) {

                case PacketType.ClientMouseEvent: {
                    ServerClientMouseEvent(packetObj);
                    break;
                }
                case PacketType.ActiveControlUpdate: {
                    ServerActiveControlUpdate(packetObj);
                    break;
                }
                case PacketType.FakeTargetUpdate: {
                    ServerFakeTargetUpdate(packetObj);
                    break;
                }
                case PacketType.AmmoCycleRequest: {
                    ServerAmmoCycleRequest(packetObj);
                    break;
                }
                case PacketType.ReticleUpdate: {
                    ServerReticleUpdate(packetObj);
                    break;
                }
                case PacketType.PlayerControlRequest:
                {
                    ServerPlayerControlRequest(packetObj);
                    break;
                }
                case PacketType.SendSingleShot:
                {
                    ServerSendSingleShot(packetObj);
                    break;
                }
                case PacketType.ClientAiAdd:
                case PacketType.ClientAiRemove: {
                    ServerClientAiExists(packetObj);
                    break;
                }
                case PacketType.OverRidesUpdate: {
                    ServerOverRidesUpdate(packetObj);
                    break;
                }
                case PacketType.RequestMouseStates: {
                    ServerRequestMouseStates(packetObj);
                    break;
                }
                case PacketType.RequestShootUpdate: {
                    ServerRequestShootUpdate(packetObj);
                    break;
                }
                case PacketType.RescanGroupRequest: {
                    ServerRescanGroupRequest(packetObj);
                    break;
                }
                case PacketType.FixedWeaponHitEvent: {
                    ServerFixedWeaponHitEvent(packetObj);
                    break;
                }
                case PacketType.FocusUpdate:
                case PacketType.NextActiveUpdate:
                case PacketType.ReleaseActiveUpdate: {
                    ServerFocusUpdate(packetObj);
                    break;
                }
                case PacketType.ProblemReport: {
                    ServerRequestReport(packetObj);
                    break;
                }
                case PacketType.TerminalMonitor: {
                    ServerTerminalMonitor(packetObj);
                    break;
                }
                default:
                    packetObj.Report.PacketValid = false;
                    Reporter.ReportData[PacketType.Invalid].Add(packetObj.Report);
                    break;
            }

            if (!packetObj.Report.PacketValid)
                Log.LineShortDate(packetObj.ErrorPacket.Error, "net");

            PacketObjPool.Return(packetObj);
        }
        #endregion

        #region ProcessRequests
        internal void ProccessServerPacketsForClients()
        {

            if ((!IsServer || !MpActive))
            {
                Log.Line($"trying to process server packets on a non-server");
                return;
            }

            PacketsToClient.AddRange(PrunedPacketsToClient.Values);
            for (int i = 0; i < PacketsToClient.Count; i++)
            {
                var packetInfo = PacketsToClient[i];
                var bytes = MyAPIGateway.Utilities.SerializeToBinary(packetInfo.Packet);
                if (packetInfo.SingleClient)
                    MyModAPIHelper.MyMultiplayer.Static.SendMessageTo(ClientPacketId, bytes, packetInfo.Packet.SenderId, true);
                else
                {
                    long entityId = packetInfo.Entity?.GetTopMostParent().EntityId ?? -1;

                    foreach (var p in Players.Values)
                    {
                        var notSender = p.SteamUserId != packetInfo.Packet.SenderId;
                        var sendPacket = notSender && packetInfo.Entity == null;
                        if (!sendPacket && notSender)
                        {
                            if (PlayerEntityIdInRange.ContainsKey(p.SteamUserId))
                            {
                                if (PlayerEntityIdInRange[p.SteamUserId].Contains(entityId)) {
                                    sendPacket = true;
                                }
                                else  {
                                    GridAi rootAi;
                                    var grid = packetInfo.Entity.GetTopMostParent() as MyCubeGrid;
                                    if (grid != null && GridToMasterAi.TryGetValue(grid, out rootAi) && PlayerEntityIdInRange[p.SteamUserId].Contains(rootAi.MyGrid.EntityId))
                                        sendPacket = true;
                                }
                            }
                        }

                        if (sendPacket)
                            MyModAPIHelper.MyMultiplayer.Static.SendMessageTo(ClientPacketId, bytes, p.SteamUserId, true);
                    }
                }
            }

            ServerPacketsForClientsClean();
        }

        private void ServerPacketsForClientsClean()
        {
            PacketsToClient.Clear();
            foreach (var pInfo in PrunedPacketsToClient.Values)
            {
                switch (pInfo.Packet.PType)
                {
                    case PacketType.AiData:
                    {
                        var iPacket = (AiDataPacket)pInfo.Packet;
                        PacketAiPool.Return(iPacket);
                        break;
                    }
                    case PacketType.CompData:
                    {
                        var iPacket = (CompDataPacket)pInfo.Packet;
                        PacketCompDataPool.Return(iPacket);
                        break;
                    }
                    case PacketType.CompState:
                    case PacketType.StateReload:
                    case PacketType.StateNoAmmo:
                    {
                        var iPacket = (CompStatePacket)pInfo.Packet;
                        PacketStatePool.Return(iPacket);
                        break;
                    }
                    case PacketType.TargetChange:
                    {
                        var iPacket = (TargetPacket)pInfo.Packet;
                        PacketTargetPool.Return(iPacket);
                        break;
                    }
                    case PacketType.ConstructGroups:
                    {
                        var iPacket = (ConstructGroupsPacket)pInfo.Packet;
                        PacketConstructPool.Return(iPacket);
                        break;
                    }
                    case PacketType.ConstructFoci:
                    {
                        var iPacket = (ConstructFociPacket)pInfo.Packet;
                        PacketConstructFociPool.Return(iPacket);
                        break;
                    }
                }
            }
            PrunedPacketsToClient.Clear();
        }

        #endregion
    }
}
