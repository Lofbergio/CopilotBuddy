using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GreenMagic;
using Styx.Helpers;
using Styx.Patchables;
using Styx.WoWInternals.WoWObjects;

namespace Styx.WoWInternals
{
    public static class Lua
    {
        #region Private Fields

        private static AllocatedMemory? _returnBuffer;
        private static readonly Dictionary<string, string> EscapeSequences = new Dictionary<string, string>
        {
            { "\\", "\\\\" },
            { "\"", "\\\"" },
            { "'", "\\'" },
            { "\n", "\\n" },
            { "\r", "\\r" },
            { "\t", "\\t" }
        };

        #endregion

        #region Public Methods

        public static string Escape(string unescaped)
        {
            if (string.IsNullOrEmpty(unescaped))
                return unescaped;

            foreach (var kvp in EscapeSequences)
            {
                unescaped = unescaped.Replace(kvp.Key, kvp.Value);
            }
            return unescaped;
        }

        public static List<string> GetReturnValues(string lua)
        {
            return GetReturnValues(lua, "CopilotBuddy");
        }

        public static List<string> GetReturnValues(string lua, string scriptName)
        {
            var executor = ObjectManager.Executor;
            if (executor == null)
                return new List<string>();

            var wow = ObjectManager.Wow;
            if (wow == null)
                return new List<string>();

            try
            {
                // Get Lua state from global
                uint pState = wow.Read<uint>((uint)GlobalOffsets.LuaState);
                if (pState == 0)
                    return new List<string>();

                // Encode script and name
                byte[] luaBytes = Encoding.UTF8.GetBytes(lua);
                byte[] nameBytes = Encoding.UTF8.GetBytes(scriptName);

                // Allocate memory for script
                var scriptMemory = new AllocatedMemory(luaBytes.Length + nameBytes.Length + 2);
                try
                {
                    scriptMemory.WriteBytes(0, luaBytes);
                    scriptMemory.WriteByte(luaBytes.Length, 0); // null terminator
                    scriptMemory.WriteBytes(luaBytes.Length + 1, nameBytes);
                    scriptMemory.WriteByte(luaBytes.Length + 1 + nameBytes.Length, 0); // null terminator

                    uint luaPtr = scriptMemory.Address;
                    uint namePtr = (uint)(scriptMemory.Address + luaBytes.Length + 1);

                    // Allocate return buffer
                    if (_returnBuffer == null)
                        _returnBuffer = new AllocatedMemory(4000);

                    // Clear return buffer
                    _returnBuffer.Write(0, 0);

                    lock (executor.AssemblyLock)
                    {
                        executor.Clear();

                        // lua_gettop(pState)
                        executor.AddLine("push {0}", pState);
                        executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_GetTop);
                        executor.AddLine("add esp, 4");
                        executor.AddLine("push eax"); // save top

                        // luaL_loadbuffer(pState, lua, len, name)
                        executor.AddLine("push {0}", namePtr);
                        executor.AddLine("push {0}", luaBytes.Length);
                        executor.AddLine("push {0}", luaPtr);
                        executor.AddLine("push {0}", pState);
                        executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_Load);
                        executor.AddLine("add esp, 16");

                        // Check for load errors
                        executor.AddLine("test eax, eax");
                        executor.AddLine("jnz @error");

                        // lua_pcall(pState, 0, LUA_MULTRET, 0)
                        executor.AddLine("push 0");
                        executor.AddLine("push -1");
                        executor.AddLine("push 0");
                        executor.AddLine("push {0}", pState);
                        executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_PCall);
                        executor.AddLine("add esp, 16");

                        // Check for pcall errors
                        executor.AddLine("test eax, eax");
                        executor.AddLine("jnz @error");

                        // Calculate number of return values
                        executor.AddLine("push {0}", pState);
                        executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_GetTop);
                        executor.AddLine("add esp, 4");
                        executor.AddLine("pop ecx"); // restore old top
                        executor.AddLine("sub eax, ecx");
                        executor.AddLine("mov [{0}], eax", _returnBuffer.Address);

