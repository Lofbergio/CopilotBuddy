#nullable disable
using System;

namespace Styx.Logic
{
    /// <summary>
    /// Event arguments for mount up events (HB 4.3.4 compatibility).
    /// </summary>
    public class MountUpEventArgs : EventArgs
    {
        /// <summary>
        /// Gets whether the mount is a flying mount.
        /// </summary>
        public bool IsFlying { get; set; }

        /// <summary>
        /// Gets the name of the mount.
        /// </summary>
        public string MountName { get; set; }

        public MountUpEventArgs()
        {
            IsFlying = false;
            MountName = string.Empty;
        }

        public MountUpEventArgs(bool isFlying, string mountName)
        {
            IsFlying = isFlying;
            MountName = mountName ?? string.Empty;
        }
    }
}
