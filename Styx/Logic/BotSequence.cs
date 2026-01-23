using System;

namespace Styx.Logic
{
    /// <summary>
    /// Bot action sequence types for 3.3.5a
    /// Note: Renamed from Sequence to BotSequence to avoid conflict with TreeSharp.Sequence
    /// </summary>
    public enum BotSequence
    {
        Pull,
        RetrieveCorpse,
        MailItems,
        VendorItemsAndRepair,
        ReleaseSpirit,
        BattlegroundsRess,
        MountUp,
        Loot
    }
}
