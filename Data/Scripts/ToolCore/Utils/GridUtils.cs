using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using ToolCore.Comp;
using ToolCore.Definitions.Serialised;
using ToolCore.Session;
using ToolCore.Utils;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using static ToolCore.Comp.ToolComp;

namespace ToolCore
{
    internal class GridData
    {
        internal Vector3D Position;
        internal Vector3D Forward;
        internal Vector3D Up;
        internal float RayLength;

        internal readonly List<MyCubeGrid> Grids = new List<MyCubeGrid>();
        internal readonly HashSet<IMySlimBlock> HitBlocksHash = new HashSet<IMySlimBlock>();

        internal void Clean()
        {
            Grids.Clear();
            HitBlocksHash.Clear();
        }
    }

    internal static class GridUtils
    {
        internal static void GetBlocksInVolume(this ToolComp comp)
        {
            try
            {
                var modeData = comp.ModeData;
                var def = modeData.Definition;
                var toolValues = comp.Values;
                var data = comp.GridData;

                for (int i = 0; i < data.Grids.Count; i++)
                {
                    var grid = data.Grids[i];
                    var gridMatrixNI = grid.PositionComp.WorldMatrixNormalizedInv;
                    var localCentre = grid.WorldToGridScaledLocal(data.Position);
                    Vector3D localForward;
                    Vector3D.TransformNormal(ref data.Forward, ref gridMatrixNI, out localForward);

                    var gridSizeR = grid.GridSizeR;
                    var radius = toolValues.BoundingRadius * gridSizeR;

                    Vector3D minExtent;
                    Vector3D maxExtent;

                    if (def.EffectShape == EffectShape.Cuboid)
                    {
                        Vector3D localUp;
                        Vector3D.TransformNormal(ref data.Up, ref gridMatrixNI, out localUp);
                        var orientation = Quaternion.CreateFromForwardUp(localForward, localUp);

                        comp.Obb.Center = localCentre;
                        comp.Obb.Orientation = orientation;
                        comp.Obb.HalfExtent = toolValues.HalfExtent * gridSizeR;

                        var box = comp.Obb.GetAABB();

                        minExtent = localCentre - box.HalfExtents;
                        maxExtent = localCentre + box.HalfExtents;

                        if (def.Debug)
                        {
                            var gridSize = grid.GridSize;
                            var drawBox = new BoundingBoxD(minExtent * gridSize, maxExtent * gridSize);
                            var drawObb = new MyOrientedBoundingBoxD(drawBox, grid.PositionComp.LocalMatrixRef);
                            comp.DrawBoxes.Add(new MyTuple<MyOrientedBoundingBoxD, Color>(drawObb, Color.LightBlue));
                        }
                    }
                    else
                    {
                        minExtent = localCentre - radius;
                        maxExtent = localCentre + radius;
                    }

                    var sMin = Vector3I.Round(minExtent);
                    var sMax = Vector3I.Round(maxExtent);

                    var gMin = grid.Min;
                    var gMax = grid.Max;

                    var min = Vector3I.Max(sMin, gMin);
                    var max = Vector3I.Min(sMax, gMax);

                    switch (def.EffectShape)
                    {
                        case EffectShape.Sphere:
                            comp.GetBlocksInSphere(data, grid, min, max, localCentre, radius);
                            break;
                        case EffectShape.Cylinder:
                            comp.GetBlocksInCylinder(data, grid, min, max, localCentre, localForward, toolValues.Radius * gridSizeR, toolValues.Length * gridSizeR);
                            break;
                        case EffectShape.Cuboid:
                            comp.GetBlocksInCuboid(data, grid, min, max, comp.Obb);
                            break;
                        case EffectShape.Line:
                            comp.GetBlocksOverlappingLine(data, grid, data.Position, data.Position + data.Forward * toolValues.Length);
                            break;
                        case EffectShape.Ray:
                            comp.GetBlockInRayPath(data, grid, data.Position + data.Forward * (data.RayLength + 0.01));
                            break;
                        default:
                            break;
                    }

                    data.HitBlocksHash.Clear();
                }
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }

            comp.CallbackComplete = false;
        }

        private static void GetBlocksInSphere(this ToolComp comp, GridData data, MyCubeGrid grid,
            Vector3I min, Vector3I max, Vector3D centre, double radius)
        {
            int i, j, k;
            for (i = min.X; i <= max.X; i++)
            {
                for (j = min.Y; j <= max.Y; j++)
                {
                    for (k = min.Z; k <= max.Z; k++)
                    {
                        var pos = new Vector3I(i, j, k);

                        var posD = (Vector3D)pos;
                        Vector3D corner = Vector3D.Clamp(centre, posD - 0.5, posD + 0.5);

                        var distSqr = Vector3D.DistanceSquared(corner, centre);
                        if (distSqr > radius * radius)
                            continue;

                        MyCube cube;
                        if (!grid.TryGetCube(pos, out cube))
                            continue;

                        var slim = (IMySlimBlock)cube.CubeBlock;
                        if (!data.HitBlocksHash.Add(slim))
                            continue;

                        if (slim.FatBlock != null && slim.FatBlock.MarkedForClose)
                            continue;

                        var projector = grid.Projector as IMyProjector;
                        if (projector != null)
                        {
                            var diff = Math.Abs(slim.Dithering + 0.25f);
                            if (diff > 0.24f)
                                continue;
                        }
                        else if (comp.Mode == ToolMode.Weld && slim.IsFullIntegrity && !slim.HasDeformation)
                            continue;

                        var colour = projector?.SlimBlock.ColorMaskHSV ?? slim.ColorMaskHSV;
                        if (comp.UseWorkColour && colour.PackHSVToUint() != comp.WorkColourPacked)
                            continue;

                        var layer = (int)Math.Ceiling(distSqr);
                        comp.MaxLayer = Math.Max(layer, comp.MaxLayer);

                        List<IMySlimBlock> list;
                        if (!comp.HitBlockLayers.TryGetValue(layer, out list))
                        {
                            list = new List<IMySlimBlock>();
                            comp.HitBlockLayers[layer] = list;
                        }
                        list.Add(slim);
                    }
                }
            }
        }

