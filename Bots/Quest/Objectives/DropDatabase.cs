// Decompiled with JetBrains decompiler
// Type: Bots.Quest.Objectives.DropDatabase
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using System.Collections.Generic;

#nullable disable
namespace Bots.Quest.Objectives;

public static class DropDatabase
{
    private static readonly Dictionary<uint, HashSet<uint>> dictionary_0 = new Dictionary<uint, HashSet<uint>>();
    private static readonly Dictionary<uint, HashSet<uint>> dictionary_1 = new Dictionary<uint, HashSet<uint>>();
    private static readonly Dictionary<uint, HashSet<uint>> dictionary_2 = new Dictionary<uint, HashSet<uint>>();

    static DropDatabase()
    {
        // Mob drops for various quests
        AddMobDrop(11882U, 20404U);
        AddMobDrop(11880U, 20404U);
        AddMobDrop(11881U, 20404U);
        AddMobDrop(15201U, 20404U);
        AddMobDrop(15542U, 20404U);
        AddMobDrop(15213U, 20404U);
        AddMobDrop(11883U, 20404U);
        AddMobDrop(14479U, 20404U);
        AddMobDrop(1125U, 2886U);
        AddMobDrop(1126U, 2886U);
        AddMobDrop(1127U, 2886U);
        AddMobDrop(1689U, 2886U);
        AddMobDrop(1126U, 769U);
        AddMobDrop(1125U, 769U);
        AddMobDrop(1127U, 769U);
        AddMobDrop(1689U, 769U);
        AddMobDrop(708U, 769U);
        AddMobDrop(3099U, 769U);
        AddMobDrop(3100U, 769U);
        AddMobDrop(3225U, 769U);
        AddMobDrop(3098U, 769U);
        AddMobDrop(119U, 769U);
        AddMobDrop(524U, 769U);
        AddMobDrop(113U, 769U);
        AddMobDrop(390U, 769U);
        AddMobDrop(330U, 769U);
        AddMobDrop(1191U, 769U);
        AddMobDrop(1190U, 769U);
        AddMobDrop(1192U, 769U);
        AddMobDrop(547U, 769U);
        AddMobDrop(1985U, 769U);
        AddMobDrop(454U, 769U);
        AddMobDrop(157U, 769U);
        AddMobDrop(17200U, 23676U);
        AddMobDrop(17201U, 23676U);
        AddMobDrop(1190U, 3172U);
        AddMobDrop(1191U, 3172U);
        AddMobDrop(1192U, 3172U);
        AddMobDrop(547U, 3172U);
        AddMobDrop(157U, 3172U);
        AddMobDrop(454U, 3172U);
        AddMobDrop(345U, 3172U);
        AddMobDrop(2163U, 3173U);
        AddMobDrop(2164U, 3173U);
        AddMobDrop(1188U, 3173U);
        AddMobDrop(1189U, 3173U);
        AddMobDrop(1797U, 3173U);
        AddMobDrop(6788U, 3173U);
        AddMobDrop(1186U, 3173U);
        AddMobDrop(1778U, 3173U);
        AddMobDrop(2165U, 3173U);
        AddMobDrop(17347U, 3173U);
        AddMobDrop(1225U, 3173U);
        AddMobDrop(17348U, 3173U);
        AddMobDrop(12432U, 3173U);
        AddMobDrop(1961U, 3173U);
        AddMobDrop(1130U, 3173U);
        AddMobDrop(17661U, 3173U);
        AddMobDrop(3809U, 3173U);
        AddMobDrop(2356U, 3173U);
        AddMobDrop(2354U, 3173U);
        AddMobDrop(1128U, 3173U);
        AddMobDrop(822U, 3173U);
        AddMobDrop(3821U, 3174U);
        AddMobDrop(4264U, 3174U);
        AddMobDrop(574U, 3174U);
        AddMobDrop(949U, 3174U);
        AddMobDrop(3820U, 3174U);
        AddMobDrop(930U, 3174U);
        AddMobDrop(4005U, 3174U);
        AddMobDrop(505U, 3174U);
        AddMobDrop(4006U, 3174U);
        AddMobDrop(4007U, 3174U);
        AddMobDrop(3819U, 3174U);
        AddMobDrop(4040U, 3174U);
        AddMobDrop(569U, 3174U);
        AddMobDrop(1111U, 3174U);
        AddMobDrop(539U, 3174U);
        AddMobDrop(442U, 3174U);
        AddMobDrop(217U, 3174U);
        AddMobDrop(1112U, 3174U);
        AddMobDrop(4263U, 3174U);
        AddMobDrop(1185U, 3174U);
        AddMobDrop(616U, 3174U);
        AddMobDrop(11921U, 3174U);
        AddMobDrop(1184U, 3174U);
        AddMobDrop(1195U, 3174U);
        AddMobDrop(1781U, 3174U);
        AddMobDrop(14266U, 3174U);
        AddMobDrop(1780U, 3174U);
        AddMobDrop(12433U, 3174U);
        AddMobDrop(471U, 3174U);
        AddMobDrop(1766U, 3164U);
        AddMobDrop(1765U, 3164U);
        AddMobDrop(345U, 723U);
        AddMobDrop(547U, 723U);
        AddMobDrop(157U, 723U);
        AddMobDrop(454U, 723U);
        AddMobDrop(2408U, 3712U);
        AddMobDrop(6369U, 3712U);
        AddMobDrop(2505U, 3712U);
        AddMobDrop(14123U, 3712U);
        AddMobDrop(5431U, 3712U);
        AddMobDrop(6352U, 3712U);
        AddMobDrop(13599U, 3712U);
        AddMobDrop(7977U, 3712U);
        AddMobDrop(13896U, 3712U);
        AddMobDrop(4397U, 3712U);
        AddMobDrop(4143U, 3712U);
        AddMobDrop(4144U, 3712U);
        AddMobDrop(8213U, 3712U);
        AddMobDrop(4142U, 3712U);
        AddMobDrop(4825U, 3712U);
        AddMobDrop(4824U, 3712U);
        AddMobDrop(14223U, 3712U);
        AddMobDrop(4887U, 3712U);
        AddGameObjectDrop(182069U, 24290U);
        AddGameObjectDrop(184504U, 29443U);
        AddMobDrop(3458U, 5092U);
        AddMobDrop(3456U, 5093U);
        AddMobDrop(3457U, 5093U);
        AddMobDrop(3459U, 5094U);
    }

