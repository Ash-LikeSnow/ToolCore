using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using ToolCore.Definitions;
using ToolCore.Definitions.Serialised;
using ToolCore.Session;
using ToolCore.Utils;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using static ToolCore.Comp.ToolComp;
using static ToolCore.Definitions.ToolDefinition;

namespace ToolCore.Comp
{
    internal class TurretComp
    {
        internal readonly List<IMySlimBlock> Targets = new List<IMySlimBlock>();

        internal readonly ToolComp Comp;
        internal readonly TurretDefinition Definition;
        internal readonly TurretPart Part1;
        internal readonly TurretPart Part2;

        internal readonly int UpdateTick;
        internal readonly bool HasTwoParts = true;

        internal IMySlimBlock ActiveTarget;
        internal bool HasTarget;
        internal bool HadTarget;
        internal bool Aligned;
        internal bool IsValid;
        internal int LastRefreshTick;
        internal int LastTargetTick = int.MaxValue;

        internal void UpdateModelData(ToolComp comp)
        {
            IsValid = Part1.UpdateModelData(comp) && (!HasTwoParts || Part2.UpdateModelData(comp));
        }

        internal TurretComp(ToolDefinition def, ToolComp comp)
        {
            Comp = comp;
            Definition = def.Turret;

            var subparts = def.Turret.Subparts;
            var partDefA = subparts[0];
            TurretPart partA;
            var partAValid = SetupPart(partDefA, out partA);
            if (subparts.Count == 1)
            {
                Part1 = partA;
                IsValid = partAValid;
                HasTwoParts = false;
                return;
            }

            var partDefB = subparts[1];
            TurretPart partB;
            var partBValid = SetupPart(partDefB, out partB);
            if (!partAValid || !partBValid)
            {
                IsValid = false;
                return;
            }

            MyEntitySubpart _;

            if (partA.Subpart.TryGetSubpartRecursive(partB.Definition.Name, out _))
            {
                Part1 = partA;
                Part2 = partB;
                IsValid = true;
                return;
            }

            if (partB.Subpart.TryGetSubpartRecursive(partA.Definition.Name, out _))
            {
                subparts.Move(1, 0);

                Part1 = partB;
                Part2 = partA;
                IsValid = true;
                return;
            }

            Logs.WriteLine("Neither specified turret subpart is a child of the other!");
            IsValid = false;

        }

        private bool SetupPart(TurretDefinition.TurretPartDef partDef, out TurretPart part)
        {
            part = null;
            MyEntitySubpart subpart;
            if (!Comp.ToolEntity.TryGetSubpartRecursive(partDef.Name, out subpart))
            {
                Logs.WriteLine($"Failed to find turret subpart {partDef.Name}");
                return false;
            }

            part = new TurretPart(partDef);
            part.Subpart = subpart;
            part.Parent = subpart.Parent;
            MyEntity _;
            part.Parent.TryGetDummy("subpart_" + partDef.Name, out part.Empty, out _);
            part.Axis = part.Empty.Matrix.Forward;

            return true;
        }

        internal bool TrackTarget()
        {
            var target = ActiveTarget;
            var targetWorld = target.CubeGrid.GridIntegerToWorld(target.Position);

            Vector3D targetLocal1;
            var parentMatrixNI1 = Part1.Parent.PositionComp.WorldMatrixNormalizedInv;
            Vector3D.Transform(ref targetWorld, ref parentMatrixNI1, out targetLocal1);
            var targetVector1 = targetLocal1 - Part1.Subpart.PositionComp.LocalMatrixRef.Translation;

            var desiredFacing1 = (Vector3)Vector3D.ProjectOnPlane(ref targetVector1, ref Part1.Axis);
            Part1.DesiredFacing = desiredFacing1;
            var desiredAngle1 = (float)Vector3.Angle(desiredFacing1, Part1.Facing) * Math.Sign(Vector3.Dot(desiredFacing1, Part1.Normal));
            if (Part1.Definition.RotationCapped && (desiredAngle1 > Part1.Definition.MaxRotation || desiredAngle1 < Part1.Definition.MinRotation))
            {
                Part1.OutOfBounds = true;
                return false;
            }

            if (HasTwoParts)
            {
                Vector3D targetLocal2;
                var parentMatrixNI2 = Part2.Parent.PositionComp.WorldMatrixNormalizedInv;
                Vector3D.Transform(ref targetWorld, ref parentMatrixNI2, out targetLocal2);
                var targetVector2 = targetLocal2 - Part2.Subpart.PositionComp.LocalMatrixRef.Translation;

                var part1AngleDiff = desiredAngle1 - Part1.CurrentRotation;
                var finalFacing = ((Vector3D)Part2.Facing).Rotate(Part2.Normal, part1AngleDiff);
                var finalAxis = ((Vector3D)Part2.Axis).Rotate(Part2.Normal, part1AngleDiff);

                var desiredFacing2 = (Vector3)Vector3D.ProjectOnPlane(ref targetVector2, ref finalAxis);
                Part2.DesiredFacing = desiredFacing2;
                var desiredAngle2 = (float)Vector3.Angle(desiredFacing2, finalFacing) * Math.Sign(Vector3.Dot(desiredFacing2, Part2.Normal));
                if (Part2.Definition.RotationCapped && (desiredAngle2 > Part2.Definition.MaxRotation || desiredAngle2 < Part2.Definition.MinRotation))
                {
                    Part2.OutOfBounds = true;
                    return false;
                }
                Part2.OutOfBounds = false;
                Part2.DesiredRotation = desiredAngle2;
            }
            Part1.OutOfBounds = false;
            Part1.DesiredRotation = desiredAngle1;

            return true;
        }

