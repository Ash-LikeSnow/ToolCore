using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolCore.Definitions;
using ToolCore.Definitions.Serialised;
using ToolCore.Session;
using ToolCore.Utils;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using static ToolCore.Definitions.ToolDefinition;

namespace ToolCore.Comp
{
    /// <summary>
    /// Holds all thrust block data
    /// </summary>
    internal partial class ToolComp : MyEntityComponentBase
    {
        internal readonly CoreGun GunBase;
        internal readonly MyInventory Inventory;
        internal readonly GridData GridData = new GridData();

        internal MyEntity ToolEntity;
        internal MyEntity Parent;
        internal IMyConveyorSorter BlockTool;
        internal IMyHandheldGunObject<MyDeviceBase> HandTool;
        internal MyResourceSinkComponent Sink;
        internal MyOrientedBoundingBoxD Obb;
        internal MyEntity3DSoundEmitter SoundEmitter;
        internal MyCubeGrid Grid;
        internal GridComp GridComp;
        internal ToolRepo Repo;

        internal Task GridsTask = new Task();
        internal IMyTerminalControlOnOffSwitch ShowInToolbarSwitch;

        internal ToolMode Mode;
        internal ToolAction Action;
        internal Trigger State;
        internal Trigger AvState;
        internal TargetTypes Targets = TargetTypes.All;

        internal readonly ConcurrentDictionary<string, float> Yields = new ConcurrentDictionary<string, float>();
        internal readonly List<MyTuple<Vector3I, MyCubeGrid>> ClientWorkSet = new List<MyTuple<Vector3I, MyCubeGrid>>();
        internal readonly Dictionary<int, List<IMySlimBlock>> HitBlockLayers = new Dictionary<int, List<IMySlimBlock>>();

        internal readonly Dictionary<ToolMode, ModeSpecificData> ModeMap = new Dictionary<ToolMode, ModeSpecificData>();

        internal readonly Dictionary<string, IMyModelDummy> Dummies = new Dictionary<string, IMyModelDummy>();
        internal readonly Dictionary<string, MyEntitySubpart> Subparts = new Dictionary<string, MyEntitySubpart>();
        internal readonly Dictionary<IMyModelDummy, MyEntity> DummyMap = new Dictionary<IMyModelDummy, MyEntity>();

        internal readonly ConcurrentCachingList<MyTuple<MyOrientedBoundingBoxD, Color>> DrawBoxes = new ConcurrentCachingList<MyTuple<MyOrientedBoundingBoxD, Color>>();
        internal readonly List<Effects> ActiveEffects = new List<Effects>();
        internal readonly List<Action<int, bool>> EventMonitors = new List<Action<int, bool>>();
        internal readonly List<ulong> ReplicatedClients = new List<ulong>();
        internal readonly List<IMySlimBlock> WorkSet = new List<IMySlimBlock>();

        internal readonly HashSet<string> FailedPulls = new HashSet<string>();
        internal readonly HashSet<string> FailedPushes = new HashSet<string>();

        internal readonly bool IsBlock;
        internal readonly bool HasTargetControls;

        internal bool Enabled = true;
        internal bool Functional = true;
        internal bool Powered = true;
        internal bool FullInit;
        internal bool Dirty;
        internal bool TargetsDirty;
        internal bool AvActive;
        internal bool UpdatePower;
        internal bool LastPushSucceeded = true;
        internal bool Broken;

        internal bool Draw;
        internal bool _trackTargets;
        internal bool TrackTargets
        {
            get { return _trackTargets; }
            set
            {
                if (_trackTargets == value)
                    return;

                _trackTargets = value;

                UpdateAvState(Trigger.Activated, value);
                if (!value)
                {
                    //WasHitting = false;
                    UpdateHitInfo(false);
                }
            }
        }
        internal bool UseWorkColour;

        internal bool Working;
        internal bool WasHitting;

        internal readonly Hit HitInfo = new Hit();
        internal MyStringHash HitMaterial = MyStringHash.GetOrCompute("Metal");

        internal int CompTick10;
        internal int CompTick20;
        internal int CompTick60;
        internal int CompTick120;
        internal int LastPushTick;
        internal int ActiveThreads;
        internal int BrokenTick;

