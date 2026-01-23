#nullable disable

namespace Styx.WoWInternals
{
    /// <summary>
    /// Represents the Lua data types used in WoW's Lua implementation.
    /// </summary>
    public enum LuaType
    {
        /// <summary>No type / invalid.</summary>
        None = -1,
        /// <summary>Nil value.</summary>
        Nil = 0,
        /// <summary>Boolean value.</summary>
        Boolean = 1,
        /// <summary>Light user data pointer.</summary>
        LightUserData = 2,
        /// <summary>Number (double precision).</summary>
        Number = 3,
        /// <summary>String value.</summary>
        String = 4,
        /// <summary>Table structure.</summary>
        Table = 5,
        /// <summary>Function reference.</summary>
        Function = 6,
        /// <summary>Full user data.</summary>
        UserData = 7,
        /// <summary>Thread/coroutine.</summary>
        Thread = 8
    }
}
