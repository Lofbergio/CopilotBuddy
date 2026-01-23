using System;
using GreenMagic;
using Styx.Helpers;

namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Représente une porte dans WoW.
    /// WoW 3.3.5a build 12340.
    /// </summary>
    public class WoWDoor : WoWAnimatedSubObject
    {
        internal WoWDoor(uint baseAddress) : base(baseAddress)
        {
        }

        /// <summary>
        /// Indique si la porte est fermée.
        /// Vérifie l'état d'animation.
        /// AnimationState 0 = fermée, 1 = en cours d'ouverture, 3 = ouverte.
        /// </summary>
        public bool IsClosed
        {
            get
            {
                WoWGameObject? owner = OwnerObject;
                if (owner == null)
                    return true;

                // AnimationState: 0 = closed, 1 = opening, 3 = open
                // For doors, state 3 means fully open
                return AnimationState != 3;
            }
        }

        /// <summary>
        /// Indique si la porte est ouverte.
        /// </summary>
        public bool IsOpen => !IsClosed;

        /// <summary>
        /// Vérifie si la porte peut être ouverte maintenant.
        /// Appelle la fonction native WoW pour vérifier.
        /// </summary>
        public bool CanOpenNow()
        {
            ExecutorRand? executor = ObjectManager.Executor;
            if (executor == null)
                throw new InvalidOperationException("Invalid executor in WoWDoor.CanOpenNow!");

            lock (executor.AssemblyLock)
            {
                executor.Clear();
                BuildCanOpenNowAsm(executor, 0, 0);
                executor.AddLine("retn");
                executor.Execute();

                Memory? wow = ObjectManager.Wow;
                if (wow == null) return false;

                byte result = wow.Read<byte>(executor.ReturnPointer);
                return result != 0;
            }
        }

        /// <summary>
        /// Vérifie si la porte peut être ouverte maintenant avec raison d'échec.
        /// </summary>
        public bool CanOpenNow(out uint reason)
        {
            ExecutorRand? executor = ObjectManager.Executor;
            if (executor == null)
                throw new InvalidOperationException("Invalid executor in WoWDoor.CanOpenNow!");

            lock (executor.AssemblyLock)
            {
                using (AllocatedMemory reasonMem = new AllocatedMemory(4))
                {
                    executor.Clear();
                    BuildCanOpenNowAsm(executor, reasonMem.Address, 0);
                    executor.AddLine("retn");
                    executor.Execute();

                    Memory? wow = ObjectManager.Wow;
                    if (wow == null)
                    {
                        reason = 0;
                        return false;
                    }

                    reason = wow.Read<uint>(reasonMem.Address);
                    byte result = wow.Read<byte>(executor.ReturnPointer);
                    return result != 0;
                }
            }
        }

        /// <summary>
        /// Construit l'assembleur pour CanOpenNow.
        /// Appelle la fonction native WoW à l'offset 7412176 (0x00713050).
        /// </summary>
        private void BuildCanOpenNowAsm(ExecutorRand executor, uint reason, uint interactDistance)
        {
            executor.AddLine($"mov ecx, {BaseAddress}");
            executor.AddLine($"push {interactDistance}");
            executor.AddLine($"push {reason}");
            executor.AddLine("call 7412176"); // Offset 3.3.5a: CGGameObject_C::CanOpenNow
        }
    }
}
