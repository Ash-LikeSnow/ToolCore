﻿using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolCore.Comp;
using ToolCore.Utils;
using VRage.ModAPI;
using VRage.Utils;
using static ToolCore.Comp.ToolComp;

namespace ToolCore.Session
{
    internal class Controls
    {
        private readonly ToolSession _session;

        private readonly List<IMyTerminalAction> _customActions = new List<IMyTerminalAction>();
        private readonly List<IMyTerminalControl> _customControls = new List<IMyTerminalControl>();

        private const string SHOOT_ACTION = "ToolCore_Shoot_Action";
        private readonly HashSet<string> _controlsToHide = new HashSet<string>()
        {
            "DrainAll",
            "blacklistWhitelist",
            "CurrentList",
            "removeFromSelectionButton",
            "candidatesList",
            "addToSelectionButton",
        };

        private readonly List<MyTerminalControlComboBoxItem> _modeList = new List<MyTerminalControlComboBoxItem>();
        private readonly List<MyTerminalControlComboBoxItem> _actionList = new List<MyTerminalControlComboBoxItem>();

        internal IMyTerminalBlock LastTerminal;

        internal Controls(ToolSession session)
        {
            _session = session;
        }

        internal void Clean()
        {
            _controlsToHide.Clear();
            _customActions.Clear();
            _customControls.Clear();
            LastTerminal = null;
        }

