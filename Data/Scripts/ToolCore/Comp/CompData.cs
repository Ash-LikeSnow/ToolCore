using ProtoBuf;
using VRageMath;

namespace ToolCore.Comp
{
    [ProtoContract]
    internal class ToolRepo
    {
        [ProtoMember(1)] public bool Activated;
        [ProtoMember(2)] public bool Draw;
        [ProtoMember(3)] public byte Mode;
        [ProtoMember(4)] public byte Action;
        [ProtoMember(5)] public byte Targets;
        [ProtoMember(6)] public bool UseWorkColour;
        [ProtoMember(7)] public Vector3 WorkColour;
        [ProtoMember(8)] public bool TrackTargets;

        internal void Sync(ToolComp comp)
        {
            Activated = comp._activated;
            Draw = comp.Draw;
            Mode = (byte)comp.Mode;
            Action = (byte)comp.Action;
            Targets = (byte)comp.Targets;
            UseWorkColour = comp.UseWorkColour;
            WorkColour = comp.WorkColour;
            TrackTargets = comp._trackTargets;
        }
    }
}
