using Sandbox.Game.Entities;
using System.Collections.Generic;
using ToolCore.Comp;
using ToolCore.Definitions.Serialised;
using ToolCore.Session;
using ToolCore.Utils;
using VRage;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using static ToolCore.Comp.ToolComp;

namespace ToolCore.Definitions
{
    /// <summary>
    /// Stored values from cubeblock definitions plus precalculated constants
    /// </summary>
    internal class ToolDefinition
    {
        internal readonly ToolType ToolType;
        internal readonly EffectShape EffectShape;
        internal readonly Trigger EventFlags;
        internal readonly WorkOrder Pattern;
        internal Location Location;
        internal readonly string EmitterName;
        internal readonly int Rate;
        internal readonly int UpdateInterval;
        internal readonly float ActivePower;
        internal readonly float IdlePower;
        internal readonly bool DamageCharacters;
        internal readonly bool PickUpFloatings;
        internal readonly bool AffectOwnGrid;
        internal readonly bool CacheBlocks;
        internal readonly bool Debug;
        internal readonly bool IsTurret;
        internal readonly bool? ShowTargetControls;

        internal readonly TurretDefinition Turret;

        internal Vector3D Offset;
        internal BoundingSphereD EffectSphere;
        internal BoundingBox EffectBox;

        internal bool HasMaterialModifiers;

        internal readonly List<ToolMode> ToolModes = new List<ToolMode>();
        internal readonly List<ToolAction> ToolActions = new List<ToolAction>();
        internal readonly Dictionary<ToolAction, ActionDefinition> ActionMap = new Dictionary<ToolAction, ActionDefinition>();
        internal readonly List<List<Vector3I>> Layers = new List<List<Vector3I>>();

        internal readonly List<Trigger> Triggers = new List<Trigger>();
        internal readonly Dictionary<Trigger, MyTuple<List<AnimationDef>, List<ParticleEffectDef>, List<BeamDef>, SoundDef>> EventEffectDefs = new Dictionary<Trigger, MyTuple<List<AnimationDef>, List<ParticleEffectDef>, List<BeamDef>, SoundDef>>();
        internal readonly Dictionary<MyVoxelMaterialDefinition, MaterialModifierDefinition> MaterialModifiers = new Dictionary<MyVoxelMaterialDefinition, MaterialModifierDefinition>();

        private MaterialModifiers[] _tempModifiers;

        internal class TurretDefinition
        {
            internal readonly int TargetRadiusSqr;
            internal readonly float AimingToleranceSqr;

            internal BoundingSphereD TargetSphere;
            internal readonly List<TurretPartDef> Subparts = new List<TurretPartDef>();

            internal TurretDefinition(TurretValues values)
            {
                TargetRadiusSqr = values.TargetRadius * values.TargetRadius;
                TargetSphere = new BoundingSphereD(Vector3D.Zero, values.TargetRadius);

                var aimingTolerance = MathHelper.ToRadians(values.AimingTolerance);
                AimingToleranceSqr = aimingTolerance * aimingTolerance;

                for (int i = 0; i < values.Subparts.Length; i++)
                {
                    var partValues = values.Subparts[i];
                    if (string.IsNullOrEmpty(partValues.Name))
                        continue;

                    var part = new TurretPartDef(partValues);
                    Subparts.Add(part);
                }
            }

            internal class TurretPartDef
            {
                internal readonly string Name;
                internal readonly Direction RotationAxis;
                internal readonly float RotationSpeed;
                internal readonly float MinRotation;
                internal readonly float MaxRotation;
                internal readonly bool RotationCapped;

                internal TurretPartDef(SubpartValues values)
                {
                    Name = values.Name;
                    RotationAxis = values.RotationAxis;
                    var range = values.MaxRotation - values.MinRotation;
                    RotationCapped = range != 0 && range != 360;
                    MinRotation = MathHelper.ToRadians(values.MinRotation);
                    MaxRotation = MathHelper.ToRadians(values.MaxRotation);
                    RotationSpeed = MathHelper.ToRadians(values.RotationSpeed / 60);
                }
            }
        }

        internal class MaterialModifierDefinition
        {
            internal float Speed = 1f;
            internal float HarvestRatio = 1f;

            public MaterialModifierDefinition()
            {
                    
            }

            public MaterialModifierDefinition(MaterialModifiers modifiers)
            {
                Speed = modifiers.SpeedRatio;
                HarvestRatio = modifiers.HarvestRatio;
            }
        }

