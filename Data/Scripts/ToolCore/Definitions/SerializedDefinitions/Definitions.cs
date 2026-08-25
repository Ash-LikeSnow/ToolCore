using Sandbox.Definitions;
using System;
using System.Xml.Serialization;
using VRage;
using VRage.ObjectBuilders;
using VRageMath;

namespace ToolCore.Definitions.Serialised
{
    #region Definitions

    /// <summary>
    /// Fields which will be deserialised from xml
    /// </summary>
    public class ToolValues
    {
        [XmlAttribute]
        public ToolType Type;

        public ToolType ToolType;

        public EffectShape EffectShape;
        public WorkOrder WorkOrder;
        public Location WorkOrigin;

        public string Emitter;

        public SerializableVector3 Offset = Vector3.Zero;

        public SerializableVector3 HalfExtent = new Vector3(1f, 1f, 1f); //Cuboid
        public float Radius = 1f; //Sphere, Cylinder
        public float Length = 1f; //Cylinder, Line
        public float Speed = 1f;
        public float HarvestRatio = 1f;
        public bool DestroyVoxels = true;
        public int WorkRate = int.MaxValue;
        public int UpdateInterval = 20;


        public float IdlePower = 0f;
        public float ActivePower = 1f;
        public float NonFriendlyMult = 0f;
        public float NPCMult = 0f;
        public bool GrindToWaste = false;
        public bool DamageCharacters = true;
        public bool PickUpFloatings = false;
        public bool AffectOwnGrid = false;
        public bool CacheBlocks = false;
        public bool? ShowTargetControls;

        public bool Debug = false;

        public TurretValues Turret;

        [XmlElement("Action")]
        public ActionValues[] Actions;

        [XmlElement("Event")]
        public Event[] Events;

        [XmlArrayItem("Material")]
        public MaterialModifiers[] MaterialSpecificModifiers;
    }

    public class ActionValues
    {
        [XmlAttribute]
        public ActionType Type = ActionType.Primary;
        [XmlAttribute]
        public float SizeRatio = 1f;
        [XmlAttribute]
        public float SpeedRatio = 1f;
        [XmlAttribute]
        public float HarvestRatio = 1f;
    }

    public class Event
    {
        [XmlAttribute]
        public Trigger Trigger;
        [XmlElement("Animation")]
        public Animation[] Animations;
        [XmlElement("ParticleEffect")]
        public ParticleEffect[] ParticleEffects;
        [XmlElement("Beam")]
        public Beam[] Beams;
        public Sound Sound;

    }

    public class Animation
    {
        public string Subpart;
        public AnimationType Type;
        public SerializableVector3 Direction = Vector3.Zero;
        public float Speed = 1f; //(degrees/metres) per second
        public int Duration = 0; //ticks
        public int WindupTime = 0; //ticks
    }

    public class ParticleEffect
    {
        public string Name;
        public Location Location;
        public string Dummy;
        public SerializableVector3 Offset = Vector3.Zero;
        public bool Loop; //deprecate?
    }

    public class Beam
    {
        public string Start;
        public string End;
        public Location EndLocation;
        public string Material = "WeaponLaser";
        public float Width = 0.5f;
        public float Length = 0f;
        public Vector4 Color = new Vector4(25, 25, 25, 1);
    }

    public class Sound
    {
        public string Name;
    }

    public class MaterialModifiers
    {
        [XmlAttribute]
        public string Category;
        [XmlAttribute]
        public string Subtype;
        [XmlAttribute]
        public float SpeedRatio = 1f;
        [XmlAttribute]
        public float HarvestRatio = 1f;
    }

    public class TurretValues
    {
        public int TargetRadius = 100;
        public float AimingTolerance = 1f;
        [XmlElement("Subpart")]
        public SubpartValues[] Subparts;
    }

    public class SubpartValues
    {
        public string Name;
        public Direction RotationAxis; // Up | Forward | Right
        public float RotationSpeed = 60f; // degrees per second
        public int MinRotation; // degrees
        public int MaxRotation; // degrees
    }

    #endregion

    #region enums

    [Flags]
    public enum ToolType
    {
        None = 0,
        Drill = 1,
        Grind = 2,
        //GrindDrill = 3,
        Weld = 4,
        //WeldDrill = 5,
        //WeldGrind = 6,
        //Multi = 7,
    }

    public enum EffectShape
    {
        Sphere = 0,
        Cylinder = 1,
        Cuboid = 2,
        Line = 3,
        Ray = 4,
    }

    public enum WorkOrder
    {
        Uniform = 0,
        InsideOut = 1,
        OutsideIn = 2,
        Forward = 3,
        Backward = 4,
    }

    public enum Location
    {
        Centre = 0,
        Emitter = 1,
        Hit = 2,
        Forward = 3,
        Parent = 4,
    }

    public enum ActionType
    {
        Primary = 0,
        Secondary = 1,
        Tertiary = 2,
    }

    [Flags]
    public enum Trigger
    {
        None = 0,
        Functional = 1,
        Powered = 2,
        Enabled = 4,
        Activated = 8,
        LeftClick = 16,
        RightClick = 32,
        Click = 48,
        Firing = 56,
        Hit = 64,
        RayHit = 128,
    }

    public enum AnimationType
    {
        Rotate = 0,
        Linear = 1,
        Hide = 2,
        Unhide = 3,
    }

    public enum Direction
    {
        Up = 0,
        Down = 1,
        Forward = 2,
        Back = 3,
        Left = 4,
        Right = 5,
    }

    #endregion

    /// <summary>
    /// Skeleton classes for deserialising from xml
    /// </summary>
    public class Definitions
    {
        public Definition[] CubeBlocks;
        [XmlArrayItem("HandItem")]
        public HandItemDefinition[] HandItems;
    }

    public class HandItemDefinition : Definition { }

    public class Definition
    {
        public SerializableDefinitionId Id;
        [XmlElement("ToolValues")]
        public ToolValues[] ToolValues;

    }
}
