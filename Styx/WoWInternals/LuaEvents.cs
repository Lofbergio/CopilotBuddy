#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GreenMagic;
using Styx.Helpers;

namespace Styx.WoWInternals
{
    public class LuaEvents
    {
        private static readonly Random _random;
        private static readonly char[] _validChars;
        private readonly Dictionary<string, LuaEventHandlerDelegate> _eventHandlers = new Dictionary<string, LuaEventHandlerDelegate>();
        private readonly Dictionary<string, string> _eventFilters = new Dictionary<string, string>();
        private readonly object _eventLock = new object();
        private readonly WaitTimer _refreshTimer;
        private string _eventTableName;
        private string _frameName;
        private string _filterTableName;
        private int _registeredEventCount;
        private static bool _printAllEvents;
        private static Func<object, string> _toStringFunc;

        internal LuaEvents()
        {
            // HB 4.3.4 uses a short compaction period to avoid event table bloat.
            this._refreshTimer = WaitTimer.FiveSeconds;
            this._refreshTimer.Reset();
        }

        static LuaEvents()
        {
            LuaEvents._random = new Random();
            LuaEvents._validChars = "ABCDEFGHIJKLMNOPQRSTUVXYZabcdefghijklmnopqrstuvxyz".ToCharArray();
        }

        ~LuaEvents()
        {
            try
            {
                if (ObjectManager.WoWProcess != null && !ObjectManager.WoWProcess.HasExited && ObjectManager.IsInGame && this._frameName != null)
                {
                    Lua.DoString(string.Format("if {0} then {0}:UnregisterAllEvents(); {0}:SetScript('OnEvent', nil); {0} = nil; end if {1} then {1} = nil; end", 
                        this._frameName, this._eventTableName));
                    if (!string.IsNullOrEmpty(this._filterTableName))
                    {
                        Lua.DoString(string.Format("if {0} then {0} = nil; end", this._filterTableName));
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Clears the internal Lua frame/table names so the next AttachEvent call
        /// re-initializes cleanly. Call this after a logout/disconnect so stale
        /// globals from the previous session don't cause GetField NullReference.
        /// </summary>
        public void Reset()
        {
            lock (_eventLock)
            {
                _frameName = null;
                _eventTableName = null;
                _filterTableName = null;
                _registeredEventCount = 0;
            }
        }

        public void AttachEvent(string eventName, LuaEventHandlerDelegate handler)
        {
            lock (_eventLock)
            {
                if (!IsInitialized)
                {
                    Initialize();
                }

                LuaEventHandlerDelegate existing;
                bool hasHandlers = _eventHandlers.TryGetValue(eventName, out existing) && existing != null;
                if (!this._eventHandlers.ContainsKey(eventName))
                {
                    this._eventHandlers[eventName] = null;
                }
                if (!hasHandlers && !string.IsNullOrEmpty(_frameName))
                {
                    Lua.DoString("{0}:RegisterEvent('{1}')", _frameName, EscapeLuaString(eventName));
                }
                Dictionary<string, LuaEventHandlerDelegate> dictionary;
                (dictionary = this._eventHandlers)[eventName] = (LuaEventHandlerDelegate)Delegate.Combine(dictionary[eventName], handler);
            }
        }

        public void DetachEvent(string eventName, LuaEventHandlerDelegate handler)
        {
            lock (_eventLock)
            {
                if (this._eventHandlers.ContainsKey(eventName))
                {
                    Dictionary<string, LuaEventHandlerDelegate> dictionary;
                    (dictionary = this._eventHandlers)[eventName] = (LuaEventHandlerDelegate)Delegate.Remove(dictionary[eventName], handler);
                    if (this._eventHandlers[eventName] == null && !string.IsNullOrEmpty(_frameName))
                    {
                        Lua.DoString("{0}:UnregisterEvent('{1}')", _frameName, EscapeLuaString(eventName));
                    }
                }
            }
        }

        public bool AddFilter(string eventName, string filterCode)
        {
            lock (_eventLock)
            {
                if (this._eventFilters.ContainsKey(eventName))
                    return false;

                this._eventFilters.Add(eventName, filterCode);

                if (IsInitialized && !string.IsNullOrEmpty(_filterTableName))
                {
                    Lua.DoString("if {0} then {0}[\"{1}\"] = function(args) {2} end; end;", _filterTableName, EscapeLuaString(eventName), filterCode);
                }

                return true;
            }
        }

        public void RemoveFilter(string eventName)
        {
            lock (_eventLock)
            {
                if (IsInitialized && !string.IsNullOrEmpty(_filterTableName))
                {
                    Lua.DoString("if {0} then {0}[\"{1}\"] = nil; end", _filterTableName, EscapeLuaString(eventName));
                }
                this._eventFilters.Remove(eventName);
            }
        }

        internal void ProcessEvents()
        {
            LuaTable globals = Lua.State.Globals;
            if (globals == null)
                return;

            LuaTValue eventTableValue = GetEventTableValue();

            if (eventTableValue == null || eventTableValue.Type != LuaType.Table)
            {
                lock (_eventLock)
                {
                    if (!IsInitialized)
                    {
                        Initialize();
                    }
                }
                return;
            }

            if (this._refreshTimer.IsFinished)
            {
                this._refreshTimer.Reset();
                Lua.DoString(string.Format("local dumpedTo = {0}; local eventCount = #{1}; if eventCount > dumpedTo then local eventCopy = {{}}; for i=dumpedTo + 1,eventCount do tinsert(eventCopy, {1}[i]); end; wipe({1}); for i=1,#eventCopy do tinsert({1}, eventCopy[i]); end; else wipe({1}); end;", this._registeredEventCount, this._eventTableName));
                this._registeredEventCount = 0;
                return;
            }

            try
            {
                LuaTable eventTable = eventTableValue.Value.Table;

                if (eventTable.ValuesCount > 131072U)
                {
                    Logging.WriteDebug("Memory moved for lua events ({0} new values); skipped for now.", eventTable.ValuesCount);
                    return;
                }

                int count = eventTable.Count;
                int numEvents = (count - this._registeredEventCount) / 3;

                if (numEvents <= 0)
                    return;

                if (numEvents > 15000)
                {
                    Logging.WriteDebug("Memory moved for lua events ({0} new events); skipped for now.", numEvents);
                    return;
                }

                LuaTValue[] values = eventTable.GetValues(this._registeredEventCount, numEvents * 3);

                for (int i = 0; i < values.Length / 3; i++)
                {
                    LuaTValue eventNameValue = values[i * 3];
                    LuaTValue fireTimeValue = values[i * 3 + 1];
                    LuaTValue argsValue = values[i * 3 + 2];

                    if (eventNameValue.Type != LuaType.String || 
                        fireTimeValue.Type != LuaType.Number || 
                        argsValue.Type != LuaType.Table)
                    {
                        Logging.WriteDebug("Memory moved for lua events (type); skipped for now.");
                        // Skip the broken tail so the tick loop does not stall on the same triplet forever.
                        this._registeredEventCount = count;
                        break;
                    }

                    string eventName = eventNameValue.Value.String.Value;
                    uint fireTimeStamp = (uint)fireTimeValue.Value.Double;
                    LuaTable argsTable = argsValue.Value.Table;

                    if (argsTable.ValuesCount > 2500U)
                    {
                        Logging.WriteDebug("Memory moved for lua events (ValuesCount); skipped for now.");
                        this._registeredEventCount = count;
                        break;
                    }

                    int argsCount = argsTable.Count;
                    if (argsCount > 1000)
                    {
                        Logging.WriteDebug("Memory moved for lua events (eventArgsCount); skipped for now.");
                        this._registeredEventCount = count;
                        break;
                    }

                    object[] args = ReadArgsFromTable(argsTable, argsCount);

                    LuaEventHandlerDelegate handler;
                    lock (_eventLock)
                    {
                        this._eventHandlers.TryGetValue(eventName, out handler);
                    }
                    if (handler != null)
                    {
                        InvokeDelegate(handler, this, new LuaEventArgs(eventName, fireTimeStamp, args));
                    }

                    this._registeredEventCount += 3;

                    if (LuaEvents.PrintAllEvents)
                    {
                        if (LuaEvents._toStringFunc == null)
                        {
                            LuaEvents._toStringFunc = new Func<object, string>(ObjectToString);
                        }
                        Logging.WriteDebug(string.Format("[EVENT] {0}: Args: {1}", eventName, string.Join(", ", args.Select(LuaEvents._toStringFunc).ToArray())));
                    }
                }
            }
            catch (ThreadInterruptedException)
            {
                // TreeRoot.Stop() interrupts the worker thread. Eating that interrupt here consumed the
                // stop entirely — a worker wedged under this method became unkillable and CB zombied
                // ("Bot thread hung during window close" ×3, log 2026-07-04_1057). Let it propagate.
                throw;
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("LuaEvents.ProcessEvents exception: " + ex.Message);
            }
        }

        private object[] ReadArgsFromTable(LuaTable table, int count)
        {
            if (count == 0)
                return new object[0];

            LuaTValue[] values = table.GetValues(0, count);
            object[] args = new object[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                LuaValue val = values[i].Value;
                switch (values[i].Type)
                {
                    case LuaType.Boolean:
                        args[i] = val.Bool;
                        break;
                    case LuaType.Number:
                        args[i] = val.Double;
                        break;
                    case LuaType.String:
                        args[i] = val.String?.Value ?? string.Empty;
                        break;
                    default:
                        args[i] = values[i];
                        break;
                }
            }

            return args;
        }

        public static bool PrintAllEvents
        {
            get { return LuaEvents._printAllEvents; }
            set { LuaEvents._printAllEvents = value; }
        }

        public bool IsInitialized => GetEventTableValue() != null;

        private static void InvokeDelegate(Delegate d, params object[] args)
        {
            if (d != null)
            {
                foreach (Delegate @delegate in d.GetInvocationList())
                {
                    try
                    {
                        @delegate.DynamicInvoke(args);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void Initialize()
        {
            this._frameName = GenerateRandomString(9, 15);
            this._eventTableName = GenerateRandomString(9, 15);
            this._filterTableName = GenerateRandomString(9, 15);
            this._registeredEventCount = 0;

            StringBuilder registerBuilder = new StringBuilder();
            foreach (var kv in _eventHandlers)
            {
                if (kv.Value != null)
                {
                    registerBuilder.AppendFormat("{0}:RegisterEvent('{1}') ", this._frameName, EscapeLuaString(kv.Key));
                }
            }

            foreach (var kv in _eventFilters)
            {
                registerBuilder.AppendFormat("{0}[\"{1}\"] = function(args) {2} end; ", this._filterTableName, EscapeLuaString(kv.Key), kv.Value);
            }

            string text = string.Format(
                "{0} = {{}}; {2} = {{}}; {1} = CreateFrame('Frame'); " +
                "{1}:SetScript('OnEvent', function(self, event, ...) local args = {{ ... }} if not {2} or not {2}[event] or {2}[event](args) then tinsert({0}, event); tinsert({0}, GetTime()*1000); tinsert({0}, args); end end); {3}",
                this._eventTableName, this._frameName, this._filterTableName, registerBuilder);
            Lua.DoString(text);
        }

        private LuaTValue GetEventTableValue()
        {
            if (!string.IsNullOrEmpty(this._eventTableName))
            {
                LuaTValue field = Lua.State.Globals.GetField(this._eventTableName);
                if (field != null && field.Type == LuaType.Table)
                {
                    return field;
                }
            }
            return null;
        }

        private static string GenerateRandomString(int minLength, int maxLength)
        {
            int randomLength = LuaEvents._random.Next(minLength, maxLength + 1);
            StringBuilder stringBuilder = new StringBuilder(randomLength);
            for (int i = 0; i < randomLength; i++)
            {
                stringBuilder.Append(LuaEvents._validChars[LuaEvents._random.Next(0, LuaEvents._validChars.Length)]);
            }
            return stringBuilder.ToString();
        }

        private static string ObjectToString(object o)
        {
            return o.ToString();
        }

        private static string EscapeLuaString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"");
        }

        /// <summary>
        /// Processes pending Lua events (used by LuaEventWait).
        /// Must acquire FrameLock because this is called from the behavior tree
        /// (outside the Pulse FrameLock) and ProcessEvents reads Lua tables in
        /// WoW memory — without FrameLock, WoW's main thread can modify Lua
        /// state mid-read, causing "Memory moved for lua events" errors and
        /// lost events (e.g. QUEST_QUERY_COMPLETE never delivered → 5s timeout).
        /// </summary>
        public static void ProcessPendingEvents()
        {
            using (new FrameLock())
            {
                // Clear cache so we read fresh event table entries that WoW
                // added since the last Pulse FrameLock.
                StyxWoW.Memory.ClearCache();
                Lua.Events.ProcessEvents();
            }
        }
    }
}