        internal int LastGridsTaskTick;

        internal volatile bool CallbackComplete = true;
        internal volatile int MaxLayer;

        internal uint WorkColourPacked;

        private Vector3 _workColour;
        internal Vector3 WorkColour
        {
            get
            {
                return _workColour;
            }
            set
            {
                _workColour = value;

                WorkColourPacked = _workColour.PackHSVToUint();
            }
        }

        private bool _activated;

        internal bool Activated
        {
            get { return _activated; }
            set
            {
                if (_activated == value)
                    return;

                if (value && !(Functional && Powered && Enabled))
                    return;

                _activated = value;

                UpdateAvState(Trigger.Activated, value);
                if (!value)
                {
                    //WasHitting = false;
                    UpdateHitInfo(false);
                }
            }
        }

        internal ActionDefinition Values
        {
            get
            {
                var action = GunBase.Shooting ? GunBase.GunAction : Action;
                var modeData = ModeMap[Mode];
                return modeData.Definition.ActionMap[action];
            }
        }

        internal ModeSpecificData ModeData
        {
            get
            {
                return ModeMap[Mode];
            }
        }

        internal ToolComp(MyEntity tool, List<ToolDefinition> defs)
        {
            ToolEntity = tool;
            BlockTool = tool as IMyConveyorSorter;
            HandTool = tool as IMyHandheldGunObject<MyDeviceBase>;
            GunBase = new CoreGun(this);

            var debug = false;
            foreach (var def in defs)
            {
                var workTick = (int)(ToolEntity.EntityId % def.UpdateInterval);
                var data = new ModeSpecificData(def, workTick);

                foreach (var mode in def.ToolModes)
                {
                    ModeMap[mode] = data;
                }

                if (def.EffectShape == EffectShape.Cuboid)
                    Obb = new MyOrientedBoundingBoxD();

                HasTargetControls |= def.ShowTargetControls ?? (def.IsTurret || def.EffectSphere.Radius >= 50);
                debug |= def.Debug;
            }

            Mode = ModeMap.Keys.FirstOrDefault();


            CompTick10 = (int)(ToolEntity.EntityId % 10);
            CompTick20 = (int)(ToolEntity.EntityId % 20);
            CompTick60 = (int)(ToolEntity.EntityId % 60);
            CompTick120 = (int)(ToolEntity.EntityId % 120);

            if (!MyAPIGateway.Session.IsServer)
                ToolSession.Instance.Networking.SendPacketToServer(new ReplicationPacket { EntityId = ToolEntity.EntityId, Add = true, PacketType = (byte)PacketType.Replicate });

            IsBlock = BlockTool != null;
            if (!IsBlock)
            {
                Parent = MyEntities.GetEntityById(HandTool.OwnerId);
                if (Parent == null)
                {
                    Logs.WriteLine("Hand tool owner null on init");
                    return;
                }

                Inventory = Parent.GetInventory(0);
                if (Inventory == null)
                    Logs.WriteLine("Hand tool owner inventory null on init");

                Draw = debug;

                return;
            }
            Inventory = (MyInventory)ToolEntity.GetInventoryBase();
            Parent = Grid = BlockTool.CubeGrid as MyCubeGrid;
            ToolSession.Instance.GridMap.TryGetValue(BlockTool.CubeGrid, out GridComp);

            BlockTool.EnabledChanged += EnabledChanged;
            BlockTool.IsWorkingChanged += IsWorkingChanged;

            Enabled = BlockTool.Enabled;
            Functional = BlockTool.IsFunctional;

            SinkInit();

            if (!ToolSession.Instance.IsDedicated)
            {
                GetShowInToolbarSwitch();
                BlockTool.AppendingCustomInfo += AppendingCustomData;
                RefreshTerminal();
            }
        }

