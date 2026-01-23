// Decompiled with JetBrains decompiler
// Type: Styx.Logic.Profiles.Quest.UseItemNode
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest;

public class UseItemNode : OrderNode
{
    public UseItemNode(Func<WoWItem> itemRetriever, uint questId, WoWPoint location)
        : this(itemRetriever, (Func<WoWObject>)null, questId, location)
    {
    }

    public UseItemNode(
        Func<WoWItem> itemRetriever,
        Func<WoWObject> targetRetriever,
        uint questId,
        WoWPoint location)
        : this(itemRetriever, targetRetriever, UseItemTargetType.None, false, questId, location)
    {
    }

    public UseItemNode(
        Func<WoWItem> itemRetriever,
        Func<WoWObject> targetRetriever,
        UseItemTargetType targetType,
        bool forceUse,
        uint questId,
        WoWPoint location)
        : base(OrderNodeType.UseItem)
    {
        this.ItemRetriever = itemRetriever != null ? itemRetriever : throw new ArgumentNullException(nameof(itemRetriever));
        if (targetRetriever == null)
            targetRetriever = (Func<WoWObject>)(() => (WoWObject)null);
        this.TargetRetriever = targetRetriever;
        this.ForceUse = forceUse;
        this.QuestId = questId;
        this.Location = location;
        this.TargetType = targetType;
    }

    public Func<WoWItem> ItemRetriever { get; private set; }

    public Func<WoWObject> TargetRetriever { get; private set; }

    public bool ForceUse { get; private set; }

    public uint QuestId { get; private set; }

    public WoWPoint Location { get; private set; }

    public UseItemTargetType TargetType { get; private set; }

    public new static OrderNode FromXml(XElement element)
    {
        uint targetId = 0U;
        var targetIdAttr = element.Attribute("TargetId") ?? element.Attribute("targetid");
        if (targetIdAttr != null)
            uint.TryParse(targetIdAttr.Value, out targetId);

        UseItemTargetType targetType = UseItemTargetType.None;
        var targetTypeAttr = element.Attribute("TargetType") ?? element.Attribute("targettype");
        if (targetTypeAttr != null)
        {
            Enum.TryParse(targetTypeAttr.Value, true, out targetType);
        }

        var questIdAttr = element.Attribute("QuestId") ?? element.Attribute("questid");
        if (questIdAttr == null)
            throw new ProfileMissingAttributeException<uint>("QuestId", element);
        if (!uint.TryParse(questIdAttr.Value, out uint questId))
            throw new ProfileAttributeExpectedException<int>(questIdAttr);

        var itemIdAttr = element.Attribute("ItemId") ?? element.Attribute("itemid");
        if (itemIdAttr == null)
            throw new ProfileMissingAttributeException<int>("ItemId", element);
        if (!uint.TryParse(itemIdAttr.Value, out uint itemId))
            throw new ProfileAttributeExpectedException<int>(itemIdAttr);

        float x = 0, y = 0, z = 0;
        var xAttr = element.Attribute("X");
        var yAttr = element.Attribute("Y");
        var zAttr = element.Attribute("Z");

        if (xAttr != null)
            float.TryParse(xAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
        if (yAttr != null)
            float.TryParse(yAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        if (zAttr != null)
            float.TryParse(zAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out z);

        return new UseItemNode(
            () => GetItem(itemId),
            () => GetTarget(targetId, targetType),
            targetType,
            false,
            questId,
            new WoWPoint(x, y, z));
    }

    private static WoWObject GetTarget(uint targetId, UseItemTargetType targetType)
    {
        WoWObject target = null;
        switch (targetType)
        {
            case UseItemTargetType.Npc:
                target = ObjectManager.GetObjectsOfType<WoWUnit>(true)
                    .OrderBy(u => u.Distance)
                    .FirstOrDefault(u => u.Entry == targetId);
                break;
            case UseItemTargetType.Gameobject:
                target = ObjectManager.GetObjectsOfType<WoWGameObject>()
                    .OrderBy(g => g.Distance)
                    .FirstOrDefault(g => g.Entry == targetId);
                break;
        }
        if (target == null && targetType != UseItemTargetType.None)
        {
            Logging.Write("Could not find target to use item on with ID {0}.", targetId);
        }
        return target;
    }

    private static WoWItem GetItem(uint itemId)
    {
        var item = ObjectManager.Me.CarriedItems.FirstOrDefault(i => i.Entry == itemId);
        if (item == null)
        {
            Logging.Write("Could not find item to use by ID. ID provided: {0}", itemId);
        }
        return item;
    }

    public enum UseItemTargetType
    {
        None,
        Npc,
        Gameobject,
    }
}
