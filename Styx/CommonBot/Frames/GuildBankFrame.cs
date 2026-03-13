using System;
using System.Collections.Generic;
using Styx.WoWInternals;

namespace Styx.CommonBot.Frames
{
    public class GuildBankFrame : IDisposable
    {
        public GuildBankFrame()
        {
            Close();
        }

        public void Dispose()
        {
            // No Lua.Events in CB — nothing to unregister
        }

        public bool IsVisible
        {
            get { return Lua.GetReturnVal<int>("return GuildBankFrame:IsShown() and 1 or 0", 0U) == 1; }
        }

        public int CurrentTab
        {
            get { return Lua.GetReturnVal<int>("return GetCurrentGuildBankTab()", 0U) - 1; }
        }

        public int NumTabs
        {
            get { return Lua.GetReturnVal<int>("return GetNumGuildBankTabs()", 0U); }
        }

        public bool CanWithdrawMoney
        {
            get { return Lua.GetReturnVal<bool>("return CanWithdrawGuildBankMoney()", 0U); }
        }

        public int Money
        {
            get { return Lua.GetReturnVal<int>("return GetGuildBankMoney()", 0U); }
        }

        public string GetItemLink(int tab, int slot)
        {
            return Lua.GetReturnVal<string>(string.Format("return GetGuildBankItemLink({0}, {1})", tab + 1, slot + 1), 0U);
        }

        public int GetItemCount(int tab, int slot)
        {
            return Lua.GetReturnVal<int>(string.Format("return GetGuildBankItemInfo({0}, {1})", tab + 1, slot + 1), 1U);
        }

        public bool GetItemAndCount(int tab, int slot, out string itemLink, out int count)
        {
            itemLink = null;
            List<string> ret = Lua.GetReturnValues(string.Format(
                "local _,count=GetGuildBankItemInfo({0},{1}) return count, GetGuildBankItemLink({0}, {1})",
                tab + 1, slot + 1));
            count = Lua.ParseLuaValue<int>(ret[0]);
            if (count == 0)
                return false;
            itemLink = ret[1];
            return true;
        }

        public void TakeItem(int tab, int slot)
        {
            Lua.DoString("AutoStoreGuildBankItem({0}, {1})", tab + 1, slot + 1);
        }

        public void PickupItem(int tab, int slot)
        {
            Lua.DoString("PickupGuildBankItem({0}, {1})", tab + 1, slot + 1);
        }

        public void SplitItem(int tab, int slot, int amount)
        {
            Lua.DoString("SplitGuildBankItem({0}, {1}, {2})", tab + 1, slot + 1, amount);
        }

        public void SetTab(int tab)
        {
            Lua.DoString("SetCurrentGuildBankTab({0})", tab + 1);
        }

        public void WithdrawMoney(int amount)
        {
            Lua.DoString("WithdrawGuildBankMoney({0})", amount);
        }

        public void DepositMoney(int amount)
        {
            Lua.DoString("DepositGuildBankMoney({0})", amount);
        }

        public void Close()
        {
            Lua.DoString("CloseGuildBankFrame()");
        }

        public GuildBankTab GetTabInfo(int tab)
        {
            return new GuildBankTab(Lua.GetReturnValues("return GetGuildBankTabInfo(" + (tab + 1) + ")"));
        }
    }
}
