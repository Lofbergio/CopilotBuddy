using Styx.WoWInternals;
using System;
using TreeSharp;

namespace CommonBehaviors
{
    public class WaitLuaEvent : WaitContinue
    {
        private readonly string _luaEvent;
        private bool _eventFired;

        public WaitLuaEvent(string luaEvent, int timeoutSeconds, Composite child)
            : base(timeoutSeconds, child)
        {
            _luaEvent = luaEvent;
        }

        public WaitLuaEvent(string luaEvent, WaitGetTimeoutDelegate timeoutRetriever, Composite child)
            : base(timeoutRetriever, child)
        {
            _luaEvent = luaEvent;
        }

        public WaitLuaEvent(string luaEvent, int timeoutSeconds, CanRunDecoratorDelegate runFunc, Composite child)
            : base(timeoutSeconds, runFunc, child)
        {
            _luaEvent = luaEvent;
        }

        public WaitLuaEvent(string luaEvent, TimeSpan timeout, Composite child)
            : base(timeout, child)
        {
            _luaEvent = luaEvent;
        }

        public WaitLuaEvent(string luaEvent, WaitGetTimeoutDelegate timeoutRetriever, CanRunDecoratorDelegate runFunc, Composite child)
            : base(timeoutRetriever, runFunc, child)
        {
            _luaEvent = luaEvent;
        }

        public override void Start(object context)
        {
            Lua.Events.AttachEvent(_luaEvent, OnLuaEvent);
            base.Start(context);
        }

        private void OnLuaEvent(object sender, LuaEventArgs e)
        {
            _eventFired = true;
        }

        protected override bool CanRun(object context)
        {
            return _eventFired;
        }

        public override void Stop(object context)
        {
            Lua.Events.DetachEvent(_luaEvent, OnLuaEvent);
            _eventFired = false;
            base.Stop(context);
        }
    }
}
