using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using ToolCore.Comp;
using ToolCore.Definitions;
using ToolCore.Definitions.Serialised;
using ToolCore.Utils;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using static ToolCore.Comp.ToolComp;
using static ToolCore.Utils.Draw;
using static ToolCore.Utils.Utils;

namespace ToolCore.Session
{
    internal partial class ToolSession
    {
        internal void CompLoop()
        {
            try
            {
                for (int i = 0; i < GridList.Count; i++)
                {
                    var gridComp = GridList[i];

                    if (gridComp.GroupMap == null)
                    {
                        var group = MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Logical, gridComp.Grid);
                        if (group != null)
                        {
                            GroupMap map;
                            if (GridGroupMap.TryGetValue(group, out map))
                                gridComp.GroupMap = map;
                        }
                    }

                    for (int j = 0; j < gridComp.ToolComps.Count; j++)
                    {
                        UpdateComp(gridComp.ToolComps[j]);
                    } //Tools loop

                } //Grids loop

                for (int i = 0; i < HandTools.Count; i++)
                {
                    UpdateComp(HandTools[i]);
                }//Handtools loop
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }
        }

        private void UpdateComp(ToolComp comp)
        {
            var step = "";
            try
            {
                var modeData = comp.ModeData;
                var def = modeData.Definition;
                step = "UpdateTool";

                UpdateTool(comp);

                var avState = comp.AvState & def.EventFlags;
                if (!comp.AvActive && avState > 0)
                {
                    AvComps.Add(comp);
                    comp.AvActive = true;
                }

                if (comp.Mode != ToolMode.Drill && modeData.WorkTick == Tick % def.UpdateInterval)
                    UpdateHitState(comp);

                if (!IsDedicated && comp.Draw && comp.Functional)
                {
                    step = "DrawComp";
                    DrawComp(comp);
                }

                if (!IsDedicated && def.Debug)
                {
                    DrawDebug(comp);
                }

                if (comp.Broken && comp.BrokenTick != Tick)
                    UnregisterBrokenComp(comp);
            }
            catch (Exception ex)
            {
                if (!comp.Broken)
                {
                    RegisterBrokenComp(comp, ex, step);
                }
            }
        }

        private void RegisterBrokenComp(ToolComp comp, Exception ex, string msg = "")
        {
            comp.Broken = true;

            if (comp.BrokenTick == 0)
            {
                Logs.LogException(ex);

                if (comp.IsBlock)
                    Logs.WriteLine($"{comp.BlockTool.CustomName} on grid {comp.Grid.DisplayName} is broken!");
                else
                    Logs.WriteLine($"{comp.HandTool.DefinitionId.SubtypeName} on player {((IMyCharacter)comp.Parent).DisplayName} is broken!");
            }
            comp.BrokenTick = Tick;
        }

        private void UnregisterBrokenComp(ToolComp comp)
        {
            comp.Broken = false;
            comp.BrokenTick = 0;

            if (comp.IsBlock)
            {
                Logs.WriteLine($"{comp.BlockTool.CustomName} on grid {comp.Grid.DisplayName} is no longer broken.");
                return;
            }

            Logs.WriteLine($"{comp.HandTool.DefinitionId.SubtypeName} on player {((IMyCharacter)comp.Parent).DisplayName} is no longer broken.");
        }

        private void DrawComp(ToolComp comp)
        {
            Vector3D worldPos, worldForward, worldUp;
            CalculateWorldVectors(comp, out worldPos, out worldForward, out worldUp);

            var toolValues = comp.Values;
            var modeData = comp.ModeData;

            MatrixD drawMatrix;
            switch (modeData.Definition.EffectShape)
            {
                case EffectShape.Sphere:
                    drawMatrix = MatrixD.CreateWorld(worldPos, worldForward, worldUp);
                    DrawSphere(drawMatrix, toolValues.Radius, Color.LawnGreen, false, 20, 0.01f);
                    break;
                case EffectShape.Cylinder:
                    drawMatrix = MatrixD.CreateWorld(worldPos, worldUp, worldForward);
                    DrawCylinder(drawMatrix, toolValues.Radius, toolValues.Length, Color.LawnGreen);
                    break;
                case EffectShape.Cuboid:
                    var quat = Quaternion.CreateFromForwardUp(worldForward, worldUp);
                    var obb = new MyOrientedBoundingBoxD(worldPos, toolValues.HalfExtent, quat);
                    DrawBox(obb, Color.LawnGreen, false, 5, 0.005f);
                    break;
                case EffectShape.Line:
                    DrawLine(worldPos, worldForward, Color.LawnGreen, 0.025f, toolValues.Length);
                    break;
                case EffectShape.Ray:
                    DrawLine(worldPos, worldForward, Color.LawnGreen, 0.025f, toolValues.Length);
                    break;
                default:
                    return;
            }
        }