        internal void FunctionalInit()
        {
            FullInit = true;

            var hasSound = false;
            foreach (var item in ModeMap)
            {
                var data = item.Value;
                if (data.FullInit)
                    continue;

                data.FullInit = true;
                var def = data.Definition;

                var effectsMap = data.EffectsMap;
                foreach (var pair in def.EventEffectDefs)
                {
                    var trigger = pair.Key;
                    var effectLists = pair.Value;

                    var effects = new Effects(effectLists.Item1, effectLists.Item2, effectLists.Item3, effectLists.Item4, this);
                    effectsMap[trigger] = effects;

                    hasSound |= effects.HasSound;
                }

                if (!def.IsTurret)
                    continue;

                foreach (var other in ModeMap.Values)
                {
                    if (other.Turret == null)
                        continue;

                    var same = true;
                    foreach (var subpart in def.Turret.Subparts)
                    {
                        var isPart1 = subpart.Name == other.Turret.Part1.Definition.Name;
                        var isPart2 = other.Turret.HasTwoParts && subpart.Name == other.Turret.Part2.Definition.Name;
                        if (!isPart1 && !isPart2)
                        {
                            same = false;
                            break;
                        }
                    }
                    if (same)
                    {
                        data.Turret = other.Turret;
                        break;
                    }
                }

                if (data.Turret == null)
                {
                    data.Turret = new TurretComp(def, this);
                }
            }

            LoadModels(true);

            if (hasSound)
                SoundEmitter = new MyEntity3DSoundEmitter(ToolEntity);

            UpdateAvState(Trigger.Functional, true);
            StorageInit();
        }

        internal void LoadModels(bool init = false)
        {
            GetDummiesAndSubpartsRecursive(ToolEntity);

            var functional = !IsBlock || BlockTool.IsFunctional;
            foreach (var entity in Subparts.Values)
            {
                entity.OnClose += (ent) => Dirty = true;
                entity.NeedsWorldMatrix = true;
            }
            ToolEntity.NeedsWorldMatrix = true;

            foreach (var mode in ModeMap.Values)
            {
                mode.UpdateModelData(this, init);
            }

            Dirty = false;
            Subparts.Clear();
            Dummies.Clear();
            DummyMap.Clear();
        }

        private void GetDummiesAndSubpartsRecursive(MyEntity entity)
        {
            var model = ((IMyEntity)entity).Model;
            if (model == null)
            {
                Logs.WriteLine($"Model for {BlockTool?.DisplayNameText ?? HandTool.DefinitionId.SubtypeName} was null!");
                return;
            }

            try
            {
                model.GetDummies(Dummies);
            }
            catch (Exception ex)
            {
                var modelName = model.AssetName;
                if (!string.IsNullOrEmpty(modelName))
                {
                    var lastSlash = modelName.LastIndexOf('\\');
                    Logs.WriteLine($"Failed to get dummies from {modelName.Substring(lastSlash + 1)} - probably a duplicate empty name in another scene!");
                }
                Logs.LogException(ex);
            }

            foreach (var dummy in Dummies.Values)
            {
                if (dummy == null)
                {
                    Logs.WriteLine("Somehow, a dummy was null...");
                    continue;
                }

                if (DummyMap.ContainsKey(dummy))
                    continue;

                DummyMap.Add(dummy, entity);
            }

            var subparts = entity.Subparts;
            if (subparts == null || subparts.Count == 0)
                return;

            foreach (var item in subparts)
            {
                var subpart = item.Value;
                if (subpart == null)
                {
                    Logs.WriteLine("Somehow, a subpart was null...");
                    continue;
                }

                Subparts.Add(item.Key, item.Value);

                GetDummiesAndSubpartsRecursive(item.Value);
            }
        }

        internal void ChangeGrid()
        {
            Grid = (MyCubeGrid)BlockTool.CubeGrid;
            ToolSession.Instance.GridMap.TryGetValue(BlockTool.CubeGrid, out GridComp);
        }

        private void AppendingCustomData(IMyTerminalBlock block, StringBuilder builder)
        {
            var modeData = ModeData;
            if (modeData.Turret == null)
                return;

            var turret = modeData.Turret;
            var target = turret.HasTarget ? turret.ActiveTarget.FatBlock?.DisplayNameText ?? turret.ActiveTarget.BlockDefinition.DisplayNameText : "None";

            builder.Append("Target: ")
                .Append(target)
                .Append("\n");
        }