    public static Dictionary<uint, HashSet<uint>> Mobs
    {
        get
        {
            Dictionary<uint, HashSet<uint>> mobs = new Dictionary<uint, HashSet<uint>>();
            foreach (KeyValuePair<uint, HashSet<uint>> keyValuePair in dictionary_0)
            {
                HashSet<uint> uintSet = new HashSet<uint>(keyValuePair.Value);
                mobs.Add(keyValuePair.Key, uintSet);
            }
            return mobs;
        }
    }

    public static Dictionary<uint, HashSet<uint>> GameObjects
    {
        get
        {
            Dictionary<uint, HashSet<uint>> gameObjects = new Dictionary<uint, HashSet<uint>>();
            foreach (KeyValuePair<uint, HashSet<uint>> keyValuePair in dictionary_1)
            {
                HashSet<uint> uintSet = new HashSet<uint>(keyValuePair.Value);
                gameObjects.Add(keyValuePair.Key, uintSet);
            }
            return gameObjects;
        }
    }

    public static Dictionary<uint, HashSet<uint>> Vendors
    {
        get
        {
            Dictionary<uint, HashSet<uint>> vendors = new Dictionary<uint, HashSet<uint>>();
            foreach (KeyValuePair<uint, HashSet<uint>> keyValuePair in dictionary_2)
            {
                HashSet<uint> uintSet = new HashSet<uint>(keyValuePair.Value);
                vendors.Add(keyValuePair.Key, uintSet);
            }
            return vendors;
        }
    }

    public static void AddMobDrop(uint mobId, uint dropItemId)
    {
        if (dictionary_0.ContainsKey(mobId))
            dictionary_0[mobId].Add(dropItemId);
        else
            dictionary_0[mobId] = new HashSet<uint>() { dropItemId };
    }

    public static void AddGameObjectDrop(uint gameObjId, uint dropItemId)
    {
        if (dictionary_1.ContainsKey(gameObjId))
            dictionary_1[gameObjId].Add(dropItemId);
        else
            dictionary_1[gameObjId] = new HashSet<uint>() { dropItemId };
    }

    public static void AddVendorDrop(uint vendorId, uint itemId)
    {
        if (dictionary_2.ContainsKey(vendorId))
            dictionary_2[vendorId].Add(itemId);
        else
            dictionary_2[vendorId] = new HashSet<uint>() { itemId };
    }

    public static bool UnitDropsItem(uint mobId, uint itemId)
    {
        HashSet<uint> uintSet;
        return dictionary_0.TryGetValue(mobId, out uintSet) && uintSet.Contains(itemId);
    }

    public static bool GameObjectDropsItem(uint gameObjId, uint itemId)
    {
        HashSet<uint> uintSet;
        return dictionary_1.TryGetValue(gameObjId, out uintSet) && uintSet.Contains(itemId);
    }

    public static bool VendorSellsItem(uint vendorId, uint itemId)
    {
        HashSet<uint> uintSet;
        return dictionary_2.TryGetValue(vendorId, out uintSet) && uintSet.Contains(itemId);
    }
}
