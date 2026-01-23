using System;

namespace GreenMagic.Internals
{
    public class PatchManager : Manager<Patch>
    {
        static PatchManager()
        {
        }

        internal PatchManager(Memory win32) : base(win32)
        {
        }

        public void Add(Patch patch)
        {
            this.Applications.Add(patch.Name, patch);
        }

        public Patch Create(uint address, byte[] patchWith, string name)
        {
            if (address == 0U)
            {
                throw new ArgumentException("Address cannot be 0!", "address");
            }
            if (patchWith == null || patchWith.Length == 0)
            {
                throw new ArgumentNullException("patchWith", "Patch bytes cannot be null, or 0 bytes long!");
            }
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException("name");
            }
            if (this.Applications.ContainsKey(name))
            {
                return null;
            }
            return new Patch(address, patchWith, name, base.Win32);
        }

        public Patch CreateAndApply(uint address, byte[] patchWith, string name)
        {
            Patch patch = this.Create(address, patchWith, name);
            if (patch != null)
            {
                patch.Apply();
            }
            return patch;
        }
    }
}
