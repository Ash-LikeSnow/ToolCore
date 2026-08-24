using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Utils;

namespace ToolCore.API
{
    internal class TCApi
    {
        /// <summary>
        /// Monitor various kinds of events, see ToolCore.Definitions.Serialised.Trigger for int mapping, bool is for active/inactive
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public void MonitorEvents(MyEntity entity, Action<int, bool> action) =>
            _monitorEvents?.Invoke(entity, action);

        /// <summary>
        /// Monitor various kinds of events, see ToolCore.Definitions.Serialised.Trigger for int mapping, bool is for active/inactive
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public void UnMonitorEvents(MyEntity entity, Action<int, bool> action) =>
            _unmonitorEvents?.Invoke(entity, action);

        //None = 0,
        //Functional = 1,
        //Powered = 2,
        //Enabled = 4,
        //Activated = 8,
        //LeftClick = 16,
        //RightClick = 32,
        //Click = 48,
        //Firing = 56,
        //Hit = 64,
        //RayHit = 128,


        private const long CHANNEL = 2172757428;
        private bool _isRegistered;
        private bool _apiInit;
        private Action _readyCallback;

        private Action<MyEntity, Action<int, bool>> _monitorEvents;
        private Action<MyEntity, Action<int, bool>> _unmonitorEvents;
        private Action<ICollection<MyStringHash>> _tcDefs;

        public bool IsReady { get; private set; }


        /// <summary>
        /// Ask ToolCore to send the API methods.
        /// <para>Throws an exception if it gets called more than once per session without <see cref="Unload"/>.</para>
        /// </summary>
        /// <param name="readyCallback">Method to be called when CoreSystems replies.</param>
        public void Load(Action readyCallback = null)
        {
            if (_isRegistered)
                throw new Exception($"{GetType().Name}.Load() should not be called multiple times!");

            _readyCallback = readyCallback;
            _isRegistered = true;
            MyAPIGateway.Utilities.RegisterMessageHandler(CHANNEL, HandleMessage);
            MyAPIGateway.Utilities.SendModMessage(CHANNEL, "ApiEndpointRequest");
        }

        public void Unload()
        {
            MyAPIGateway.Utilities.UnregisterMessageHandler(CHANNEL, HandleMessage);

            ApiAssign(null);

            _isRegistered = false;
            _apiInit = false;
            IsReady = false;
        }

        private void HandleMessage(object obj)
        {
            if (_apiInit || obj is string
            ) // the sent "ApiEndpointRequest" will also be received here, explicitly ignoring that
                return;

            var dict = obj as IReadOnlyDictionary<string, Delegate>;

            if (dict == null)
                return;

            ApiAssign(dict);

            IsReady = true;
            _readyCallback?.Invoke();
        }

        public void ApiAssign(IReadOnlyDictionary<string, Delegate> delegates)
        {
            _apiInit = (delegates != null);

            AssignMethod(delegates, "RegisterEventMonitor", ref _monitorEvents);
            AssignMethod(delegates, "UnRegisterEventMonitor", ref _unmonitorEvents);
            AssignMethod(delegates, "GetTCDefs", ref _tcDefs);

        }

        private void AssignMethod<T>(IReadOnlyDictionary<string, Delegate> delegates, string name, ref T field)
            where T : class
        {
            if (delegates == null)
            {
                field = null;
                return;
            }

            Delegate del;
            if (!delegates.TryGetValue(name, out del))
                throw new Exception($"{GetType().Name} :: Couldn't find {name} delegate of type {typeof(T)}");

            field = del as T;

            if (field == null)
                throw new Exception(
                    $"{GetType().Name} :: Delegate {name} is not type {typeof(T)}, instead it's: {del.GetType()}");
        }

    }

}
