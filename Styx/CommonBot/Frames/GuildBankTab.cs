using System.Collections.Generic;
using Styx.WoWInternals;

namespace Styx.CommonBot.Frames
{
    public class GuildBankTab
    {
        internal GuildBankTab(List<string> args)
        {
            Name               = args[0];
            Icon               = args[1];
            IsViewable         = Lua.ParseLuaValue<bool>(args[2]);
            CanDeposit         = Lua.ParseLuaValue<bool>(args[3]);
            NumWithdrawls      = Lua.ParseLuaValue<int>(args[4]);
            RemainingWithdrawals = Lua.ParseLuaValue<int>(args[5]);
            IsFiltered         = Lua.ParseLuaValue<bool>(args[6]);
        }

        public string Name               { get; private set; }
        public string Icon               { get; private set; }
        public bool   IsViewable         { get; private set; }
        public bool   CanDeposit         { get; private set; }
        public int    NumWithdrawls      { get; private set; }
        public int    RemainingWithdrawals { get; private set; }
        public bool   IsFiltered         { get; private set; }
    }
}