        private void DrawDebug(ToolComp comp)
        {
            comp.DrawBoxes.ApplyAdditions();
            foreach (var tuple in comp.DrawBoxes)
            {
                //DrawLine(tuple.Item1.Center, Vector3D.One, tuple.Item2, 0.02f, 0.1f);
                DrawBox(tuple.Item1, tuple.Item2, false, 2, 0.01f);
            }
        }

        private void UpdateHitState(ToolComp comp)
        {
            var operational = comp.Functional && comp.Powered && comp.Enabled;
            var firing = comp.Activated || comp.GunBase.Shooting;

            var hitting = comp.Working && operational && firing;
            if (hitting != comp.WasHitting)
            {
                comp.WasHitting = hitting;
                comp.UpdateAvState(Trigger.Hit, hitting);
            }

            comp.Working = false;
        }

        private void CalculateWorldVectors(ToolComp comp, out Vector3D worldPos, out Vector3D worldForward, out Vector3D worldUp)
        {
            var modeData = comp.ModeData;
            var def = modeData.Definition;
            var pos = comp.ToolEntity.PositionComp;

            try
            {
                switch (def.Location)
                {
                    case Location.Emitter:
                        var partMatrix = modeData.MuzzlePart.PositionComp.WorldMatrixRef;
                        var muzzleMatrix = (MatrixD)modeData.Muzzle.Matrix;

                        var localPos = muzzleMatrix.Translation;
                        var muzzleForward = Vector3D.Normalize(muzzleMatrix.Forward);
                        var muzzleUp = Vector3D.Normalize(muzzleMatrix.Up);

                        if (!Vector3D.IsZero(def.Offset))
                        {
                            localPos += def.Offset;
                        }

                        Vector3D.Transform(ref localPos, ref partMatrix, out worldPos);
                        Vector3D.TransformNormal(ref muzzleForward, ref partMatrix, out worldForward);
                        Vector3D.TransformNormal(ref muzzleUp, ref partMatrix, out worldUp);
                        break;
                    case Location.Parent:
                        var parent = comp.Parent;
                        var parentMatrix = comp.IsBlock ? parent.PositionComp.WorldMatrixRef : ((IMyCharacter)parent).GetHeadMatrix(true);
                        worldPos = comp.IsBlock ? parent.PositionComp.WorldAABB.Center : parentMatrix.Translation;

                        Vector3D offset;
                        Vector3D.Rotate(ref def.Offset, ref parentMatrix, out offset);
                        worldPos += offset;

                        worldForward = parentMatrix.Forward;
                        worldUp = parentMatrix.Up;
                        break;
                    case Location.Centre:
                        var toolMatrix = pos.WorldMatrixRef;
                        worldPos = pos.WorldAABB.Center;
                        Vector3D.Rotate(ref def.Offset, ref toolMatrix, out offset);
                        worldPos += offset;

                        worldForward = toolMatrix.Forward;
                        worldUp = toolMatrix.Up;
                        break;
                    default:
                        worldPos = Vector3D.Zero;
                        worldForward = Vector3D.Forward;
                        worldUp = Vector3D.Up;
                        break;
                }
            }
            catch (Exception ex)
            {
                worldPos = Vector3D.Zero;
                worldForward = Vector3D.Forward;
                worldUp = Vector3D.Up;

                if (!comp.Broken)
                {
                    Logs.WriteLine($"Location: {def.Location} - Emitter Part Null: {modeData.MuzzlePart == null} - Parent Null: {comp.Parent == null} - Muzzle Null: {modeData.Muzzle == null}");
                    RegisterBrokenComp(comp, ex);
                }
            }
        }

