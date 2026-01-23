using System;
using GreenMagic;
using Styx.Helpers;

namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Représente un sous-objet WoW (chaise, porte, bobber, etc.).
    /// WoW 3.3.5a build 12340.
    /// </summary>
    public class WoWSubObject
    {
        /// <summary>
        /// Crée un nouveau sous-objet WoW.
        /// </summary>
        /// <param name="baseAddress">Adresse de base en mémoire</param>
        internal WoWSubObject(uint baseAddress)
        {
            BaseAddress = baseAddress;
        }

        /// <summary>
        /// Adresse de base du sous-objet en mémoire.
        /// </summary>
        public uint BaseAddress { get; private set; }

        /// <summary>
        /// Distance d'interaction avec le sous-objet.
        /// Offset +12 (0xC).
        /// </summary>
        public float InteractDistance
        {
            get
            {
                Memory? wow = ObjectManager.Wow;
                if (wow == null) return 0f;
                return wow.Read<float>(BaseAddress + 12);
            }
        }

        /// <summary>
        /// GameObject propriétaire de ce sous-objet.
        /// Offset +4 pour le GUID.
        /// </summary>
        public WoWGameObject? OwnerObject
        {
            get
            {
                Memory? wow = ObjectManager.Wow;
                if (wow == null) return null;

                try
                {
                    uint guidLow = wow.Read<uint>(BaseAddress + 4);
                    // Construction du GUID complet depuis le low GUID
                    // Pour 3.3.5a, les GameObjects utilisent le type HIGHGUID_GAMEOBJECT (0xF11)
                    ulong guid = ((ulong)0xF110000000000000) | guidLow;
                    return ObjectManager.GetObjectByGuid<WoWGameObject>(guid);
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Vérifie si le sous-objet peut être utilisé.
        /// Appelle la méthode virtuelle CanUse() via vtable.
        /// </summary>
        public bool CanUse()
        {
            ExecutorRand? executor = ObjectManager.Executor;
            if (executor == null)
                throw new InvalidOperationException("Invalid executor in WoWSubObject.CanUse!");

            lock (executor.AssemblyLock)
            {
                executor.Clear();
                executor.AddLine($"mov ecx, {BaseAddress}");
                executor.AddLine("mov eax, [ecx]");
                executor.AddLine("mov eax, [eax+24]"); // Offset +24 pour vtable CanUse
                executor.AddLine("call eax");
                executor.AddLine("retn");
                executor.Execute();

                Memory? wow = ObjectManager.Wow;
                if (wow == null) return false;

                byte result = wow.Read<byte>(executor.ReturnPointer);
                return result != 0;
            }
        }

        /// <summary>
        /// Vérifie si le sous-objet peut être utilisé maintenant.
        /// </summary>
        public bool CanUseNow()
        {
            ExecutorRand? executor = ObjectManager.Executor;
            if (executor == null)
                throw new InvalidOperationException("Invalid executor in WoWSubObject.CanUseNow");

            lock (executor.AssemblyLock)
            {
                executor.Clear();
                BuildCanUseNowAsm(executor, 0, 0, 0);
                executor.AddLine("retn");
                executor.Execute();

                Memory? wow = ObjectManager.Wow;
                if (wow == null) return false;

                byte result = wow.Read<byte>(executor.ReturnPointer);
                return result != 0;
            }
        }

        /// <summary>
        /// Vérifie si le sous-objet peut être utilisé maintenant avec raison d'échec.
        /// </summary>
        public bool CanUseNow(out GameError reason)
        {
            ExecutorRand? executor = ObjectManager.Executor;
            if (executor == null)
                throw new InvalidOperationException("Invalid executor in WoWSubObject.CanUseNow");

            lock (executor.AssemblyLock)
            {
                using (AllocatedMemory reasonMem = new AllocatedMemory(4))
                {
                    executor.Clear();
                    BuildCanUseNowAsm(executor, reasonMem.Address, 0, 0);
                    executor.AddLine("retn");
                    executor.Execute();

                    Memory? wow = ObjectManager.Wow;
                    if (wow == null)
                    {
                        reason = (GameError)0;
                        return false;
                    }

                    reason = (GameError)wow.Read<uint>(reasonMem.Address);
                    byte result = wow.Read<byte>(executor.ReturnPointer);
                    return result != 0;
                }
            }
        }

        /// <summary>
        /// Utilise le sous-objet (appelle la méthode Use via vtable).
        /// </summary>
        public void Use()
        {
            ExecutorRand? executor = ObjectManager.Executor;
            if (executor == null)
                throw new InvalidOperationException("Invalid executor in WoWSubObject.Use");

            lock (executor.AssemblyLock)
            {
                executor.Clear();
                executor.AddLine($"mov ecx, {BaseAddress}");
                executor.AddLine("mov eax, [ecx]");
                executor.AddLine("mov eax, [eax+36]"); // Offset +36 pour vtable Use
                executor.AddLine("call eax");
                executor.AddLine("retn");
                executor.Execute();
            }
        }

        /// <summary>
        /// Construit l'assembleur pour CanUseNow.
        /// Appelle la méthode virtuelle CanUseNow via vtable (+28).
        /// </summary>
        private void BuildCanUseNowAsm(ExecutorRand executor, uint reason, uint interactDistance, uint a4)
        {
            executor.AddLine($"mov ecx, {BaseAddress}");
            executor.AddLine("mov eax, [ecx]");
            executor.AddLine("mov eax, [eax+28]"); // Offset +28 pour vtable CanUseNow
            executor.AddLine($"push {a4}");
            executor.AddLine($"push {interactDistance}");
            executor.AddLine($"push {reason}");
            executor.AddLine("call eax");
        }
    }
}
