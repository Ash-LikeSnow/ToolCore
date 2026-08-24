using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using IMyTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;
using IMyCubeGrid = VRage.Game.ModAPI.Ingame.IMyCubeGrid;
using ToolCore.Session;
using VRage.Game.Entity;
using ToolCore.Comp;
using VRage.Game;
using VRage.Utils;

namespace ToolCore.API
{
    internal class APIBackend
    {
        internal readonly Dictionary<string, Delegate> ModApiMethods;
        internal Dictionary<string, Delegate> PbApiMethods;

        private readonly ToolSession _session;

        internal APIBackend(ToolSession session)
        {
            _session = session;

            ModApiMethods = new Dictionary<string, Delegate>
            {
                ["RegisterEventMonitor"] = new Action<MyEntity, Action<int, bool>>(RegisterEventMonitorCallback),
                ["UnRegisterEventMonitor"] = new Action<MyEntity, Action<int, bool>>(UnRegisterEventMonitorCallback),
                ["GetTCDefs"] = new Action<ICollection<MyStringHash>>(TCGetDefs),
            };
        }


        internal void PbInit()
        {
            PbApiMethods = new Dictionary<string, Delegate>
            {
                ["RegisterEventMonitor"] = new Action<IMyTerminalBlock, Action<int, bool>>(PbRegisterEventMonitorCallback),
                ["UnRegisterEventMonitor"] = new Action<IMyTerminalBlock, Action<int, bool>>(PbUnRegisterEventMonitorCallback),
            };
            var pb = MyAPIGateway.TerminalControls.CreateProperty<IReadOnlyDictionary<string, Delegate>, Sandbox.ModAPI.IMyTerminalBlock>("ToolCorePbAPI");
            pb.Getter = b => PbApiMethods;
            MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(pb);
            _session.PbApiInited = true;
        }
        private void TCGetDefs(ICollection<MyStringHash> collection)
        {
            foreach (var def in _session.DefinitionMap)
                collection.Add(MyStringHash.GetOrCompute(def.Key.SubtypeId));
        }

        private void PbRegisterEventMonitorCallback(IMyTerminalBlock tool, Action<int, bool> callBack) => RegisterEventMonitorCallback((MyEntity)tool, callBack);
        private void RegisterEventMonitorCallback(MyEntity tool, Action<int, bool> callBack)
        {
            var comp = tool.Components.Get<ToolComp>();
            if (comp == null)
                return;

            comp.EventMonitors.Add(callBack);
        }

        private void PbUnRegisterEventMonitorCallback(IMyTerminalBlock tool, Action<int, bool> callBack) => UnRegisterEventMonitorCallback((MyEntity)tool, callBack);
        private void UnRegisterEventMonitorCallback(MyEntity tool, Action<int, bool> callBack)
        {
            var comp = tool.Components.Get<ToolComp>();
            if (comp == null)
                return;

            comp.EventMonitors.Remove(callBack);
        }

    }
}
