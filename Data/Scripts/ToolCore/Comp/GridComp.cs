using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System.Collections.Generic;
using ToolCore.Session;
using ToolCore.Utils;
using VRage.Game.ModAPI;

namespace ToolCore.Comp
{
    /// <summary>
    /// Holds all grid specific data
    /// </summary>
    internal class GridComp
    {
        internal MyCubeGrid Grid;
        internal GroupMap GroupMap;

        internal readonly List<ToolComp> ToolComps = new List<ToolComp>();

        internal long CompTick60;
        internal long CompTick20;

        internal bool UnderControl;
        internal bool Dirty;

        internal void Init(MyCubeGrid grid)
        {
            Grid = grid;

            CompTick20 = Grid.EntityId % 20;
            CompTick60 = Grid.EntityId % 60;

            Grid.OnFatBlockAdded += FatBlockAdded;
            Grid.OnFatBlockRemoved += FatBlockRemoved;

            var group = MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Logical, grid);
            if (group != null)
            {
                GroupMap map;
                if (ToolSession.Instance.GridGroupMap.TryGetValue(group, out map))
                    GroupMap = map;
            }
            else Logs.WriteLine("group null at GridComp.Init()");

            foreach (var block in Grid.GetFatBlocks())
            {
                if (block is IMyConveyorSorter)
                    FatBlockAdded(block);
            }

        }

        internal void Clean()
        {

            Grid.OnFatBlockAdded -= FatBlockAdded;
            Grid.OnFatBlockRemoved -= FatBlockRemoved;

            Grid = null;

            ToolComps.Clear();
        }

        internal void UpdateWeaponSys()
        {
            var weaponSystem = ((IMyCubeGrid)Grid).WeaponSystem;
            foreach (var comp in ToolComps)
                weaponSystem.Register(comp.GunBase);
        }

        internal void FatBlockAdded(MyCubeBlock block)
        {
            if (block is IMyConveyorSorter)
            {
                ToolComp comp;
                if (ToolSession.Instance.ToolMap.TryGetValue(block.EntityId, out comp) && !ToolComps.Contains(comp))
                {
                    ToolComps.Add(comp);
                    ((IMyCubeGrid)Grid).WeaponSystem.Register(comp.GunBase);
                }
            }
        }

        private void FatBlockRemoved(MyCubeBlock block)
        {
            if (block is IMyConveyorSorter)
            {
                ToolComp comp;
                if (ToolSession.Instance.ToolMap.TryGetValue(block.EntityId, out comp) && comp?.GunBase != null && ToolComps.Remove(comp))
                {
                    if (!Grid.MarkedForClose)
                    {
                        var weaponSystem = ((IMyCubeGrid)Grid).WeaponSystem;
                        weaponSystem.Register(comp.GunBase);
                        weaponSystem.Unregister(comp.GunBase);
                    }
                }
            }
        }
    }

    internal class GroupMap
    {
        internal IMyGridGroupData GroupData;

        internal HashSet<IMyCubeGrid> ConnectedGrids = new HashSet<IMyCubeGrid>();

        internal void Init(IMyGridGroupData data)
        {
            GroupData = data;
        }

        public void OnGridAdded(IMyGridGroupData newGroup, IMyCubeGrid grid, IMyGridGroupData oldGroup)
        {
            ConnectedGrids.Add(grid);

            GridComp gridComp;
            if (!ToolSession.Instance.GridMap.TryGetValue(grid, out gridComp))
                return;

            gridComp.GroupMap = this;
            gridComp.UpdateWeaponSys();
        }

        public void OnGridRemoved(IMyGridGroupData oldGroup, IMyCubeGrid grid, IMyGridGroupData newGroup)
        {
            ConnectedGrids.Remove(grid);
        }

        internal void Clean()
        {
            GroupData = null;

            ConnectedGrids.Clear();
        }
    }

}