        internal class ModeSpecificData
        {
            internal readonly ToolDefinition Definition;
            internal readonly Dictionary<Trigger, Effects> EffectsMap = new Dictionary<Trigger, Effects>();

            internal readonly int WorkTick;

            internal TurretComp Turret;
            internal MyEntity MuzzlePart;
            internal IMyModelDummy Muzzle;

            internal bool FullInit;
            internal bool HasEmitter;

            internal ModeSpecificData(ToolDefinition def, int workTick)
            {
                Definition = def;
                WorkTick = workTick;
            }

            internal void UpdateModelData(ToolComp comp, bool init = false)
            {
                if (!init)
                {
                    if (!ToolSession.Instance.IsDedicated)
                    {
                        foreach (var effects in EffectsMap.Values)
                        {
                            effects.UpdateModelData(comp);
                        }
                    }
                }

                if (Definition.IsTurret)
                    Turret.UpdateModelData(comp);

                if (string.IsNullOrEmpty(Definition.EmitterName) || !comp.Dummies.TryGetValue(Definition.EmitterName, out Muzzle))
                    return;

                HasEmitter = true;
                MuzzlePart = comp.DummyMap[Muzzle];

                var functional = !comp.IsBlock || comp.BlockTool.IsFunctional;
                if (!HasEmitter && functional && Definition.Location == Location.Emitter)
                    Definition.Location = Location.Centre;
            }
        }

        internal class PositionData
        {
            internal int Index;
            internal float Distance;
            internal float Distance2;
            internal bool Contained;
            internal Vector3D Position;
            internal StorageInfo StorageInfo;

            public PositionData(int index, float distance, StorageInfo info)
            {
                Index = index;
                Distance = distance;
                StorageInfo = info;
            }

            public PositionData(int index, float distance, float distance2)
            {
                Index = index;
                Distance = distance;
                Distance2 = distance2;
            }

            public PositionData(int index, float distance, float distance2, StorageInfo info)
            {
                Index = index;
                Distance = distance;
                Distance2 = distance2;
                StorageInfo = info;
            }

            public PositionData(int index, float distance, Vector3D position, bool contained)
            {
                Index = index;
                Distance = distance;
                Position = position;
                Contained = contained;
            }
        }

        internal class StorageInfo
        {
            internal Vector3I Min;
            internal Vector3I Max;
            internal bool Dirty;

            public StorageInfo(Vector3I min, Vector3I max, bool dirty = false)
            {
                Min = min;
                Max = max;
                Dirty = dirty;
            }
        }

        internal enum ToolMode
        {
            Drill = 4,
            Weld = 8,
            Grind = 16,
        }

        internal enum ToolAction
        {
            Primary = 0,
            Secondary = 1,
            Tertiary = 2,
        }

        [Flags]
        internal enum TargetTypes : byte
        {
            None = 0,
            Own = 1,
            Friendly = 2,
            Neutral = 4,
            Hostile = 8,
            All = 15,
        }

        internal class Hit
        {
            internal Vector3D Position;
            internal MyStringHash Material;
            internal bool IsValid;
        }

        internal void UpdateHitInfo(bool valid, Vector3D? pos = null, MyStringHash? material = null)
        {
            if (valid)
            {
                HitInfo.Position = pos.Value;
                HitInfo.Material = material.Value;

                if (HitInfo.IsValid)
                    return;

                UpdateAvState(Trigger.RayHit, true);
                HitInfo.IsValid = true;
                return;
            }

            if (!HitInfo.IsValid)
                return;

            UpdateAvState(Trigger.RayHit, false);
            HitInfo.IsValid = false;
        }

