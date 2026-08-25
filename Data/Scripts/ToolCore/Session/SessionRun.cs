using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using ToolCore.Utils;
using VRage.Game.Components;
using VRage.Utils;

namespace ToolCore.Session
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.AfterSimulation)]
    internal partial class ToolSession : MySessionComponentBase
    {
        internal static int Tick;
        internal int TickMod10;
        internal int TickMod20;
        internal int TickMod60;
        internal int TickMod120;
        internal bool Tick10;
        internal bool Tick20;
        internal bool Tick60;
        internal bool Tick120;
        internal bool Tick600;

        internal bool IsServer;
        internal bool IsDedicated;
        internal bool IsMultiPlayer;

        private bool FirstRun = true;

        public override void LoadData()
        {
            IsServer = MyAPIGateway.Session.IsServer;
            IsMultiPlayer = MyAPIGateway.Multiplayer.MultiplayerActive;
            IsDedicated = MyAPIGateway.Utilities.IsDedicated;

            MyEntities.OnEntityCreate += OnEntityCreate;
            MyEntities.OnCloseAll += OnCloseAll;

            Logs.InitLogs();

            LoadDefinitions();
            LoadToolCoreDefs();

            if (!IsDedicated)
            {
                MyAPIGateway.TerminalControls.CustomActionGetter += Controls.CustomActionGetter;
                MyAPIGateway.TerminalControls.CustomControlGetter += Controls.CustomControlGetter;
            }
        }

        public override void BeforeStart()
        {
            if (IsMultiPlayer)
            {
                if (IsServer)
                {
                    MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(Networking.ServerPacketId, Networking.ProcessPacket);

                    MyVisualScriptLogicProvider.PlayerDisconnected += PlayerDisconnected;
                    MyVisualScriptLogicProvider.PlayerRespawnRequest += PlayerConnected;
                }
                else
                {
                    MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(Networking.ClientPacketId, Networking.ProcessPacket);
                }
            }
            foreach (var faction in MyAPIGateway.Session.Factions.Factions)
            {
                if (!faction.Value.AcceptHumans || faction.Value.IsEveryoneNpc())
                {
                    foreach (var mbr in faction.Value.Members.Values)
                        npcIDList.Add(mbr.PlayerId);
                }
            }

            APIServer.Load();
            DSAPI.Load();
            MGPAPI = new MultigridProjector.Api.MultigridProjectorModAgent();

            if (IsServer)
            {
                Settings.LoadConfigFile();
                LoadSettings(Settings.CoreSettings);
            }
            //Controls.CreateTerminalControls<IMyConveyorSorter>();

        }

        public override void UpdateBeforeSimulation()
        {
            Tick++;
            TickMod10 = Tick % 10;
            TickMod20 = Tick % 20;
            TickMod60 = Tick % 60;
            TickMod120 = Tick % 120;

            Tick10 = Tick % 10 == 0;
            Tick20 = TickMod20 == 0;
            Tick60 = TickMod60 == 0;
            Tick120 = Tick % 120 == 0;
            Tick600 = Tick % 600 == 0;

            CompLoop();

            if (!_startComps.IsEmpty || !_startGrids.IsEmpty)
                StartComps();

            if (FirstRun)
            {
                if (!IsDedicated)
                    InitPlayers();
                FirstRun = false;
            }

            //if (Tick20 && BlockLimits.TrackPCU && AggregatorTask.IsComplete)
            //    AggregatorTask = MyAPIGateway.Parallel.Start(BlockLimits.AggregateStatsParallel);
        }

        public override void UpdateAfterSimulation()
        {
            if (IsDedicated) return;
            try
            {
                AvLoop();
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }
        }

        protected override void UnloadData()
        {
            if (IsMultiPlayer)
            {
                if (IsServer)
                {
                    MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(Networking.ServerPacketId, Networking.ProcessPacket);

                    MyVisualScriptLogicProvider.PlayerDisconnected -= PlayerDisconnected;
                    MyVisualScriptLogicProvider.PlayerRespawnRequest -= PlayerConnected;
                }
                else
                {
                    MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(Networking.ClientPacketId, Networking.ProcessPacket);
                }
            }

            MyEntities.OnEntityCreate -= OnEntityCreate;
            MyEntities.OnCloseAll -= OnCloseAll;

            if (!IsDedicated)
            {
                MyAPIGateway.TerminalControls.CustomActionGetter -= Controls.CustomActionGetter;
                MyAPIGateway.TerminalControls.CustomControlGetter -= Controls.CustomControlGetter;
            }

            if (!AggregatorTask.IsComplete)
                AggregatorTask.Wait();

            Logs.WriteLine($"Waiting for tasks to complete before closing...");
            var taskCount = 0;
            foreach (var gridComp in GridList)
            {
                foreach (var comp in gridComp.ToolComps)
                {
                    if (!comp.GridsTask.IsComplete)
                    {
                        comp.GridsTask.Wait();
                        taskCount++;
                    }
                }
            }
            Logs.WriteLine($"Waited for {taskCount} tasks to complete before closing");

            DSAPI.Unload();
            APIServer.Unload();

            Controls.Clean();
            Settings.Clean();

            Logs.Close();
            Clean();

        }

    }
}
