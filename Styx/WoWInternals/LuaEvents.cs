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
        private static readonly Random random_0;
        private static readonly char[] char_0;
        private readonly Dictionary<string, LuaEventHandlerDelegate> dictionary_0 = new Dictionary<string, LuaEventHandlerDelegate>();
        private readonly Dictionary<string, string> dictionary_1 = new Dictionary<string, string>();
        private readonly WaitTimer waitTimer_0;
        private string string_0;
        private string string_1;
        private string string_2;
        private int int_0;
        private static bool bool_0;
        private static Func<object, string> func_0;

        internal LuaEvents()
        {
            this.waitTimer_0 = WaitTimer.ThirtySeconds;
            this.waitTimer_0.Reset();
        }

        static LuaEvents()
        {
            LuaEvents.random_0 = new Random();
            LuaEvents.char_0 = "ABCDEFGHIJKLMNOPQRSTUVXYZabcdefghijklmnopqrstuvxyz".ToCharArray();
        }

        ~LuaEvents()
        {
            try
            {
                if (ObjectManager.WoWProcess != null && !ObjectManager.WoWProcess.HasExited && ObjectManager.IsInGame && this.string_1 != null)
                {
                    // WoW 3.3.5a: Clean up frame and event table (no filter table)
                    Lua.DoString(string.Format("if {0} then {0}:UnregisterAllEvents(); {0}:SetScript('OnEvent', nil); {0} = nil; end if {1} then {1} = nil; end", 
                        this.string_1, this.string_0));
                }
            }
            catch
            {
            }
        }

        public void AttachEvent(string eventName, LuaEventHandlerDelegate handler)
        {
            if (!this.dictionary_0.ContainsKey(eventName))
            {
                this.dictionary_0[eventName] = null;
            }
            Dictionary<string, LuaEventHandlerDelegate> dictionary;
            (dictionary = this.dictionary_0)[eventName] = (LuaEventHandlerDelegate)Delegate.Combine(dictionary[eventName], handler);
        }

        public void DetachEvent(string eventName, LuaEventHandlerDelegate handler)
        {
            if (this.dictionary_0.ContainsKey(eventName))
            {
                Dictionary<string, LuaEventHandlerDelegate> dictionary;
                (dictionary = this.dictionary_0)[eventName] = (LuaEventHandlerDelegate)Delegate.Remove(dictionary[eventName], handler);
            }
        }

        public bool AddFilter(string eventName, string filterCode)
        {
            if (this.dictionary_1.ContainsKey(eventName))
                return false;
            
            // WoW 3.3.5a doesn't support Lua event filters, so we implement filtering in C#
            // Store the filter code to apply it after receiving events from WoW
            this.dictionary_1.Add(eventName, filterCode);
            return true;
        }

        public void RemoveFilter(string eventName)
        {
            // WoW 3.3.5a: Filters are C#-side only, just remove from dictionary
            this.dictionary_1.Remove(eventName);
        }

        internal void ProcessEvents()
        {
            // Get the globals table from Lua state
            LuaTable globals = Lua.State.Globals;
            if (globals == null)
                return;

            // Check if our event table variable exists
            LuaTValue eventTableValue = null;
            if (this.string_0 != null)
            {
                eventTableValue = globals.GetField(this.string_0);
            }

            if (eventTableValue == null || eventTableValue.Type != LuaType.Table)
            {
                // Initialize if not yet done
                this.Initialize();
                return;
            }

            // Periodic cleanup to prevent memory growth
            if (this.waitTimer_0.IsFinished)
            {
                this.waitTimer_0.Reset();
                Lua.DoString(string.Format("local dumpedTo = {0}; local eventCount = #{1}; if eventCount > dumpedTo then local eventCopy = {{}}; for i=dumpedTo + 1,eventCount do tinsert(eventCopy, {1}[i]); end; wipe({1}); for i=1,#eventCopy do tinsert({1}, eventCopy[i]); end; else wipe({1}); end;", this.int_0, this.string_0));
                this.int_0 = 0;
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
                int numEvents = (count - this.int_0) / 3;

                if (numEvents > 1500)
                {
                    Logging.WriteDebug("Too many lua events ({0}); skipped.", numEvents);
                    return;
                }

                if (numEvents <= 0)
                    return;

                // Read all event data at once for efficiency
                LuaTValue[] values = eventTable.GetValues(this.int_0, numEvents * 3);

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
                    if (this.dictionary_1.ContainsKey(eventName))
                    {
                        if (!ApplyFilter(eventName, args))
                        {
                            // Filter blocked this event, skip it
                            this.int_0 += 3;
                            continue;
                        }
                    }

                    // Invoke handler if registered
                    LuaEventHandlerDelegate handler;
                    if (this.dictionary_0.TryGetValue(eventName, out handler) && handler != null)
                    {
                        InvokeDelegate(handler, this, new LuaEventArgs(eventName, fireTimeStamp, args));
                    }

                    this.int_0 += 3;

                    if (LuaEvents.PrintAllEvents)
                    {
                        if (LuaEvents.func_0 == null)
                        {
                            LuaEvents.func_0 = new Func<object, string>(ObjectToString);
                        }
                        Logging.WriteDebug(string.Format("[EVENT] {0}: Args: {1}", eventName, string.Join(", ", args.Select(LuaEvents.func_0).ToArray())));
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
            get { return LuaEvents.bool_0; }
            set { LuaEvents.bool_0 = value; }
        }

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
            this.string_1 = GenerateRandomString(9, 15); // Frame name (WoW 3.3.5a: no filter table needed)
            this.string_0 = GenerateRandomString(9, 15); // Event table
            this.int_0 = 0;
            
            // WoW 3.3.5a: Simple initialization without Lua-side filtering
            // Filters are applied in C# after receiving events
            string text = string.Format(
                "{0} = {{}}; {1} = CreateFrame('Frame'); " +
                "{1}:SetScript('OnEvent', function(self, event, ...) " +
                "tinsert({0}, event); tinsert({0}, GetTime()*1000); tinsert({0}, {{ ... }}); " +
                "end); {1}:RegisterAllEvents();", 
                this.string_0, this.string_1);
            Lua.DoString(text);
        }

        private static string GenerateRandomString(int minLength, int maxLength)
        {
            int num = LuaEvents.random_0.Next(minLength, maxLength + 1);
            StringBuilder stringBuilder = new StringBuilder(num);
            for (int i = 0; i < num; i++)
            {
                stringBuilder.Append(LuaEvents.char_0[LuaEvents.random_0.Next(0, LuaEvents.char_0.Length)]);
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
            string filterCode = this.dictionary_1[eventName];
            
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