        internal TargetTypes GetRelationToPlayer(long playerId, IMyFaction toolFaction)
        {
            var ownerId = IsBlock ? BlockTool.OwnerId : HandTool.OwnerIdentityId;
            if (ownerId == playerId)
            {
                return TargetTypes.Own;
            }

            if (ownerId == 0 || playerId == 0)
            {
                return TargetTypes.Neutral;
            }

            if (toolFaction == null)
            {
                return TargetTypes.Hostile;
            }

            var targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
            if (targetFaction == null)
            {
                return TargetTypes.Hostile;
            }

            if (toolFaction == targetFaction)
            {
                return TargetTypes.Friendly;
            }

            var factionRelation = MyAPIGateway.Session.Factions.GetRelationBetweenFactions(toolFaction.FactionId, targetFaction.FactionId);
            if (factionRelation == MyRelationsBetweenFactions.Enemies)
            {
                return TargetTypes.Hostile;
            }

            if (factionRelation == MyRelationsBetweenFactions.Friends)
            {
                return TargetTypes.Friendly;
            }

            return TargetTypes.Neutral;
        }

        internal void SetMode(ToolMode newMode)
        {
            var oldData = ModeMap[Mode];
            var newData = ModeMap[newMode];

            Mode = newMode;

            if (oldData == newData)
                return;

            foreach (var effects in oldData.EffectsMap.Values)
            {
                effects.Expired = effects.Active;
            }

            foreach (var map in newData.EffectsMap)
            {
                var trigger = map.Key;
                if ((trigger & AvState) == 0)
                    continue;

                var effects = map.Value;
                if (!effects.Active)
                {
                    ActiveEffects.Add(effects);
                    effects.Active = true;
                    continue;
                }

                if (effects.Expired)
                {
                    effects.Expired = false;
                    effects.SoundStopped = false;
                    effects.Restart = true;
                }
            }
        }

        internal void UpdateAvState(Trigger state, bool add)
        {
            //Logs.WriteLine($"UpdateAvState() {state} {add} {force}");
            var data = ModeData;

            var keepFiring = !add && (Activated || GunBase.Shooting) && (state & Trigger.Firing) > 0;

            foreach (var flag in ToolSession.Instance.Triggers)
            {
                if (add && (flag & state) == 0)
                    continue;

                if (keepFiring || flag < state)
                    continue;

                if (add) State |= flag;
                else State &= ~flag;

                if ((flag & data.Definition.EventFlags) == 0)
                    continue;

                if (add) AvState |= flag;
                else AvState &= ~flag;

                foreach (var monitor in EventMonitors)
                    monitor.Invoke((int)state, add);

                UpdateEffects(flag, add);

                if (!add) // maybe remove this later :|
                {
                    if (flag == Trigger.Hit) WasHitting = false;
                    if (flag == Trigger.RayHit) HitInfo.IsValid = false;
                }
            }
        }

        internal void UpdateEffects(Trigger state, bool add)
        {
            if (ToolSession.Instance.IsDedicated) return; //TEMPORARY!!! or not?

            Effects effects;
            if (!ModeData.EffectsMap.TryGetValue(state, out effects))
                return;

            if (!add)
            {
                effects.Expired = effects.Active;
                return;
            }

            if (!effects.Active)
            {
                ActiveEffects.Add(effects);
                effects.Active = true;
                return;
            }

            if (effects.Expired)
            {
                effects.Expired = false;
                effects.SoundStopped = false;
                effects.Restart = true;
            }
        }

        internal bool IsPowered()
        {
            if (Sink == null)
            {
                return Powered = false;
            }

            Sink.Update();
            var required = RequiredInput();
            var elec = MyResourceDistributorComponent.ElectricityId;
            var distributor = (MyResourceDistributorComponent)(BlockTool.CubeGrid).ResourceDistributor;
            var isPowered = MyUtils.IsEqual(required, 0f) || Sink.IsPoweredByType(elec) && (Sink.ResourceAvailableByType(elec) >= required || distributor != null && distributor.MaxAvailableResourceByType(elec) >= required);

            return Powered = isPowered;
        }

        private void EnabledChanged(IMyTerminalBlock block)
        {
            Enabled = (block as IMyFunctionalBlock).Enabled;

            Sink.Update();
            UpdatePower = true;

            if (!Enabled)
            {
                WasHitting = false;
                UpdateHitInfo(false);
            }

            if (!Powered) return;

            UpdateAvState(Trigger.Enabled, Enabled);
        }

