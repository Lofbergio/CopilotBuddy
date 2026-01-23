using System;

namespace GreenMagic.Internals
{
    public class Patch : IDisposable, IMemoryOperation
    {
        private readonly uint _address;

        private readonly byte[] _originalBytes;

        private readonly byte[] _patchBytes;

        private readonly Memory _win32;

        internal Patch(uint address, byte[] patchWith, string name, Memory win)
        {
            this.Name = name;
            this._win32 = win;
            this._address = address;
            this._patchBytes = patchWith;
            this._originalBytes = this._win32.ReadBytes(address, patchWith.Length);
        }

        public void Dispose()
        {
            if (this.IsApplied)
            {
                this.Remove();
            }
            GC.SuppressFinalize(this);
        }

        public bool Remove()
        {
            if (this._win32.WriteBytes(this._address, this._originalBytes) != this._originalBytes.Length)
            {
                return false;
            }
            this.IsApplied = false;
            return true;
        }

        public bool Apply()
        {
            if (this._win32.WriteBytes(this._address, this._patchBytes) != this._patchBytes.Length)
            {
                return false;
            }
            this.IsApplied = true;
            return true;
        }

        public bool IsApplied { get; private set; }

        public string Name { get; private set; }

        ~Patch()
        {
            this.Dispose();
        }
    }
}
