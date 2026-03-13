using System.Collections.Generic;
using System.Linq;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.CommonBot.Inventory
{
    public class EquipmentSet
    {
        public EquipmentSet(int index)
        {
            Name = Lua.GetReturnVal<string>(string.Format("return GetEquipmentSetInfo({0})", index), 0U);
        }

        public string Name { get; }

        public bool IsEquipped
        {
            get { return Lua.GetReturnVal<bool>(string.Format("return GetEquipmentSetInfoByName(\"{0}\")", Name), 2U); }
        }

        public int NumItems
        {
            get { return Lua.GetReturnVal<int>(string.Format("return GetEquipmentSetInfoByName(\"{0}\")", Name), 3U); }
        }

        public int NumEquipped
        {
            get { return Lua.GetReturnVal<int>(string.Format("return GetEquipmentSetInfoByName(\"{0}\")", Name), 4U); }
        }

        public int NumInInventory
        {
            get { return Lua.GetReturnVal<int>(string.Format("return GetEquipmentSetInfoByName(\"{0}\")", Name), 5U); }
        }

        public int NumMissing
        {
            get { return Lua.GetReturnVal<int>(string.Format("return GetEquipmentSetInfoByName(\"{0}\")", Name), 6U); }
        }

        public int NumIgnored
        {
            get { return Lua.GetReturnVal<int>(string.Format("return GetEquipmentSetInfoByName(\"{0}\")", Name), 7U); }
        }

        public IEnumerable<WoWItem> Items
        {
            get { return Enumerable.Empty<WoWItem>(); }
        }

        public void UseEquipmentSet()
        {
            Lua.DoString("UseEquipmentSet(\"{0}\")", Name);
        }

        public void DeleteEquipmentSet()
        {
            Lua.DoString("DeleteEquipmentSet(\"{0}\")", Name);
        }

        public void SaveEquipmentSet()
        {
            Lua.DoString("SaveEquipmentSet(\"{0}\")", Name);
        }

        public override string ToString()
        {
            using (StyxWoW.Memory.AcquireFrame())
            {
                return string.Format(
                    "Name: {0} IsEquipped: {1} NumItems: {2} NumEquipped: {3} NumInInventory: {4} NumMissing: {5} NumIgnored: {6}",
                    Name, IsEquipped, NumItems, NumEquipped, NumInInventory, NumMissing, NumIgnored);
            }
        }
    }
}