        private void IsWorkingChanged(IMyCubeBlock block)
        {
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (!MyAPIGateway.Session.IsServer)
                ToolSession.Instance.Networking.SendPacketToServer(new ReplicationPacket { EntityId = ToolEntity.EntityId, Add = true, PacketType = (byte)PacketType.Replicate });
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();

            Close();
        }

        public override bool IsSerialized()
        {
            if (ToolEntity.Storage == null || Repo == null) return false;

            Repo.Sync(this);
            ToolEntity.Storage[ToolSession.Instance.CompDataGuid] = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(Repo));

            return false;
        }

        private void SinkInit()
        {
            var sinkInfo = new MyResourceSinkInfo()
            {
                MaxRequiredInput = ModeData.Definition.ActivePower,
                RequiredInputFunc = RequiredInput,
                ResourceTypeId = MyResourceDistributorComponent.ElectricityId
            };

            Sink = ToolEntity.Components?.Get<MyResourceSinkComponent>();
            if (Sink != null)
            {
                Sink.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, RequiredInput);
            }
            else
            {
                Logs.WriteLine("No sink found on init, creating!");
                Sink = new MyResourceSinkComponent();
                Sink.Init(MyStringHash.GetOrCompute("Defense"), sinkInfo);
                ToolEntity.Components.Add(Sink);
            }

            var distributor = (MyResourceDistributorComponent)BlockTool.CubeGrid.ResourceDistributor;
            if (distributor == null)
            {
                Logs.WriteLine("Grid distributor null on sink init!");
                return;
            }

