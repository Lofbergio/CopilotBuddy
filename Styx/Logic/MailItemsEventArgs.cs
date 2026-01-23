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

        public MailItemsEventArgs()
        {
            AdditionalItems = new List<WoWItem>();
        }
    }
}