        private static void GetBlocksInCylinder(this ToolComp comp, GridData data, MyCubeGrid grid,
            Vector3I min, Vector3I max, Vector3D centre, Vector3D forward, double radius, double length)
        {
            var endOffset = forward * (length / 2);
            var endOffsetAbs = Vector3D.Abs(endOffset);
            centre = centre + endOffset;

            //if (debug)
            //{
            //    var centrepoint = grid.GridIntegerToWorld(centre);
            //    DrawScaledPoint(centrepoint, 0.3f, Color.Green);
            //    var end1 = grid.GridIntegerToWorld(centre + endOffset);
            //    var end2 = grid.GridIntegerToWorld(centre - endOffset);
            //    DrawScaledPoint(end1, 0.3f, Color.Green);
            //    DrawScaledPoint(end2, 0.3f, Color.Green);
            //    DrawLine(end1, end2, Color.Green, 0.05f);
            //}

            int i, j, k;
            for (i = min.X; i <= max.X; i++)
            {
                for (j = min.Y; j <= max.Y; j++)
                {
                    for (k = min.Z; k <= max.Z; k++)
                    {
                        var pos = new Vector3I(i, j, k);

                        var posD = (Vector3D)pos;
                        var blockOffset = posD - centre;
                        var blockDir = Vector3D.ProjectOnPlane(ref blockOffset, ref forward);

                        var clampedIntersect = Vector3D.Clamp(posD - blockDir, centre - endOffsetAbs, centre + endOffsetAbs);
                        var corner = Vector3D.Clamp(clampedIntersect, posD - 0.5, posD + 0.5);

                        //if (debug)
                        //{
                        //    var a = grid.GridIntegerToWorld(clampedIntersect);
                        //    DrawScaledPoint(a, 0.1f, Color.Blue);

                        //    var b = grid.GridIntegerToWorld(corner);
                        //    DrawScaledPoint(b, 0.2f, Color.Red);

                        //    DrawLine(a, b, Color.Yellow, 0.02f);
                        //}

                        var distSqr = Vector3D.DistanceSquared(corner, clampedIntersect);
                        if (distSqr > radius * radius)
                            continue;

                        var cornerOffset = corner - centre;
                        var lengthVector = Vector3D.ProjectOnVector(ref cornerOffset, ref forward);
                        var lengthSqr = lengthVector.LengthSquared();
                        if (lengthSqr > length * length)
                            continue;

                        MyCube cube;
                        if (!grid.TryGetCube(pos, out cube))
                            continue;

                        var slim = (IMySlimBlock)cube.CubeBlock;
                        if (!data.HitBlocksHash.Add(slim))
                            continue;

                        if (slim.FatBlock != null && slim.FatBlock.MarkedForClose)
                            continue;

                        var projector = grid.Projector as IMyProjector;
                        if (projector != null)
                        {
                            var diff = Math.Abs(slim.Dithering + 0.25f);
                            if (diff > 0.24f)
                                continue;
                        }
                        else if (comp.Mode == ToolMode.Weld && slim.IsFullIntegrity && !slim.HasDeformation)
                            continue;

                        var colour = projector?.SlimBlock.ColorMaskHSV ?? slim.ColorMaskHSV;
                        if (comp.UseWorkColour && colour.PackHSVToUint() != comp.WorkColourPacked)
                            continue;

                        var layer = (int)Math.Ceiling(distSqr);
                        comp.MaxLayer = Math.Max(layer, comp.MaxLayer);

                        List<IMySlimBlock> list;
                        if (!comp.HitBlockLayers.TryGetValue(layer, out list))
                        {
                            list = new List<IMySlimBlock>();
                            comp.HitBlockLayers[layer] = list;
                        }
                        list.Add(slim);

                    }
                }
            }
        }