        internal void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyConveyorSorter) || !_session.DefinitionMap.ContainsKey(block.BlockDefinition))
                return;

            controls.RemoveRange(28, 7);

            foreach (var newControl in _customControls)
                controls.Add(newControl);

            GetMode(block);
            GetAction(block);
            LastTerminal = block;
        }

        internal void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
        {
            if (!(block is IMyConveyorSorter))
                return;

            if (_session.DefinitionMap.ContainsKey(block.BlockDefinition))
            {
                actions.RemoveAt(13);
                return;
            }

            int index;
            for (index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                if (action.Id == SHOOT_ACTION)
                {
                    actions.RemoveRange(index, 4);
                    break;
                }
            }
        }

        internal void CreateTerminalControls<T>() where T : IMyConveyorSorter
        {
            if (MyAPIGateway.TerminalControls == null)
            {

                _session.ControlsInited = false;
                return;
            }

            _customControls.Add(Separator<T>());
            _customControls.Add(ToolShootSwitch<T>());
            _customControls.Add(SelectMode<T>());
            _customControls.Add(SelectAction<T>());
            _customControls.Add(DrawSwitch<T>());

            //foreach (var control in _customControls)
            //    MyAPIGateway.TerminalControls.AddControl<T>(control);

            _customActions.Add(CreateActivateOnOffAction<T>());
            _customActions.Add(CreateActivateOnAction<T>());
            _customActions.Add(CreateActivateOffAction<T>());
            _customActions.Add(CreateModeAction<T>());
            _customActions.Add(CreateActionAction<T>());
            _customActions.Add(CreateDrawAction<T>());

            foreach (var action in _customActions)
                MyAPIGateway.TerminalControls.AddAction<T>(action);
        }

        #region ShootOnOff

        internal IMyTerminalControlOnOffSwitch ToolShootSwitch<T>() where T : IMyConveyorSorter
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, T>("ToolCore_Shoot");
            control.Title = MyStringId.GetOrCompute("Activate");
            control.Tooltip = MyStringId.GetOrCompute("Activate tool function");
            control.OnText = MyStringId.GetOrCompute("On");
            control.OffText = MyStringId.GetOrCompute("Off");
            control.Getter = GetActivated;
            control.Setter = SetActivated;
            control.Visible = IsTrue;
            control.Enabled = IsFunctional;

            return control;

        }

        internal bool GetActivated(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return false;

            return comp.Activated;
        }

        internal void SetActivated(IMyTerminalBlock block, bool activated)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            comp.Activated = activated;

            if (_session.IsServer) return;

            _session.Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Activated, activated ? 1 : 0));
        }

        internal IMyTerminalAction CreateActivateOnOffAction<T>() where T : IMyConveyorSorter
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>(SHOOT_ACTION);
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.Name = new StringBuilder("Activate/Deactivate");
            action.Action = ToggleActivated;
            action.Writer = ToggleActivatedWriter;
            action.Enabled = IsTrue;

            return action;
        }

        internal void ToggleActivated(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            comp.Activated = !comp.Activated;

            _session.Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Activated, comp.Activated ? 1 : 0));
        }

        internal void ToggleActivatedWriter(IMyTerminalBlock block, StringBuilder builder)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            builder.Append(comp.Activated ? "Active" : "Inactive");
        }

        internal IMyTerminalAction CreateActivateOnAction<T>() where T : IMyConveyorSorter
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>(SHOOT_ACTION + "_On");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
            action.Name = new StringBuilder("Activate");
            action.Action = SetActivatedOn;
            action.Writer = SetActivatedOnWriter;
            action.Enabled = IsTrue;

            return action;
        }

        internal void SetActivatedOn(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            var wasActivated = comp.Activated;
            comp.Activated = true;

            if (_session.IsServer || comp.Activated == wasActivated)
                return;

            _session.Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Activated, 1));
        }

        internal void SetActivatedOnWriter(IMyTerminalBlock block, StringBuilder builder)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            builder.Append(comp.Activated ? "Active" : "Inactive");
        }

        internal IMyTerminalAction CreateActivateOffAction<T>() where T : IMyConveyorSorter
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>(SHOOT_ACTION + "_Off");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
            action.Name = new StringBuilder("Deactivate");
            action.Action = SetActivatedOff;
            action.Writer = SetActivatedOffWriter;
            action.Enabled = IsTrue;

            return action;
        }

        internal void SetActivatedOff(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            var wasActivated = comp.Activated;
            comp.Activated = false;

            if (_session.IsServer || comp.Activated == wasActivated)
                return;

            _session.Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Activated, 0));
        }

        internal void SetActivatedOffWriter(IMyTerminalBlock block, StringBuilder builder)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            builder.Append(comp.Activated ? "Active" : "Inactive");
        }

        #endregion

        #region Mode

        internal IMyTerminalControlCombobox SelectMode<T>() where T : IMyConveyorSorter
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, T>("ToolCore_Mode");
            control.Title = MyStringId.GetOrCompute("Mode");
            control.Tooltip = MyStringId.GetOrCompute("Select tool mode");
            control.ComboBoxContent = ModeSelectContent;
            control.Getter = GetMode;
            control.Setter = SetMode;
            control.Visible = HasModeSelect;
            control.Enabled = IsFunctional;

            return control;
        }

        internal void ModeSelectContent(List<MyTerminalControlComboBoxItem> modes)
        {
            modes.AddList(_modeList);
        }

        internal long GetMode(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return 0;

            var id = 0;
            _modeList.Clear();
            foreach (var mode in comp.ModeMap.Keys)
            {
                var key = (int)mode;
                if (mode == comp.Mode)
                    id = key;

                var item = new MyTerminalControlComboBoxItem { Key = key, Value = MyStringId.GetOrCompute(mode.ToString())};
                _modeList.Add(item);
            }

            return id;
        }

        internal void SetMode(IMyTerminalBlock block, long id)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            //comp.Mode = (ToolComp.ToolMode)id;
            comp.SetMode((ToolComp.ToolMode)id);

            _session.Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Mode, (int)comp.Mode));
        }

        internal bool HasModeSelect(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return false;

            return comp.ModeMap.Count > 1;
        }

        internal IMyTerminalAction CreateModeAction<T>() where T : IMyConveyorSorter
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>("ToolCore_Mode_Action");
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.Name = new StringBuilder("Mode Select");
            action.Action = SwitchMode;
            action.Writer = SwitchModeWriter;
            action.Enabled = HasModeSelect;

            return action;
        }

        internal void SwitchMode(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            var modes = comp.ModeMap.Keys;
            var enumerator = modes.GetEnumerator();
            enumerator.MoveNext();
            while (enumerator.Current != comp.Mode)
            {
                enumerator.MoveNext();
            }
            var newMode = enumerator.MoveNext() ? enumerator.Current : modes.First();

            comp.SetMode(newMode);

            _session.Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Mode, (int)comp.Mode));
        }

        internal void SwitchModeWriter(IMyTerminalBlock block, StringBuilder builder)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            builder.Append(comp.Mode.ToString());
        }

        #endregion

        #region Action

        internal IMyTerminalControlCombobox SelectAction<T>() where T : IMyConveyorSorter
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, T>("ToolCore_Action");
            control.Title = MyStringId.GetOrCompute("Action");
            control.Tooltip = MyStringId.GetOrCompute("Select tool action (primary/secondary)");
            control.ComboBoxContent = ActionSelectContent;
            control.Getter = GetAction;
            control.Setter = SetAction;
            control.Visible = HasActionSelect;
            control.Enabled = IsFunctional;

            return control;
        }

        internal void ActionSelectContent(List<MyTerminalControlComboBoxItem> actions)
        {
            actions.AddList(_actionList);
        }

        internal long GetAction(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return 0;

            var id = 0;
            _actionList.Clear();
            var actions = comp.ModeData.Definition.ToolActions;
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (action == comp.Action)
                    id = i;

                var item = new MyTerminalControlComboBoxItem { Key = i, Value = MyStringId.GetOrCompute(action.ToString()) };
                _actionList.Add(item);
            }

            return id;
        }

        internal void SetAction(IMyTerminalBlock block, long id)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            var actions = comp.ModeData.Definition.ToolActions;
            comp.Action = actions[(int)id];

            _session.Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Action, (int)comp.Action));
        }

        internal bool HasActionSelect(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return false;

            return comp.ModeData.Definition.ToolActions.Count > 1;
        }

        internal IMyTerminalAction CreateActionAction<T>() where T : IMyConveyorSorter
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>("ToolCore_Action_Action");
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.Name = new StringBuilder("Action Select");
            action.Action = SwitchAction;
            action.Writer = SwitchActionWriter;
            action.Enabled = HasActionSelect;

            return action;
        }

        internal void SwitchAction(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            var actions = comp.ModeData.Definition.ToolActions;
            var index = actions.IndexOf(comp.Action);
            var next = index + 1;
            var newIndex = next < actions.Count ? next : 0;
            comp.Action = comp.ModeData.Definition.ToolActions[newIndex];

            _session.Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Action, (int)comp.Action));
        }

        internal void SwitchActionWriter(IMyTerminalBlock block, StringBuilder builder)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            builder.Append(comp.Action.ToString());
        }

        #endregion

        #region Draw

        internal IMyTerminalControlOnOffSwitch DrawSwitch<T>() where T : IMyConveyorSorter
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, T>("ToolCore_Draw");
            control.Title = MyStringId.GetOrCompute("Draw");
            control.Tooltip = MyStringId.GetOrCompute("Show tool effect area");
            control.OnText = MyStringId.GetOrCompute("On");
            control.OffText = MyStringId.GetOrCompute("Off");
            control.Getter = GetDraw;
            control.Setter = SetDraw;
            control.Visible = IsTrue;
            control.Enabled = IsFunctional;

            return control;

        }

        internal bool GetDraw(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return false;

            return comp.Draw;
        }

        internal void SetDraw(IMyTerminalBlock block, bool enabled)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            comp.Draw = enabled;

            _session.Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Draw, enabled ? 1 : 0));
        }

        internal IMyTerminalAction CreateDrawAction<T>() where T : IMyConveyorSorter
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>("ToolCore_Draw_Action");
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.Name = new StringBuilder("Draw On/Off");
            action.Action = ToggleDraw;
            action.Writer = ToggleDrawWriter;
            action.Enabled = IsTrue;

            return action;
        }

        internal void ToggleDraw(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            comp.Draw = !comp.Draw;

            _session.Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Draw, comp.Draw ? 1 : 0));
        }

        internal void ToggleDrawWriter(IMyTerminalBlock block, StringBuilder builder)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            builder.Append(comp.Draw ? "On" : "Off");
        }

        #endregion

        #region Generics

        internal IMyTerminalControlSeparator Separator<T>() where T : IMyTerminalBlock
        {
            var c = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, T>("ToolCore_Separator");

            c.Enabled = IsTrue;
            c.Visible = IsTrue;

            return c;
        }

        internal bool IsFunctional(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return false;

            return comp.Functional;
        }

        internal bool IsTrue(IMyTerminalBlock block)
        {
            return true;
        }

        internal bool IsFalse(IMyTerminalBlock block)
        {
            return false;
        }

        #endregion
    }
}
