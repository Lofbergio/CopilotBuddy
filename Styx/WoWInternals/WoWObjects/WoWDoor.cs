using System;
using GreenMagic;
using Styx.Helpers;

namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// Represents a door in WoW.
    /// WoW 3.3.5a build 12340.
    /// </summary>
    public class WoWDoor : WoWAnimatedSubObject
    {
        internal WoWDoor(uint baseAddress) : base(baseAddress)
        {
        }

        /// <summary>
        /// Indicates whether the door is closed.
        /// Checks the animation state.
        /// AnimationState 0 = closed, 1 = opening, 3 = open.
        /// </summary>
        public bool IsClosed
        {
            get
            {
                WoWGameObject? owner = OwnerObject;
                if (owner == null)
                    return true;

                if (owner.GetDataSlot(GameObjectDataSlot.Opens, out bool opens) && opens)
                {
                    if (owner.GetDataSlot(GameObjectDataSlot.Alternate, out bool alternate) && alternate)
                        return AnimationState != 1;
                    return AnimationState != 3;
                }
                return true;
            }
        }

        /// <summary>
        /// Indicates whether the door is open.
        /// </summary>
        public bool IsOpen => !IsClosed;

        /// <summary>
        /// Checks if the door can be opened now.
        /// Calls the native WoW function to check.
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

                byte result;
                using (StyxWoW.Memory.TemporaryCacheState(false))
                {
                    result = wow.Read<byte>(executor.ReturnPointer);
                }
                return result != 0;
            }
        }

        /// <summary>
        /// Checks if the door can be opened now with failure reason.
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

                    using (StyxWoW.Memory.TemporaryCacheState(false))
                    {
                        reason = wow.Read<uint>(reasonMem.Address);
                        byte result = wow.Read<byte>(executor.ReturnPointer);
                        return result != 0;
                    }
                }
            }
        }

        /// <summary>
        /// Builds the assembler for CanOpenNow.
        /// Calls the native WoW function at offset 7412176 (0x007119D0).
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