        private static void GetBlocksInCuboid(this ToolComp comp, GridData data, MyCubeGrid grid,
            Vector3I min, Vector3I max, MyOrientedBoundingBoxD obb)
        {
            int i, j, k;
            for (i = min.X; i <= max.X; i++)
            {
                for (j = min.Y; j <= max.Y; j++)
                {
                    for (k = min.Z; k <= max.Z; k++)
                    {
                        var pos = new Vector3I(i, j, k);

                        var posD = (Vector3D)pos;

                        var box = new BoundingBoxD(posD - 0.5, posD + 0.5);
                        if (obb.Contains(ref box) == ContainmentType.Disjoint)
                            continue;

                        MyCube cube;
                        if (!grid.TryGetCube(pos, out cube))
                            continue;

                        var slim = (IMySlimBlock)cube.CubeBlock;
                        if (!data.HitBlocksHash.Add(slim))
                            continue;

                        if (slim.FatBlock != null && slim.FatBlock.MarkedForClose)
                            continue;

                        var projector = grid.Projector as IMyProjector;
                        if (projector != null)
                        {
                            var diff = Math.Abs(slim.Dithering + 0.25f);
                            if (diff > 0.24f)
                                continue;
                        }
                        else if (comp.Mode == ToolMode.Weld && slim.IsFullIntegrity && !slim.HasDeformation)
                            continue;

                        var colour = projector?.SlimBlock.ColorMaskHSV ?? slim.ColorMaskHSV;
                        if (comp.UseWorkColour && colour.PackHSVToUint() != comp.WorkColourPacked)
                            continue;

                        var distSqr = Vector3D.DistanceSquared(posD, obb.Center);

                        var layer = (int)Math.Ceiling(distSqr);
                        comp.MaxLayer = Math.Max(layer, comp.MaxLayer);

                        List<IMySlimBlock> list;
                        if (!comp.HitBlockLayers.TryGetValue(layer, out list))
                        {
                            list = new List<IMySlimBlock>();
                            comp.HitBlockLayers[layer] = list;
                        }
                        list.Add(slim);

                    }
                }
            }
        }

        private static void GetBlocksOverlappingLine(this ToolComp comp, GridData data, MyCubeGrid grid,
            Vector3D start, Vector3D end)
        {
            var hitPositions = new List<Vector3I>();
            grid.RayCastCells(start, end, hitPositions);

            for (int i = 0; i < hitPositions.Count; i++)
            {
                var pos = hitPositions[i];

                MyCube cube;
                if (!grid.TryGetCube(pos, out cube))
                    continue;

                var slim = (IMySlimBlock)cube.CubeBlock;
                if (!data.HitBlocksHash.Add(slim))
                    continue;

                if (slim.FatBlock != null && slim.FatBlock.MarkedForClose)
                    continue;

                var projector = grid.Projector as IMyProjector;
                if (projector != null)
                {
                    var diff = Math.Abs(slim.Dithering + 0.25f);
                    if (diff > 0.24f)
                        continue;
                    //var projGrid = (MyCubeGrid)projector.CubeGrid;
                    //var projGridPos = projGrid.WorldToGridInteger(grid.GridIntegerToWorld(slim.Position));
                    //if (projGrid.GetCubeBlock(projGridPos) != null)
                    //{
                    //    continue;
                    //}
                }
                else if (comp.Mode == ToolMode.Weld && slim.IsFullIntegrity && !slim.HasDeformation)
                    continue;

                var colour = projector?.SlimBlock.ColorMaskHSV ?? slim.ColorMaskHSV;
                if (comp.UseWorkColour && colour.PackHSVToUint() != comp.WorkColourPacked)
                    continue;

                var distSqr = Vector3D.DistanceSquared(start, grid.GridIntegerToWorld(pos));

                var layer = (int)Math.Ceiling(distSqr);
                comp.MaxLayer = Math.Max(layer, comp.MaxLayer);

                List<IMySlimBlock> list;
                if (!comp.HitBlockLayers.TryGetValue(layer, out list))
                {
                    list = new List<IMySlimBlock>();
                    comp.HitBlockLayers[layer] = list;
                }
                list.Add(slim);
            }
        }

        private static void GetBlockInRayPath(this ToolComp comp, GridData data, MyCubeGrid grid,
            Vector3D pos)
        {
            var localPos = Vector3D.Transform(pos, grid.PositionComp.WorldMatrixNormalizedInv);
            var cellPos = Vector3I.Round(localPos * grid.GridSizeR);

            MyCube cube;
            if (!grid.TryGetCube(cellPos, out cube))
                return;

            var slim = (IMySlimBlock)cube.CubeBlock;

            if (slim.FatBlock != null && slim.FatBlock.MarkedForClose)
                return;

            var projector = grid.Projector as IMyProjector;
            if (projector != null)
            {
                var diff = Math.Abs(slim.Dithering + 0.25f);
                if (diff > 0.24f)
                    return;
            }
            else if (comp.Mode == ToolMode.Weld && slim.IsFullIntegrity && !slim.HasDeformation)
                return;

            var colour = projector?.SlimBlock.ColorMaskHSV ?? slim.ColorMaskHSV;
            if (comp.UseWorkColour && colour.PackHSVToUint() != comp.WorkColourPacked)
                return;

            var layer = (int)Math.Ceiling(data.RayLength);
            comp.MaxLayer = Math.Max(layer, comp.MaxLayer);

            List<IMySlimBlock> list;
            if (!comp.HitBlockLayers.TryGetValue(layer, out list))
            {
                list = new List<IMySlimBlock>();
                comp.HitBlockLayers[layer] = list;
            }
            list.Add(slim);
        }