        internal class ActionDefinition
        {
            internal float Speed;
            internal float HarvestRatio;
            internal bool DestroyVoxels;

            internal Vector3 HalfExtent;
            internal float Radius;
            internal float Length;
            internal float BoundingRadius;

            public ActionDefinition(ActionValues values, float speed, float harvestRatio, bool destroyVoxels, Vector3 half, float radius, float length, float bRadius, EffectShape shape)
            {
                Speed = speed * values.SpeedRatio;
                HarvestRatio = harvestRatio * values.HarvestRatio;
                DestroyVoxels = destroyVoxels;
                HalfExtent = half * values.SizeRatio;
                Radius = radius * values.SizeRatio;
                Length = length;
                BoundingRadius = bRadius;

                if (shape != EffectShape.Line && shape != EffectShape.Ray)
                {
                    Length *= values.SizeRatio;
                    BoundingRadius *= values.SizeRatio;
                }
            }

            public ActionDefinition(float speed, float harvestRatio, bool destroyVoxels, Vector3 half, float radius, float length, float bRadius)
            {
                Speed = speed;
                HarvestRatio = harvestRatio;
                DestroyVoxels = destroyVoxels;
                HalfExtent = half;
                Radius = radius;
                Length = length;
                BoundingRadius = bRadius;
            }
        }

        internal class AnimationDef
        {
            internal readonly string Subpart;
            internal readonly AnimationType Type;
            internal readonly Vector3 Direction;
            internal readonly float Speed;
            internal readonly int Duration;
            internal readonly int WindupTime;
            internal readonly Matrix Transform;
            internal readonly bool IsContinuous;
            internal readonly bool HasWindup;
            internal readonly float WindupFraction;

            internal AnimationDef(Animation animation)
            {
                Subpart = animation.Subpart;
                Type = animation.Type;
                Direction = Vector3.Normalize(animation.Direction);
                Speed = animation.Speed;
                Duration = animation.Duration;
                WindupTime = animation.WindupTime;
                IsContinuous = Duration <= 0;
                HasWindup = WindupTime > 0;

                var speedTicks = Speed / 60f;
                switch (Type)
                {
                    case AnimationType.Rotate:
                        var radsPerTick = MathHelper.ToRadians(speedTicks);
                        Transform = Matrix.CreateFromAxisAngle(Direction, radsPerTick);
                        if (HasWindup) WindupFraction = radsPerTick / WindupTime;
                        break;
                    case AnimationType.Linear:
                        Transform = Matrix.CreateTranslation(Direction * speedTicks);
                        if (HasWindup) WindupFraction = speedTicks / WindupTime;
                        break;
                    default:
                        Transform = Matrix.Zero;
                        if (HasWindup) WindupFraction = 1f / WindupTime;
                        break;

                }
            }
        }

        internal class ParticleEffectDef
        {
            internal readonly string Name;
            internal readonly Location Location;
            internal readonly string Dummy;
            internal readonly Vector3 Offset;
            internal readonly bool Lookup;

            internal readonly Dictionary<MyStringHash, string> ParticleMap;

            public ParticleEffectDef(ParticleEffect particleEffect, ToolSession session)
            {
                Name = particleEffect.Name;
                Location = particleEffect.Location;
                Dummy = particleEffect.Dummy;
                Offset = particleEffect.Offset;

                if (!Name.StartsWith("MaterialProperties/"))
                    return;

                Name = Name.Substring(19);

                Lookup = session.ParticleMap.TryGetValue(Name, out ParticleMap);
                if (!Lookup) Logs.WriteLine("No particle effect map found for material " + Name);
            }
        }

        internal class BeamDef
        {
            internal readonly string Start;
            internal readonly string End;
            internal readonly Location EndLocation;
            internal readonly MyStringId Material;
            internal readonly float Width;
            internal readonly Vector4 Color;
            internal readonly float Length;

            internal BeamDef(Beam beam, float maxLength)
            {
                Start = beam.Start;
                End = beam.End;
                EndLocation = beam.EndLocation;
                Material = MyStringId.GetOrCompute(beam.Material);
                Width = beam.Width;
                Color = beam.Color;
                Length = beam.Length > 0f ? beam.Length : maxLength;
            }
        }