                        // Process return values
                        executor.AddLine("mov edi, {0}", _returnBuffer.Address + 4);
                        executor.AddLine("mov esi, ecx");
                        executor.AddLine("inc esi");
                        executor.AddLine("@loop:");
                        executor.AddLine("cmp eax, 0");
                        executor.AddLine("jle @done");

                        // lua_tolstring(pState, index, NULL)
                        executor.AddLine("push 0");
                        executor.AddLine("push esi");
                        executor.AddLine("push {0}", pState);
                        executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_ToLString);
                        executor.AddLine("add esp, 12");

                        executor.AddLine("mov [edi], eax");
                        executor.AddLine("add edi, 4");
                        executor.AddLine("inc esi");
                        executor.AddLine("dec eax");
                        executor.AddLine("jmp @loop");

                        // Error handling
                        executor.AddLine("@error:");
                        executor.AddLine("mov dword [{0}], -1", _returnBuffer.Address);

                        executor.AddLine("@done:");
                        executor.AddLine("xor eax, eax");
                        executor.AddLine("retn");

                        executor.Execute();
                    }

                    // Read return values
                    int count = _returnBuffer.Read<int>(0);
                    if (count < 0)
                        return new List<string>();

                    var results = new List<string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        uint strPtr = _returnBuffer.Read<uint>((i + 1) * 4);
                        if (strPtr != 0)
                            results.Add(wow.Read<string>(strPtr));
                        else
                            results.Add(string.Empty);
                    }
                    return results;
                }
                finally
                {
                    scriptMemory?.Dispose();
                }
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        [Obsolete("Use GetReturnValues instead. They do the same.")]
        public static List<string> LuaGetReturnValue(string lua, string scriptName)
        {
            return GetReturnValues(lua, scriptName);
        }

        public static T GetReturnVal<T>(string lua, int retVal)
        {
            return GetReturnVal<T>(lua, (uint)retVal);
        }

        public static T GetReturnVal<T>(string lua, uint retVal)
        {
            try
            {
                var returnValues = GetReturnValues(lua);

                if (retVal >= returnValues.Count)
                    return default(T)!;

                string value = returnValues[(int)retVal];

                // Handle special types
                if (typeof(T) == typeof(bool))
                {
                    // In Lua, nil and false are false, everything else is true
                    bool result = !string.IsNullOrEmpty(value) &&
                                  !value.Equals("nil", StringComparison.OrdinalIgnoreCase) &&
                                  !value.Equals("false", StringComparison.OrdinalIgnoreCase) &&
                                  !value.Equals("0", StringComparison.OrdinalIgnoreCase);
                    return (T)(object)result;
                }

                if (string.IsNullOrEmpty(value) || value.Equals("nil", StringComparison.OrdinalIgnoreCase))
                    return default(T)!;

                // Convert to target type
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return default(T)!;
            }
        }

        public static void DoString(string lua)
        {
            DoString(lua, "CopilotBuddy", 0);
        }

        public static void DoString(string lua, string luaFile, uint pState)
        {
            var executor = ObjectManager.Executor;
            if (executor == null)
                return;

            var wow = ObjectManager.Wow;
            if (wow == null)
                return;

            try
            {
                // Get Lua state if not provided
                if (pState == 0)
                    pState = wow.Read<uint>((uint)GlobalOffsets.LuaState);

                if (pState == 0)
                    return;

                // Encode script and name
                byte[] luaBytes = Encoding.UTF8.GetBytes(lua);
                byte[] nameBytes = Encoding.UTF8.GetBytes(luaFile);

                var scriptMemory = new AllocatedMemory(luaBytes.Length + nameBytes.Length + 2);
                try
                {
                    scriptMemory.WriteBytes(0, luaBytes);
                    scriptMemory.WriteByte(luaBytes.Length, 0);
                    scriptMemory.WriteBytes(luaBytes.Length + 1, nameBytes);
                    scriptMemory.WriteByte(luaBytes.Length + 1 + nameBytes.Length, 0);

                    uint luaPtr = scriptMemory.Address;
                    uint namePtr = (uint)(scriptMemory.Address + luaBytes.Length + 1);

                    lock (executor.AssemblyLock)
                    {
                        executor.Clear();

                        // luaL_loadbuffer(pState, lua, len, name)
                        executor.AddLine("push {0}", namePtr);
                        executor.AddLine("push {0}", luaBytes.Length);
                        executor.AddLine("push {0}", luaPtr);
                        executor.AddLine("push {0}", pState);
                        executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_Load);
                        executor.AddLine("add esp, 16");

                        // lua_pcall(pState, 0, 0, 0)
                        executor.AddLine("push 0");
                        executor.AddLine("push 0");
                        executor.AddLine("push 0");
                        executor.AddLine("push {0}", pState);
                        executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_PCall);
                        executor.AddLine("add esp, 16");

                        executor.AddLine("retn");
                        executor.Execute();
                    }
                }
                finally
                {
                    scriptMemory?.Dispose();
                }
            }
            catch (Exception)
            {
                // Silently fail
            }
        }

        public static void DoString(string szLua, string szLuaFile)
        {
            DoString(szLua, szLuaFile, 0);
        }

        public static void DoString(string format, params object[] args)
        {
            DoString(string.Format(format, args), "CopilotBuddy");
        }

        public static int GetTop(uint pState)
        {
            var executor = ObjectManager.Executor;
            if (executor == null)
                return 0;

            lock (executor.AssemblyLock)
            {
                executor.Clear();
                executor.AddLine("push {0}", pState);
                executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_GetTop);
                executor.AddLine("add esp, 4");
                executor.AddLine("retn");
                executor.Execute();

                return executor.Memory.Read<int>(executor.ReturnPointer);
            }
        }

        public static void ShowLuaStack(uint pState)
        {
            int top = GetTop(pState);
            for (int i = 1; i <= top; i++)
            {
                string value = ToLString(pState, i, 0);
                Logging.WriteDebug("Stack[{0}]: {1}", i, value);
            }
        }

        public static string ToLString(uint pState, int index, int len)
        {
            var executor = ObjectManager.Executor;
            if (executor == null)
                return string.Empty;

            lock (executor.AssemblyLock)
            {
                executor.Clear();
                executor.AddLine("push {0}", len);
                executor.AddLine("push {0}", index);
                executor.AddLine("push {0}", pState);
                executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_ToLString);
                executor.AddLine("add esp, 12");
                executor.AddLine("retn");
                executor.Execute();

                uint strPtr = executor.Memory.Read<uint>(executor.ReturnPointer);
                if (strPtr == 0)
                    return string.Empty;

                return ObjectManager.Wow?.Read<string>(strPtr) ?? string.Empty;
            }
        }

        public static T GetLocalizedText<T>(string szLuaVariable)
        {
            string text = GetLocalizedText(szLuaVariable, StyxWoW.Me?.BaseAddress ?? 0);
            return (T)Convert.ChangeType(text, typeof(T), CultureInfo.InvariantCulture);
        }

        public static string GetLocalizedText(string szLuaVariable)
        {
            return GetLocalizedText(szLuaVariable, StyxWoW.Me?.BaseAddress ?? 0);
        }

        public static string GetLocalizedText(string szLuaVariable, uint lpLocalPlayer)
        {
            var executor = ObjectManager.Executor;
            if (executor == null)
                return string.Empty;

            if (string.IsNullOrEmpty(szLuaVariable))
                return string.Empty;

            if (lpLocalPlayer == 0)
                return string.Empty;

            uint varPtr = 0;
            try
            {
                lock (executor.AssemblyLock)
                {
                    byte[] varBytes = Encoding.UTF8.GetBytes(szLuaVariable + "\0");
                    varPtr = executor.Memory.AllocateMemory(varBytes.Length);
                    executor.Memory.Write(varPtr, varBytes);

                    executor.Clear();
                    executor.AddLine("push 0");
                    executor.AddLine("push {0}", varPtr);
                    executor.AddLine("mov ecx, {0}", lpLocalPlayer);
                    executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript__GetLocalizedText);
                    executor.AddLine("retn");
                    executor.Execute();

                    uint resultPtr = executor.Memory.Read<uint>(executor.ReturnPointer);
                    if (resultPtr != 0)
                        return executor.Memory.Read<string>(resultPtr) ?? string.Empty;

                    return string.Empty;
                }
            }
            finally
            {
                if (varPtr != 0)
                    executor.Memory.FreeMemory(varPtr);
            }
        }

        public static int GetLocalizedInt32(string szLuaVariable, uint lpLocalPlayer)
        {
            string text = GetLocalizedText(szLuaVariable, lpLocalPlayer);
            if (string.IsNullOrEmpty(text) || text == "nil")
                return 0;
            return int.TryParse(text, out int result) ? result : 0;
        }

        public static uint GetLocalizedUInt32(string szLuaVariable, uint lpLocalPlayer)
        {
            string text = GetLocalizedText(szLuaVariable, lpLocalPlayer);
            if (string.IsNullOrEmpty(text) || text == "nil")
                return 0;
            return uint.TryParse(text, out uint result) ? result : 0;
        }

        public static long GetLocalizedInt64(string szLuaVariable, uint lpLocalPlayer)
        {
            string text = GetLocalizedText(szLuaVariable, lpLocalPlayer);
            if (string.IsNullOrEmpty(text) || text == "nil")
                return 0;
            return long.TryParse(text, out long result) ? result : 0;
        }

        public static ulong GetLocalizedUInt64(string szLuaVariable, uint lpLocalPlayer)
        {
            string text = GetLocalizedText(szLuaVariable, lpLocalPlayer);
            if (string.IsNullOrEmpty(text) || text == "nil")
                return 0;
            return ulong.TryParse(text, out ulong result) ? result : 0;
        }

        public static bool GetLocalizedBool(string szLuaVariable, uint lpLocalPlayer)
        {
            string text = GetLocalizedText(szLuaVariable, lpLocalPlayer);
            if (string.IsNullOrEmpty(text) || text == "nil")
                return false;
            return bool.TryParse(text, out bool result) && result;
        }

        public static LuaState State
        {
            get
            {
                var wow = ObjectManager.Wow;
                if (wow == null)
                    return new LuaState(0);
                return new LuaState(wow.Read<uint>((uint)GlobalOffsets.LuaState));
            }
        }

        private static LuaEvents _events;

        public static LuaEvents Events
        {
            get { return _events ??= new LuaEvents(); }
        }

        internal static void ProcessEvents()
        {
            try
            {
                Events.ProcessEvents();
            }
            catch (Exception ex)
            {
                Logging.WriteException(ex);
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents the Lua interpreter state in WoW memory.
    /// </summary>
    public class LuaState
    {
        // Offset to globals table in LuaState structure for WotLK 3.3.5a (build 12340)
        private const uint GlobalsOffset = 72; // 0x48

        private LuaTable _cachedGlobals;

        public uint Address { get; }

        public LuaState(uint address)
        {
            Address = address;
        }

        public bool IsValid => Address != 0;

        /// <summary>
        /// Gets the global variables table from the Lua state.
        /// This allows direct memory reading of Lua tables without executing Lua code.
        /// </summary>
        public LuaTable Globals
        {
            get
            {
                if (_cachedGlobals == null && Address != 0)
                {
                    // Read the globals table from LuaState + offset
                    // The globals table is stored as a LuaTValue at this offset
                    var tvalue = new LuaTValue(Address + GlobalsOffset);
                    if (tvalue.Type == LuaType.Table)
                    {
                        _cachedGlobals = tvalue.Value.Table;
                    }
                }
                return _cachedGlobals;
            }
        }
    }
}