        internal static void OnGetBlocksComplete(this ToolComp comp)
        {
            var session = ToolSession.Instance;

            try
            {
                var modeData = comp.ModeData;
                var def = modeData.Definition;
                var workSet = comp.WorkSet;
                var layers = comp.HitBlockLayers;

                var gridData = comp.GridData;
                var pos = gridData.Position;

                if (def.CacheBlocks && gridData.Grids.Count > 0)
                {
                    for (int s = workSet.Count - 1; s >= 0; s--)
                    {
                        var slim = workSet[s];
                        var fatClose = slim?.FatBlock == null ? false : slim.FatBlock.MarkedForClose;
                        var gridClose = slim?.CubeGrid == null || slim.CubeGrid.MarkedForClose;
                        var skip = slim == null || slim.IsFullyDismounted || comp.Mode == ToolMode.Weld && slim.IsFullIntegrity && !slim.HasDeformation;
                        if (fatClose || gridClose || skip)
                        {
                            workSet.RemoveAt(s);
                            continue;
                        }
                    }
                }
                gridData.Clean();

                if (workSet.Count > 0)
                {
                    layers[0] = workSet;
                }

                if (layers.Count > 0)
                {
                    switch (comp.Mode)
                    {
                        case ToolMode.Drill:
                            break;
                        case ToolMode.Grind:
                            comp.GrindBlocks();
                            break;
                        case ToolMode.Weld:
                            if (def.Rate > 4)
                            {
                                comp.WeldBlocksBulk();
                                break;
                            }
                            comp.WeldBlocks();
                            break;
                        default:
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }

            session.TempBlocks.Clear();
            comp.FailedPulls.Clear();
            comp.HitBlockLayers.Clear();
            comp.MaxLayer = 0;
            comp.CallbackComplete = true;
        }

        internal static void GrindBlocks(this ToolComp comp)
        {
            var modeData = comp.ModeData;
            var def = modeData.Definition;
            var inventory = comp.Inventory;
            var toolValues = comp.Values;
            var maxBlocks = def.Rate;

            var grindAmount = toolValues.Speed * MyAPIGateway.Session.GrinderSpeedMultiplier;

            var count = 0;
            for (int i = 0; i <= comp.MaxLayer; i++)
            {
                List<IMySlimBlock> layer;
                if (!comp.HitBlockLayers.TryGetValue(i, out layer))
                    continue;

                for (int j = 0; j < layer.Count; j++)
                {
                    if (count >= maxBlocks)
                        return;

                    var slim = layer[j];
                    var fat = slim.FatBlock;
                    var grid = slim.CubeGrid as MyCubeGrid;
                    if (slim.IsFullyDismounted || grid.MarkedForClose || fat != null && fat.MarkedForClose)
                    {
                        if (def.Debug) comp.DebugDrawBlock(slim, Color.Red);
                        continue;
                    }

                    comp.Working = true;
                    count++;

                    var cubeDef = (MyCubeBlockDefinition)slim.BlockDefinition;
                    MyCubeBlockDefinition.PreloadConstructionModels(cubeDef);


                    if (!ToolSession.Instance.IsServer)
                    {
                        if (def.CacheBlocks)
                        {
                            var integrityChange = grindAmount * cubeDef.IntegrityPointsPerSec / cubeDef.DisassembleRatio;
                            if (slim.Integrity > integrityChange)
                            {
                                comp.ClientWorkSet.Add(new MyTuple<Vector3I, MyCubeGrid>(slim.Position, grid));
                            }
                        }

                        continue;
                    }

                    MyDamageInformation damageInfo = new MyDamageInformation(false, grindAmount, MyDamageType.Grind, comp.ToolEntity.EntityId);
                    if (slim.UseDamageSystem) ToolSession.Instance.Session.DamageSystem.RaiseBeforeDamageApplied(slim, ref damageInfo);

                    slim.DecreaseMountLevel(damageInfo.Amount, inventory, false);
                    slim.MoveItemsFromConstructionStockpile(inventory, MyItemFlags.None);

                    if (slim.UseDamageSystem) ToolSession.Instance.Session.DamageSystem.RaiseAfterDamageApplied(slim, damageInfo);

                    if (slim.IsFullyDismounted)
                    {
                        if (fat != null && fat.HasInventory)
                        {
                            Utils.Utils.EmptyBlockInventories((MyCubeBlock)fat, inventory);
                        }

                        slim.SpawnConstructionStockpile();
                        grid.RazeBlock(slim.Min);

                        if (def.Debug) comp.DebugDrawBlock(slim, Color.GreenYellow);
                        continue;
                    }

                    if (def.Debug) comp.DebugDrawBlock(slim, Color.Green);

                    if (def.CacheBlocks)
                    {
                        comp.WorkSet.Add(slim);
                    }

                }

                if (count >= maxBlocks)
                    return;
            }
        }

        internal static void WeldBlocks(this ToolComp comp)
        {
            var modeData = comp.ModeData;
            var def = modeData.Definition;
            var inventory = comp.Inventory;
            var toolValues = comp.Values;
            var maxBlocks = def.Rate;
            var ownerId = comp.IsBlock ? comp.BlockTool.OwnerId : comp.HandTool.OwnerIdentityId;
            var steamId = MyAPIGateway.Players.TryGetSteamId(ownerId);

            var missing = ToolSession.Instance.MissingComponents;
            var creative = MyAPIGateway.Session.CreativeMode;
            var weldAmount = toolValues.Speed * MyAPIGateway.Session.WelderSpeedMultiplier;

            var count = 0;
            for (int i = 0; i <= comp.MaxLayer; i++)
            {
                List<IMySlimBlock> layer;
                if (!comp.HitBlockLayers.TryGetValue(i, out layer))
                    continue;

                for (int j = 0; j < layer.Count; j++)
                {
                    if (count >= maxBlocks)
                        return;

                    var slim = layer[j];
                    var fat = slim.FatBlock;
                    var grid = slim.CubeGrid as MyCubeGrid;
                    if (slim.IsFullyDismounted || grid.MarkedForClose || fat != null && fat.MarkedForClose)
                    {
                        if (def.Debug) comp.DebugDrawBlock(slim, Color.Red);
                        continue;
                    }

                    var projector = grid.Projector as IMyProjector;
                    if (projector == null && slim.IsFullIntegrity && !slim.HasDeformation)
                    {
                        if (def.Debug) comp.DebugDrawBlock(slim, Color.White);
                        continue;
                    }

                    missing.Clear();
                    var cubeDef = slim.BlockDefinition as MyCubeBlockDefinition;

                    if (projector == null)
                    {
                        slim.GetMissingComponents(missing);
                    }
                    else
                    {
                        var components = cubeDef.Components;
                        if (components != null && components.Length != 0)
                        {
                            var firstComp = components[0].Definition.Id.SubtypeName;
                            if (missing.ContainsKey(firstComp))
                                missing[firstComp] += 1;
                            else missing[firstComp] = 1;
                        }
                    }

                    if (!creative && ToolSession.Instance.MissingComponents.Count > 0 && !comp.TryPullComponents())
                    {
                        if (def.IsTurret && modeData.Turret.ActiveTarget == slim) modeData.Turret.DeselectTarget();
                        if (def.Debug) comp.DebugDrawBlock(slim, Color.Pink);
                        continue;
                    }

                    if (projector != null)
                    {
                        if (projector.CanBuild(slim, true) != BuildCheckResult.OK)
                        {
                            if (def.Debug) comp.DebugDrawBlock(slim, Color.Yellow);
                            continue;
                        }

                        if (!ToolSession.Instance.IsServer)
                        {
                            if (!creative && inventory.GetItemAmount(cubeDef.Components[0].Definition.Id) < 1)
                            {
                                if (def.IsTurret && modeData.Turret.ActiveTarget == slim) modeData.Turret.DeselectTarget();
                                continue;
                            }

                            comp.Working = true;
                            count++;

                            if (def.CacheBlocks && !creative)
                            {
                                var newPos = projector.CubeGrid.WorldToGridInteger(grid.GridIntegerToWorld(slim.Position));
                                comp.ClientWorkSet.Add(new MyTuple<Vector3I, MyCubeGrid>(newPos, (MyCubeGrid)projector.CubeGrid));
                            }
                            continue;
                        }

                        if (steamId > 1 && cubeDef.DLCs != null && cubeDef.DLCs.Length > 0 && !OwnsDLC(cubeDef, steamId))
                        {
                            if (def.IsTurret && modeData.Turret.ActiveTarget == slim) modeData.Turret.DeselectTarget();
                            if (def.Debug) comp.DebugDrawBlock(slim, Color.Gold);
                            continue;
                        }

                        if (!creative && inventory.RemoveItemsOfType(1, cubeDef.Components[0].Definition.Id) < 1)
                        {
                            if (def.IsTurret && modeData.Turret.ActiveTarget == slim) modeData.Turret.DeselectTarget();
                            if (def.Debug) comp.DebugDrawBlock(slim, Color.Pink);
                            continue;
                        }

                        var builtBy = comp.IsBlock ? comp.BlockTool.SlimBlock.BuiltBy : ownerId;
                        //Temp check for MP active to avoid NRE in HasClientArmor
                        projector.Build(slim, ToolSession.Instance.IsMultiPlayer ? ownerId : MyAPIGateway.Session.Player.IdentityId, comp.ToolEntity.EntityId, true, builtBy);

                        if (def.CacheBlocks)
                        {
                            var pos = projector.CubeGrid.WorldToGridInteger(grid.GridIntegerToWorld(slim.Position));
                            var newSlim = projector.CubeGrid.GetCubeBlock(pos);

                            if (newSlim != null && !newSlim.IsFullIntegrity)
                            {
                                comp.WorkSet.Add(newSlim);
                            }
                        }

                        comp.Working = true;
                        count++;

                        if (def.Debug) comp.DebugDrawBlock(slim, Color.Green);
                        continue;
                    }

                    if (!slim.IsFullIntegrity && !creative && !slim.CanContinueBuild(inventory))
                    {
                        if (def.IsTurret && modeData.Turret.ActiveTarget == slim) modeData.Turret.DeselectTarget();
                        if (def.Debug) comp.DebugDrawBlock(slim, Color.Blue);
                        continue;
                    }

                    if (!ToolSession.Instance.IsServer)
                    {
                        comp.Working = true;
                        count++;

                        if (def.CacheBlocks)
                        {
                            var integrityChange = weldAmount * cubeDef.IntegrityPointsPerSec;
                            if (integrityChange < slim.MaxIntegrity - slim.Integrity)
                            {
                                comp.ClientWorkSet.Add(new MyTuple<Vector3I, MyCubeGrid>(slim.Position, grid));
                            }
                        }

                        continue;
                    }

                    //if (welder != null)
                    //{
                    //    if (slim.WillBecomeFunctional(weldAmount) && !welder.IsWithinWorldLimits(gridComp.Projector, "", cubeDef.PCU - 1))
                    //        continue;
                    //}

                    comp.Working = true;
                    count++;
                    if (def.Debug) comp.DebugDrawBlock(slim, Color.Green);

                    slim.MoveItemsToConstructionStockpile(inventory);

                    slim.IncreaseMountLevel(weldAmount, ownerId, inventory, 0.15f, false);

                    //slim.MoveItemsFromConstructionStockpile(inventory);

                    if (def.CacheBlocks && !slim.IsFullIntegrity || slim.HasDeformation)
                    {
                        comp.WorkSet.Add(slim);
                    }

                }

                if (count >= maxBlocks)
                    return;
            }
        }

        private static bool TryPullComponents(this ToolComp comp)
        {
            var session = ToolSession.Instance;
            var inventory = comp.Inventory;
            var tool = comp.ToolEntity;

            var first = true;
            foreach (var component in session.MissingComponents)
            {
                var required = component.Value;
                var subtype = component.Key;
                var defId = new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype);

                var current = inventory.GetItemAmount(defId);
                var difference = required - current;
                MyFixedPoint pulled;
                if (comp.IsBlock && difference > 0 && inventory.CargoPercentage < 0.999f && !comp.FailedPulls.Contains(subtype))
                {
                    pulled = comp.Grid.ConveyorSystem.PullItem(defId, difference, tool, inventory, false);
                    current += pulled;
                    if (pulled < 1)
                    {
                        comp.FailedPulls.Add(subtype);
                    }
                }

                if (current < 1)
                {
                    if (first)
                        return false;

                    return true;
                }
                first = false;
            }
            return true;
        }

        private static bool OwnsDLC(MyCubeBlockDefinition def, ulong steamId)
        {
            foreach (var dlc in def.DLCs)
            {
                if (!MyAPIGateway.DLC.HasDLC(dlc, steamId))
                    return false;
            }

            return true;
        }

        private static void DebugDrawBlock(this ToolComp comp, IMySlimBlock slim, Color color)
        {
            var grid = (MyCubeGrid)slim.CubeGrid;
            var worldPos = grid.GridIntegerToWorld(slim.Position);
            var matrix = grid.PositionComp.WorldMatrixRef;
            matrix.Translation = worldPos;

            var sizeHalf = grid.GridSizeHalf - 0.05;
            var halfExtent = new Vector3D(sizeHalf, sizeHalf, sizeHalf);
            var bb = new BoundingBoxD(-halfExtent, halfExtent);
            var obb = new MyOrientedBoundingBoxD(bb, matrix);
            comp.DrawBoxes.Add(new MyTuple<MyOrientedBoundingBoxD, Color>(obb, color));
        }

        internal static void WeldBlocksBulk(this ToolComp comp)
        {
            var session = ToolSession.Instance;
            var modeData = comp.ModeData;
            var def = modeData.Definition;
            var inventory = comp.Inventory;
            var toolValues = comp.Values;
            var tool = comp.ToolEntity;
            var tempBlocks = session.TempBlocks;
            var maxBlocks = def.Rate;
            var ownerId = comp.IsBlock ? comp.BlockTool.OwnerId : comp.HandTool.OwnerIdentityId;
            var steamId = MyAPIGateway.Players.TryGetSteamId(ownerId);

            var missing = session.MissingComponents;
            var tempMissing = session.TempMissing;
            var creative = MyAPIGateway.Session.CreativeMode;
            var weldAmount = toolValues.Speed * MyAPIGateway.Session.WelderSpeedMultiplier;

            var remaining = maxBlocks;
            int i = 0, j = 0;
            while (remaining > 0 && i <= comp.MaxLayer)
            {
                tempBlocks.Clear();

                var tryCount = 0;
                while (i <= comp.MaxLayer)
                {
                    List<IMySlimBlock> layer;
                    if (!comp.HitBlockLayers.TryGetValue(i, out layer))
                    {
                        i++;
                        continue;
                    }

                    while (j < layer.Count)
                    {
                        if (tryCount >= remaining)
                            break;

                        var slim = layer[j];
                        j++;

                        var fat = slim.FatBlock;
                        var grid = slim.CubeGrid as MyCubeGrid;
                        if (slim.IsFullyDismounted || grid.MarkedForClose || fat != null && fat.MarkedForClose)
                        {
                            if (def.Debug) comp.DebugDrawBlock(slim, Color.Red);
                            continue;
                        }

                        var projector = grid.Projector as IMyProjector;
                        if (projector == null && slim.IsFullIntegrity && !slim.HasDeformation)
                        {
                            if (def.Debug) comp.DebugDrawBlock(slim, Color.White);
                            continue;
                        }

                        if (projector == null)
                        {
                            tempMissing.Clear();
                            slim.GetMissingComponents(tempMissing);
                            if (!creative && comp.FailedPulls.Contains(tempMissing.Keys.FirstOrDefault()))
                            {
                                if (def.IsTurret && modeData.Turret.ActiveTarget == slim) modeData.Turret.DeselectTarget();
                                if (def.Debug) comp.DebugDrawBlock(slim, Color.Pink);
                                continue;
                            }

                            foreach (var item in tempMissing)
                            {
                                var key = item.Key;
                                var value = item.Value;
                                if (missing.ContainsKey(key))
                                {
                                    missing[key] += value;
                                    continue;
                                }
                                missing[key] = value;
                            }

                            tempBlocks.Add(slim);
                            tryCount++;

                            continue;
                        }

                        var cubeDef = slim.BlockDefinition as MyCubeBlockDefinition;
                        var components = cubeDef.Components;
                        if (components != null && components.Length != 0)
                        {
                            var firstComp = components[0].Definition.Id.SubtypeName;
                            if (!creative && comp.FailedPulls.Contains(firstComp))
                            {
                                if (def.Debug) comp.DebugDrawBlock(slim, Color.Pink);
                                continue;
                            }

                            if (missing.ContainsKey(firstComp))
                                missing[firstComp] += 1;
                            else missing[firstComp] = 1;

                            tempBlocks.Add(slim);
                            tryCount++;
                        }
                    }

                    if (tryCount >= remaining)
                        break;

                    i++;
                    j = 0;
                }

                foreach (var component in missing)
                {
                    var required = component.Value;
                    if (required == 0)
                    {
                        Logs.WriteLine("Required component is zero");
                        continue;
                    }

                    var subtype = component.Key;
                    MyDefinitionId defId = new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype);

                    var current = inventory.GetItemAmount(defId);
                    var difference = required - current;
                    if (comp.IsBlock && difference > 0 && inventory.CargoPercentage < 0.999f && !comp.FailedPulls.Contains(subtype))
                    {
                        var pulled = comp.Grid.ConveyorSystem.PullItem(defId, difference, tool, inventory, false);
                        current += pulled;
                        if (pulled < 1)
                        {
                            comp.FailedPulls.Add(subtype);
                        }
                    }

                }
                missing.Clear();

                var successCount = 0;
                for (int k = 0; k < tempBlocks.Count; k++)
                {
                    var slim = tempBlocks[k];
                    var grid = (MyCubeGrid)slim.CubeGrid;

                    var cubeDef = slim.BlockDefinition as MyCubeBlockDefinition;
                    if (grid.Projector != null)
                    {
                        var projector = (IMyProjector)grid.Projector;
                        if (projector.CanBuild(slim, true) != BuildCheckResult.OK)
                        {
                            if (def.Debug) comp.DebugDrawBlock(slim, Color.Yellow);
                            continue;
                        }

                        if (!ToolSession.Instance.IsServer)
                        {
                            if (!creative && inventory.GetItemAmount(cubeDef.Components[0].Definition.Id) < 1)
                            {
                                if (def.IsTurret && modeData.Turret.ActiveTarget == slim) modeData.Turret.DeselectTarget();
                                continue;
                            }

                            comp.Working = true;
                            successCount++;

                            if (def.CacheBlocks && !creative)
                            {
                                var slimPos = projector.CubeGrid.WorldToGridInteger(slim.CubeGrid.GridIntegerToWorld(slim.Position));
                                comp.ClientWorkSet.Add(new MyTuple<Vector3I, MyCubeGrid>(slimPos, (MyCubeGrid)projector.CubeGrid));
                            }
                            continue;
                        }

                        if (steamId > 1 && cubeDef.DLCs != null && cubeDef.DLCs.Length > 0 && !OwnsDLC(cubeDef, steamId))
                        {
                            if (def.IsTurret && modeData.Turret.ActiveTarget == slim) modeData.Turret.DeselectTarget();
                            if (def.Debug) comp.DebugDrawBlock(slim, Color.Gold);
                            continue;
                        }

                        if (!creative && inventory.RemoveItemsOfType(1, cubeDef.Components[0].Definition.Id) < 1)
                        {
                            if (def.IsTurret && modeData.Turret.ActiveTarget == slim) modeData.Turret.DeselectTarget();
                            if (def.Debug) comp.DebugDrawBlock(slim, Color.Pink);
                            continue;
                        }

                        var builtBy = comp.IsBlock ? comp.BlockTool.SlimBlock.BuiltBy : ownerId;
                        //Temp check for MP active to avoid NRE in HasClientArmor
                        projector.Build(slim, session.IsMultiPlayer ? ownerId : MyAPIGateway.Session.Player.IdentityId, tool.EntityId, true, builtBy);

                        if (def.CacheBlocks)
                        {
                            var pos = projector.CubeGrid.WorldToGridInteger(slim.CubeGrid.GridIntegerToWorld(slim.Position));
                            var newSlim = projector.CubeGrid.GetCubeBlock(pos);

                            if (newSlim != null && !newSlim.IsFullIntegrity)
                            {
                                comp.WorkSet.Add(newSlim);
                            }
                        }
                        comp.Working = true;
                        successCount++;

                        if (def.Debug) comp.DebugDrawBlock(slim, Color.Green);
                        continue;
                    }

                    if (!slim.IsFullIntegrity && !creative && !slim.CanContinueBuild(inventory))
                    {
                        if (def.IsTurret && modeData.Turret.ActiveTarget == slim) modeData.Turret.DeselectTarget();
                        if (def.Debug) comp.DebugDrawBlock(slim, Color.Blue);
                        continue;
                    }

                    if (!session.IsServer)
                    {
                        comp.Working = true;
                        successCount++;

                        var integrityChange = weldAmount * cubeDef.IntegrityPointsPerSec;
                        if (def.CacheBlocks && integrityChange < slim.MaxIntegrity - slim.Integrity)
                            comp.ClientWorkSet.Add(new MyTuple<Vector3I, MyCubeGrid>(slim.Position, grid));

                        continue;
                    }

                    //if (welder != null)
                    //{
                    //    if (slim.WillBecomeFunctional(weldAmount) && !welder.IsWithinWorldLimits(gridComp.Projector, "", cubeDef.PCU - 1))
                    //        continue;
                    //}

                    comp.Working = true;
                    successCount++;
                    if (def.Debug) comp.DebugDrawBlock(slim, Color.Green);

                    slim.MoveItemsToConstructionStockpile(inventory);

                    slim.IncreaseMountLevel(weldAmount, ownerId, inventory, 0.15f, false);
                    //slim.MoveItemsFromConstructionStockpile(inventory);

                    if (def.CacheBlocks && !slim.IsFullIntegrity || slim.HasDeformation)
                    {
                        comp.WorkSet.Add(slim);
                    }
                }

                remaining -= successCount;
            }

        }