        internal void DeselectTarget()
        {
            HadTarget = HasTarget;
            HasTarget = false;

            if (Comp.IsBlock) Comp.RefreshTerminal();
            //Logs.WriteLine("Deselecting target");
        }

        internal void GoHome()
        {
            Part1.DesiredRotation = 0;
            if (HasTwoParts) Part2.DesiredRotation = 0;
        }

        internal void SelectNewTarget(Vector3D worldPos)
        {
            //Logs.WriteLine($"Selecting from {Targets.Count} targets");
            for (int i = Targets.Count - 1; i >= 0; i--)
            {
                var next = Targets[i];
                Targets.RemoveAt(i);

                var projector = ((MyCubeGrid)next.CubeGrid).Projector as IMyProjector;

                var closing = next.CubeGrid.MarkedForClose || next.FatBlock != null && next.FatBlock.MarkedForClose;
                var finished = next.IsFullyDismounted || Comp.Mode == ToolMode.Weld && projector == null && next.IsFullIntegrity && !next.HasDeformation;
                var outOfRange = Vector3D.DistanceSquared(next.CubeGrid.GridIntegerToWorld(next.Position), worldPos) > Definition.TargetRadiusSqr;
                if (closing || finished || outOfRange || (projector != null && projector.CanBuild(next, true) != BuildCheckResult.OK))
                {
                    continue;
                }

                ActiveTarget = next;
                if (false && !TrackTarget())
                    continue;
                HadTarget = HasTarget;
                HasTarget = true;
                if (Comp.IsBlock) Comp.RefreshTerminal();

                var target = ActiveTarget.FatBlock?.DisplayNameText ?? ActiveTarget.BlockDefinition.DisplayNameText;
                //Logs.WriteLine($"Targeting " + target);
                return;
            }

            HadTarget = false;
        }

        internal void RefreshTargetList(ToolDefinition def, Vector3D worldPos)
        {
            Targets.Clear();
            //Comp.GridData.Clean(Comp);
            //Logs.WriteLine("Refreshing target list");

            var ownerId = Comp.IsBlock ? Comp.BlockTool.OwnerId : Comp.HandTool.OwnerIdentityId;
            var toolFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);

            var gridData = Comp.GridData;
            var turretDef = def.Turret;
            turretDef.TargetSphere.Center = worldPos;

            var session = ToolSession.Instance;
            var entities = session.Entities;
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref turretDef.TargetSphere, entities);
            foreach (var entity in entities)
            {
                if (!(entity is MyCubeGrid))
                    continue;

                var grid = entity as MyCubeGrid;

                if (!grid.Editable)
                    continue;

                if (Comp.IsBlock && !def.AffectOwnGrid && (grid == Comp.Grid || Comp.GridComp.GroupMap.ConnectedGrids.Contains(grid)))
                    continue;

                var weldMode = Comp.Mode == ToolMode.Weld;
                var projector = grid.Projector as IMyProjector;
                if (projector == null)
                {
                    if (grid.IsPreview)
                        continue;
                }
                else if (!weldMode || projector.BuildableBlocksCount == 0)
                {
                    continue;
                }

                if (!weldMode && (grid.Immune || !grid.DestructibleBlocks || grid.Physics == null || !grid.Physics.Enabled))
                    continue;

                if (session.DSAPIReady)
                {
                    var shieldBlock = session.DSAPI.MatchEntToShieldFast(entity, true);
                    if (shieldBlock != null)
                    {
                        var relation = Comp.GetRelationToPlayer(shieldBlock.OwnerId, toolFaction);
                        if (relation > TargetTypes.Friendly)
                            continue;
                    }
                }

                if (Comp.HasTargetControls)
                {
                    var gridOwner = grid.Projector?.OwnerId ?? grid.BigOwners.FirstOrDefault();
                    var relation = Comp.GetRelationToPlayer(gridOwner, toolFaction);
                    if ((relation & Comp.Targets) == TargetTypes.None)
                        continue;
                }

                gridData.Grids.Add(grid);
            }
            entities.Clear();

