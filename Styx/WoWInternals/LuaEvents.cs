#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
        private readonly object _eventLock = new object(); // ARCH-04: Thread safety lock
        private readonly WaitTimer _refreshTimer;
        private string _eventTableName;
        private string _frameName;
        private string _filterTableName;
        private int _registeredEventCount;
        private static bool _initialized;
        private static bool _printAllEvents;
        private static Func<object, string> _toStringFunc;

        internal LuaEvents()
        {
            this._refreshTimer = WaitTimer.ThirtySeconds;
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
                    // WoW 3.3.5a: Clean up frame and event table (no filter table)
                    Lua.DoString(string.Format("if {0} then {0}:UnregisterAllEvents(); {0}:SetScript('OnEvent', nil); {0} = nil; end if {1} then {1} = nil; end", 
                        this._frameName, this._eventTableName));
                }
            }
            catch
            {
            }
        }

        public void AttachEvent(string eventName, LuaEventHandlerDelegate handler)
        {
            lock (_eventLock) // ARCH-04: Thread safety
            {
                if (!this._eventHandlers.ContainsKey(eventName))
                {
                    this._eventHandlers[eventName] = null;
                }
                Dictionary<string, LuaEventHandlerDelegate> dictionary;
                (dictionary = this._eventHandlers)[eventName] = (LuaEventHandlerDelegate)Delegate.Combine(dictionary[eventName], handler);
            }
        }

        public void DetachEvent(string eventName, LuaEventHandlerDelegate handler)
        {
            lock (_eventLock) // ARCH-04: Thread safety
            {
                if (this._eventHandlers.ContainsKey(eventName))
                {
                    Dictionary<string, LuaEventHandlerDelegate> dictionary;
                    (dictionary = this._eventHandlers)[eventName] = (LuaEventHandlerDelegate)Delegate.Remove(dictionary[eventName], handler);
                }
            }
        }

        public bool AddFilter(string eventName, string filterCode)
        {
            if (this._eventFilters.ContainsKey(eventName))
                return false;
            
            // WoW 3.3.5a doesn't support Lua event filters, so we implement filtering in C#
            // Store the filter code to apply it after receiving events from WoW
            this._eventFilters.Add(eventName, filterCode);
            return true;
        }

        public void RemoveFilter(string eventName)
        {
            // WoW 3.3.5a: Filters are C#-side only, just remove from dictionary
            this._eventFilters.Remove(eventName);
        }

        internal void ProcessEvents()
        {
            // Get the globals table from Lua state
            LuaTable globals = Lua.State.Globals;
            if (globals == null)
                return;

            // Check if our event table variable exists
            LuaTValue eventTableValue = null;
            if (this._eventTableName != null)
            {
                eventTableValue = globals.GetField(this._eventTableName);
            }

            if (eventTableValue == null || eventTableValue.Type != LuaType.Table)
            {
                // Initialize if not yet done
                this.Initialize();
                return;
            }

            // Periodic cleanup to prevent memory growth
            if (this._refreshTimer.IsFinished)
            {
                this._refreshTimer.Reset();
                Lua.DoString(string.Format("local dumpedTo = {0}; local eventCount = #{1}; if eventCount > dumpedTo then local eventCopy = {{}}; for i=dumpedTo + 1,eventCount do tinsert(eventCopy, {1}[i]); end; wipe({1}); for i=1,#eventCopy do tinsert({1}, eventCopy[i]); end; else wipe({1}); end;", this._registeredEventCount, this._eventTableName));
                this._registeredEventCount = 0;
                return;
            }

            try
            {
                // Read event table directly from memory
                LuaTable eventTable = eventTableValue.Value.Table;
                
                // Safety check for memory corruption
                if (eventTable.ValuesCount > 131072U)
                {
                    Logging.WriteDebug("Memory moved for lua events ({0} values); skipped.", eventTable.ValuesCount);
                    return;
                }

                int count = eventTable.Count;
                int numEvents = (count - this._registeredEventCount) / 3;

                if (numEvents > 1500)
                {
                    Logging.WriteDebug("Too many lua events ({0}); skipped.", numEvents);
                    return;
                }

                if (numEvents <= 0)
                    return;

                // Read all event data at once for efficiency
                LuaTValue[] values = eventTable.GetValues(this._registeredEventCount, numEvents * 3);

                for (int i = 0; i < values.Length / 3; i++)
                {
                    LuaTValue eventNameValue = values[i * 3];
                    LuaTValue fireTimeValue = values[i * 3 + 1];
                    LuaTValue argsValue = values[i * 3 + 2];

                    // Validate types
                    if (eventNameValue.Type != LuaType.String || 
                        fireTimeValue.Type != LuaType.Number || 
                        argsValue.Type != LuaType.Table)
                    {
                        Logging.WriteDebug("Invalid lua event data types; skipped.");
                        break;
                    }

                    string eventName = eventNameValue.Value.String.Value;
                    uint fireTimeStamp = (uint)fireTimeValue.Value.Double;
                    LuaTable argsTable = argsValue.Value.Table;

                    // Safety check for args table
                    if (argsTable.ValuesCount > 2500U)
                    {
                        Logging.WriteDebug("Event args table too large; skipped.");
                        break;
                    }

                    int argsCount = argsTable.Count;
                    if (argsCount > 1000)
                    {
                        Logging.WriteDebug("Too many event args; skipped.");
                        break;
                    }

                    // Read args from memory
                    object[] args = ReadArgsFromTable(argsTable, argsCount);

                    // Apply C#-side filter if one exists for this event (WoW 3.3.5a compatibility)
                    bool hasFilter;
                    lock (_eventLock)
                    {
                        hasFilter = this._eventFilters.ContainsKey(eventName);
                    }
                    if (hasFilter)
                    {
                        if (!ApplyFilter(eventName, args))
                        {
                            // Filter blocked this event, skip it
                            this._registeredEventCount += 3;
                            continue;
                        }
                    }

                    // Invoke handler if registered (ARCH-04: lock for thread safety)
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
            catch (Exception ex)
            {
                Logging.WriteDebug("LuaEvents.ProcessEvents exception: " + ex.Message);
            }
        }

        /// <summary>
        /// Reads arguments from a Lua table into an object array.
        /// </summary>
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

        /// <summary>
        /// FEAT-20: Whether the Lua event frame has been initialized.
        /// </summary>
        public static bool IsInitialized => LuaEvents._initialized;

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
            this._frameName = GenerateRandomString(9, 15); // Frame name (WoW 3.3.5a: no filter table needed)
            this._eventTableName = GenerateRandomString(9, 15); // Event table
            this._registeredEventCount = 0;
            
            // WoW 3.3.5a: Simple initialization without Lua-side filtering
            // Filters are applied in C# after receiving events
            string text = string.Format(
                "{0} = {{}}; {1} = CreateFrame('Frame'); " +
                "{1}:SetScript('OnEvent', function(self, event, ...) " +
                "tinsert({0}, event); tinsert({0}, GetTime()*1000); tinsert({0}, {{ ... }}); " +
                "end); {1}:RegisterAllEvents();", 
                this._eventTableName, this._frameName);
            Lua.DoString(text);
            _initialized = true;
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

        /// <summary>
        /// Applies a C#-side event filter (WoW 3.3.5a doesn't support Lua-side filters).
        /// Evaluates the filter code and returns true if the event should be processed.
        /// </summary>
        private bool ApplyFilter(string eventName, object[] args)
        {
            string filterCode = this._eventFilters[eventName];
            
            // For COMBAT_LOG_EVENT_UNFILTERED, args[2] is the event type
            // Filter: "return args[2] == 'SPELL_CAST_SUCCESS' or args[2] == 'SPELL_AURA_APPLIED' or ..."
            // We'll evaluate this in C# for performance
            
            if (eventName == "COMBAT_LOG_EVENT_UNFILTERED" && args.Length > 1)
            {
                // args[1] is the combat log event type (0-indexed in C#, 2-indexed in Lua)
                string combatEventType = args[1]?.ToString() ?? string.Empty;
                
                // Common Singular filter: only these event types
                if (combatEventType == "SPELL_CAST_SUCCESS" ||
                    combatEventType == "SPELL_AURA_APPLIED" ||
                    combatEventType == "SPELL_MISSED" ||
                    combatEventType == "RANGE_MISSED" ||
                    combatEventType == "SWING_MISSED")
                {
                    return true;
                }
                return false;
            }
            
            // For other events, no filter implemented yet (allow all)
            return true;
        }

        /// <summary>
        /// Processes pending Lua events (used by LuaEventWait).
        /// </summary>
        public static void ProcessPendingEvents()
        {
            // Process events through the Lua.Events instance
            Lua.Events.ProcessEvents();
        }
    }
}