        private void UpdateTool(ToolComp comp)
        {
            var modeData = comp.ModeData;
            var def = modeData.Definition;
            var tickModUpdate = Tick % def.UpdateInterval;
            var workTick = modeData.WorkTick == tickModUpdate;

            var tool = comp.ToolEntity;
            var block = comp.BlockTool;
            var handTool = comp.HandTool;
            var turret = modeData.Turret;
            var isBlock = comp.IsBlock;
            var isTurret = def.IsTurret && turret != null;

            var tTickDiff = Tick - comp.LastGridsTaskTick;
            if (!comp.GridsTask.IsComplete && tTickDiff > 0 && (tTickDiff % 100) == 0)
            {
                Logs.WriteLine($"{comp.BlockTool?.DisplayNameText ?? "handtool"} has frozen grid work task!");
            }

            if (isBlock && comp.Functional != block.IsFunctional)
            {
                comp.Functional = block.IsFunctional;
                comp.UpdateAvState(Trigger.Functional, block.IsFunctional);
                comp.Dirty = true;
            }

            if (isBlock && comp.Grid != block.CubeGrid)
            {
                comp.ChangeGrid();
            }

            if (!comp.Functional)
                return;

            if (!comp.FullInit)
                comp.FunctionalInit();

            if (isBlock && (comp.UpdatePower || comp.CompTick20 == TickMod20))
            {
                var wasPowered = comp.Powered;
                var isPowered = comp.IsPowered();
                //if (comp.UpdatePower) Logs.WriteLine($"UpdatePower: {wasPowered} : {isPowered}");
                if (wasPowered != isPowered)
                {
                    comp.UpdateAvState(Trigger.Powered, comp.Powered);

                    if (!isPowered)
                    {
                        comp.WasHitting = false;
                        comp.UpdateHitInfo(false);
                    }
                }
                comp.UpdatePower = false;
            }

            if (!comp.Powered || !isBlock && ((IMyCharacter)comp.Parent).SuitEnergyLevel <= 0)
                return;

            if (comp.Dirty)
            {
                comp.LoadModels();
            }

            if (isBlock && !block.Enabled)
                return;

            var fill = comp.Inventory.VolumeFillFactor;
            var needsPushing = comp.CompTick60 == TickMod60 && (fill > 0f || comp.Yields.Count > 0);
            if (IsServer && comp.Mode != ToolMode.Weld && needsPushing)
                comp.ManageInventory();

            Vector3D worldPos, worldForward, worldUp;
            CalculateWorldVectors(comp, out worldPos, out worldForward, out worldUp);

            if (modeData.Definition.EffectShape == EffectShape.Cylinder)
                worldPos = worldPos - worldForward * comp.Values.Length * 0.5f;

            var activated = comp.Activated;
            var handToolShooting = !isBlock && comp.HandTool.IsShooting;
            var shooting = activated || handToolShooting || comp.GunBase.Shooting;

            var turretAligned = false;
            if (isTurret && comp.TrackTargets)
            {
                var dirty = comp.TargetsDirty || turret.Targets.Count < 1;
                if (dirty && comp.GridsTask.IsComplete && comp.CallbackComplete)
                {
                    if ((Tick - turret.LastRefreshTick) > def.UpdateInterval && tickModUpdate < (def.UpdateInterval / 2))
                    {
                        turret.RefreshTargetList(def, worldPos);
                        turret.LastRefreshTick = Tick;

                        if (comp.TargetsDirty)
                        {
                            turret.DeselectTarget();
                            comp.TargetsDirty = false;
                        }
                    }
                }

                if (workTick)
                {
                    if (turret.HasTarget)
                    {
                        var target = turret.ActiveTarget;
                        var projector = ((MyCubeGrid)target.CubeGrid).Projector as IMyProjector;

                        var closing = target.CubeGrid.MarkedForClose || target.FatBlock != null && target.FatBlock.MarkedForClose;
                        var finished = target.IsFullyDismounted || comp.Mode == ToolMode.Weld && projector == null && target.IsFullIntegrity && !target.HasDeformation;
                        var outOfRange = Vector3D.DistanceSquared(target.CubeGrid.GridIntegerToWorld(target.Position), worldPos) > turret.Definition.TargetRadiusSqr;
                        if (closing || finished || outOfRange || (projector != null && projector.CanBuild(target, true) != BuildCheckResult.OK) || !turret.TrackTarget())
                        {
                            turret.DeselectTarget();
                        }
                    }

                    if (!turret.HasTarget && turret.Targets.Count > 0)
                        turret.SelectNewTarget(worldPos);

                    while (turret.HasTarget && !turret.TrackTarget() && turret.Targets.Count > 0)
                    {
                        turret.Targets.RemoveAt(turret.Targets.Count - 1);
                        turret.SelectNewTarget(worldPos);
                    }

                    if (turret.HasTarget)
                        turret.LastTargetTick = Tick;

                    //Delay at least a couple projector updates to ensure no targets before returning home
                    if (!turret.HasTarget && turret.Part1.DesiredRotation != 0 && turret.LastTargetTick + 200 <= Tick)
                    {
                        turret.GoHome();
                    }
                }

                var part1 = turret.Part1;
                var diff1 = part1.DesiredRotation - part1.CurrentRotation;

                //Checks for closest angle crossing over +/-Pi
                if (diff1 > Pi || diff1 < -Pi)
                {
                    if (diff1 < -Pi)
                        diff1 = Pi2 + diff1;
                    else
                        diff1 = -(Pi2 - diff1);
                }

                if (!MyUtils.IsZero(diff1, 0.001f))
                {
                    var amount = MathHelper.Clamp(diff1, -part1.Definition.RotationSpeed, part1.Definition.RotationSpeed);
                    part1.CurrentRotation += amount;
                    diff1 -= amount;
                }

                var visDiff1 = part1.CurrentRotation - part1.VisualRotation;
                if (!MyUtils.IsZero(visDiff1, 0.001f))
                {
                    var rotation = part1.RotationFactory.Invoke(visDiff1);
                    var lm = part1.Subpart.PositionComp.LocalMatrixRef;
                    var translation = lm.Translation;
                    lm *= rotation;
                    lm.Translation = translation;
                    part1.Subpart.PositionComp.SetLocalMatrix(ref lm);
                    part1.VisualRotation = part1.CurrentRotation;
                }

                if (def.Debug && !IsDedicated)
                {
                    DrawLocalVector(part1.DesiredFacing, part1.Subpart, part1.Parent, Color.Green);
                    DrawLocalVector(part1.Facing, part1.Subpart, part1.Parent, Color.Blue);
                }

                var diff2 = 0f;
                if (turret.HasTwoParts)
                {
                    var part2 = turret.Part2;
                    diff2 = part2.DesiredRotation - part2.CurrentRotation;
                    if (!MyUtils.IsZero(diff2, 0.001f))
                    {
                        var amount = MathHelper.Clamp(diff2, -part2.Definition.RotationSpeed, part2.Definition.RotationSpeed);
                        part2.CurrentRotation += amount;
                        diff2 -= amount;
                    }

                    var visDiff2 = part2.CurrentRotation - part2.VisualRotation;
                    if (!MyUtils.IsZero(visDiff2, 0.001f))
                    {
                        var rotation = part2.RotationFactory.Invoke(visDiff2);
                        var lm = part2.Subpart.PositionComp.LocalMatrixRef;
                        var translation = lm.Translation;
                        lm *= rotation;
                        lm.Translation = translation;
                        part2.Subpart.PositionComp.SetLocalMatrix(ref lm);
                        part2.VisualRotation = part2.CurrentRotation;
                    }

                    if (def.Debug && !IsDedicated)
                    {
                        DrawLocalVector(part2.DesiredFacing, part2.Subpart, part2.Parent, Color.Red);
                        DrawLocalVector(part2.Facing, part2.Subpart, part2.Parent, Color.Blue);
                    }
                }

                var angleSqr = diff1 * diff1 + diff2 * diff2;
                var aligned = turret.HasTarget && !part1.OutOfBounds && turret.HasTwoParts ? !turret.Part2.OutOfBounds : true && angleSqr < turret.Definition.AimingToleranceSqr;
                if (aligned != turret.Aligned)
                {
                    turret.Aligned = aligned;

                    if (!shooting)
                    {
                        comp.UpdateAvState(Trigger.Firing, turret.Aligned);
                    }
                }

                if (turret.HasTarget && turret.ActiveTarget != null && comp.Draw && !IsDedicated)
                {
                    var slim = turret.ActiveTarget;
                    DrawLine(worldPos, slim.CubeGrid.GridIntegerToWorld(slim.Position), Color.BlueViolet, 0.01f);
                }

                turretAligned = turret.HasTarget && turret.Aligned;
            }

            if (!shooting && !turretAligned)
                return;

            if (activated)
            {
                if (!MySessionComponentSafeZones.IsActionAllowed(comp.Parent, CastHax(MySessionComponentSafeZones.AllowedActions, (int)comp.Mode)))
                {
                    comp.Activated = false;
                    return;
                }
            }

            var ownerId = isBlock ? block.OwnerId : handTool.OwnerIdentityId;

            if (!isBlock && !IsDedicated && ownerId == MyAPIGateway.Session.LocalHumanPlayer?.IdentityId)
            {
                var leftMousePressed = MyAPIGateway.Input.IsLeftMousePressed();
                if (leftMousePressed || MyAPIGateway.Input.IsRightMousePressed())
                {
                    var action = leftMousePressed ? ToolAction.Primary : ToolAction.Secondary;
                    if (action != comp.Action)
                    {
                        comp.Action = action;
                        Networking.SendPacketToServer(new SbyteUpdatePacket(comp.ToolEntity.EntityId, FieldType.Action, (int)comp.Action));
                        return;
                    }
                }
            }

            var toolValues = comp.Values;

            // Initial raycast?
            IHitInfo hitInfo = null;
            if (!IsDedicated || workTick && def.EffectShape == EffectShape.Ray)
            {
                if (def.EffectShape == EffectShape.Cylinder)
                    MyAPIGateway.Physics.CastRay(worldPos, worldPos + worldForward * toolValues.Length, out hitInfo);
                else if (def.EffectShape == EffectShape.Sphere)
                    MyAPIGateway.Physics.CastRay(worldPos, worldPos + worldForward * toolValues.Length, out hitInfo);
                else if  (def.EffectShape == EffectShape.Cuboid)
                    MyAPIGateway.Physics.CastRay(worldPos - worldForward * toolValues.Length * 0.707f, worldPos + worldForward * toolValues.Length * 0.707f, out hitInfo);
                else
                    MyAPIGateway.Physics.CastRay(worldPos, worldPos + worldForward * toolValues.Length, out hitInfo);
                if (hitInfo?.HitEntity != null)
                {
                    MyStringHash material;
                    var entity = hitInfo.HitEntity;
                    var hitPos = hitInfo.Position;
                    if (entity is MyVoxelBase)
                    {
                        var voxelMatDef = ((MyVoxelBase)entity).GetMaterialAt(ref hitPos);
                        material = voxelMatDef?.MaterialTypeNameHash ?? MyStringHash.GetOrCompute("Rock");
                    }
                    else if (entity is IMyCharacter)
                        material = MyStringHash.GetOrCompute("Character");
                    else if (entity is MyEnvironmentSector)
                        material = MyStringHash.GetOrCompute("Tree");
                    else
                        material = MyStringHash.GetOrCompute("Metal");

                    if (def.Location == Location.Hit)
                        worldPos = hitPos;

                    comp.UpdateHitInfo(true, hitInfo.Position, material);
                }
                else comp.UpdateHitInfo(false);
            }

            if (!workTick)
                return;

            if (comp.ActiveThreads > 0 || !comp.GridsTask.IsComplete || !comp.CallbackComplete)
                return;
            //TODO: fix hanging debug draws when block is done/turret is done working
            comp.DrawBoxes.ClearList();

            if (def.CacheBlocks && comp.Mode != ToolMode.Drill)
            {
                if (!IsServer)
                {
                    foreach (var item in comp.ClientWorkSet)
                    {
                        MyCube cube;
                        if (!item.Item2.TryGetCube(item.Item1, out cube))
                            continue;

                        var slim = (IMySlimBlock)cube.CubeBlock;
                        comp.WorkSet.Add(slim);
                    }
                }

                for (int s = comp.WorkSet.Count - 1; s >= 0; s--)
                {
                    var slim = comp.WorkSet[s];
                    var fatClose = slim?.FatBlock == null ? false : slim.FatBlock.MarkedForClose;
                    var gridClose = slim?.CubeGrid == null || slim.CubeGrid.MarkedForClose;
                    var skip = slim == null || slim.IsFullyDismounted || comp.Mode == ToolMode.Weld && slim.IsFullIntegrity && !slim.HasDeformation;
                    if (fatClose || gridClose || skip)
                    {
                        comp.WorkSet.RemoveAt(s);
                        continue;
                    }
                }

                if (comp.WorkSet.Count == def.Rate)
                {
                    comp.GridData.Position = worldPos;
                    comp.OnGetBlocksComplete();
                    return;
                }
            }

            var line = false;
            var rayLength = toolValues.Length;
            switch (def.EffectShape)
            {
                case EffectShape.Sphere:
                case EffectShape.Cylinder:
                case EffectShape.Cuboid:
                    def.EffectSphere.Center = worldPos;
                    def.EffectSphere.Radius = toolValues.BoundingRadius;
                    MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref def.EffectSphere, Entities);
                    break;
                case EffectShape.Line:
                    var effectLine = new LineD(worldPos, worldPos + worldForward * toolValues.Length);
                    line = true;
                    MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref effectLine, _lineOverlaps);
                    break;
                case EffectShape.Ray:
                    if (hitInfo?.HitEntity != null)
                    {
                        Entities.Add((MyEntity)hitInfo.HitEntity);
                        rayLength *= hitInfo.Fraction;
                    }
                    break;
                default:
                    return;
            }

            var damageType = (int)def.ToolType < 2 ? MyDamageType.Drill : (int)def.ToolType < 4 ? MyDamageType.Grind : MyDamageType.Weld;
            var toolFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);

            //comp.GridData.Clean(comp);

            var count = line ? _lineOverlaps.Count : Entities.Count;
            for (int k = 0; k < count; k++)
            {
                var entity = line ? _lineOverlaps[k].Element : Entities[k];

                if (entity == null || entity.MarkedForClose)
                    continue;

                if (DSAPIReady)
                {
                    var shieldBlock = DSAPI.MatchEntToShieldFast(entity, true);
                    if (shieldBlock != null)
                    {
                        var relation = comp.GetRelationToPlayer(shieldBlock.OwnerId, toolFaction);
                        if (relation > TargetTypes.Friendly)
                            continue;
                    }
                }

                if (entity is IMyDestroyableObject)
                {
                    if (!IsServer)
                        continue;

                    if (entity is IMyCharacter && !def.DamageCharacters)
                        continue;

                    if (!isBlock && !def.AffectOwnGrid && entity == comp.Parent)
                        continue;

                    var obb = new MyOrientedBoundingBoxD(entity.PositionComp.LocalAABB, entity.PositionComp.WorldMatrixRef);
                    if (def.Debug) DrawBox(obb, Color.Red, false, 8);
                    switch (def.EffectShape)
                    {
                        case EffectShape.Sphere:
                            if (obb.Contains(ref def.EffectSphere) == ContainmentType.Disjoint)
                                continue;
                            break;
                        case EffectShape.Cylinder:
                            var cylCtr = worldPos + worldForward * toolValues.Length * 0.5;
                            var offset = obb.Center - cylCtr;
                            var halfEdge = entity.PositionComp.LocalAABB.HalfExtents.AbsMax();

                            var radial = Vector3D.ProjectOnPlane(ref offset, ref worldForward);
                            var radialDistSqr = (float)radial.LengthSquared();
                            var radiusPlus = toolValues.Radius + halfEdge;
                            if (radialDistSqr > (radiusPlus * radiusPlus))
                                continue;
                            var axial = Vector3D.ProjectOnVector(ref offset, ref worldForward);
                            var axialDistSqr = (float)axial.LengthSquared();
                            var halfLen = (toolValues.Length / 2) + halfEdge;
                            if (axialDistSqr > (halfLen * halfLen))
                                continue;
                            break;
                        case EffectShape.Cuboid:
                            var orientation = Quaternion.CreateFromForwardUp(worldForward, worldUp);
                            comp.Obb.Center = worldPos;
                            comp.Obb.Orientation = orientation;
                            comp.Obb.HalfExtent = toolValues.HalfExtent;
                            if (obb.Contains(ref comp.Obb) == ContainmentType.Disjoint)
                                continue;
                            break;
                        case EffectShape.Line:
                            var effectLine = new LineD(worldPos, worldPos + worldForward * toolValues.Length);
                            if (obb.Intersects(ref effectLine) == null)
                                continue;
                            break;
                        default:
                            break;

                    }
                    comp.Working = true;

                    if (isBlock && def.PickUpFloatings)
                    {
                        if (entity is MyFloatingObject)
                        {
                            comp.Inventory.TakeFloatingObject(entity as MyFloatingObject);
                            continue;
                        }
                        if (entity is MyCargoContainerInventoryBagEntity)
                        {
                            comp.Inventory.TakeFloatingBag(entity as MyCargoContainerInventoryBagEntity);
                            continue;
                        }
                    }

                    var damage = entity is IMyCharacter ? 1f : 100f;
                    var destroyableObject = (IMyDestroyableObject)entity;
                    destroyableObject.DoDamage(damage, damageType, true, null, ownerId);
                    continue;
                }

                /*                if (entity is MyEnvironmentSector)
                                {
                                    IHitInfo hitInfo;
                                    if (MyAPIGateway.Physics.CastRay(worldPos, worldPos + def.EffectSphere.Radius * worldForward, out hitInfo))
                                    {
                                        var hitEntity = hitInfo.HitEntity;
                                        if (hitEntity is MyEnvironmentSector)
                                        {
                                            var sector = hitEntity as MyEnvironmentSector;
                                            uint shapeKey = hitInfo.Value.HkHitInfo.GetShapeKey(0);
                                            int itemFromShapeKey = sector.GetItemFromShapeKey(shapeKey);
                                            if (sector.DataView.Items[itemFromShapeKey].ModelIndex >= 0)
                                            {
                                                MyBreakableEnvironmentProxy module = sector.GetModule<MyBreakableEnvironmentProxy>();
                                                Vector3D hitnormal = base.CubeGrid.WorldMatrix.Right + base.CubeGrid.WorldMatrix.Forward;
                                                hitnormal.Normalize();
                                                float num = 10f;
                                                float mass = base.CubeGrid.Physics.Mass;
                                                float num2 = num * num * mass;
                                                module.BreakAt(itemFromShapeKey, hitInfo.Value.HkHitInfo.Position, hitnormal, (double)num2);
                                            }
                                        }
                                    }
                                }*/

                if (entity is IMyVoxelBase)
                {
                    if (comp.Mode != ToolMode.Drill)
                        continue;

                    var voxel = (IMyVoxelBase)entity;

                    if ((voxel as MyVoxelBase).GetOrePriority() == 0)
                        continue;

                    var localCentre = Vector3D.Transform(worldPos + Vector3D.TransformNormal((voxel as MyVoxelBase).SizeInMetresHalf, voxel.WorldMatrix), voxel.WorldMatrixNormalizedInv);
                    var matrixNI = voxel.PositionComp.WorldMatrixNormalizedInv;
                    Vector3D localForward;
                    Vector3D.TransformNormal(ref worldForward, ref matrixNI, out localForward);

                    Vector3I minExtent;
                    Vector3I maxExtent;
                    if (def.EffectShape == EffectShape.Cuboid)
                    {
                        Vector3D localUp;
                        Vector3D.TransformNormal(ref worldUp, ref matrixNI, out localUp);
                        var orientation = Quaternion.CreateFromForwardUp(localForward, localUp);

                        comp.Obb.Center = localCentre;
                        comp.Obb.Orientation = orientation;
                        comp.Obb.HalfExtent = toolValues.HalfExtent;

                        var box = comp.Obb.GetAABB();
                        minExtent = Vector3I.Round(localCentre - box.HalfExtents);
                        maxExtent = Vector3I.Round(localCentre + box.HalfExtents);
                    }
                    else
                    {
                        var drillRadius = toolValues.BoundingRadius;
                        minExtent = Vector3I.Round(localCentre - drillRadius);
                        maxExtent = Vector3I.Round(localCentre + drillRadius);
                    }

                    var size = voxel.Storage.Size;
                    var min = Vector3I.Max(minExtent, Vector3I.Zero);
                    var max = Vector3I.Min(maxExtent, size);

                    if (def.Debug)
                    {
                        var offset = (voxel as MyVoxelBase).SizeInMetresHalf;
                        var drawBox = new BoundingBoxD((Vector3D)min - offset, (Vector3D)max - offset);
                        var drawObb = new MyOrientedBoundingBoxD(drawBox, voxel.PositionComp.LocalMatrixRef);
                        DrawBox(drawObb, Color.IndianRed, false, 4, 0.005f);
                    }

                    var data = DrillDataPool.Count > 0 ? DrillDataPool.Pop() : new DrillData();
                    data.Voxel = voxel;
                    switch (def.EffectShape)
                    {
                        case EffectShape.Sphere:
                            data.Min = min;
                            data.Max = max;
                            data.Origin = localCentre;
                            data.Direction = localForward;
                            MyAPIGateway.Parallel.Start(comp.DrillSphere, comp.OnDrillComplete, data);
                            break;
                        case EffectShape.Cylinder:
                            data.Min = min;
                            data.Max = max;
                            data.Origin = localCentre;
                            data.Direction = localForward;
                            MyAPIGateway.Parallel.Start(comp.DrillCylinder, comp.OnDrillComplete, data);
                            break;
                        case EffectShape.Cuboid:
                            data.Min = min;
                            data.Max = max;
                            data.Origin = localCentre;
                            data.Direction = localForward;
                            MyAPIGateway.Parallel.Start(comp.DrillCuboid, comp.OnDrillComplete, data);
                            break;
                        case EffectShape.Line:
                            data.Origin = worldPos;
                            data.Direction = worldForward;
                            MyAPIGateway.Parallel.Start(comp.DrillLine, comp.OnDrillComplete, data);
                            break;
                        default:
                            break;
                    }
                    comp.ActiveThreads++;

                }

                if (entity is MyCubeGrid)
                {
                    var grid = entity as MyCubeGrid;

                    if (!grid.Editable)
                        continue;

                    if (isBlock && !def.AffectOwnGrid && (grid == comp.Grid || comp.GridComp.GroupMap.ConnectedGrids.Contains(grid)))
                        continue;

                    var weldMode = comp.Mode == ToolMode.Weld;
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

                    if (comp.HasTargetControls)
                    {
                        var gridOwner = grid.Projector?.OwnerId ?? grid.BigOwners.FirstOrDefault();
                        var relation = comp.GetRelationToPlayer(gridOwner, toolFaction);
                        if ((relation & comp.Targets) == TargetTypes.None)
                            continue;
                    }

                    comp.GridData.Grids.Add(grid);
                }

            } //Hits loop

            var gridData = comp.GridData;
            if (gridData.Grids.Count == 0)
                return;

            gridData.Position = worldPos;
            gridData.Forward = worldForward;
            gridData.Up = worldUp;
            gridData.RayLength = rayLength;
            comp.GridsTask = MyAPIGateway.Parallel.Start(comp.GetBlocksInVolume, comp.OnGetBlocksComplete);
            comp.LastGridsTaskTick = Tick;

            Entities.Clear();
            _lineOverlaps.Clear();

        }

        internal void StartComps()
        {
            try
            {
                _startGrids.ApplyAdditions();
                for (int i = 0; i < _startGrids.Count; i++)
                {
                    var grid = _startGrids[i];

                    if (grid?.Physics == null || grid.IsPreview)
                        continue;

                    var gridComp = GridCompPool.Count > 0 ? GridCompPool.Pop() : new GridComp();
                    gridComp.Init(grid);

                    GridList.Add(gridComp);
                    GridMap[grid] = gridComp;
                    grid.OnClose += OnGridClose;
                }
                _startGrids.ClearImmediate();

                _startComps.ApplyAdditions();
                for (int i = 0; i < _startComps.Count; i++)
                {
                    var entity = _startComps[i];

                    if (entity is MyCubeBlock)
                    {
                        var block = (MyCubeBlock)entity;

                        if (block?.CubeGrid?.Physics == null || block.CubeGrid.IsPreview)
                            continue;

                        GridComp gridComp;
                        if (!GridMap.TryGetValue(block.CubeGrid, out gridComp))
                            continue;

                        var tool = block as IMyConveyorSorter;
                        if (tool != null)
                        {
                            List<ToolDefinition> defs;
                            if (!DefinitionMap.TryGetValue(tool.BlockDefinition, out defs))
                                continue;

                            var comp = new ToolComp(entity, defs);
                            ToolMap[block.EntityId] = comp;
                            entity.Components.Add(comp);
                            //((IMyCubeGrid)gridComp.Grid).WeaponSystem.Register(comp.GunBase);
                            gridComp.FatBlockAdded(block);

                            continue;
                        }

                        //var controller = block as MyShipController;
                        //if (controller != null)
                        //{
                        //    var control = new Control(controller);
                        //    gridComp.Controllers.TryAdd(controller, control);
                        //}

                        continue;
                    }

                    if (entity is IMyHandheldGunObject<MyDeviceBase>)
                    {
                        if (!entity.DefinitionId.HasValue)
                            continue;

                        List<ToolDefinition> defs;
                        if (!DefinitionMap.TryGetValue(entity.DefinitionId.Value, out defs))
                            continue;

                        var comp = new ToolComp(entity, defs);
                        ToolMap[entity.EntityId] = comp;
                        entity.Components.Add(comp);
                        comp.FunctionalInit();
                        HandTools.Add(comp);
                    }
                }
                _startComps.ClearImmediate();
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }

        }
    }
}
