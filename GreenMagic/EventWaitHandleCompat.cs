using System;
using System.Security.AccessControl;
using System.Threading;

namespace GreenMagic
{
    /// <summary>
    /// Helpers pour compatibilité .NET Framework → .NET moderne
    /// Utilise EventWaitHandleAcl.Create() de .NET 9+
    /// </summary>
    internal static class EventWaitHandleCompat
    {
        /// <summary>
        /// Crée un EventWaitHandle avec sécurité (.NET moderne)
        /// Wrapper pour EventWaitHandleAcl.Create()
        /// </summary>
        public static EventWaitHandle Create(
            bool initialState,
            EventResetMode mode,
            string name,
            out bool createdNew,
            EventWaitHandleSecurity security)
        {
            // .NET 9+ utilise EventWaitHandleAcl.Create()
            return EventWaitHandleAcl.Create(initialState, mode, name, out createdNew, security);
        }
    }
}
