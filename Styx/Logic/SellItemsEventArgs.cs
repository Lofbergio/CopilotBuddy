using System;
using System.Collections.Generic;

namespace Styx.Logic
{
    /// <summary>
    /// Event args for selling items to vendor
    /// </summary>
    public class SellItemsEventArgs
    {
        public List<string> NameExceptions { get; set; }
        public List<uint> IdExceptions { get; set; }

        /// <summary>False when the caller is only asking what WOULD be sold (a per-tick gate).
        /// Subscribers must stay silent then — the payload resolvers run every second.</summary>
        public bool Verbose { get; set; }

        public SellItemsEventArgs()
        {
            NameExceptions = new List<string>();
            IdExceptions = new List<uint>();
        }
    }
}