            if (gridData.Grids.Count == 0)
                return;

            gridData.Position = worldPos;
            Comp.GridsTask = MyAPIGateway.Parallel.Start(Comp.GetBlockTargets, Comp.OnGetBlockTargetsComplete);
            Comp.LastGridsTaskTick = ToolSession.Tick;
        }

        internal class TurretPart
        {
            internal readonly TurretDefinition.TurretPartDef Definition;

            internal Func<float, Matrix> RotationFactory;

            internal MyEntitySubpart Subpart;
            internal MyEntity Parent;
            internal IMyModelDummy Empty;
            internal Vector3 Position;
            internal Vector3D Axis;
            internal Vector3 Facing;
            internal Vector3 Normal;

            internal Vector3 DesiredFacing;

            internal float CurrentRotation;
            internal float DesiredRotation;
            internal float VisualRotation;
            internal bool OutOfBounds;

            internal TurretPart(TurretDefinition.TurretPartDef def)
            {
                Definition = def;
            }

            internal bool UpdateModelData(ToolComp comp)
            {
                if (!comp.Subparts.TryGetValue(Definition.Name, out Subpart))
                    return false;

                if (!comp.Dummies.TryGetValue("subpart_" + Definition.Name, out Empty))
                    return false;

                Parent = comp.DummyMap[Empty];

                Position = Empty.Matrix.Translation;

                switch (Definition.RotationAxis)
                {
                    case Direction.Up:
                        RotationFactory = Matrix.CreateRotationY;
                        Axis = Subpart.PositionComp.LocalMatrixRef.Up;
                        Facing = Subpart.PositionComp.LocalMatrixRef.Forward;
                        Normal = Subpart.PositionComp.LocalMatrixRef.Left;
                        break;
                    case Direction.Down:
                        RotationFactory = Matrix.CreateRotationY;
                        Axis = Subpart.PositionComp.LocalMatrixRef.Down;
                        Facing = Subpart.PositionComp.LocalMatrixRef.Backward;
                        Normal = Subpart.PositionComp.LocalMatrixRef.Left;
                        break;
                    case Direction.Forward:
                        RotationFactory = Matrix.CreateRotationZ;
                        Axis = Subpart.PositionComp.LocalMatrixRef.Forward;
                        Facing = Subpart.PositionComp.LocalMatrixRef.Up;
                        Normal = Subpart.PositionComp.LocalMatrixRef.Right;
                        break;
                    case Direction.Back:
                        RotationFactory = Matrix.CreateRotationZ;
                        Axis = Subpart.PositionComp.LocalMatrixRef.Backward;
                        Facing = Subpart.PositionComp.LocalMatrixRef.Down;
                        Normal = Subpart.PositionComp.LocalMatrixRef.Right;
                        break;
                    case Direction.Left:
                        RotationFactory = Matrix.CreateRotationX;
                        Axis = Subpart.PositionComp.LocalMatrixRef.Left;
                        Facing = Subpart.PositionComp.LocalMatrixRef.Backward;
                        Normal = Subpart.PositionComp.LocalMatrixRef.Up;
                        break;
                    case Direction.Right:
                        RotationFactory = Matrix.CreateRotationX;
                        Axis = Subpart.PositionComp.LocalMatrixRef.Right;
                        Facing = Subpart.PositionComp.LocalMatrixRef.Forward;
                        Normal = Subpart.PositionComp.LocalMatrixRef.Up;
                        break;
                }

                DesiredRotation = 0f;
                CurrentRotation = 0f;
                VisualRotation = 0f;

                return true;
            }
        }
    }

}