        internal class SoundDef
        {
            internal readonly MySoundPair SoundPair;
            internal readonly string Name;
            internal readonly bool Lookup;

            internal readonly Dictionary<MyStringHash, MySoundPair> SoundMap;

            public SoundDef(Sound sound, ToolSession session)
            {
                Name = sound.Name;

                if (!Name.StartsWith("MaterialProperties/"))
                {
                    SoundPair = new MySoundPair(Name, false);
                    return;
                }

                Name = Name.Substring(19);

                Lookup = session.SoundMap.TryGetValue(Name, out SoundMap);
                if (!Lookup) Logs.WriteLine("No sound map found for material " + Name);
            }
        }

        public ToolDefinition(ToolValues values, ToolSession session)
        {
            ToolType = values.Type > 0 ? values.Type : values.ToolType;

            ToolType.GetModes(ToolModes);
            if (ToolModes.Count == 0)
            {
                Logs.WriteLine($"Tool definition has no valid tool modes!");
                return;
            }

            EffectShape = values.EffectShape;
            Pattern = values.WorkOrder;
            Location = values.WorkOrigin;
            Offset = (Vector3)values.Offset;
            EmitterName = values.Emitter;
            Rate = values.WorkRate > 0 ? values.WorkRate : int.MaxValue;
            UpdateInterval = values.UpdateInterval;
            ActivePower = values.ActivePower;
            IdlePower = values.IdlePower;
            DamageCharacters = values.DamageCharacters;
            PickUpFloatings = values.PickUpFloatings;
            CacheBlocks = values.CacheBlocks;
            AffectOwnGrid = values.AffectOwnGrid;
            Debug = !session.IsDedicated && values.Debug;
            ShowTargetControls = values.ShowTargetControls;

            DefineParameters(values, session);
            IsTurret = DefineTurret(values.Turret, out Turret);
            _tempModifiers = values.MaterialSpecificModifiers;

            EventFlags = DefineEvents(values.Events, session, values.Length);

        }

        private bool DefineTurret(TurretValues values, out TurretDefinition def)
        {
            def = null;
            if (values == null || values.Subparts.Length == 0)
                return false;

            def = new TurretDefinition(values);
            return true;
        }

        private void DefineParameters(ToolValues values, ToolSession session)
        {
            var speed = values.Speed;
            var hRatio = values.HarvestRatio;
            var destroyVoxels = values.DestroyVoxels;

            var halfExtent = (Vector3)values.HalfExtent;
            var radius = values.Radius;
            var length = values.Length;
            var boundingRadius = 0f;

            Vector3I pos = new Vector3I();
            List<Vector3I> layer;
            var tempLayers = new Dictionary<int, List<Vector3I>>();
            switch (EffectShape)
            {
                case EffectShape.Sphere:
                    EffectSphere = new BoundingSphereD(Vector3D.Zero, radius);
                    int radiusInt = 2 * MathHelper.CeilToInt(radius);
                    for (int i = -radiusInt; i <= radiusInt; i++)
                    {
                        pos.X = i;
                        for (int j = -radiusInt; j <= radiusInt; j++)
                        {
                            pos.Y = j;
                            for (int k = -radiusInt; k <= radiusInt; k++)
                            {
                                pos.Z = k;
                                var dist = MathHelper.CeilToInt(((Vector3D)pos).Length());
                                if (dist > (radiusInt + 1)) continue;
                                if (tempLayers.TryGetValue(dist, out layer))
                                    layer.Add(pos);
                                else tempLayers[dist] = new List<Vector3I>() { pos };
                            }
                        }
                    }
                    for (int i = 0; i <= radiusInt + 1; i++)
                    {
                        if (!tempLayers.ContainsKey(i))
                            continue;

                        Layers.Add(tempLayers[i]);
                    }
                    boundingRadius = radius;
                    length = Location == Location.Hit ? length : radius;
                    break;
                case EffectShape.Cylinder:
                    halfExtent = new Vector3(radius, radius, length * 0.5f);
                    EffectBox = new BoundingBox(-halfExtent, halfExtent);
                    EffectSphere = new BoundingSphereD(Vector3D.Zero, halfExtent.Length());
                    boundingRadius = length;
                    break;
                case EffectShape.Cuboid:
                    EffectBox = new BoundingBox(-halfExtent, halfExtent);
                    var halfExtentLength = (float)halfExtent.Length();
                    EffectSphere = new BoundingSphereD(Vector3D.Zero, halfExtentLength);
                    boundingRadius = halfExtentLength;
                    length = halfExtentLength;
                    break;
                case EffectShape.Line:
                    boundingRadius = length * 0.5f; //mm
                    //SegmentLength = radius > 0 ? length * 0.5f / radius : length;
                    halfExtent = new Vector3(radius, radius, radius);
                    break;
                case EffectShape.Ray:
                    break;
            }

            if (values.Actions == null || values.Actions.Length == 0)
            {
                ActionMap.Add(ToolAction.Primary, new ActionDefinition(speed, hRatio, destroyVoxels, halfExtent, radius, length, boundingRadius));
                ToolActions.Add(ToolAction.Primary);
                return;
            }

            foreach (var action in values.Actions)
            {
                var actionValues = new ActionDefinition(action, speed, hRatio, destroyVoxels, halfExtent, radius, length, boundingRadius, EffectShape);
                ActionMap.Add((ToolAction)action.Type, actionValues);
                ToolActions.Add((ToolAction)action.Type);
            }
        }