        #region Turret targeting

        internal static void GetBlockTargets(this ToolComp comp)
        {
            try
            {
                var modeData = comp.ModeData;
                var def = modeData.Definition;
                var toolValues = comp.Values;
                var data = comp.GridData;

                for (int i = 0; i < data.Grids.Count; i++)
                {
                    var grid = data.Grids[i];
                    var gridMatrixNI = grid.PositionComp.WorldMatrixNormalizedInv;
                    var localCentre = grid.WorldToGridScaledLocal(data.Position);

                    var gridSizeR = grid.GridSizeR;
                    var radius = def.Turret.TargetSphere.Radius * gridSizeR;

                    var minExtent = localCentre - radius;
                    var maxExtent = localCentre + radius;

                    var sMin = Vector3I.Round(minExtent);
                    var sMax = Vector3I.Round(maxExtent);

                    var gMin = grid.Min;
                    var gMax = grid.Max;

                    var min = Vector3I.Max(sMin, gMin);
                    var max = Vector3I.Min(sMax, gMax);

                    var count = 0;
                    int x, y, z;
                    for (x = min.X; x <= max.X; x++)
                    {
                        for (y = min.Y; y <= max.Y; y++)
                        {
                            for (z = min.Z; z <= max.Z; z++)
                            {
                                var pos = new Vector3I(x, y, z);

                                var posD = (Vector3D)pos;
                                Vector3D corner = Vector3D.Clamp(localCentre, posD - 0.5, posD + 0.5);

                                var distSqr = Vector3D.DistanceSquared(corner, localCentre);
                                if (distSqr > radius * radius)
                                    continue;

                                MyCube cube;
                                if (!grid.TryGetCube(pos, out cube))
                                    continue;

                                var slim = (IMySlimBlock)cube.CubeBlock;
                                if (!data.HitBlocksHash.Add(slim))
                                    continue;

                                if (slim.FatBlock != null && slim.FatBlock.MarkedForClose)
                                    continue;

                                var projector = grid.Projector as IMyProjector;
                                if (projector != null)
                                {
                                    var diff = Math.Abs(slim.Dithering + 0.25f);
                                    if (diff > 0.24f)
                                        continue;
                                }
                                else if (comp.Mode == ToolMode.Weld && slim.IsFullIntegrity && !slim.HasDeformation)
                                    continue;

                                var colour = projector?.SlimBlock.ColorMaskHSV ?? slim.ColorMaskHSV;
                                if (comp.UseWorkColour && colour.PackHSVToUint() != comp.WorkColourPacked)
                                    continue;

                                var layer = (int)Math.Ceiling(distSqr);
                                comp.MaxLayer = Math.Max(layer, comp.MaxLayer);

                                List<IMySlimBlock> list;
                                if (!comp.HitBlockLayers.TryGetValue(layer, out list))
                                {
                                    list = new List<IMySlimBlock>();
                                    comp.HitBlockLayers[layer] = list;
                                }
                                list.Add(slim);
                                count++;
                            }
                        }
                    }
                    data.HitBlocksHash.Clear();
                }
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }
            comp.CallbackComplete = false;
        }

        internal static void OnGetBlockTargetsComplete(this ToolComp comp)
        {
            try
            {
                var session = ToolSession.Instance;
                var turret = comp.ModeData.Turret;

                for (int i = comp.MaxLayer; i > 0; i--)
                {
                    List<IMySlimBlock> layer;
                    if (!comp.HitBlockLayers.TryGetValue(i, out layer))
                        continue;

                    for (int j = 0; j < layer.Count; j++)
                    {
                        var slim = layer[j];
                        var fat = slim.FatBlock;
                        var grid = slim.CubeGrid as MyCubeGrid;
                        if (slim.IsFullyDismounted || grid.MarkedForClose || fat != null && fat.MarkedForClose)
                        {
                            continue;
                        }
                        turret.Targets.Add(slim);
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }

            comp.MaxLayer = 0;
            comp.GridData.Clean();
            comp.HitBlockLayers.Clear();
            comp.CallbackComplete = true;
        }

        #endregion
    }
}
