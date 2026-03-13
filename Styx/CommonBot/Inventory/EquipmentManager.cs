using System;
using System.Collections.Generic;
using System.Linq;
using Styx.WoWInternals;

namespace Styx.CommonBot.Inventory
{
    public static class EquipmentManager
    {
        public static readonly List<EquipmentSet> EquipmentSets = new List<EquipmentSet>();

        public static void Initialize()
        {
            EquipmentSets.Clear();
            int count = Lua.GetReturnVal<int>("return GetNumEquipmentSets()", 0U);
            for (int i = 1; i <= count; i++)
            {
                EquipmentSets.Add(new EquipmentSet(i));
            }
        }

        public static bool IsEnabled
        {
            get { return Lua.GetReturnVal<bool>("return CanUseEquipmentSets()", 0U); }
        }

        public static EquipmentSet ActiveSet
        {
            get
            {
                using (StyxWoW.Memory.AcquireFrame())
                {
                    return EquipmentSets.FirstOrDefault(s => s.IsEquipped);
                }
            }
        }

        public static bool EquipSet(string name)
        {
            using (StyxWoW.Memory.AcquireFrame())
            {
                EquipmentSet set = EquipmentSets.FirstOrDefault(s => s.Name.Equals(name));
                if (set == null)
                    return false;
                set.UseEquipmentSet();
                return true;
            }
        }
    }
}