            distributor.AddSink(Sink);
            Sink.Update();
        }

        private float RequiredInput()
        {
            if (!Functional || !Enabled)
                return 0f;

            if (Activated || Working || GunBase.WantsToShoot)
                return ModeData.Definition.ActivePower;

            return ModeData.Definition.IdlePower;
        }

        internal void OnDrillComplete(WorkData data)
        {
            var session = ToolSession.Instance;
            var drillData = (DrillData)data;
            var storageDatas = drillData.StorageDatas;
            if (drillData?.Voxel?.Storage == null)
            {
                Logs.WriteLine($"Null reference in OnDrillComplete - DrillData null: {drillData == null} - Voxel null: {drillData?.Voxel == null}");
            }
            for (int i = storageDatas.Count - 1; i >= 0; i--)
            {
                var info = storageDatas[i];
                if (!info.Dirty)
                    continue;

                drillData?.Voxel?.Storage?.NotifyRangeChanged(ref info.Min, ref info.Max, MyStorageDataTypeFlags.ContentAndMaterial);
            }

            drillData.Clean();
            session.DrillDataPool.Push(drillData);

            ActiveThreads--;
            if (ActiveThreads > 0) return;

            var isHitting = Functional && Powered && Enabled && Working && (Activated || GunBase.Shooting);
            if (isHitting != WasHitting)
            {
                UpdateAvState(Trigger.Hit, isHitting);
                WasHitting = isHitting;
            }
            Working = false;
        }

        internal void ManageInventory()
        {
            var session = ToolSession.Instance;
            var tryUpdate = ToolSession.Tick - LastPushTick > 1200;
            //Logs.WriteLine($"ManageInventory() {LastPushSucceeded} : {ToolSession.Tick - LastPushTick}");

            foreach (var ore in Yields.Keys)
            {
                var gross = Yields[ore];
                if (!LastPushSucceeded && !tryUpdate && FailedPushes.Contains(ore))
                {
                    session.TempItems[ore] = gross;
                    continue;
                }
                var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(ore);
                var itemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(oreOb);
                var itemVol = itemDef.Volume;
                var amount = (MyFixedPoint)(gross / itemDef.Volume);
                //Logs.WriteLine($"Holding {amount} ore");

                MyFixedPoint transferred;
                Grid.ConveyorSystem.PushGenerateItem(itemDef.Id, amount, out transferred, BlockTool, false);
                //Logs.WriteLine($"Pushed {transferred}");

                amount -= transferred;
                if (amount == MyFixedPoint.Zero)
                {
                    LastPushSucceeded = true;
                    FailedPushes.Remove(ore);
                    continue;
                }

                if (FailedPushes.Add(ore))
                {
                    session.TempItems[ore] = (float)amount * itemVol;
                    LastPushSucceeded = true;
                    continue;
                }

                LastPushSucceeded = false;

                var fits = Inventory.ComputeAmountThatFits(itemDef.Id);
                var toAdd = amount;
                if (fits < amount)
                {
                    toAdd = fits;
                    amount -= fits;
                    session.TempItems[ore] = (float)amount * itemVol;
                }
                Inventory.AddItems(toAdd, oreOb);
                //Logs.WriteLine($"Added {toAdd}");
            }

            Yields.Clear();
            foreach (var yield in session.TempItems)
            {
                Yields[yield.Key] = yield.Value;
            }
            session.TempItems.Clear();

            if (!(LastPushSucceeded || tryUpdate) || Inventory.Empty())
                return;

            var items = new List<MyPhysicalInventoryItem>(Inventory.GetItems());
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                MyFixedPoint transferred;
                LastPushSucceeded = Grid.ConveyorSystem.PushGenerateItem(item.Content.GetId(), item.Amount, out transferred, BlockTool, false);
                if (!LastPushSucceeded)
                    break;

                Inventory.RemoveItems(item.ItemId, transferred);
            }

        }

        private void GetShowInToolbarSwitch()
        {
            List<IMyTerminalControl> items;
            MyAPIGateway.TerminalControls.GetControls<IMyUpgradeModule>(out items);

            foreach (var item in items)
            {

                if (item.Id == "ShowInToolbarConfig")
                {
                    ShowInToolbarSwitch = (IMyTerminalControlOnOffSwitch)item;
                    break;
                }
            }
        }

        internal void RefreshTerminal()
        {
            BlockTool.RefreshCustomInfo();

            if (ShowInToolbarSwitch != null)
            {
                var originalSetting = ShowInToolbarSwitch.Getter(BlockTool);
                ShowInToolbarSwitch.Setter(BlockTool, !originalSetting);
                ShowInToolbarSwitch.Setter(BlockTool, originalSetting);
            }
        }

        private void StorageInit()
        {
            string rawData;
            ToolRepo loadRepo = null;
            if (ToolEntity.Storage == null)
            {
                ToolEntity.Storage = new MyModStorageComponent();
            }
            else if (ToolEntity.Storage.TryGetValue(ToolSession.Instance.CompDataGuid, out rawData))
            {
                try
                {
                    var base64 = Convert.FromBase64String(rawData);
                    loadRepo = MyAPIGateway.Utilities.SerializeFromBinary<ToolRepo>(base64);
                }
                catch (Exception ex)
                {
                    Logs.LogException(ex);
                }
            }

            if (loadRepo != null)
            {
                Sync(loadRepo);
            }
            else
            {
                Repo = new ToolRepo();
            }
        }

        private void Sync(ToolRepo repo)
        {
            Repo = repo;

            Activated = repo.Activated;
            Draw = repo.Draw;
            Mode = (ToolMode)repo.Mode;
            Action = (ToolAction)repo.Action;
            Targets = (TargetTypes)repo.Targets;
            UseWorkColour = repo.UseWorkColour;
            WorkColour = repo.WorkColour;
            TrackTargets = repo.TrackTargets;
        }

        internal void Close()
        {
            if (!ToolSession.Instance.IsServer)
                ToolSession.Instance.Networking.SendPacketToServer(new ReplicationPacket { EntityId = ToolEntity.EntityId, Add = false, PacketType = (byte)PacketType.Replicate });

            Clean();

            if (IsBlock)
            {
                BlockTool.EnabledChanged -= EnabledChanged;
                BlockTool.IsWorkingChanged -= IsWorkingChanged;

                if (!ToolSession.Instance.IsDedicated)
                    BlockTool.AppendingCustomInfo -= AppendingCustomData;

                return;
            }

            ToolSession.Instance.HandTools.Remove(this);
        }

        internal void Clean()
        {
            Grid = null;
            GridComp = null;

            ShowInToolbarSwitch = null;
        }

        public override string ComponentTypeDebugString => "ToolCore";
    }
}
