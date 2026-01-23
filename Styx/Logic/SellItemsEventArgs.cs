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

        public SellItemsEventArgs()
        {
            NameExceptions = new List<string>();
            IdExceptions = new List<uint>();
        }
    }
}