        internal void DefineMaterialModifiers(ToolSession session)
        {
            var modifiers = _tempModifiers;
            if (modifiers == null || modifiers.Length == 0)
                return;

            HasMaterialModifiers = true;

            var categories = new Dictionary<string, MaterialModifierDefinition>();
            var subtypes = new Dictionary<string, MaterialModifierDefinition>();
            foreach (var modifier in modifiers)
            {
                if (!string.IsNullOrEmpty(modifier.Subtype))
                {
                    subtypes.Add(modifier.Subtype, new MaterialModifierDefinition(modifier));
                }

                if (!string.IsNullOrEmpty(modifier.Category))
                {
                    categories.Add(modifier.Category, new MaterialModifierDefinition(modifier));
                }
            }

            foreach (var matList in session.MaterialCategoryMap)
            {
                MaterialModifierDefinition mods;
                if (!categories.TryGetValue(matList.Key, out mods))
                    mods = new MaterialModifierDefinition();

                foreach (var mat in matList.Value)
                {
                    MaterialModifierDefinition subtypeMods;
                    if (subtypes.TryGetValue(mat.Id.SubtypeName, out subtypeMods))
                        MaterialModifiers.Add(mat, subtypeMods);

                    MaterialModifiers.Add(mat, mods);
                }
            }
        }

        private Trigger DefineEvents(Event[] events, ToolSession session, float maxLength)
        {
            if (events == null || events.Length == 0)
                return Trigger.None;

            Trigger flags = 0;
            foreach (var eventDef in events)
            {
                var hasAnimations = eventDef.Animations != null && eventDef.Animations.Length > 0;
                var hasParticleEffects = eventDef.ParticleEffects != null && eventDef.ParticleEffects.Length > 0;
                var hasBeams = eventDef.Beams != null && eventDef.Beams.Length > 0;
                var hasSound = !string.IsNullOrEmpty(eventDef.Sound?.Name);
                if (!hasAnimations && !hasParticleEffects && !hasBeams && !hasSound)
                    continue;

                var animationDefs = new List<AnimationDef>();
                if (hasAnimations)
                {
                    foreach (var animation in eventDef.Animations)
                    {
                        animationDefs.Add(new AnimationDef(animation));
                    }
                }

                var particleEffectDefs = new List<ParticleEffectDef>();
                if (hasParticleEffects)
                {
                    foreach (var particleEffect in eventDef.ParticleEffects)
                    {
                        particleEffectDefs.Add(new ParticleEffectDef(particleEffect, session));
                    }
                }

                var beamDefs = new List<BeamDef>();
                if (hasBeams)
                {
                    foreach (var beam in eventDef.Beams)
                    {
                        beamDefs.Add(new BeamDef(beam, maxLength));
                    }
                }

                SoundDef soundDef = null;
                if (hasSound)
                    soundDef = new SoundDef(eventDef.Sound, session);

                var value = new MyTuple<List<AnimationDef>, List<ParticleEffectDef>, List<BeamDef>, SoundDef>(animationDefs, particleEffectDefs, beamDefs, soundDef);
                EventEffectDefs[eventDef.Trigger] = value;

                if (!Triggers.Contains(eventDef.Trigger))
                    Triggers.Add(eventDef.Trigger);

                flags |= eventDef.Trigger;
            }

            return flags;
        }

    }
}
