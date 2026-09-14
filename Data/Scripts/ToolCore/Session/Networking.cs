using ProtoBuf;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using ToolCore.Comp;
using ToolCore.Definitions;
using ToolCore.Utils;
using VRageMath;
using static ToolCore.Comp.ToolComp;

namespace ToolCore.Session
{
    internal class Networking
    {
        internal const ushort ServerPacketId = 65351;
        internal const ushort ClientPacketId = 65352;

        internal readonly ToolSession Session;

        internal Networking(ToolSession session)
        {
            Session = session;
        }

        public void SendPacketToServer(Packet packet)
        {
            var rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);
            MyModAPIHelper.MyMultiplayer.Static.SendMessageToServer(ServerPacketId, rawData, true);
        }

        public void SendPacketToClient(Packet packet, ulong client)
        {
            var rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);
            MyModAPIHelper.MyMultiplayer.Static.SendMessageTo(ClientPacketId, rawData, client, true);
        }

        public void SendPacketToClients(Packet packet, List<ulong> clients, ulong source)
        {
            var rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);

            foreach (var client in clients)
            {
                if (client == source)
                    continue;

                MyModAPIHelper.MyMultiplayer.Static.SendMessageTo(ClientPacketId, rawData, client, true);
            }
        }

        internal void ProcessPacket(ushort id, byte[] rawData, ulong sender, bool reliable)
        {
            try
            {
                var packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(rawData);
                if (packet == null || packet.EntityId != 0 && !Session.ToolMap.ContainsKey(packet.EntityId))
                {
                    Logs.WriteLine($"Invalid packet - null:{packet == null}");
                    return;
                }

                var comp = packet.EntityId == 0 ? null : Session.ToolMap[packet.EntityId];
                switch ((PacketType)packet.PacketType)
                {
                    case PacketType.Replicate:
                        var rPacket = packet as ReplicationPacket;
                        if (rPacket.Add)
                            comp.ReplicatedClients.Add(sender);
                        else
                            comp.ReplicatedClients.Remove(sender);
                        break;
                    case PacketType.Settings:
                        var sPacket = packet as SettingsPacket;
                        Session.LoadSettings(sPacket.Settings);
                        break;
                    case PacketType.Update:
                        var uPacket = packet as UpdatePacket;
                        UpdateComp(uPacket, comp);
                        if (Session.IsServer) SendPacketToClients(uPacket, comp.ReplicatedClients, sender);
                        break;
                    default:
                        Logs.WriteLine($"Invalid packet type - {packet.GetType()}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }

        }

        private void UpdateComp(UpdatePacket packet, ToolComp comp)
        {
            switch ((FieldType)packet.Field)
            {
                case FieldType.Activated:
                    comp.Activated = ((BoolUpdatePacket)packet).Value;
                    break;
                case FieldType.Mode:
                    comp.Mode = (ToolMode)((SbyteUpdatePacket)packet).Value;
                    break;
                case FieldType.Action:
                    comp.Action = (ToolAction)((SbyteUpdatePacket)packet).Value;
                    break;
                case FieldType.Draw:
                    comp.Draw = ((BoolUpdatePacket)packet).Value;
                    break;
                case FieldType.TrackTargets:
                    comp.TrackTargets = ((BoolUpdatePacket)packet).Value;
                    break;
                case FieldType.TargetType:
                    var value = ((SbyteUpdatePacket)packet).Value;
                    var target = (TargetTypes)Math.Abs(value);
                    var on = value > 0;
                    if (on) comp.Targets |= target;
                    else comp.Targets &= ~target;
                    comp.TargetsDirty = true;
                    break;
                case FieldType.UseColour:
                    comp.UseWorkColour = ((BoolUpdatePacket)packet).Value;
                    comp.TargetsDirty = true;
                    break;
                case FieldType.Colour:
                    var packedHsv = ((UintUpdatePacket)packet).Value;
                    var hsv = ColorExtensions.UnpackHSVFromUint(packedHsv);
                    comp.WorkColour = hsv;
                    comp.WorkColourPacked = comp.WorkColour.PackHSVToUint();
                    comp.TargetsDirty = true;
                    break;
                default:
                    break;
            }
        }

        internal void SendServerConfig(ulong steamId)
        {
            var packet = new SettingsPacket()
            {
                EntityId = 0L,
                PacketType = (byte)PacketType.Settings,
                Settings = Session.Settings.CoreSettings
            };
            SendPacketToClient(packet, steamId);
        }

    }

    [ProtoContract]
    [ProtoInclude(4, typeof(UpdatePacket))]
    [ProtoInclude(6, typeof(ReplicationPacket))]
    [ProtoInclude(7, typeof(SettingsPacket))]
    public class Packet
    {
        [ProtoMember(1)] internal long EntityId;
        [ProtoMember(2)] internal byte PacketType;
    }

    [ProtoContract]
    [ProtoInclude(4, typeof(BoolUpdatePacket))]
    [ProtoInclude(6, typeof(SbyteUpdatePacket))]
    [ProtoInclude(7, typeof(UintUpdatePacket))]
    public class UpdatePacket : Packet
    {
        [ProtoMember(1)] internal byte Field;
    }

    [ProtoContract]
    public class BoolUpdatePacket : UpdatePacket
    {
        [ProtoMember(2)] internal bool Value;

        public BoolUpdatePacket(long entityId, FieldType field, bool value)
        {
            EntityId = entityId;
            PacketType = (byte)Session.PacketType.Update;
            Field = (byte)field;
            Value = value;
        }

        public BoolUpdatePacket()
        {

        }
    }

    [ProtoContract]
    public class SbyteUpdatePacket : UpdatePacket
    {
        [ProtoMember(2)] internal sbyte Value;

        public SbyteUpdatePacket(long entityId, FieldType field, int value)
        {
            EntityId = entityId;
            PacketType = (byte)Session.PacketType.Update;
            Field = (byte)field;
            Value = (sbyte)value;
        }

        public SbyteUpdatePacket()
        {

        }
    }

    [ProtoContract]
    public class UintUpdatePacket : UpdatePacket
    {
        [ProtoMember(2)] internal uint Value;

        public UintUpdatePacket(long entityId, FieldType field, uint value)
        {
            EntityId = entityId;
            PacketType = (byte)Session.PacketType.Update;
            Field = (byte)field;
            Value = value;
        }

        public UintUpdatePacket()
        {

        }
    }

    //[ProtoContract]
    //public class ValidationPacket : Packet
    //{
    //    public class DefinitionValues
    //    {
    //        public byte ToolType;
    //        public byte EffectShape;
    //        public byte WorkOrder;
    //        public byte WorkOrigin;

    //        public string Emitter;

    //        public SerializableVector3 Offset;
    //        public SerializableVector3 HalfExtent;

    //        public float Radius;
    //        public float Length;
    //        public float Speed;
    //        public float HarvestRatio;
    //        public float IdlePower;
    //        public float ActivePower;

    //        public ushort WorkRate;
    //        public ushort UpdateInterval;

    //        public bool DamageCharacters;
    //        public bool AffectOwnGrid;
    //        public bool CacheBlocks;
    //    }
    //}

    [ProtoContract]
    public class ReplicationPacket : Packet
    {
        [ProtoMember(1)] internal bool Add;
    }

    [ProtoContract]
    public class SettingsPacket : Packet
    {
        [ProtoMember(1)] internal ToolCoreSettings Settings;
    }

    [ProtoContract]
    public class PCUPacket : Packet
    {
        [ProtoMember(1)] internal long ID;
        [ProtoMember(2)] internal int PCU;
    }

    public enum PacketType : byte
    {
        Replicate,
        Settings,
        Update,
    }

    public enum FieldType : byte
    {
        Invalid = 0,
        Activated = 1,
        Mode = 2,
        Action = 3,
        Draw = 4,
        TrackTargets = 5,
        TargetType = 6,
        UseColour = 7,
        Colour = 8,
    }

}
