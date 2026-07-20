using System;
using System.Collections.Generic;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic
{
    /// <summary>
    /// Event args for mail items operation
    /// </summary>
    public class MailItemsEventArgs
    {
        public List<WoWItem> AdditionalItems { get; set; }

        /// <summary>False when the caller is only asking what WOULD be mailed (a per-tick gate).
        /// Subscribers must stay silent then — the payload resolvers run every second.</summary>
        public bool Verbose { get; set; }

        public MailItemsEventArgs()
        {
            AdditionalItems = new List<WoWItem>();
        }
    }
}
