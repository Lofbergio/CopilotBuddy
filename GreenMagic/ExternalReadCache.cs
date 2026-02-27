using System;
using System.Runtime.InteropServices;

namespace GreenMagic
{
    /// <summary>
    /// Helper returned by <see cref="Memory.SaveCacheState"/> and
    /// <see cref="Memory.TemporaryCacheState"/>.  Mirrors HB's
    /// GreyMagic.ExternalReadCache; saves the current cache-enabled state
    /// and optionally toggles it until disposed.
    /// </summary>
    public class ExternalReadCache : IDisposable
    {
        private bool _disposed;
        private bool _cacheState;
        private readonly Memory _mem;

        public ExternalReadCache(Memory mem)
            : this(mem, mem.CacheEnabled)
        {
        }

        public ExternalReadCache(Memory mem, bool enabledTemporarily)
        {
            _mem = mem ?? throw new ArgumentNullException(nameof(mem));
            _cacheState = mem.CacheEnabled;
            if (enabledTemporarily)
                _mem.EnableCache();
            else
                _mem.DisableCache();
        }

        private void RestoreCache()
        {
            if (!_disposed)
            {
                if (_cacheState)
                    _mem.EnableCache();
                else
                    _mem.DisableCache();
                _disposed = true;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                RestoreCache();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}