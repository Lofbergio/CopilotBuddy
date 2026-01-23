using Styx.Helpers;
using Styx.WoWInternals;
using System;
using TreeSharp;

namespace CommonBehaviors
{
    public class ActionWaitForLuaEvent : TreeSharp.Action
    {
        private readonly string _luaEvent;
        private readonly int _timeoutSeconds;
        private DateTime _timeoutTime;
        private bool _eventFired;

        public ActionWaitForLuaEvent(string luaEvent, int timeoutSeconds)
        {
            _luaEvent = luaEvent;
            _timeoutSeconds = timeoutSeconds;
        }

        public override void Start(object context)
        {
            _eventFired = false;
            _timeoutTime = DateTime.Now.AddSeconds(_timeoutSeconds);
            Lua.Events.AttachEvent(_luaEvent, OnLuaEvent);
            base.Start(context);
        }

        private void OnLuaEvent(object sender, LuaEventArgs e)
        {
            _eventFired = true;
        }

        protected override RunStatus Run(object context)
        {
            if (DateTime.Now > _timeoutTime)
            {
                Logging.Write(_luaEvent + " wait timed out.");
                return RunStatus.Failure;
            }
            return _eventFired ? RunStatus.Success : RunStatus.Running;
        }

        public override void Stop(object context)
        {
            Lua.Events.DetachEvent(_luaEvent, OnLuaEvent);
            _eventFired = false;
            base.Stop(context);
        }
    }
}
